using System;
using System.Collections.Generic;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Skin
{
    /// <summary>
    /// The comic look over a whole window or inspector, not only its banner (owner, 15 Oct 2026): while a scope is
    /// open, the styles IMGUI and Odin draw with are turned to the Colour Bible (ink boxes on a dotted indigo field,
    /// paper text, indigo buttons that light cyan, Orbitron titles) and turned back when it closes, so nothing drawn
    /// outside our own windows and inspectors changes. Textures are made once and kept.
    /// </summary>
    public sealed class KnSkinScope : IDisposable
    {
        private struct Saved
        {
            public GUIStyle Style;
            public Texture2D Normal, Hover, Active, Focused, OnNormal, OnHover, OnActive;
            public Color NormalText, HoverText, ActiveText, FocusedText, OnNormalText, OnHoverText, OnActiveText;
            public Font Font;
            public int FontSize;
            public FontStyle FontStyle;
        }

        private static Texture2D ink, field, box, button, buttonLit, buttonOn, textField, paperLine;
        private static readonly List<Saved> saved = new List<Saved>(48);
        private static int depth;

        public static bool IsOpen => depth > 0;

        public KnSkinScope()
        {
            if (!KnEditorSkin.Enabled) return;
            depth++;
            if (depth > 1) return;
            Make();
            Apply();
        }

        public void Dispose()
        {
            if (depth == 0) return;
            depth--;
            if (depth > 0) return;
            foreach (Saved s in saved) Restore(s);
            saved.Clear();
        }

        // ---------------------------------------------------------------- the backdrop

        /// <summary>The dotted field behind a whole window or an inspector's body, with a thin ink frame.</summary>
        public static void Backdrop(Rect rect)
        {
            if (!KnEditorSkin.Enabled || Event.current.type != EventType.Repaint) return;
            Make();
            EditorGUI.DrawRect(rect, Darken(KnEditorSkin.DeepIndigo, 0.55f));
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTextureWithTexCoords(rect, KnEditorSkin.Dots, new Rect(0f, 0f, rect.width / KnEditorSkin.Dots.width, rect.height / KnEditorSkin.Dots.height));
            GUI.color = Color.white;
        }

        private static readonly Dictionary<int, float> heights = new Dictionary<int, float>();

        /// <summary>
        /// Call at the top of an inspector: draws the backdrop as tall as the inspector was last frame (an IMGUI
        /// layout's height is only known after it is drawn), then returns the y it started at for <see cref="EndBody"/>.
        /// </summary>
        public static float BeginBody(int key)
        {
            Rect start = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (heights.TryGetValue(key, out float height) && height > 0f)
                Backdrop(new Rect(start.x - 18f, start.y, start.width + 36f, height));
            return start.y;
        }

        public static void EndBody(int key, float startY)
        {
            Rect end = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) heights[key] = end.y - startY + 6f;
        }

        // ---------------------------------------------------------------- the styles

        private static Color Darken(Color c, float keep) => new Color(c.r * keep, c.g * keep, c.b * keep, 1f);

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static void Make()
        {
            if (ink != null) return;
            ink = Solid(KnEditorSkin.Ink);
            field = Solid(Darken(KnEditorSkin.DeepIndigo, 0.7f));
            box = Solid(new Color(KnEditorSkin.Ink.r, KnEditorSkin.Ink.g, KnEditorSkin.Ink.b, 0.78f));
            button = Solid(KnEditorSkin.DeepIndigo);
            buttonLit = Solid(KnEditorSkin.ShadowIndigo);
            buttonOn = Solid(new Color(0.16f, 0.52f, 0.56f, 1f)); // neon cyan, dimmed: a tab or toggle that is on
            textField = Solid(new Color(0.05f, 0.05f, 0.11f, 1f));
            paperLine = Solid(KnEditorSkin.Paper);
        }

        private static void Keep(GUIStyle s)
        {
            if (s == null) return;
            saved.Add(new Saved
            {
                Style = s,
                Normal = s.normal.background, Hover = s.hover.background, Active = s.active.background, Focused = s.focused.background,
                OnNormal = s.onNormal.background, OnHover = s.onHover.background, OnActive = s.onActive.background,
                NormalText = s.normal.textColor, HoverText = s.hover.textColor, ActiveText = s.active.textColor, FocusedText = s.focused.textColor,
                OnNormalText = s.onNormal.textColor, OnHoverText = s.onHover.textColor, OnActiveText = s.onActive.textColor,
                Font = s.font, FontSize = s.fontSize, FontStyle = s.fontStyle,
            });
        }

        private static void Restore(in Saved v)
        {
            GUIStyle s = v.Style;
            s.normal.background = v.Normal; s.hover.background = v.Hover; s.active.background = v.Active; s.focused.background = v.Focused;
            s.onNormal.background = v.OnNormal; s.onHover.background = v.OnHover; s.onActive.background = v.OnActive;
            s.normal.textColor = v.NormalText; s.hover.textColor = v.HoverText; s.active.textColor = v.ActiveText; s.focused.textColor = v.FocusedText;
            s.onNormal.textColor = v.OnNormalText; s.onHover.textColor = v.OnHoverText; s.onActive.textColor = v.OnActiveText;
            s.font = v.Font; s.fontSize = v.FontSize; s.fontStyle = v.FontStyle;
        }

        private static void Text(GUIStyle s, Color colour, Font font = null, int size = 0, FontStyle? weight = null)
        {
            if (s == null) return;
            Keep(s);
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = colour;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = colour;
            if (font != null) s.font = font;
            if (size > 0) s.fontSize = size;
            if (weight.HasValue) s.fontStyle = weight.Value;
        }

        private static void Box(GUIStyle s, Texture2D back, Color text)
        {
            if (s == null) return;
            Keep(s);
            s.normal.background = s.hover.background = s.active.background = s.focused.background = back;
            s.onNormal.background = s.onHover.background = s.onActive.background = back;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = text;
        }

        private static void ButtonLike(GUIStyle s, Texture2D rest, Texture2D lit, Texture2D on, Color text)
        {
            if (s == null) return;
            Keep(s);
            s.normal.background = rest; s.hover.background = lit; s.active.background = on; s.focused.background = rest;
            s.onNormal.background = on; s.onHover.background = on; s.onActive.background = on;
            s.normal.textColor = s.hover.textColor = s.focused.textColor = text;
            s.active.textColor = KnEditorSkin.Ink;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = KnEditorSkin.Paper;
            s.font = KnEditorSkin.Exo != null ? KnEditorSkin.Exo : s.font;
        }

        private static void Apply()
        {
            Color paper = KnEditorSkin.Paper, dim = new Color(0.80f, 0.78f, 0.72f, 1f);
            GUISkin skin = GUI.skin;

            // Words.
            Text(skin.label, paper, KnEditorSkin.Exo, 12);
            Text(EditorStyles.label, paper, KnEditorSkin.Exo, 12);
            Text(EditorStyles.boldLabel, paper, KnEditorSkin.Orbitron, 11, FontStyle.Normal);
            Text(EditorStyles.miniLabel, dim, KnEditorSkin.Exo, 10);
            Text(EditorStyles.centeredGreyMiniLabel, dim, KnEditorSkin.Exo, 10);
            Text(EditorStyles.wordWrappedLabel, paper, KnEditorSkin.Exo, 12);
            Text(EditorStyles.largeLabel, paper, KnEditorSkin.Orbitron, 14);
            Text(EditorStyles.foldout, paper, KnEditorSkin.Exo, 12);
            Text(EditorStyles.foldoutHeader, paper, KnEditorSkin.Orbitron, 11);
            Text(SirenixGUIStyles.Label, paper, KnEditorSkin.Exo, 12);
            Text(SirenixGUIStyles.BoldLabel, paper, KnEditorSkin.Orbitron, 11, FontStyle.Normal);
            Text(SirenixGUIStyles.Title, KnEditorSkin.SignalYellow, KnEditorSkin.Orbitron, 12, FontStyle.Normal);
            Text(SirenixGUIStyles.BoldTitle, KnEditorSkin.SignalYellow, KnEditorSkin.Orbitron, 12, FontStyle.Normal);
            Text(SirenixGUIStyles.SectionHeader, KnEditorSkin.NeonCyan, KnEditorSkin.Orbitron, 12, FontStyle.Normal);
            Text(SirenixGUIStyles.MultiLineLabel, paper, KnEditorSkin.Exo, 12);
            Text(SirenixGUIStyles.RichTextLabel, paper, KnEditorSkin.Exo, 12);

            // Boxes and fields.
            Box(skin.box, box, paper);
            Box(EditorStyles.helpBox, box, dim);
            Box(SirenixGUIStyles.BoxContainer, box, paper);
            Box(SirenixGUIStyles.BoxHeaderStyle, ink, KnEditorSkin.SignalYellow);
            Box(SirenixGUIStyles.MessageBox, box, paper);
            Box(SirenixGUIStyles.ToolbarBackground, ink, paper);
            Box(EditorStyles.toolbar, ink, paper);
            Box(skin.textField, textField, paper);
            Box(EditorStyles.textField, textField, paper);
            Box(EditorStyles.numberField, textField, paper);
            Box(EditorStyles.textArea, textField, paper);
            Box(skin.textArea, textField, paper);
            Box(skin.horizontalSlider, textField, paper);
            Box(EditorStyles.popup, button, paper);
            Box(EditorStyles.objectField, textField, KnEditorSkin.NeonCyan);
            Box(EditorStyles.toggle, textField, paper);
            Box(skin.toggle, textField, paper);

            // Buttons and tabs.
            ButtonLike(skin.button, button, buttonLit, buttonOn, paper);
            ButtonLike(EditorStyles.miniButton, button, buttonLit, buttonOn, paper);
            ButtonLike(EditorStyles.miniButtonLeft, button, buttonLit, buttonOn, paper);
            ButtonLike(EditorStyles.miniButtonMid, button, buttonLit, buttonOn, paper);
            ButtonLike(EditorStyles.miniButtonRight, button, buttonLit, buttonOn, paper);
            ButtonLike(EditorStyles.toolbarButton, ink, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.Button, button, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ButtonLeft, button, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ButtonMid, button, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ButtonRight, button, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ButtonSelected, buttonOn, buttonOn, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ButtonLeftSelected, buttonOn, buttonOn, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ButtonMidSelected, buttonOn, buttonOn, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ButtonRightSelected, buttonOn, buttonOn, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.MiniButton, button, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ToolbarButton, ink, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ToolbarButtonSelected, buttonOn, buttonOn, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.ToolbarTab, ink, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.TagButton, button, buttonLit, buttonOn, paper);
            ButtonLike(SirenixGUIStyles.DropDownMiniButton, button, buttonLit, buttonOn, paper);
        }
    }
}
