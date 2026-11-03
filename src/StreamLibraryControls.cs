using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Alta.Character;

namespace ATTSmoothCamera
{
    public sealed partial class StreamCameraMod
    {
        string presetName = "My camera", pathName = "My camera path";
        int selectedPreset, selectedPath;
        readonly List<CameraPoint> draft = new List<CameraPoint>();
        bool recording, pathPlaying, pathHolding, pathPaused, pathLoop, pathWasSuspended;
        float recordElapsed, nextRecordAt, pathElapsed, pathSpeed = 1f, keyframeSpacing = 2f;
        CameraPath playingPath;
        bool PathHasControl { get { return playingPath != null && (pathPlaying || pathHolding); } }
        string PathStatus { get { return (pathHolding ? "Path finished; holding the last shot" : pathPaused ? "Path paused" : "Playing: " + playingPath.Name) + "  " + pathElapsed.ToString("0.0") + " s"; } }

        CameraPreset CurrentPreset(string name)
        {
            return new CameraPreset { Name = name, Mode = (int)mode, Distance = distance.Value, Height = height.Value,
                Shoulder = shoulder.Value, Fov = fov.Value, PositionHalfLife = positionHalfLife.Value,
                RotationHalfLife = rotationHalfLife.Value, OrbitSpeed = orbitSpeed.Value,
                HorizonLock = horizonLock.Value, SmartFraming = smartFraming.Value, AvoidWalls = avoidWalls.Value };
        }
        void ApplyPreset(CameraPreset preset)
        {
            ChangeMode((StreamMode)preset.Mode);
            distance.Value = preset.Distance; height.Value = preset.Height; shoulder.Value = preset.Shoulder;
            fov.Value = preset.Fov; positionHalfLife.Value = preset.PositionHalfLife; rotationHalfLife.Value = preset.RotationHalfLife;
            orbitSpeed.Value = preset.OrbitSpeed; horizonLock.Value = preset.HorizonLock; smartFraming.Value = preset.SmartFraming;
            avoidWalls.Value = preset.AvoidWalls;
            SetEnabled(true); presetName = preset.Name; Notice("Loaded " + preset.Name + ".");
        }
        void SavePreset()
        {
            library.UpsertPreset(CurrentPreset(presetName)); library.Save();
            for (int i = 0; i < library.Presets.Count; i++) if (String.Equals(library.Presets[i].Name, presetName.Trim(), StringComparison.OrdinalIgnoreCase)) selectedPreset = i;
            Notice("Preset saved. It will remain available next session.");
        }
        void StartRecording()
        {
            if (output == null || !output.enabled) { Notice("Enable the camera and join before recording."); return; }
            StopPath(false); frozenPhoto = false; draft.Clear(); recordElapsed = 0f; nextRecordAt = 0f;
            recording = true; Notice("Recording camera movement. Move through the route in VR, then stop and save.");
        }
        CameraPoint CurrentPoint(float time)
        {
            Vector3 p = output.transform.position; Quaternion q = output.transform.rotation;
            return new CameraPoint { Time = time, X = p.x, Y = p.y, Z = p.z, Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w, Fov = output.fieldOfView };
        }
        void RecordFrame()
        {
            recordElapsed += Safe(Time.unscaledDeltaTime, 0f, .5f, 0f);
            if (recordElapsed < nextRecordAt) return;
            if (draft.Count >= CameraLibrary.MaxPoints) { recording = false; Notice("Recording reached its point limit. Save the path below."); return; }
            draft.Add(CurrentPoint(draft.Count == 0 ? 0f : recordElapsed)); nextRecordAt = recordElapsed + .25f;
        }
        void StopRecording()
        {
            if (recording && output != null && draft.Count > 0 && draft.Count < CameraLibrary.MaxPoints && recordElapsed > draft[draft.Count - 1].Time + .001f)
                draft.Add(CurrentPoint(recordElapsed));
            recording = false; Notice("Recording stopped. Name the path and save it below.");
        }
        void AddKeyframe()
        {
            if (output == null || !output.enabled) { Notice("Join and enable the camera before adding a keyframe."); return; }
            if (recording) { Notice("Stop recording before adding manual keyframes."); return; }
            if (draft.Count >= CameraLibrary.MaxPoints) { Notice("This path has reached its point limit."); return; }
            float time = draft.Count == 0 ? 0f : draft[draft.Count - 1].Time + keyframeSpacing;
            draft.Add(CurrentPoint(time)); Notice("Keyframe " + draft.Count + " added at " + time.ToString("0.0") + " s.");
        }
        void SavePath()
        {
            if (recording) StopRecording();
            library.UpsertPath(new CameraPath { Name = pathName, Points = new List<CameraPoint>(draft) }); library.Save();
            Notice("Saved camera path. Keep your player near the route so the world is loaded.");
        }
        void PlayPath(CameraPath path)
        {
            if (recording) StopRecording();
            CameraPathSampler.Duration(path); // Validate before changing the active view.
            playingPath = path; pathElapsed = 0f; pathPlaying = true; pathPaused = false; pathHolding = false;
            pathWasSuspended = true; frozenPhoto = false; transitionActive = false;
            SetEnabled(true); Notice("Playing camera movement only. Your VR player stays under your control.");
        }
        void StopPath(bool transition)
        {
            if (transition) BeginTransition();
            avoidanceReset = true;
            playingPath = null; pathPlaying = false; pathHolding = false; pathPaused = false; pathElapsed = 0f;
        }
        void PausePathClock() { pathWasSuspended = true; }
        bool TryPathPose(float dt, out Vector3 position, out Quaternion rotation, out float viewFov)
        {
            position = Vector3.zero; rotation = Quaternion.identity; viewFov = Safe(fov.Value, 35f, 110f, 75f);
            if (!PathHasControl) return false;
            float duration = CameraPathSampler.Duration(playingPath);
            if (pathPlaying && !pathPaused && !pathWasSuspended) pathElapsed += dt * pathSpeed;
            pathWasSuspended = false;
            if (pathElapsed >= duration) {
                if (pathLoop) pathElapsed %= duration;
                else { pathElapsed = duration; pathPlaying = false; pathHolding = true; }
            }
            CameraPoint point = CameraPathSampler.Sample(playingPath, pathElapsed);
            position = new Vector3(point.X, point.Y, point.Z); rotation = new Quaternion(point.Qx, point.Qy, point.Qz, point.Qw); viewFov = point.Fov;
            return true;
        }
        void CapturePhoto()
        {
            if (output == null || !output.enabled || !seeded) { Notice("Enable the camera and join before taking a photo."); return; }
            EndRenderScope(); photoInProgress = true;
            try {
                photos.Capture(output, Path.Combine(dataDirectory, "Screenshots"), photoScale,
                    delegate(Camera camera) { photoScope.Begin(player as PlayerCharacter, showHeadThisFrame, camera.transform.position); },
                    delegate { photoScope.End(); });
                Notice(photos.LastStatus);
            } finally { photoScope.End(); photoInProgress = false; }
        }
        void FreezePhoto(bool freeze)
        {
            if (freeze && (output == null || !output.enabled)) { Notice("Enable the camera first."); return; }
            if (freeze) { StopPath(false); frozenPosition = output.transform.position; frozenRotation = output.transform.rotation; transitionActive = false; }
            else BeginTransition();
            frozenPhoto = freeze; avoidanceReset = true;
        }
        void SetCleanCapture(bool value)
        {
            cleanCapture = value;
            if (value) { menu = false; Notice("Clean capture is on. F6 opens settings again."); }
            else if (output != null) output.targetTexture = null;
        }
        void EnsureCleanTarget()
        {
            if (!cleanCapture) { if (output.targetTexture != null) output.targetTexture = null; return; }
            float scale = Mathf.Min(1f, 3840f / Mathf.Max(1, Screen.width), 2160f / Mathf.Max(1, Screen.height));
            int width = Mathf.Max(16, Mathf.RoundToInt(Screen.width * scale)), h = Mathf.Max(16, Mathf.RoundToInt(Screen.height * scale));
            if (cleanTarget == null || cleanTarget.width != width || cleanTarget.height != h) {
                ReleaseCleanTarget();
                cleanTarget = new RenderTexture(width, h, 24, RenderTextureFormat.ARGB32);
                cleanTarget.name = "ATTStreamCleanCapture"; cleanTarget.hideFlags = HideFlags.HideAndDontSave;
                if (!cleanTarget.Create()) { cleanCapture = false; Notice("The clean capture image could not be allocated."); ReleaseCleanTarget(); return; }
                renderedFrame = -10;
            }
            output.targetTexture = cleanTarget;
        }
        void ReleaseCleanTarget()
        {
            if (output != null && output.targetTexture == cleanTarget) output.targetTexture = null;
            if (cleanTarget != null) { cleanTarget.Release(); UnityEngine.Object.Destroy(cleanTarget); cleanTarget = null; }
        }
    }
}
