using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Skin
{
    /// <summary>
    /// The comic look of the project's own Editor tooling (owner, 15 Oct 2026: "make our Inspector and tools look
    /// like our comic / Synty art style and Ben-Day dots"): the Colour Bible's inks, a Ben-Day dot field, the HUD's
    /// Orbitron and Exo 2, drawn as banners over our inspectors and windows and as the bars of every [Header].
    /// Nothing of Unity's own is restyled; it can be switched off in Preferences ▸ Krytical Nova.
    /// </summary>
    public static class KnEditorSkin
    {
        public const string EnabledKey = "KryticalNova.ComicInspector";
        private const string OrbitronPath = "Assets/Synty/InterfaceSciFiSoldierHUD/Fonts/Orbitron/Orbitron-ExtraBold.ttf";
        private const string ExoPath = "Assets/Synty/InterfaceSciFiSoldierHUD/Fonts/exo-2-0/Exo2.0-SemiBold.otf";

        // The Colour Bible (Design/Rendering-Bible.md §2).
        public static readonly Color Ink = new Color32(0x14, 0x12, 0x2B, 0xFF);
        public static readonly Color ShadowIndigo = new Color32(0x4C, 0x52, 0xBF, 0xFF);
        public static readonly Color DeepIndigo = new Color32(0x2B, 0x2C, 0x6E, 0xFF);
        public static readonly Color Paper = new Color32(0xF4, 0xEE, 0xDF, 0xFF);
        public static readonly Color NeonCyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
        public static readonly Color SignalYellow = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        public static readonly Color Coral = new Color32(0xFF, 0x9E, 0x66, 0xFF);

        // The dots: a 45° grid, as the icons' (IconTreatmentSettings: cell 9, radius 0.36).
        private const int DotCell = 9;
        private const float DotRadius = 0.36f;

        private static Texture2D dots;
        private static Font orbitron, exo;
        private static GUIStyle bannerTitle, bannerSub, headerText, boxStyle;

        /// <summary>On unless switched off in Preferences.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        /// <summary>True for our own code: the skin never touches a vendor's or Unity's.</summary>
        public static bool IsOurs(System.Type type) =>
            type != null && type.Namespace != null && type.Namespace.StartsWith("KryticalNova", System.StringComparison.Ordinal);

        public static Font Orbitron => orbitron != null ? orbitron : orbitron = AssetDatabase.LoadAssetAtPath<Font>(OrbitronPath);
        public static Font Exo => exo != null ? exo : exo = AssetDatabase.LoadAssetAtPath<Font>(ExoPath);

        /// <summary>The Ben-Day field, tiled: shadow-indigo dots on deep indigo.</summary>
        public static Texture2D Dots
        {
            get
            {
                if (dots != null) return dots;
                const int size = DotCell * 4;
                dots = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                var pixels = new Color32[size * size];
                float radius2 = DotRadius * DotCell * DotRadius * DotCell;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        // Rotated 45°: the grid runs along the diagonals, as the comic pass draws it.
                        float u = (x + y) * 0.70710678f, v = (x - y) * 0.70710678f;
                        float du = u - Mathf.Round(u / DotCell) * DotCell, dv = v - Mathf.Round(v / DotCell) * DotCell;
                        bool dot = du * du + dv * dv <= radius2;
                        pixels[y * size + x] = dot ? (Color32)ShadowIndigo : (Color32)DeepIndigo;
                    }
                dots.SetPixels32(pixels);
                dots.Apply(false, true);
                return dots;
            }
        }

        private static GUIStyle BannerTitle => bannerTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            font = Orbitron, fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = Paper }, padding = new RectOffset(10, 10, 0, 0),
        };
        private static GUIStyle BannerSub => bannerSub ??= new GUIStyle(EditorStyles.label)
        {
            font = Exo, fontSize = 10, alignment = TextAnchor.MiddleRight, normal = { textColor = NeonCyan }, padding = new RectOffset(10, 10, 0, 0),
        };
        private static GUIStyle HeaderText => headerText ??= new GUIStyle(EditorStyles.boldLabel)
        {
            font = Orbitron, fontSize = 10, alignment = TextAnchor.MiddleLeft, normal = { textColor = Paper }, padding = new RectOffset(8, 8, 0, 0),
        };

        /// <summary>The banner over an inspector or a window: a dot field, an ink rule, the title in Orbitron and a note in Exo.</summary>
        public static void Banner(string title, string note = null, float height = 30f)
        {
            if (!Enabled) return;
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            DrawBanner(rect, title, note);
            GUILayout.Space(4f);
        }

        public static void DrawBanner(Rect rect, string title, string note)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, Ink);
                var field = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
                GUI.DrawTextureWithTexCoords(field, Dots, new Rect(0f, 0f, field.width / Dots.width, field.height / Dots.height));
                // A paper keyline under the ink, as the icons have.
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), Paper);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), NeonCyan);
            }
            GUI.Label(rect, (title ?? string.Empty).ToUpperInvariant(), BannerTitle);
            if (!string.IsNullOrEmpty(note)) GUI.Label(rect, note, BannerSub);
        }

        /// <summary>The bar a [Header] becomes: a short ink strip with a signal-yellow tab and the words in Orbitron.</summary>
        public static void Header(string text, float height = 20f)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, height + 6f, GUILayout.ExpandWidth(true));
            rect.y += 6f;
            rect.height = height;
            DrawHeader(rect, text);
        }

        public static void DrawHeader(Rect rect, string text)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, Ink);
                var field = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
                GUI.DrawTextureWithTexCoords(field, Dots, new Rect(0f, 0f, field.width / Dots.width, field.height / Dots.height));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), SignalYellow);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), ShadowIndigo);
            }
            GUI.Label(rect, (text ?? string.Empty).ToUpperInvariant(), HeaderText);
        }

        /// <summary>A sentence in Exo, in the paper colour: for a window's own notes.</summary>
        public static void Note(string text)
        {
            var style = new GUIStyle(EditorStyles.wordWrappedLabel) { font = Exo, fontSize = 11 };
            EditorGUILayout.LabelField(text, style);
        }
    }
}
