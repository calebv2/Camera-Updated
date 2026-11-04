using UnityEngine;

namespace ATTSmoothCamera
{
    public sealed partial class StreamCameraMod
    {
        void DrawDesktopHud()
        {
            theme.Ensure();
            int previousDepth = GUI.depth;
            try {
                GUI.depth = -30001;
                if (desktop.Captured && mode == StreamMode.SmoothFirstPerson && output != null && output.enabled) {
                    Color previousColor = GUI.color;
                    try {
                        GUI.color = new Color(.30f, .89f, .84f, .9f);
                        GUI.DrawTexture(new Rect(Screen.width * .5f - 1f, Screen.height * .5f - 4f, 2f, 8f), Texture2D.whiteTexture);
                        GUI.DrawTexture(new Rect(Screen.width * .5f - 4f, Screen.height * .5f - 1f, 8f, 2f), Texture2D.whiteTexture);
                    } finally { GUI.color = previousColor; }
                } else if (!desktop.Captured) {
                    GUI.Label(new Rect(18f, Screen.height - 66f, Mathf.Max(200f, Screen.width - 36f), 58f),
                        desktop.Status + "\nF6 settings  |  Click the game view to resume", theme.Body);
                }
            } finally { GUI.depth = previousDepth; }
        }
        void DrawDesktopTab()
        {
            Heading("DESKTOP PLAY", "Optional flatscreen gameplay with keyboard, mouse and two virtual hands.");
            BeginCard("ENABLE FLATSCREEN CONTROLS");
            bool requested = desktop != null && desktop.Requested;
            bool wanted = DrawToggle(requested, "Enable flatscreen character controls");
            if (wanted != requested) SetDesktopEnabled(wanted);
            GUILayout.Label(desktop == null ? "Desktop controls are unavailable." : desktop.Status, theme.Body);
            GUILayout.Label("Starts off each launch. Join as a normal character; Fly mode has no body or hands. Keep FlatscreenATTMod.dll out of the active Mods folder while using this mode.", theme.Muted);
            GUILayout.Label("This option takes over your character's head and hands. Switch it off here before returning to VR controls.", theme.Body);
            if (desktop != null && desktop.Requested && GUILayout.Button("Play / resume desktop controls", theme.ActiveButton)) ResumeDesktop();
            GUILayout.Label("Play / resume enables the live camera and ends camera-path playback or a held photo shot.", theme.Muted);
            EndCard();

            BeginCard("LOOK + BODY");
            desktopSensitivity.Value = NiceSlider("Mouse sensitivity", desktopSensitivity.Value, .03f, .4f, " deg/px");
            desktopInvertY.Value = DrawToggle(desktopInvertY.Value, "Invert vertical mouse look");
            desktopHeight.Value = NiceSlider("Standing eye height", desktopHeight.Value, 1.2f, 2.1f, " m");
            desktopCrouchHeight.Value = NiceSlider("Crouching eye height", desktopCrouchHeight.Value, .65f, 1.3f, " m");
            GUILayout.Label("WASD walks using the game's locomotion and collisions. Hold Shift to run and Ctrl to crouch. Movement stops while settings are open, the game loses focus or Prefabulator is active.", theme.Body);
            EndCard();

            BeginCard("YOUR HANDS");
            desktopToggleGrips.Value = DrawToggle(desktopToggleGrips.Value, "Toggle grips with Q / E (off = hold the key)");
            desktopReach.Value = NiceSlider("Hand reach", desktopReach.Value, .3f, .85f, " m");
            GUILayout.Label("Q grabs with your left hand; E grabs with your right. Left / right mouse buttons squeeze or use the matching hand. Position your hands next to an object to interact using the game's normal nearby interactions.", theme.Body);
            GUILayout.Label("Hold Z for the left hand or X for the right, then move the mouse to position that hand. Add Alt to rotate it. Scroll adjusts reach; Alt + scroll rolls the selected hand. Mouse look pauses while adjusting a hand.", theme.Muted);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Release both hands", theme.Button) && desktop != null) desktop.ReleaseItems();
            if (GUILayout.Button("Reset hand positions", theme.Button) && desktop != null) desktop.ResetHands();
            GUILayout.EndHorizontal();
            EndCard();

            BeginCard("CONTROLS AT A GLANCE");
            GUILayout.Label("WASD  -  walk\nMouse  -  look\nShift  -  run\nCtrl  -  crouch\nQ / E  -  left / right grip\nLeft / right click  -  left / right squeeze or use\nZ / X + mouse  -  move left / right hand\nAlt + Z / X + mouse  -  rotate that hand\nMouse wheel  -  adjust reach\nAlt + mouse wheel  -  roll selected hand\nR  -  reset hand poses without dropping items\nEscape  -  release mouse and pause controls\nF6  -  open / close settings", theme.Body);
            GUILayout.Label("Close settings, then click to capture the mouse. That first click only resumes controls. Camera views and streaming tools remain on the other tabs.", theme.Muted);
            EndCard();

            if (GUILayout.Button("Save desktop preferences", theme.Button)) Run(delegate {
                cfg.SaveToFile(false);
                Notice("Desktop preferences saved. Enable flatscreen controls when you want to play; they still start off next launch.");
            });
            GUILayout.Label("Desktop play is a preview. Keyboard hands cannot reproduce every VR gesture; some crafting and tool actions need manual hand positioning.", theme.Muted);
        }
    }
}
