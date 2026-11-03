using System;
using System.Collections.Generic;
using UnityEngine;

namespace ATTSmoothCamera
{
    // Each style is cloned from the current skin: no global GUI.skin styles or
    // textures belonging to Unity / another mod are changed or destroyed.
    internal sealed class StreamUiTheme : IDisposable
    {
        public GUIStyle Window, Title, Subtitle, Heading, Body, Muted, Card;
        public GUIStyle Button, ActiveButton, DangerButton, Tab, ActiveTab;
        public GUIStyle TextField, Toggle, ToggleLabel, Slider, Thumb;
        public Texture2D ToggleOffIcon, ToggleOnIcon;

        readonly List<Texture2D> owned = new List<Texture2D>();
        readonly Dictionary<Color, Texture2D> solids = new Dictionary<Color, Texture2D>();
        bool ready;

        // Same palette as Prefabulator 1.0.18's in-game editor.
        static readonly Color Canvas = new Color(0.055f, 0.075f, 0.09f, 0.97f);
        static readonly Color Surface = new Color(0.075f, 0.10f, 0.12f, 0.98f);
        static readonly Color Control = new Color(0.12f, 0.16f, 0.19f, 1f);
        static readonly Color Hover = new Color(0.19f, 0.26f, 0.29f, 1f);
        static readonly Color Pressed = new Color(0.09f, 0.31f, 0.32f, 1f);
        static readonly Color Ivory = new Color(0.88f, 0.91f, 0.93f, 1f);
        static readonly Color Subdued = new Color(0.60f, 0.67f, 0.71f, 1f);
        static readonly Color Selected = new Color(0.08f, 0.30f, 0.30f, 1f);
        static readonly Color SelectedText = new Color(0.30f, 0.89f, 0.84f, 1f);
        static readonly Color Gold = new Color(0.88f, 0.75f, 0.51f, 1f);
        static readonly Color Field = new Color(0.035f, 0.05f, 0.065f, 1f);

        // GUI.skin may only be read during OnGUI. Call this there, before using
        // any field; subsequent calls allocate nothing.
        public void Ensure()
        {
            if (ready) return;
            GUISkin skin = GUI.skin;
            Window = Box(skin.window, Canvas, Canvas, Canvas, Ivory);
            Window.padding = new RectOffset(18, 18, 16, 18);
            Window.margin = new RectOffset(0, 0, 0, 0);
            Window.border = new RectOffset(0, 0, 0, 0);
            Window.fontSize = 1; // Caller draws its own title with the Title style.
            Window.contentOffset = Vector2.zero;

            Title = Label(skin.label, Gold, 18, FontStyle.Bold);
            Title.margin = new RectOffset(0, 0, 0, 5);
            Subtitle = Label(skin.label, Subdued, 11, FontStyle.Normal);
            Subtitle.margin = new RectOffset(0, 0, 0, 8);
            Heading = Label(skin.label, Gold, 14, FontStyle.Bold);
            Heading.margin = new RectOffset(0, 0, 11, 6);
            Body = Label(skin.label, Ivory, 13, FontStyle.Normal);
            Muted = Label(skin.label, Subdued, 11, FontStyle.Normal);

            Card = Box(skin.box, Surface, Surface, Surface, Ivory);
            Card.padding = new RectOffset(13, 13, 11, 11);
            Card.margin = new RectOffset(0, 0, 4, 7);
            Card.border = new RectOffset(0, 0, 0, 0);
            Card.alignment = TextAnchor.UpperLeft;

            Button = ButtonStyle(skin.button, Control, Hover, Pressed, Ivory, 13);
            Button.fontStyle = FontStyle.Bold;
            ActiveButton = ButtonStyle(skin.button, Selected, Pressed, Selected, SelectedText, 13);
            ActiveButton.fontStyle = FontStyle.Bold;
            DangerButton = ButtonStyle(skin.button, new Color(0.34f, 0.12f, 0.14f, 1f), new Color(0.50f, 0.15f, 0.17f, 1f), new Color(0.34f, 0.12f, 0.14f, 1f), new Color(1f, 0.83f, 0.80f, 1f), 13);
            DangerButton.fontStyle = FontStyle.Bold;
            Tab = ButtonStyle(skin.button, Control, Hover, Pressed, new Color(0.67f, 0.74f, 0.77f, 1f), 12);
            Tab.fontStyle = FontStyle.Bold;
            Tab.padding = new RectOffset(10, 10, 8, 8);
            Tab.margin = new RectOffset(0, 5, 0, 0);
            Tab.fixedHeight = 34;
            ActiveTab = ButtonStyle(skin.button, Selected, Pressed, Selected, SelectedText, 12);
            ActiveTab.fontStyle = FontStyle.Bold;
            ActiveTab.padding = new RectOffset(10, 10, 8, 8);
            ActiveTab.margin = new RectOffset(0, 5, 0, 0);
            ActiveTab.fixedHeight = 34;

            TextField = Box(skin.textField, Field, Field, Field, new Color(0.95f, 0.96f, 0.96f, 1f));
            TextField.fontSize = 13;
            TextField.padding = new RectOffset(10, 10, 8, 8);
            TextField.margin = new RectOffset(0, 0, 3, 6);
            TextField.border = new RectOffset(0, 0, 0, 0);
            TextField.fixedHeight = 34;
            TextField.alignment = TextAnchor.MiddleLeft;
            TextField.wordWrap = false;

            // Root draws these rows with the same explicit 20px checkbox icons
            // as Prefabulator, avoiding Unity's ambiguous tiny default toggle.
            Toggle = Box(skin.button, Control, Hover, Pressed, Ivory);
            Toggle.fontSize = 12;
            Toggle.alignment = TextAnchor.MiddleLeft;
            Toggle.padding = new RectOffset(12, 12, 9, 9);
            Toggle.margin = new RectOffset(0, 0, 3, 4);
            Toggle.fixedHeight = 34;
            Set(Toggle.onNormal, Selected, SelectedText);
            Set(Toggle.onHover, Pressed, SelectedText);
            Set(Toggle.onActive, Selected, SelectedText);
            Set(Toggle.onFocused, Pressed, SelectedText);
            ToggleLabel = Label(skin.label, new Color(0.90f, 0.94f, 0.95f, 1f), 12, FontStyle.Normal);
            ToggleLabel.padding = new RectOffset(0, 0, 0, 0);
            ToggleLabel.margin = new RectOffset(0, 0, 0, 0);
            ToggleLabel.alignment = TextAnchor.MiddleLeft;
            ToggleOffIcon = ToggleIcon(false);
            ToggleOnIcon = ToggleIcon(true);

            Slider = Box(skin.horizontalSlider, Control, Hover, Hover, Ivory);
            Slider.border = new RectOffset(0, 0, 0, 0);
            Slider.padding = new RectOffset(0, 0, 0, 0);
            Slider.margin = new RectOffset(0, 0, 7, 11);
            Slider.fixedHeight = 6;
            Thumb = Box(skin.horizontalSliderThumb, new Color(0.40f, 0.57f, 0.60f, 1f), SelectedText, Pressed, Ivory);
            Thumb.border = new RectOffset(0, 0, 0, 0);
            Thumb.padding = new RectOffset(0, 0, 0, 0);
            Thumb.margin = new RectOffset(0, 0, -5, -5);
            Thumb.fixedWidth = 16;
            Thumb.fixedHeight = 16;
            ready = true;
        }

        GUIStyle Label(GUIStyle original, Color text, int size, FontStyle weight)
        {
            GUIStyle style = Box(original, Color.clear, Color.clear, Color.clear, text);
            style.fontSize = size;
            style.fontStyle = weight;
            style.alignment = TextAnchor.UpperLeft;
            style.wordWrap = true;
            style.richText = false;
            style.padding = new RectOffset(0, 0, 2, 2);
            style.margin = new RectOffset(0, 0, 1, 3);
            return style;
        }
        GUIStyle ButtonStyle(GUIStyle original, Color normal, Color hover, Color active, Color text, int size)
        {
            GUIStyle style = Box(original, normal, hover, active, text);
            style.fontSize = size;
            style.alignment = TextAnchor.MiddleCenter;
            style.padding = new RectOffset(12, 12, 8, 8);
            style.margin = new RectOffset(0, 0, 3, 5);
            style.border = new RectOffset(0, 0, 0, 0);
            style.fixedHeight = 34;
            style.wordWrap = true;
            return style;
        }
        GUIStyle Box(GUIStyle original, Color normal, Color hover, Color active, Color text)
        {
            GUIStyle style = new GUIStyle(original);
            style.font = GUI.skin.font;
            style.richText = false;
            Set(style.normal, normal, text);
            Set(style.hover, hover, text);
            Set(style.active, active, text);
            Set(style.focused, hover, text);
            Set(style.onNormal, active, text);
            Set(style.onHover, hover, text);
            Set(style.onActive, active, text);
            Set(style.onFocused, hover, text);
            return style;
        }
        void Set(GUIStyleState state, Color background, Color text)
        {
            state.background = background.a <= 0f ? null : Solid(background);
            state.textColor = text;
        }
        Texture2D Solid(Color color)
        {
            Texture2D texture;
            if (solids.TryGetValue(color, out texture)) return texture;
            texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            owned.Add(texture);
            solids.Add(color, texture);
            return texture;
        }
        Texture2D ToggleIcon(bool checkedValue)
        {
            Texture2D icon = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            icon.name = checkedValue ? "ATTStreamToggleOn" : "ATTStreamToggleOff";
            icon.hideFlags = HideFlags.HideAndDontSave;
            icon.filterMode = FilterMode.Point;
            icon.wrapMode = TextureWrapMode.Clamp;
            Color border = checkedValue ? new Color(0.36f, 0.91f, 0.85f, 1f)
                : new Color(0.40f, 0.57f, 0.60f, 1f);
            Color fill = new Color(0.035f, 0.08f, 0.10f, 1f);
            for (int y = 0; y < 20; y++)
                for (int x = 0; x < 20; x++)
                    icon.SetPixel(x, y, x < 2 || x > 17 || y < 2 || y > 17 ? border : fill);
            if (checkedValue) {
                for (int x = 4; x <= 8; x++) {
                    int y = 10 - (x - 4);
                    icon.SetPixel(x, y, border);
                    icon.SetPixel(x, y + 1, border);
                }
                for (int x = 8; x <= 15; x++) {
                    int y = 6 + (x - 8);
                    icon.SetPixel(x, y, border);
                    icon.SetPixel(x, y + 1, border);
                }
            }
            icon.Apply(false, true);
            owned.Add(icon);
            return icon;
        }
        public void Dispose()
        {
            foreach (Texture2D texture in owned)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            owned.Clear();
            solids.Clear();
            Window = Title = Subtitle = Heading = Body = Muted = Card = null;
            Button = ActiveButton = DangerButton = Tab = ActiveTab = null;
            TextField = Toggle = ToggleLabel = Slider = Thumb = null;
            ToggleOffIcon = ToggleOnIcon = null;
            ready = false;
        }
    }
}
