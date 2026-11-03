using System;
using System.IO;
using UnityEngine;

namespace ATTSmoothCamera
{
    public sealed partial class StreamCameraMod
    {
        static readonly string[] TabNames = { "Camera", "Presets", "Paths", "Photos", "Help" };
        public override void OnGUI()
        {
            if (!initialized) return;
            if (cleanCapture && cleanTarget != null && output != null && output.enabled && renderedFrame >= Time.frameCount - 1 && Event.current.type == EventType.Repaint) {
                int previousDepth = GUI.depth;
                try { GUI.depth = -30000; GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), cleanTarget, ScaleMode.ScaleToFit, false); }
                finally { GUI.depth = previousDepth; }
            }
            if (!menu) return;
            theme.Ensure();
            window.width = Mathf.Min(640f, Mathf.Max(300f, Screen.width - 32f));
            window.height = Mathf.Min(840f, Mathf.Max(300f, Screen.height - 48f));
            window.x = Mathf.Clamp(window.x, 0f, Mathf.Max(0f, Screen.width - window.width));
            window.y = Mathf.Clamp(window.y, 0f, Mathf.Max(0f, Screen.height - window.height));
            window = GUILayout.Window(734190, window, DrawMenu, "", theme.Window);
        }
        void DrawMenu(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("Stream Camera", theme.Title);
            GUILayout.Label("Desktop streaming controls  |  F6", theme.Subtitle);
            GUILayout.EndVertical();
            if (GUILayout.Button("Close", theme.Button, GUILayout.Width(68f))) menu = false;
            GUILayout.EndHorizontal();
            GUILayout.BeginVertical(theme.Card);
            GUILayout.BeginHorizontal();
            bool wanted = DrawToggle(enabled, "Camera enabled");
            if (wanted != enabled) SetEnabled(wanted);
            GUILayout.FlexibleSpace(); GUILayout.Label(recording ? "RECORDING" : PathHasControl ? "CAMERA PATH" : enabled ? "LIVE VIEW" : "OFF", theme.Heading);
            GUILayout.EndHorizontal();
            GUILayout.Label(status, theme.Body);
            GUILayout.EndVertical();
            GUILayout.Space(10f);
            int tabsPerRow = window.width < 400f ? 2 : 3;
            for (int row = 0; row * tabsPerRow < TabNames.Length; row++) {
                GUILayout.BeginHorizontal();
                for (int i = row * tabsPerRow; i < Mathf.Min(TabNames.Length, (row + 1) * tabsPerRow); i++)
                    if (GUILayout.Button(TabNames[i], tab == i ? theme.ActiveTab : theme.Tab)) { tab = i; menuScroll = Vector2.zero; }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6f);
            menuScroll = GUILayout.BeginScrollView(menuScroll, false, true);
            if (!String.IsNullOrEmpty(notice)) {
                GUILayout.BeginVertical(theme.Card); GUILayout.Label(notice, theme.Body);
                if (GUILayout.Button("Dismiss", theme.Button, GUILayout.Width(100f))) notice = "";
                GUILayout.EndVertical();
            }
            switch (tab) {
                case 0: DrawCameraTab(); break;
                case 1: DrawPresetsTab(); break;
                case 2: DrawPathsTab(); break;
                case 3: DrawPhotosTab(); break;
                default: DrawHelpTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("F6 opens or closes all camera controls", theme.Muted);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save startup view", theme.Button)) Run(delegate { startupMode.Value = mode; cfg.SaveToFile(false); Notice("Current camera settings saved for your next launch."); });
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0f, 0f, window.width - 90f, 45f));
        }
        void Heading(string title, string description)
        {
            GUILayout.Label(title, theme.Heading);
            if (!String.IsNullOrEmpty(description)) GUILayout.Label(description, theme.Muted);
        }
        void BeginCard(string title) { GUILayout.BeginVertical(theme.Card); if (!String.IsNullOrEmpty(title)) GUILayout.Label(title, theme.Heading); }
        void EndCard() { GUILayout.EndVertical(); GUILayout.Space(8f); }
        bool ActionButton(string text, bool active) { return GUILayout.Button(text, active ? theme.ActiveButton : theme.Button); }
        bool DrawToggle(bool value, string label)
        {
            float rowHeight = Mathf.Max(34f, theme.ToggleLabel.CalcHeight(new GUIContent(label), Mathf.Max(140f, window.width - 104f)) + 12f);
            Rect row = GUILayoutUtility.GetRect(1f, rowHeight, GUILayout.ExpandWidth(true));
            bool clicked = GUI.Button(row, "", value ? theme.ActiveButton : theme.Toggle);
            Color previousColor = GUI.color;
            try {
                if (!GUI.enabled) GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * .5f);
                GUI.DrawTexture(new Rect(row.x + 8f, row.y + (row.height - 20f) * .5f, 20f, 20f), value ? theme.ToggleOnIcon : theme.ToggleOffIcon);
                GUI.Label(new Rect(row.x + 38f, row.y + 4f, row.width - 46f, row.height - 8f), label, theme.ToggleLabel);
            } finally { GUI.color = previousColor; }
            return clicked ? !value : value;
        }
        float NiceSlider(string label, float value, float min, float max, string unit)
        {
            value = Safe(value, min, max, min);
            GUILayout.BeginHorizontal(); GUILayout.Label(label, theme.Body); GUILayout.FlexibleSpace();
            GUILayout.Label(value.ToString("0.00") + unit, theme.Muted, GUILayout.Width(85f)); GUILayout.EndHorizontal();
            return GUILayout.HorizontalSlider(value, min, max, theme.Slider, theme.Thumb);
        }
        void DrawCameraTab()
        {
            Heading("CHOOSE YOUR SHOT", "Switching views uses a smooth camera transition.");
            for (int row = 0; row < 2; row++) {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++) { StreamMode shot = (StreamMode)(row * 2 + col); if (ActionButton(ModeName(shot), mode == shot && !PathHasControl && !frozenPhoto)) ChangeMode(shot); }
                GUILayout.EndHorizontal();
            }
            if (ActionButton("Crafting close-up  |  Frame your hands and held tools", mode == StreamMode.Crafting && !PathHasControl)) ChangeMode(StreamMode.Crafting);
            GUILayout.Space(10f);
            BeginCard("FRAMING");
            smartFraming.Value = DrawToggle(smartFraming.Value, "Smart framing - include head, body, hands and held tools");
            horizonLock.Value = DrawToggle(horizonLock.Value, "Level horizon in smooth first-person");
            avoidWalls.Value = DrawToggle(avoidWalls.Value, "Avoid walls - find a clear angle and recover smoothly");
            distance.Value = NiceSlider("Follow distance", distance.Value, .7f, 12f, " m");
            height.Value = NiceSlider("Height above head", height.Value, -.4f, 3f, " m");
            shoulder.Value = NiceSlider("Shoulder offset", shoulder.Value, -3f, 3f, " m");
            fov.Value = NiceSlider("Field of view", fov.Value, 35f, 110f, " deg");
            if (smartFraming.Value) GUILayout.Label("Distance is a minimum. Smart framing can pull back to fit your pose and tools.", theme.Muted);
            EndCard();
            BeginCard("MOTION");
            positionHalfLife.Value = NiceSlider("Position smoothing", positionHalfLife.Value, .01f, 2f, " s");
            rotationHalfLife.Value = NiceSlider("Look smoothing", rotationHalfLife.Value, .01f, 2f, " s");
            transitionSeconds.Value = NiceSlider("View transition", transitionSeconds.Value, .05f, 3f, " s");
            orbitSpeed.Value = NiceSlider("Orbit speed", orbitSpeed.Value, -90f, 90f, " deg/s");
            GUILayout.Label("Larger smoothing values make camera movement slower and softer.", theme.Muted);
            EndCard();
            BeginCard("CLEAN CAPTURE");
            GUILayout.Label("Hide desktop menus and overlays behind a clean copy of the camera image. F6 brings settings back.", theme.Body);
            if (GUILayout.Button("Start clean capture", theme.ActiveButton)) SetCleanCapture(true);
            EndCard();
            if (GUILayout.Button("Reset camera defaults", theme.Button)) {
                ChangeMode(StreamMode.SmoothFirstPerson); distance.Value = 2.8f; height.Value = .15f; shoulder.Value = .4f;
                fov.Value = 75f; positionHalfLife.Value = .08f; rotationHalfLife.Value = .12f; orbitSpeed.Value = 12f;
                horizonLock.Value = true; smartFraming.Value = true; avoidWalls.Value = true; transitionSeconds.Value = .65f;
            }
        }
        void DrawPresetsTab()
        {
            Heading("YOUR CAMERA PRESETS", "Save a complete shot and load it with one click.");
            BeginCard("SAVE THIS VIEW");
            presetName = GUILayout.TextField(presetName, 48, theme.TextField);
            if (GUILayout.Button("Save / replace preset", theme.ActiveButton)) Run(SavePreset);
            EndCard();
            for (int i = 0; i < library.Presets.Count; i++) {
                int index = i; CameraPreset preset = library.Presets[index];
                BeginCard(preset.Name);
                GUILayout.Label(ModeName((StreamMode)preset.Mode) + "  |  " + preset.Distance.ToString("0.0") + " m  |  " + preset.Fov.ToString("0") + " deg", theme.Muted);
                GUILayout.BeginHorizontal();
                if (ActionButton("Load preset", selectedPreset == index)) { selectedPreset = index; Run(delegate { ApplyPreset(preset); }); }
                if (GUILayout.Button("Delete", theme.DangerButton, GUILayout.Width(90f))) { Run(delegate { library.Presets.RemoveAt(index); library.Save(); }); GUILayout.EndHorizontal(); EndCard(); break; }
                GUILayout.EndHorizontal(); EndCard();
            }
            if (library.Presets.Count == 0) GUILayout.Label("No saved presets yet. Set up a camera on the Camera tab, then save it here.", theme.Body);
        }
        void DrawPathsTab()
        {
            Heading("CAMERA PATHS", "Record a tour or add individual shots. Playback moves only the desktop camera.");
            BeginCard("BUILD A PATH");
            GUILayout.Label("Move in VR or change camera views and sliders to set the next shot. Keep your player near the route so the world is loaded.", theme.Body);
            GUILayout.BeginHorizontal();
            if (ActionButton(recording ? "Stop recording" : "Record movement", recording)) { if (recording) StopRecording(); else StartRecording(); }
            if (GUILayout.Button("Add current shot", theme.Button)) AddKeyframe();
            GUILayout.EndHorizontal();
            GUILayout.Label(draft.Count + " points  |  " + (draft.Count == 0 ? "0.0" : draft[draft.Count - 1].Time.ToString("0.0")) + " seconds captured", theme.Muted);
            keyframeSpacing = NiceSlider("Manual shot spacing", keyframeSpacing, .5f, 10f, " s");
            pathName = GUILayout.TextField(pathName, 48, theme.TextField);
            if (GUILayout.Button("Save / replace path", theme.ActiveButton)) Run(SavePath);
            if (GUILayout.Button("Clear unsaved points", theme.Button)) { recording = false; draft.Clear(); }
            EndCard();
            BeginCard("PLAYBACK");
            pathLoop = DrawToggle(pathLoop, "Loop playback (match the first and last shot for a seamless loop)");
            pathSpeed = NiceSlider("Playback speed", pathSpeed, .25f, 3f, " x");
            if (PathHasControl) {
                GUILayout.Label(PathStatus, theme.Body);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(pathHolding ? "Replay" : pathPaused ? "Resume" : "Pause", theme.Button)) { if (pathHolding) PlayPath(playingPath); else pathPaused = !pathPaused; }
                if (GUILayout.Button("Return to my player", theme.ActiveButton)) StopPath(true);
                GUILayout.EndHorizontal();
            }
            EndCard();
            for (int i = 0; i < library.Paths.Count; i++) {
                int index = i; CameraPath path = library.Paths[index];
                BeginCard(path.Name);
                GUILayout.Label(path.Points.Count + " points  |  " + CameraPathSampler.Duration(path).ToString("0.0") + " s", theme.Muted);
                GUILayout.BeginHorizontal();
                if (ActionButton("Play path", selectedPath == index && PathHasControl)) { selectedPath = index; Run(delegate { PlayPath(path); }); }
                if (GUILayout.Button("Delete", theme.DangerButton, GUILayout.Width(90f))) { Run(delegate { if (playingPath != null && playingPath.Name == path.Name) StopPath(true); library.Paths.RemoveAt(index); library.Save(); }); GUILayout.EndHorizontal(); EndCard(); break; }
                GUILayout.EndHorizontal(); EndCard();
            }
        }
        void DrawPhotosTab()
        {
            Heading("PHOTO STUDIO", "Full-resolution PNG images with no camera menu in the shot.");
            BeginCard("FRAME YOUR PHOTO");
            bool hold = DrawToggle(frozenPhoto, "Hold the desktop camera in place while I pose in VR");
            if (hold != frozenPhoto) FreezePhoto(hold);
            fov.Value = NiceSlider("Field of view", fov.Value, 35f, 110f, " deg");
            GUILayout.Label("Choose a camera view first. Holding the camera does not freeze the game or your character.", theme.Muted);
            EndCard();
            BeginCard("CAPTURE");
            GUILayout.Label("Resolution multiplier", theme.Body);
            GUILayout.BeginHorizontal();
            for (int i = 1; i <= 4; i++) if (ActionButton(i + "x", photoScale == i)) photoScale = i;
            GUILayout.EndHorizontal();
            GUILayout.Label("Large photos are limited to about 33 megapixels to control memory use.", theme.Muted);
            if (GUILayout.Button("Take photo", theme.ActiveButton)) photoRequested = true;
            GUILayout.Label(photos.LastStatus, theme.Body);
            GUILayout.Label("Saved to: " + Path.Combine(dataDirectory, "Screenshots"), theme.Muted);
            EndCard();
        }
        void DrawHelpTab()
        {
            Heading("READY TO STREAM", "F6 is the only shortcut used by this mod.");
            BeginCard("GET STARTED");
            GUILayout.Label("1. Enable the camera and select a shot.\n2. In OBS, capture the A Township Tale desktop window.\n3. Start clean capture to hide desktop overlays.\n4. Press F6 whenever you need the controls again.", theme.Body);
            EndCard();
            BeginCard("WHAT STAYS YOURS");
            GUILayout.Label("The headset view, character controls and server state stay native. The camera follows only your character. No server install or console token is needed.", theme.Body);
            EndCard();
            BeginCard("CAMERA PATHS");
            GUILayout.Label("Paths save camera movement, not a recording of the game. Other players and objects remain live. Stay near the route to keep its world chunks loaded. Teleporting or changing characters stops playback and recording.", theme.Body);
            EndCard();
            BeginCard("YOUR FILES");
            GUILayout.Label("Presets and paths are kept between sessions in:\n" + dataDirectory + "\n\nThe library keeps a previous copy when saved.", theme.Muted);
            EndCard();
        }
    }
}
