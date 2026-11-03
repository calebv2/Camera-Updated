using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using Alta.Character;

[assembly: MelonInfo(typeof(ATTSmoothCamera.StreamCameraMod), "ATT Smooth Stream Camera", "0.2.1", "VManOP")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace ATTSmoothCamera
{
    public enum StreamMode { SmoothFirstPerson, FollowBehind, FollowFront, Orbit, Crafting }

    public sealed partial class StreamCameraMod : MelonMod
    {
        MelonPreferences_Category cfg;
        MelonPreferences_Entry<bool> enabledPref, avoidWalls, horizonLock, smartFraming;
        MelonPreferences_Entry<StreamMode> startupMode;
        MelonPreferences_Entry<float> distance, height, shoulder, fov, positionHalfLife, rotationHalfLife, orbitSpeed, snapDistance, transitionSeconds;
        Camera output, source;
        PlayerController player;
        readonly HeadRenderScope headScope = new HeadRenderScope();
        readonly HeadRenderScope photoScope = new HeadRenderScope();
        readonly CameraAvoidance obstacles = new CameraAvoidance();
        readonly StreamPhotoCapture photos = new StreamPhotoCapture();
        readonly StreamUiTheme theme = new StreamUiTheme();
        CameraLibrary library;
        string dataDirectory;
        bool initialized, enabled, menu, seeded, mirrorOwned, mirrorBefore, showHeadThisFrame, headWarning, photoInProgress;
        bool renderingOutput, transitionActive, photoRequested, cleanCapture, frozenPhoto, avoidanceReset = true;
        int nestedCameras, photoScale = 2, renderedFrame = -10;
        StreamMode mode;
        Vector3 lastHead, transitionPosition, frozenPosition;
        Quaternion transitionRotation, frozenRotation;
        float smoothYaw, orbitAngle, nextProbe, nextError, transitionStart, transitionFov;
        Camera[] cameras = new Camera[0];
        RenderTexture cleanTarget;
        string status = "Waiting for your player...", notice = "";
        Rect window = new Rect(24, 30, 660, 760);
        Vector2 menuScroll;
        int tab;
        Renderer[] leftTool = new Renderer[0], rightTool = new Renderer[0];
        float nextTools;

        public override void OnInitializeMelon()
        {
            if (Application.isBatchMode) return;
            cfg = MelonPreferences.CreateCategory("ATTSmoothCamera");
            enabledPref = cfg.CreateEntry<bool>("Enabled", true);
            startupMode = cfg.CreateEntry<StreamMode>("StartupMode", StreamMode.SmoothFirstPerson);
            distance = cfg.CreateEntry<float>("FollowDistance", 2.8f);
            height = cfg.CreateEntry<float>("HeightAboveHead", 0.15f);
            shoulder = cfg.CreateEntry<float>("ShoulderOffset", 0.4f);
            fov = cfg.CreateEntry<float>("FieldOfView", 75f);
            positionHalfLife = cfg.CreateEntry<float>("PositionHalfLife", 0.08f);
            rotationHalfLife = cfg.CreateEntry<float>("RotationHalfLife", 0.12f);
            orbitSpeed = cfg.CreateEntry<float>("OrbitDegreesPerSecond", 12f);
            snapDistance = cfg.CreateEntry<float>("TeleportSnapDistance", 5f);
            transitionSeconds = cfg.CreateEntry<float>("TransitionSeconds", 0.65f);
            avoidWalls = cfg.CreateEntry<bool>("AvoidWalls", true);
            horizonLock = cfg.CreateEntry<bool>("HorizonLock", true);
            smartFraming = cfg.CreateEntry<bool>("SmartFraming", true);
            dataDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UserData/ATTSmoothCamera");
            bool newLibrary = !File.Exists(Path.Combine(dataDirectory, "camera-library.xml"));
            library = CameraLibrary.Load(dataDirectory);
            if (!newLibrary) Notice(library.Status);
            enabled = enabledPref.Value;
            mode = ValidMode(startupMode.Value);
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
            initialized = true;
            MelonLogger.Msg("Streaming camera 0.2.1 ready. F6 opens every camera control. No other camera hotkeys are registered.");
            if (newLibrary) Run(delegate {
                library.UpsertPreset(new CameraPreset { Name = "Smooth view", Mode = 0 });
                library.UpsertPreset(new CameraPreset { Name = "Full body", Mode = 1, Distance = 3.2f });
                library.UpsertPreset(new CameraPreset { Name = "Crafting close-up", Mode = 4, Fov = 65f });
                library.Save();
            });
        }

        public override void OnUpdate()
        {
            if (!initialized) return;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[Key.F6].wasPressedThisFrame) {
                if (cleanCapture) { SetCleanCapture(false); menu = true; }
                else menu = !menu;
            }
        }

        public override void OnLateUpdate()
        {
            if (!initialized) return;
            EndRenderScope();
            try {
                Tick();
                if (photoRequested) { photoRequested = false; CapturePhoto(); }
            } catch (Exception error) {
                ReleaseOutput();
                status = "Camera paused: " + error.GetType().Name;
                if (Time.unscaledTime >= nextError) { nextError = Time.unscaledTime + 10f; MelonLogger.Warning(status + ": " + error.Message); }
            }
        }

        void Tick()
        {
            if (!enabled) { ReleaseOutput(); status = "Camera off. Enable it in F6 settings."; return; }
            PlayerController current = PlayerController.Current;
            if (current == null || !current.IsLocalPlayer || current.Head == null || !current.IsAlive) {
                ReleaseOutput(); player = null; status = "Waiting for your living player..."; return;
            }
            bool changed = player != current, sourceChanged = false;
            bool teleported = seeded && CameraMath.ShouldSnap((current.Head.position - lastHead).sqrMagnitude,
                Safe(snapDistance.Value, 1f, 100f, 5f), changed);
            if (teleported) {
                avoidanceReset = true;
                if (recording) { StopRecording(); Notice("Recording stopped after a teleport or character change. Save the captured points on Paths."); }
                StopPath(false); frozenPhoto = false;
            }
            if (changed || Time.unscaledTime >= nextProbe) {
                player = current;
                if (changed) { leftTool = new Renderer[0]; rightTool = new Renderer[0]; nextTools = 0f; }
                Camera nextSource = FindSource(current);
                sourceChanged = source != nextSource;
                source = nextSource;
                cameras = Camera.allCameras;
                nextProbe = Time.unscaledTime + 0.5f;
            }
            if (EditorCameraActive()) {
                if (output != null) output.enabled = false;
                seeded = false;
                PausePathClock();
                status = "Streaming pauses while the Prefabulator editor camera is active.";
                return;
            }
            if (source == null || !source.gameObject.activeInHierarchy) { ReleaseOutput(); status = "Waiting for your player camera..."; return; }
            if (mode != StreamMode.SmoothFirstPerson && !(current is PlayerCharacter) && !PathHasControl) {
                ReleaseOutput(); status = "This view needs a VR character. Fly has no visible avatar."; return;
            }
            if (sourceChanged) seeded = false;
            EnsureOutput(changed || sourceChanged);
            if (!mirrorOwned) { mirrorBefore = XRSettings.showDeviceView; mirrorOwned = true; }
            XRSettings.showDeviceView = false;
            Vector3 head = current.Head.position;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            bool snap = !seeded || CameraMath.ShouldSnap((head - lastHead).sqrMagnitude, Safe(snapDistance.Value, 1f, 100f, 5f), changed);
            if (snap) transitionActive = false;
            bool firstPerson = mode == StreamMode.SmoothFirstPerson;
            Vector3 pivot = head;
            Vector3 desired;
            Quaternion rotation;
            float wantedFov = Safe(fov.Value, 35f, 110f, 75f);
            bool initialPathCut = pathWasSuspended;
            bool pathFrame = TryPathPose(dt, out desired, out rotation, out wantedFov);
            if (pathFrame) { firstPerson = (head - desired).sqrMagnitude < .35f * .35f; transitionActive = false; }
            else if (frozenPhoto) { desired = frozenPosition; rotation = frozenRotation; firstPerson = (head - desired).sqrMagnitude < .35f * .35f; }
            else if (firstPerson) {
                desired = head;
                rotation = horizonLock.Value ? LevelRotation(current.Head.rotation) : current.Head.rotation;
            } else {
                float yaw = current.Head.eulerAngles.y;
                smoothYaw = snap ? yaw : Mathf.LerpAngle(smoothYaw, yaw, CameraMath.Blend(Safe(rotationHalfLife.Value, .01f, 2f, .12f), dt));
                if (mode == StreamMode.Orbit) orbitAngle = CameraMath.OrbitAngle(orbitAngle, Safe(orbitSpeed.Value, -90f, 90f, 12f), dt);
                float viewYaw = smoothYaw + (mode == StreamMode.FollowFront ? 180f : 0f) + (mode == StreamMode.Orbit ? orbitAngle : 0f);
                Quaternion heading = Quaternion.Euler(0f, viewYaw, 0f);
                if (mode == StreamMode.Crafting) {
                    Bounds work = WorkBounds(current);
                    pivot = work.center;
                    float range = Mathf.Clamp(work.extents.magnitude / Mathf.Max(.1f, Mathf.Sin(wantedFov * Mathf.Deg2Rad * .5f)) * 1.2f, .7f, 3f);
                    desired = pivot - heading * Vector3.forward * range + heading * Vector3.right * .55f + Vector3.up * .45f;
                } else {
                    Bounds frame = SubjectBounds(current);
                    pivot = smartFraming.Value ? frame.center : head + Vector3.down * .3f;
                    float range = Safe(distance.Value, .7f, 12f, 2.8f);
                    if (smartFraming.Value) {
                        float aspect = Mathf.Max(.2f, (float)Screen.width / Mathf.Max(1, Screen.height));
                        float halfAngle = Mathf.Atan(Mathf.Tan(wantedFov * Mathf.Deg2Rad * .5f) * Mathf.Min(1f, aspect));
                        range = Mathf.Max(range, frame.extents.magnitude * 1.12f / Mathf.Max(.1f, Mathf.Sin(halfAngle)));
                    }
                    desired = new Vector3(pivot.x, head.y + Safe(height.Value, -.4f, 3f, .15f), pivot.z)
                        - heading * Vector3.forward * Mathf.Min(range, 18f)
                        + heading * Vector3.right * Safe(shoulder.Value, -3f, 3f, .4f);
                }
                if (avoidWalls.Value) { desired = obstacles.Resolve(current, pivot, desired, dt, snap || avoidanceReset); avoidanceReset = false; }
                Vector3 look = pivot - desired;
                if (look.sqrMagnitude < .35f * .35f) {
                    desired = head; rotation = horizonLock.Value ? LevelRotation(current.Head.rotation) : current.Head.rotation;
                    firstPerson = true; snap = true;
                } else rotation = Quaternion.LookRotation(look, Vector3.up);
            }
            if (!Finite(desired) || !Finite(rotation)) { StopPath(false); ReleaseOutput(); status = "Camera paused: invalid saved pose."; return; }
            Vector3 next;
            Quaternion facing;
            float nextFov;
            if (transitionActive && !snap && !pathFrame) {
                float t = Mathf.Clamp01((Time.unscaledTime - transitionStart) / Safe(transitionSeconds.Value, .05f, 3f, .65f));
                float blend = t * t * (3f - 2f * t);
                next = Vector3.Lerp(transitionPosition, desired, blend);
                facing = Quaternion.Slerp(transitionRotation, rotation, blend);
                nextFov = Mathf.Lerp(transitionFov, wantedFov, blend);
                if (t >= 1f) transitionActive = false;
            } else {
                float a = snap || pathFrame || frozenPhoto ? 1f : CameraMath.Blend(Safe(positionHalfLife.Value, .01f, 2f, .08f), dt);
                float b = snap || pathFrame || frozenPhoto ? 1f : CameraMath.Blend(Safe(rotationHalfLife.Value, .01f, 2f, .12f), dt);
                next = Vector3.Lerp(output.transform.position, desired, a);
                facing = Quaternion.Slerp(output.transform.rotation, rotation, b);
                nextFov = Mathf.Lerp(output.fieldOfView, wantedFov, b);
            }
            if (!firstPerson && !pathFrame && !frozenPhoto && avoidWalls.Value) {
                next = obstacles.Clamp(current, pivot, next);
                if ((next - pivot).sqrMagnitude < .35f * .35f) { next = head; facing = horizonLock.Value ? LevelRotation(current.Head.rotation) : current.Head.rotation; firstPerson = true; }
            }
            if (pathFrame && !initialPathCut && seeded && avoidWalls.Value) {
                Vector3 limited = obstacles.Clamp(current, output.transform.position, next);
                if ((limited - next).sqrMagnitude > .01f) {
                    StopPath(false); frozenPhoto = true;
                    frozenPosition = output.transform.position; frozenRotation = output.transform.rotation;
                    next = frozenPosition; facing = frozenRotation;
                    Notice("Path stopped by an obstacle. The camera is held safely; choose a view to return to your player.");
                    firstPerson = (head - next).sqrMagnitude < .35f * .35f;
                }
            }
            bool settledFirstPerson = mode == StreamMode.SmoothFirstPerson && !transitionActive && !pathFrame && !frozenPhoto;
            showHeadThisFrame = !settledFirstPerson && (next - head).sqrMagnitude >= .45f * .45f;
            output.transform.SetPositionAndRotation(next, facing);
            output.fieldOfView = Safe(nextFov, 35f, 110f, 75f);
            EnsureCleanTarget();
            output.enabled = true;
            lastHead = head;
            seeded = true;
            if (recording) RecordFrame();
            status = PathHasControl ? PathStatus : frozenPhoto ? "Photo camera held in place. You can pose in VR." :
                ModeName(mode) + (firstPerson && mode != StreamMode.SmoothFirstPerson ? " - tight-space first-person fallback" : " - live");
        }

        Camera FindSource(PlayerController current)
        {
            if (current.Camera != null && current.Camera != output && current.Camera.gameObject.activeInHierarchy) return current.Camera;
            Camera nearest = null; float best = Single.MaxValue;
            foreach (Camera camera in Camera.allCameras) {
                if (camera == null || camera == output || !camera.stereoEnabled || camera.targetTexture != null) continue;
                float squared = (camera.transform.position - current.Head.position).sqrMagnitude;
                if (squared < best) { nearest = camera; best = squared; }
            }
            return nearest;
        }
        bool EditorCameraActive(bool refresh = false)
        {
            if (refresh) cameras = Camera.allCameras;
            foreach (Camera camera in cameras)
                if (camera != null && camera.enabled && camera.gameObject.activeInHierarchy && camera.name == "PrefabEditorModCam") return true;
            return false;
        }
        void EnsureOutput(bool changed)
        {
            if (output == null) {
                GameObject go = new GameObject("ATTSmoothStreamCamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                output = go.AddComponent<Camera>(); output.enabled = false; changed = true;
            }
            if (!changed && seeded) return;
            output.CopyFrom(source);
            output.enabled = false;
            output.stereoTargetEye = StereoTargetEyeMask.None;
            output.targetTexture = null;
            output.targetDisplay = 0;
            output.rect = new Rect(0f, 0f, 1f, 1f);
            output.depth = 90f;
            output.clearFlags = source.clearFlags == CameraClearFlags.Depth || source.clearFlags == CameraClearFlags.Nothing ? CameraClearFlags.Skybox : source.clearFlags;
            output.nearClipPlane = .03f;
            output.layerCullDistances = new float[32]; output.layerCullSpherical = false;
            output.useOcclusionCulling = false;
            output.ResetProjectionMatrix(); output.ResetWorldToCameraMatrix(); output.ResetCullingMatrix();
            if (changed) MelonLogger.Msg("Streaming source: " + source.name + ". Own-avatar LOD preservation active.");
        }

        Bounds SubjectBounds(PlayerController subject)
        {
            Vector3 head = subject.Head.position;
            Vector3 feet = subject.PlayerFeetPosition;
            if (!Finite(feet) || (feet - head).sqrMagnitude > 25f) feet = head + Vector3.down * 1.65f;
            Bounds bounds = new Bounds((head + feet) * .5f, new Vector3(.55f, Mathf.Max(1f, head.y - feet.y + .3f), .45f));
            AddHand(ref bounds, subject.LeftController); AddHand(ref bounds, subject.RightController);
            AddTools(ref bounds, subject);
            return bounds;
        }
        Bounds WorkBounds(PlayerController subject)
        {
            Vector3 head = subject.Head.position;
            Vector3 center = head + subject.Head.forward * .5f + Vector3.down * .55f;
            bool left = ValidHand(subject.LeftController, head), right = ValidHand(subject.RightController, head);
            if (left && right) center = (subject.LeftController.transform.position + subject.RightController.transform.position) * .5f;
            else if (left) center = subject.LeftController.transform.position;
            else if (right) center = subject.RightController.transform.position;
            Bounds bounds = new Bounds(center, Vector3.one * .3f);
            AddHand(ref bounds, subject.LeftController); AddHand(ref bounds, subject.RightController);
            AddTools(ref bounds, subject);
            return bounds;
        }
        static bool ValidHand(Controller hand, Vector3 head)
        {
            return hand != null && Finite(hand.transform.position) && (hand.transform.position - head).sqrMagnitude < 16f;
        }
        void AddHand(ref Bounds bounds, Controller hand)
        {
            if (ValidHand(hand, player.Head.position))
                bounds.Encapsulate(new Bounds(hand.transform.position, Vector3.one * .18f));
        }
        void AddTools(ref Bounds bounds, PlayerController subject)
        {
            if (Time.unscaledTime >= nextTools) {
                leftTool = HeldRenderers(subject.LeftController); rightTool = HeldRenderers(subject.RightController);
                nextTools = Time.unscaledTime + .2f;
            }
            AddRenderers(ref bounds, leftTool, subject.Head.position); AddRenderers(ref bounds, rightTool, subject.Head.position);
        }
        static Renderer[] HeldRenderers(Controller hand)
        {
            if (hand == null || hand.Interactor == null || hand.Interactor.InteractingWith == null) return new Renderer[0];
            Interactable held = hand.Interactor.InteractingWith;
            Alta.Networking.NetworkPrefab root = held.GetComponentInParent<Alta.Networking.NetworkPrefab>();
            return root != null ? root.GetComponentsInChildren<Renderer>() : held.GetComponentsInChildren<Renderer>();
        }
        static void AddRenderers(ref Bounds bounds, Renderer[] renderers, Vector3 head)
        {
            foreach (Renderer renderer in renderers) {
                if (renderer == null || !renderer.enabled) continue;
                Bounds item = renderer.bounds;
                if (Finite(item.center) && Finite(item.extents) && item.extents.sqrMagnitude < 36f && (item.center - head).sqrMagnitude < 36f) bounds.Encapsulate(item);
            }
        }
        static Quaternion LevelRotation(Quaternion value)
        {
            Vector3 forward = value * Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > .995f) return Quaternion.Euler(value.eulerAngles.x, value.eulerAngles.y, 0f);
            return Quaternion.LookRotation(forward, Vector3.up);
        }
        void BeginTransition()
        {
            if (output == null || !seeded || !output.enabled) return;
            transitionPosition = output.transform.position; transitionRotation = output.transform.rotation; transitionFov = output.fieldOfView;
            transitionStart = Time.unscaledTime; transitionActive = true;
        }
        void ChangeMode(StreamMode value)
        {
            BeginTransition(); StopPath(false); frozenPhoto = false;
            mode = ValidMode(value); orbitAngle = 0f; avoidanceReset = true; SetEnabled(true);
        }
        void SetEnabled(bool value)
        {
            if (value && !enabled) avoidanceReset = true;
            enabled = value; enabledPref.Value = value;
            if (!value) { if (recording) StopRecording(); StopPath(false); ReleaseOutput(); }
        }
        static StreamMode ValidMode(StreamMode value) { return (int)value >= 0 && (int)value < 5 ? value : StreamMode.SmoothFirstPerson; }
        static float Safe(float value, float min, float max, float fallback) { return Single.IsNaN(value) || Single.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max); }
        static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        static bool Finite(Quaternion value) { return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w); }
        static string ModeName(StreamMode value) { return new string[] { "Smooth first-person", "Behind", "Front", "Orbit", "Crafting close-up" }[(int)ValidMode(value)]; }
        void Run(Action action) { try { action(); } catch (Exception error) { Notice(error.Message); MelonLogger.Warning("Camera control: " + error.Message); } }
        void Notice(string message) { notice = message; }

        void ApplyHeadScope()
        {
            try { headScope.Begin(player as PlayerCharacter, showHeadThisFrame, output.transform.position); }
            catch (Exception error) { headScope.End(); if (!headWarning) { headWarning = true; MelonLogger.Warning("Avatar render scope: " + error.Message); } }
        }
        void BeforeCamera(Camera camera)
        {
            if (photoInProgress) return;
            if (camera == output && output != null && output.enabled && enabled) { EndRenderScope(); renderingOutput = true; ApplyHeadScope(); }
            else if (renderingOutput) { headScope.End(); nestedCameras++; }
            else headScope.End();
        }
        void AfterCamera(Camera camera)
        {
            if (photoInProgress) return;
            if (camera == output) { renderedFrame = Time.frameCount; EndRenderScope(); }
            else if (renderingOutput && nestedCameras > 0) { nestedCameras--; if (nestedCameras == 0) ApplyHeadScope(); }
        }
        void EndRenderScope() { headScope.End(); renderingOutput = false; nestedCameras = 0; }
        void ReleaseOutput()
        {
            EndRenderScope();
            if (output != null) output.enabled = false;
            if (mirrorOwned && !EditorCameraActive(true)) { XRSettings.showDeviceView = mirrorBefore; mirrorOwned = false; }
            seeded = false; transitionActive = false;
            PausePathClock();
        }
        public override void OnDeinitializeMelon()
        {
            if (!initialized) return;
            Camera.onPreCull -= BeforeCamera; Camera.onPostRender -= AfterCamera;
            ReleaseOutput(); photoScope.End();
            if (mirrorOwned) {
                GameObject lease = new GameObject("ATTStreamCameraMirrorRestore"); UnityEngine.Object.DontDestroyOnLoad(lease);
                lease.AddComponent<MirrorRestoreLease>().PreviousValue = mirrorBefore; mirrorOwned = false;
            }
            ReleaseCleanTarget();
            if (output != null) UnityEngine.Object.Destroy(output.gameObject);
            theme.Dispose(); initialized = false;
        }
    }
    public sealed class MirrorRestoreLease : MonoBehaviour
    {
        public bool PreviousValue;
        void Update()
        {
            foreach (Camera camera in Camera.allCameras)
                if (camera != null && camera.enabled && camera.name == "PrefabEditorModCam") return;
            XRSettings.showDeviceView = PreviousValue; UnityEngine.Object.Destroy(gameObject);
        }
    }
}
