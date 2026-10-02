using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Rip Vertices Studio editor design language: near-black panels, a single warm orange
    /// accent for actions, blue for analysis, green/amber/red status colors, a compact left
    /// sidebar and generously spaced content sections.
    /// </summary>
    internal static class QDUI
    {
        // ------------------------------------------------------------------ palette
        public static readonly Color Bg          = FromHex("#101010");
        public static readonly Color BgSidebar   = FromHex("#0B0B0B");
        public static readonly Color Panel       = FromHex("#181818");
        public static readonly Color PanelLight  = FromHex("#1F1F1F");
        public static readonly Color Line        = FromHex("#2A2A2A");
        public static readonly Color Accent      = FromHex("#FF7A1A");
        public static readonly Color AccentSoft  = FromHex("#3A2313");
        public static readonly Color Blue        = FromHex("#4FA3FF");
        public static readonly Color Green       = FromHex("#4CCB6C");
        public static readonly Color Amber       = FromHex("#FFC24B");
        public static readonly Color Red         = FromHex("#FF5D5D");
        public static readonly Color Text        = FromHex("#EDEDED");
        public static readonly Color TextDim     = FromHex("#8F8F8F");
        public static readonly Color TextFaint   = FromHex("#5C5C5C");

        public static Color FromHex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // ------------------------------------------------------------------ textures
        static readonly Dictionary<Color, Texture2D> s_texCache = new Dictionary<Color, Texture2D>();

        public static Texture2D Tex(Color c)
        {
            if (s_texCache.TryGetValue(c, out var t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            t.SetPixel(0, 0, c);
            t.Apply();
            s_texCache[c] = t;
            return t;
        }

        // ------------------------------------------------------------------ styles
        sealed class UiState
        {
            public bool s_stylesReady;
            public GUIStyle TitleAccent, TitleWhite, Tagline, FeatureStrip, VersionChip;
            public GUIStyle SidebarSection, SidebarButton, SidebarButtonActive;
            public GUIStyle SidebarSubButton, SidebarSubButtonActive;
            public GUIStyle SidebarMainButton, SidebarMainButtonActive;
            public GUIStyle s_sidebarFallbackIcon;
            public GUIStyle SectionHeader, SubHeader, Body, BodyDim, HelpText;
            public GUIStyle FieldLabel, FieldLabelHighlight, ValueLabel, StatValue, StatLabel;
            public GUIStyle AccentBtn, GhostBtn, ChipBtn, ChipBtnActive, PillLabel;
            public GUIStyle s_cardStyle;
            public GUIStyle[] s_workflowCardStyles;
            public GUIStyle s_stepBadgeStyle, s_stepTitleStyle, s_foldArrowStyle;
            public GUIStyle s_presetGlyphStyle, s_presetGlyphActiveStyle;
            public GUIStyle s_presetLabelStyle, s_presetLabelActiveStyle;
            public string HighlightedLabel;
            public double HighlightUntil = double.PositiveInfinity;
            public string SearchSection;
            public string s_currentSection;
            public Vector2 s_contentOrigin;
            public bool s_capturingFields;
            public GUIStyle s_namedProfileTileStyle, s_newProfileGlyphStyle;
        }

        static readonly UiState s_state = new UiState();

        public static GUIStyle TitleAccent => s_state.TitleAccent;
        public static GUIStyle TitleWhite => s_state.TitleWhite;
        public static GUIStyle Tagline => s_state.Tagline;
        public static GUIStyle FeatureStrip => s_state.FeatureStrip;
        public static GUIStyle VersionChip => s_state.VersionChip;
        public static GUIStyle SidebarSection => s_state.SidebarSection;
        public static GUIStyle SidebarButton => s_state.SidebarButton;
        public static GUIStyle SidebarButtonActive => s_state.SidebarButtonActive;
        public static GUIStyle SidebarSubButton => s_state.SidebarSubButton;
        public static GUIStyle SidebarSubButtonActive => s_state.SidebarSubButtonActive;
        public static GUIStyle SidebarMainButton => s_state.SidebarMainButton;
        public static GUIStyle SidebarMainButtonActive => s_state.SidebarMainButtonActive;
        public static GUIStyle SectionHeader => s_state.SectionHeader;
        public static GUIStyle SubHeader => s_state.SubHeader;
        public static GUIStyle Body => s_state.Body;
        public static GUIStyle BodyDim => s_state.BodyDim;
        public static GUIStyle HelpText => s_state.HelpText;
        public static GUIStyle FieldLabel => s_state.FieldLabel;
        public static GUIStyle FieldLabelHighlight => s_state.FieldLabelHighlight;
        public static GUIStyle ValueLabel => s_state.ValueLabel;
        public static GUIStyle StatValue => s_state.StatValue;
        public static GUIStyle StatLabel => s_state.StatLabel;
        public static GUIStyle AccentBtn => s_state.AccentBtn;
        public static GUIStyle GhostBtn => s_state.GhostBtn;
        public static GUIStyle ChipBtn => s_state.ChipBtn;
        public static GUIStyle ChipBtnActive => s_state.ChipBtnActive;
        public static GUIStyle PillLabel => s_state.PillLabel;
        internal static string HighlightedLabel => s_state.HighlightedLabel;
        internal static double HighlightUntil => s_state.HighlightUntil;
        internal static string SearchSection => s_state.SearchSection;
        internal static bool HasSettingHighlight => s_state.HighlightedLabel != null;
        internal static double SettingHighlightUntil => s_state.HighlightUntil;

        internal static void BeginSettingsHighlight(string label, string section)
        {
            s_state.HighlightedLabel = label;
            s_state.HighlightUntil = 0d;
            s_state.SearchSection = section;
        }

        internal static void ShowSettingsHighlight()
        {
            s_state.HighlightUntil = EditorApplication.timeSinceStartup + 1d;
        }

        internal static void ClearSettingsHighlight()
        {
            s_state.HighlightedLabel = null;
            s_state.SearchSection = null;
            s_state.HighlightUntil = double.PositiveInfinity;
        }

        internal static void SetSearchSectionForTesting(string section)
        {
            s_state.SearchSection = section;
        }

        internal static Vector2 GetSettingsContentOrigin() => s_state.s_contentOrigin;

        // Per-repaint GUIStyle allocations cached in static lazy fields.
        static readonly Dictionary<string, bool> s_foldStates =
            new Dictionary<string, bool>();
        static readonly Dictionary<Color, GUIStyle> s_noteIconStyles =
            new Dictionary<Color, GUIStyle>();
        static readonly Dictionary<Color, GUIStyle> s_pillStyles =
            new Dictionary<Color, GUIStyle>();
        static readonly Dictionary<Color, GUIStyle> s_statValueStyles =
            new Dictionary<Color, GUIStyle>();
        static readonly Dictionary<string, Texture> s_sidebarIconCache =
            new Dictionary<string, Texture>();
        static readonly Dictionary<Color, GUIStyle> s_outlineCardOuterStyles =
            new Dictionary<Color, GUIStyle>();
        static readonly Dictionary<Color, GUIStyle> s_outlineCardInnerStyles =
            new Dictionary<Color, GUIStyle>();
        static readonly Dictionary<FracturePattern, Texture2D> s_patternIcons =
            new Dictionary<FracturePattern, Texture2D>();

        public static void EnsureStyles()
        {
            if (s_state.s_stylesReady && s_state.TitleAccent != null) return;
            s_state.s_stylesReady = true;

            s_state.TitleAccent = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 19, richText = true,
                normal = { textColor = Accent }
            };
            s_state.TitleWhite = new GUIStyle(s_state.TitleAccent) { normal = { textColor = Text } };

            s_state.Tagline = new GUIStyle(EditorStyles.label)
            {
                fontSize = 9, fontStyle = FontStyle.Bold,
                normal = { textColor = TextDim }
            };
            s_state.FeatureStrip = new GUIStyle(EditorStyles.label)
            {
                fontSize = 9, fontStyle = FontStyle.Bold, richText = true,
                normal = { textColor = Accent }
            };
            s_state.VersionChip = new GUIStyle(EditorStyles.label)
            {
                fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Accent }
            };

            s_state.SidebarSection = new GUIStyle(EditorStyles.label)
            {
                fontSize = 9, fontStyle = FontStyle.Bold,
                normal = { textColor = TextFaint },
                margin = new RectOffset(10, 4, 10, 2)
            };
            s_state.SidebarButton = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, richText = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 6, 0, 0),
                margin = new RectOffset(6, 6, 1, 1),
                fixedHeight = 26,
                normal = { textColor = TextDim, background = null },
                hover = { textColor = Text, background = Tex(PanelLight) }
            };
            s_state.SidebarButtonActive = new GUIStyle(s_state.SidebarButton)
            {
                normal = { textColor = Text, background = Tex(AccentSoft) },
                hover = { textColor = Text, background = Tex(AccentSoft) }
            };
            s_state.SidebarMainButton = new GUIStyle(s_state.SidebarButton)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 38,
                padding = new RectOffset(46, 6, 0, 0),
                normal = { textColor = Text, background = Tex(Panel) },
                hover = { textColor = Text, background = Tex(PanelLight) }
            };
            s_state.SidebarMainButtonActive = new GUIStyle(s_state.SidebarMainButton)
            {
                normal = { textColor = Text, background = Tex(AccentSoft) },
                hover = { textColor = Text, background = Tex(AccentSoft) }
            };
            // Detail workspaces are first-class navigation. Give them enough height,
            // weight and icon room to remain scannable in the narrow sidebar.
            s_state.SidebarSubButton = new GUIStyle(s_state.SidebarButton)
            {
                fontSize = 12, fontStyle = FontStyle.Bold, fixedHeight = 30,
                padding = new RectOffset(44, 6, 0, 0)
            };
            s_state.SidebarSubButtonActive = new GUIStyle(s_state.SidebarSubButton)
            {
                normal = { textColor = Text, background = Tex(AccentSoft) },
                hover = { textColor = Text, background = Tex(AccentSoft) }
            };
            s_state.s_sidebarFallbackIcon = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            s_state.SectionHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Text },
                margin = new RectOffset(0, 0, 2, 2)
            };
            s_state.SubHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = TextDim }
            };
            s_state.Body = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, wordWrap = true, richText = true,
                normal = { textColor = Text }
            };
            s_state.BodyDim = new GUIStyle(s_state.Body) { normal = { textColor = TextDim } };
            s_state.HelpText = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10, wordWrap = true, richText = true,
                normal = { textColor = TextDim, background = null },
                hover = { textColor = TextDim, background = null },
                active = { textColor = TextDim, background = null },
                focused = { textColor = TextDim, background = null },
                onNormal = { textColor = TextDim, background = null },
                onHover = { textColor = TextDim, background = null },
                onActive = { textColor = TextDim, background = null },
                onFocused = { textColor = TextDim, background = null }
            };

            s_state.FieldLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = Text }
            };
            s_state.FieldLabelHighlight = new GUIStyle(s_state.FieldLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Accent }
            };
            s_state.ValueLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10, alignment = TextAnchor.MiddleRight,
                normal = { textColor = TextDim }
            };
            s_state.StatValue = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                normal = { textColor = Text }
            };
            s_state.StatLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 8, fontStyle = FontStyle.Bold,
                normal = { textColor = TextFaint }
            };

            s_state.AccentBtn = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11, fontStyle = FontStyle.Bold, fixedHeight = 32,
                normal = { textColor = Color.black, background = Tex(Accent) },
                hover = { textColor = Color.black, background = Tex(FromHex("#FF8F3D")) },
                active = { textColor = Color.black, background = Tex(FromHex("#D96613")) }
            };
            s_state.GhostBtn = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10, fixedHeight = 24,
                normal = { textColor = Text, background = Tex(PanelLight) },
                hover = { textColor = Text, background = Tex(FromHex("#282828")) },
                active = { textColor = Text, background = Tex(FromHex("#121212")) }
            };
            s_state.ChipBtn = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10, fixedHeight = 22,
                normal = { textColor = TextDim, background = Tex(PanelLight) },
                hover = { textColor = Text, background = Tex(FromHex("#282828")) },
                active = { textColor = Text, background = Tex(AccentSoft) }
            };
            s_state.ChipBtnActive = new GUIStyle(s_state.ChipBtn)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Accent, background = Tex(AccentSoft) },
                hover = { textColor = Accent, background = Tex(AccentSoft) }
            };
            s_state.PillLabel = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 9, alignment = TextAnchor.MiddleCenter
            };

            s_state.s_cardStyle = new GUIStyle
            {
                normal = { background = Tex(Panel) },
                padding = new RectOffset(12, 12, 10, 12),
                margin = new RectOffset(0, 0, 0, 8)
            };
        }

        // ------------------------------------------------------------------ chrome
        public static void FillBackground(Rect windowRect)
        {
            GUI.DrawTexture(windowRect, Tex(Bg), ScaleMode.StretchToFill);
        }

        /// <summary>Product banner: two-tone title, tagline, feature strip, version chip.</summary>
        public static void DrawHeader(float width, string accentWord, string whiteWord,
            string tagline, string features, string version)
        {
            EnsureStyles();
            Rect r = GUILayoutUtility.GetRect(width, 58f);
            GUI.DrawTexture(r, Tex(FromHex("#0C0C0C")), ScaleMode.StretchToFill);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), Line);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 1f), new Color(Accent.r, Accent.g, Accent.b, 0.55f));

            var title = new Rect(r.x + 14f, r.y + 8f, r.width - 90f, 24f);
            GUIContent accentContent = new GUIContent(accentWord);
            float accentW = s_state.TitleAccent.CalcSize(accentContent).x;
            GUI.Label(title, accentWord, s_state.TitleAccent);
            GUI.Label(new Rect(title.x + accentW + 2f, title.y, title.width - accentW, title.height), whiteWord, s_state.TitleWhite);

            GUI.Label(new Rect(r.x + 15f, r.y + 33f, r.width - 90f, 12f), tagline, s_state.Tagline);
            GUI.Label(new Rect(r.x + 15f, r.y + 45f, r.width - 90f, 12f), features, s_state.FeatureStrip);

            var chip = new Rect(r.xMax - 52f, r.y + 8f, 40f, 16f);
            GUI.DrawTexture(chip, Tex(AccentSoft), ScaleMode.StretchToFill);
            GUI.Label(chip, version, s_state.VersionChip);
        }

        public static bool DrawSidebarButton(string label, bool selected, bool enabled = true)
        {
            EnsureStyles();
            using (new EditorGUI.DisabledScope(!enabled))
            {
                bool clicked = GUILayout.Button(label, selected ? s_state.SidebarButtonActive : s_state.SidebarButton);
                var r = GUILayoutUtility.GetLastRect();
                if (selected)
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 3f, r.height), Accent);
                return clicked;
            }
        }

        public static bool DrawSidebarSubButton(string label, bool selected,
            string iconName = null, string tooltip = null,
            string fallbackGlyph = "")
        {
            EnsureStyles();
            Texture icon = ResolveSidebarIcon(iconName);
            // Draw the image separately at a consistent size. GUILayout otherwise uses
            // each built-in icon's native dimensions, which made Play tiny and allowed
            // missing Joint icons to collapse the entire icon column.
            var content = new GUIContent(label, tooltip ?? label);
            bool clicked = GUILayout.Button(content,
                selected ? s_state.SidebarSubButtonActive : s_state.SidebarSubButton);
            var r = GUILayoutUtility.GetLastRect();
            Rect iconRect = new Rect(r.x + 16f, r.y + 5f, 20f, 20f);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            else if (!string.IsNullOrEmpty(fallbackGlyph))
            {
                Color previous = GUI.color;
                GUI.color = selected ? Accent : TextDim;
                GUI.Label(iconRect, fallbackGlyph, s_state.s_sidebarFallbackIcon);
                GUI.color = previous;
            }
            if (selected)
                EditorGUI.DrawRect(new Rect(r.x + 6f, r.y, 3f, r.height), Accent);
            return clicked;
        }

        public static bool DrawSidebarMainButton(string label, bool selected,
            string iconName, string tooltip, string fallbackGlyph = "Q")
        {
            EnsureStyles();
            Texture icon = ResolveSidebarIcon(iconName);

            bool clicked = GUILayout.Button(new GUIContent(label, tooltip),
                selected ? s_state.SidebarMainButtonActive : s_state.SidebarMainButton);
            Rect r = GUILayoutUtility.GetLastRect();
            Rect iconRect = new Rect(r.x + 14f, r.y + 7f, 24f, 24f);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            else
            {
                Color previous = GUI.color;
                GUI.color = selected ? Accent : Text;
                GUI.Label(iconRect, fallbackGlyph, s_state.s_sidebarFallbackIcon);
                GUI.color = previous;
            }
            if (selected)
                EditorGUI.DrawRect(new Rect(r.x, r.y, 4f, r.height), Accent);
            return clicked;
        }

        static Texture ResolveSidebarIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;
            if (s_sidebarIconCache.TryGetValue(iconName, out Texture cached) &&
                cached != null) return cached;

            Texture icon;
            if (iconName.StartsWith("Assets/"))
            {
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconName);
            }
            else
            {
                GUIContent iconContent = EditorGUIUtility.IconContent(iconName);
                icon = iconContent != null ? iconContent.image : null;
                if (icon == null && iconName.StartsWith("d_"))
                {
                    iconContent = EditorGUIUtility.IconContent(iconName.Substring(2));
                    icon = iconContent != null ? iconContent.image : null;
                }
            }

            if (icon != null) s_sidebarIconCache[iconName] = icon;
            return icon;
        }

        public static void SidebarLabel(string label)
        {
            EnsureStyles();
            GUILayout.Label(label, s_state.SidebarSection);
        }

        public static void SidebarSeparator()
        {
            var r = GUILayoutUtility.GetRect(1f, 8f);
            EditorGUI.DrawRect(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 1f), Line);
        }

        // ------------------------------------------------------------------ content blocks
        public static void BeginCard()
        {
            EnsureStyles();
            EditorGUILayout.BeginVertical(s_state.s_cardStyle);
        }

        /// <summary>
        /// Subtly tinted workflow card used only for Start steps. The tints make the
        /// Select → Configure → Bake sequence scannable without adding decorative noise.
        /// </summary>
        public static void BeginWorkflowCard(int step)
        {
            EnsureStyles();
            if (s_state.s_workflowCardStyles == null)
            {
                s_state.s_workflowCardStyles = new[]
                {
                    new GUIStyle(s_state.s_cardStyle)
                    {
                        normal = { background = Tex(FromHex("#182029")) }
                    },
                    new GUIStyle(s_state.s_cardStyle)
                    {
                        normal = { background = Tex(FromHex("#211B16")) }
                    },
                    new GUIStyle(s_state.s_cardStyle)
                    {
                        normal = { background = Tex(FromHex("#17231B")) }
                    }
                };
            }
            int index = Mathf.Clamp(step - 1, 0, s_state.s_workflowCardStyles.Length - 1);
            EditorGUILayout.BeginVertical(s_state.s_workflowCardStyles[index]);
        }

        public static void EndCard() => EditorGUILayout.EndVertical();

        /// <summary>Card with a thin colored outline, used to emphasize the current editing surface.</summary>
        public static void BeginOutlinedCard(Color outline)
        {
            EnsureStyles();
            if (!s_outlineCardOuterStyles.TryGetValue(outline, out var outer))
            {
                outer = new GUIStyle(s_state.s_cardStyle)
                {
                    normal = { background = Tex(outline) },
                    padding = new RectOffset(1, 1, 1, 1)
                };
                s_outlineCardOuterStyles[outline] = outer;
            }
            if (!s_outlineCardInnerStyles.TryGetValue(outline, out var inner))
            {
                inner = new GUIStyle(s_state.s_cardStyle)
                {
                    normal = { background = Tex(FromHex("#21170F")) },
                    margin = new RectOffset(0, 0, 0, 0)
                };
                s_outlineCardInnerStyles[outline] = inner;
            }
            EditorGUILayout.BeginVertical(outer);
            EditorGUILayout.BeginVertical(inner);
        }

        public static void EndOutlinedCard()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Card with a clickable, collapsible header. Returns whether the body should be
        /// drawn; always pair with <see cref="EndFoldCard"/>. The open state is persisted
        /// per title in EditorPrefs. An optional status pill summarizes the collapsed
        /// content so nothing important is invisible while folded.
        /// </summary>
        public static bool BeginFoldCard(string title, bool defaultOpen = false,
            string status = null, Color? statusColor = null, float statusWidth = 108f)
        {
            s_sectionStack.Push(s_state.s_currentSection);
            EnsureStyles();
            s_state.s_foldArrowStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 9, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = TextDim }
            };

            if (!s_foldStates.TryGetValue(title, out bool open))
            {
                open = EditorPrefs.GetBool("QuickDestruction.Fold." + title, defaultOpen);
                s_foldStates[title] = open;
            }

            EditorGUILayout.BeginVertical(s_state.s_cardStyle);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(open ? "▼" : "▶", s_state.s_foldArrowStyle,
                GUILayout.Width(14f), GUILayout.Height(18f));
            GUILayout.Label(title, s_state.SectionHeader);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(status))
                StatusPill(status, statusColor ?? TextDim, statusWidth);
            EditorGUILayout.EndHorizontal();
            Rect header = GUILayoutUtility.GetLastRect();
            CaptureSection(title);
            EditorGUIUtility.AddCursorRect(header, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown &&
                header.Contains(Event.current.mousePosition))
            {
                open = !open;
                s_foldStates[title] = open;
                EditorPrefs.SetBool("QuickDestruction.Fold." + title, open);
                Event.current.Use();
                // The body control tree appears/disappears; restart on a fresh Layout.
                GUIUtility.ExitGUI();
            }
            if (open)
            {
                var r = GUILayoutUtility.GetRect(1f, 3f);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 26f, 2f), Accent);
                GUILayout.Space(6f);
            }
            return open;
        }

        public static void EndFoldCard()
        {
            EditorGUILayout.EndVertical();
            s_state.s_currentSection = s_sectionStack.Count > 0 ? s_sectionStack.Pop() : null;
        }

        /// <summary>
        /// Forces a fold card open by its title. Search results land on controls that are
        /// often inside a collapsed card, and arriving at a tab where the setting is still
        /// hidden would make the jump feel broken. Writes both the live state and the
        /// EditorPref so the card stays open the way a manual click leaves it.
        /// </summary>
        public static void SetFoldOpen(string title, bool open)
        {
            if (string.IsNullOrEmpty(title)) return;
            s_foldStates[title] = open;
            EditorPrefs.SetBool("QuickDestruction.Fold." + title, open);
        }

        /// <summary>
        /// Label of the control a search result jumped to. Its row draws in the accent
        /// colour for a short timed interval, which turns "the right tab is open" into
        /// "there is the setting". Rows are otherwise unchanged, so no layout shifts.
        /// </summary>
        static readonly Stack<string> s_sectionStack = new Stack<string>();
        static readonly Dictionary<string, GUIContent> s_fieldContents = new Dictionary<string, GUIContent>();
        static readonly List<FieldLocation> s_fieldLocations = new List<FieldLocation>(192);
        static readonly Dictionary<string, Rect> s_sectionLocations = new Dictionary<string, Rect>();

        struct FieldLocation
        {
            public string Label;
            public Rect Rect;
            public bool InSection;
        }

        public static GUIContent FieldContent(string label)
        {
            if (!s_fieldContents.TryGetValue(label, out GUIContent content))
            {
                content = new GUIContent(label, QuickDestructionSettingHelp.GetTooltip(label));
                s_fieldContents.Add(label, content);
            }
            return content;
        }

        public static void BeginSettingsCapture()
        {
            s_state.s_capturingFields = true;
            Rect origin = GUILayoutUtility.GetRect(0f, 0f);
            s_state.s_contentOrigin = GUIUtility.GUIToScreenPoint(origin.position);
            s_state.s_currentSection = null;
            s_sectionStack.Clear();
            if (Event.current.type != EventType.Repaint) return;
            s_fieldLocations.Clear();
            s_sectionLocations.Clear();
        }

        public static void EndSettingsCapture() => s_state.s_capturingFields = false;

        public static void DrawFieldLabel(string label, params GUILayoutOption[] options)
        {
            GUILayout.Label(FieldContent(label), FieldLabelFor(label), options);
            CaptureField(label);
        }

        public static void CaptureField(string label)
        {
            if (Event.current.type != EventType.Repaint) return;
            Rect rect = GUILayoutUtility.GetLastRect();
            GUI.Label(rect, new GUIContent(string.Empty, FieldContent(label).tooltip), GUIStyle.none);
            if (!s_state.s_capturingFields) return;
            rect.position = GUIUtility.GUIToScreenPoint(rect.position) - s_state.s_contentOrigin;
            bool inSection = s_state.s_currentSection == s_state.SearchSection || s_sectionStack.Contains(s_state.SearchSection);
            // Rows capture their label first, then the complete control. Keep the whole
            // row so navigation and its outline include the slider/value as well.
            if (s_fieldLocations.Count > 0)
            {
                FieldLocation previous = s_fieldLocations[s_fieldLocations.Count - 1];
                if (previous.Label == label && previous.InSection == inSection &&
                    rect.Contains(previous.Rect.center))
                    s_fieldLocations.RemoveAt(s_fieldLocations.Count - 1);
            }
            s_fieldLocations.Add(new FieldLocation
            {
                Label = label, Rect = rect,
                InSection = inSection
            });
        }

        /// <summary>Draw after the scroll view so disabled fields retain a visible outline.</summary>
        public static void DrawSettingsHighlight(Rect viewport)
        {
            if (!s_state.s_capturingFields || Event.current.type != EventType.Repaint ||
                string.IsNullOrEmpty(s_state.HighlightedLabel) ||
                EditorApplication.timeSinceStartup >= s_state.HighlightUntil ||
                !TryGetSettingRect(s_state.HighlightedLabel, out Rect rect)) return;
            rect.position = GUIUtility.ScreenToGUIPoint(s_state.s_contentOrigin + rect.position);
            rect = Rect.MinMaxRect(Mathf.Max(viewport.xMin, rect.xMin - 3f),
                Mathf.Max(viewport.yMin, rect.yMin - 2f),
                Mathf.Min(viewport.xMax, rect.xMax + 3f),
                Mathf.Min(viewport.yMax, rect.yMax + 2f));
            if (rect.width <= 0f || rect.height <= 0f) return;
            bool enabled = GUI.enabled;
            Color color = GUI.color;
            GUI.enabled = true;
            GUI.color = Color.white;
            try
            {
                EditorGUI.DrawRect(rect, new Color(Accent.r, Accent.g, Accent.b, 0.12f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), Accent);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), Accent);
                EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), Accent);
            }
            finally { GUI.enabled = enabled; GUI.color = color; }
        }

        static void CaptureSection(string title)
        {
            s_state.s_currentSection = title;
            if (!s_state.s_capturingFields || Event.current.type != EventType.Repaint) return;
            Rect rect = GUILayoutUtility.GetLastRect();
            rect.position = GUIUtility.GUIToScreenPoint(rect.position) - s_state.s_contentOrigin;
            s_sectionLocations[title] = rect;
        }

        public static void SubHeaderLabel(string title)
        {
            GUILayout.Label(title, s_state.SubHeader);
            CaptureSection(title);
        }

        public static bool TryGetSettingRect(string label, out Rect rect)
        {
            bool found = false;
            rect = default;
            foreach (FieldLocation field in s_fieldLocations)
            {
                if (field.Label != label) continue;
                if (!found || field.InSection) { rect = field.Rect; found = true; }
                if (field.InSection) return true;
            }
            return found;
        }

        public static bool TryGetSectionRect(string section, out Rect rect) =>
            s_sectionLocations.TryGetValue(section, out rect);

        public static GUIStyle FieldLabelFor(string label) =>
            !string.IsNullOrEmpty(s_state.HighlightedLabel) && label == s_state.HighlightedLabel &&
                EditorApplication.timeSinceStartup < s_state.HighlightUntil
                ? s_state.FieldLabelHighlight
                : s_state.FieldLabel;

        public static void Header(string text)
        {
            EnsureStyles();
            GUILayout.Label(text, s_state.SectionHeader);
            CaptureSection(text);
            var r = GUILayoutUtility.GetRect(1f, 3f);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 26f, 2f), Accent);
            GUILayout.Space(6f);
        }

        /// <summary>Large numbered workflow step header: accent number badge, bold title
        /// and a right-aligned live status pill.</summary>
        public static void StepHeader(string number, string title, string status,
            Color statusColor)
        {
            EnsureStyles();
            s_state.s_stepBadgeStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.black }
            };
            s_state.s_stepTitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = Text }
            };

            EditorGUILayout.BeginHorizontal();
            Rect badge = GUILayoutUtility.GetRect(26f, 26f, GUILayout.Width(26f));
            GUI.DrawTexture(badge, Tex(Accent), ScaleMode.StretchToFill);
            GUI.Label(badge, number, s_state.s_stepBadgeStyle);
            GUILayout.Space(8f);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(4f);
            GUILayout.Label(title, s_state.s_stepTitleStyle);
            CaptureSection(title);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.BeginVertical();
                GUILayout.Space(4f);
                StatusPill(status, statusColor, 128f);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
            var underline = GUILayoutUtility.GetRect(1f, 5f);
            EditorGUI.DrawRect(new Rect(underline.x, underline.y + 2f, 26f, 2f), Accent);
            GUILayout.Space(5f);
        }

        public static void InfoBox(string message) => NoteBox("i", Blue, message);
        public static void WarnBox(string message) => NoteBox("!", Amber, message);
        public static void ErrorBox(string message) => NoteBox("x", Red, message);

        /// <summary>
        /// Compact progressive-disclosure help. Common controls stay visually clean while
        /// longer explanations and examples remain one click away. Critical warnings should
        /// still use WarnBox/ErrorBox and must not be hidden in this foldout.
        /// </summary>
        public static void HelpFoldout(string key, string title, string message,
            bool defaultOpen = false)
        {
            bool open = BeginHelpFoldout(key, title, defaultOpen);
            if (open)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14f);
                GUILayout.Label("i", s_state.PillLabel, GUILayout.Width(12f));
                GUILayout.Space(3f);
                GUILayout.Label(message, s_state.HelpText);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(3f);
            }
            EndHelpFoldout();
        }

        /// <summary>
        /// Progressive-disclosure help header whose open body may contain arbitrary editor
        /// controls such as copyable code samples. Always pair with EndHelpFoldout.
        /// </summary>
        public static bool BeginHelpFoldout(string key, string title,
            bool defaultOpen = false)
        {
            EnsureStyles();
            string stateKey = "QuickDestruction.Help." + key;
            if (!s_foldStates.TryGetValue(stateKey, out bool open))
            {
                open = EditorPrefs.GetBool(stateKey, defaultOpen);
                s_foldStates[stateKey] = open;
            }

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(2f);
            GUILayout.Label(open ? "v" : ">", s_state.BodyDim, GUILayout.Width(12f));
            GUILayout.Label(title, s_state.BodyDim);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            Rect header = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(header, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown &&
                header.Contains(Event.current.mousePosition))
            {
                open = !open;
                s_foldStates[stateKey] = open;
                EditorPrefs.SetBool(stateKey, open);
                Event.current.Use();
                GUIUtility.ExitGUI();
            }
            return open;
        }

        public static void EndHelpFoldout() => EditorGUILayout.EndVertical();

        static void NoteBox(string glyph, Color color, string message)
        {
            EnsureStyles();
            var content = new GUIContent(message);
            float h = Mathf.Max(30f, s_state.HelpText.CalcHeight(content, EditorGUIUtility.currentViewWidth - 110f) + 14f);
            Rect r = GUILayoutUtility.GetRect(1f, h);
            r.x += 2f; r.width -= 4f;
            GUI.DrawTexture(r, Tex(PanelLight), ScaleMode.StretchToFill);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3f, r.height), color);

            if (!s_noteIconStyles.TryGetValue(color, out var iconStyle))
            {
                iconStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = color, background = null },
                    hover = { textColor = color, background = null },
                    active = { textColor = color, background = null },
                    focused = { textColor = color, background = null }
                };
                s_noteIconStyles[color] = iconStyle;
            }
            GUI.Label(new Rect(r.x + 4f, r.y, 20f, r.height), glyph, iconStyle);
            GUI.Label(new Rect(r.x + 26f, r.y + 6f, r.width - 34f, r.height - 12f), message, s_state.HelpText);
            GUILayout.Space(4f);
        }

        // ------------------------------------------------------------------ fields
        public const float LabelWidth = 150f;

        public static float SliderRow(string label, float value, float min, float max, string format = "0.###")
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            DrawFieldLabel(label, GUILayout.Width(LabelWidth));

            // The slider keeps its place in the row but is drawn last. Reading the text
            // field after the slider meant a committed entry arrived one frame too late:
            // the handle had already been drawn from the previous value and stayed there
            // until an unrelated event repainted the window.
            Rect sliderRect = GUILayoutUtility.GetRect(
                GUIContent.none, GUI.skin.horizontalSlider, GUILayout.ExpandWidth(true));

            string typed = EditorGUILayout.DelayedTextField(
                value.ToString(format, CultureInfo.InvariantCulture), GUILayout.Width(56f));
            if (TryParseTypedValue(typed, out float parsed))
            {
                float clamped = Mathf.Clamp(parsed, min, max);
                // An out-of-range entry must not keep displaying the number that was
                // rejected; dropping focus lets the field redraw the clamped value.
                if (!Mathf.Approximately(clamped, parsed))
                    GUIUtility.keyboardControl = 0;
                value = clamped;
            }

            value = GUI.HorizontalSlider(sliderRect, value, min, max);

            EditorGUILayout.EndHorizontal();
            CaptureField(label);
            GUILayout.Space(3f);
            return value;
        }

        /// <summary>
        /// Accepts either decimal separator. Fields print invariant text, but a user on a
        /// comma-decimal system types "0,35", which invariant parsing drops silently; the
        /// old current-culture parse had the mirror bug and read "0.35" as 35 because the
        /// dot is that culture's thousands separator.
        /// </summary>
        static bool TryParseTypedValue(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }

        public static int IntSliderRow(string label, int value, int min, int max)
        {
            int newValue = Mathf.RoundToInt(SliderRow(label, value, min, max, "0"));
            return newValue;
        }

        public static int PopupRow(string label, int index, string[] options)
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            DrawFieldLabel(label, GUILayout.Width(LabelWidth));
            index = EditorGUILayout.Popup(index, options);
            EditorGUILayout.EndHorizontal();
            CaptureField(label);
            GUILayout.Space(3f);
            return index;
        }

        public static bool ToggleRow(string label, bool value)
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            DrawFieldLabel(label, GUILayout.Width(LabelWidth));
            value = EditorGUILayout.Toggle(value, GUILayout.Width(20f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            CaptureField(label);
            GUILayout.Space(3f);
            return value;
        }

        public static UnityEngine.Object ObjectRow(string label, UnityEngine.Object value, System.Type type, bool allowScene = false)
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            DrawFieldLabel(label, GUILayout.Width(LabelWidth));
            value = EditorGUILayout.ObjectField(value, type, allowScene);
            EditorGUILayout.EndHorizontal();
            CaptureField(label);
            GUILayout.Space(3f);
            return value;
        }

        public static string TextRow(string label, string value)
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            DrawFieldLabel(label, GUILayout.Width(LabelWidth));
            value = EditorGUILayout.TextField(value);
            EditorGUILayout.EndHorizontal();
            CaptureField(label);
            GUILayout.Space(3f);
            return value;
        }

        // ------------------------------------------------------------------ widgets
        public static void StatusPill(string text, Color color, float width = 84f,
            string tooltip = null)
        {
            EnsureStyles();
            Rect r = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(width));
            GUI.DrawTexture(r, Tex(new Color(color.r, color.g, color.b, 0.16f)), ScaleMode.StretchToFill);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3f, r.height), color);
            if (!s_pillStyles.TryGetValue(color, out var style))
            {
                style = new GUIStyle(s_state.PillLabel) { normal = { textColor = color } };
                s_pillStyles[color] = style;
            }
            GUI.Label(r, new GUIContent(text, tooltip), style);
        }

        public static void StatBlock(string value, string label, Color? valueColor = null)
        {
            EnsureStyles();
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(64f));
            GUIStyle vs;
            if (valueColor.HasValue)
            {
                if (!s_statValueStyles.TryGetValue(valueColor.Value, out vs))
                {
                    vs = new GUIStyle(s_state.StatValue) { normal = { textColor = valueColor.Value } };
                    s_statValueStyles[valueColor.Value] = vs;
                }
            }
            else
            {
                vs = s_state.StatValue;
            }
            GUILayout.Label(value, vs);
            GUILayout.Label(label, s_state.StatLabel);
            EditorGUILayout.EndVertical();
        }

        public static bool BigButton(string label)
        {
            EnsureStyles();
            return GUILayout.Button(label, s_state.AccentBtn);
        }

        public static bool Chip(string label, bool active)
        {
            EnsureStyles();
            return GUILayout.Button(label, active ? s_state.ChipBtnActive : s_state.ChipBtn);
        }

        /// <summary>Selectable preset card used on Start and Fracture.</summary>
        public static bool PresetChip(FracturePattern pattern, string label, bool active)
        {
            EnsureStyles();
            Texture2D icon = GetPatternIcon(pattern);
            if (icon == null)
                return DrawGlyphPresetChip(pattern, label, active);

            const float cardWidth = 94f;
            const float cardHeight = 118f;
            Rect rect = GUILayoutUtility.GetRect(cardWidth, cardHeight,
                GUILayout.Width(cardWidth), GUILayout.Height(cardHeight));
            EditorGUI.DrawRect(rect, active ? AccentSoft : PanelLight);
            bool clicked = GUI.Button(rect, new GUIContent(string.Empty,
                $"{label}: {PatternGlyphTooltip(pattern)}"), GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                // The source PNGs intentionally contain a transparent outer margin, so the
                // actual dark card sits cleanly inside this selectable tile without cropping.
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
                if (active)
                {
                    const float border = 2f;
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, border), Accent);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), Accent);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, border, rect.height), Accent);
                    EditorGUI.DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), Accent);
                }
            }
            return clicked;
        }



        /// <summary>
        /// The tile that turns the current preset into a saved, reusable profile. It lives
        /// in the pattern grid because that is where the author has just decided what the
        /// object is made from, and naming it is the natural next step from there.
        /// </summary>
        public static bool CreateProfileChip()
        {
            EnsureStyles();
            if (s_state.s_newProfileGlyphStyle == null)
            {
                s_state.s_newProfileGlyphStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 26,
                    fontStyle = FontStyle.Bold
                };
            }
            if (s_state.s_namedProfileTileStyle == null) BuildNamedProfileTileStyle();

            const float cardWidth = 94f;
            const float cardHeight = 118f;
            Rect rect = GUILayoutUtility.GetRect(cardWidth, cardHeight,
                GUILayout.Width(cardWidth), GUILayout.Height(cardHeight));

            bool clicked = GUI.Button(rect, new GUIContent(string.Empty,
                "Save the current settings as a reusable Destruction Profile asset"),
                GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                Rect card = new Rect(rect.x + 6f, rect.y + 6f,
                    rect.width - 12f, rect.height - 12f);
                DrawRoundedRect(card, 6f, new Color(0.05f, 0.05f, 0.05f, 1f));

                bool hover = rect.Contains(Event.current.mousePosition);
                Color tint = hover ? Accent : TextDim;
                s_state.s_newProfileGlyphStyle.normal.textColor = tint;
                s_state.s_namedProfileTileStyle.normal.textColor = tint;

                var glyphRect = new Rect(card.x, card.y + 18f, card.width, 34f);
                GUI.Label(glyphRect, "+", s_state.s_newProfileGlyphStyle);
                var labelRect = new Rect(card.x, card.y + 54f,
                    card.width, card.height - 58f);
                GUI.Label(labelRect, "CREATE NEW\nFROM SELECTED", s_state.s_namedProfileTileStyle);
            }
            return clicked;
        }

        static void BuildNamedProfileTileStyle()
        {
            s_state.s_namedProfileTileStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 9,
                richText = false,
                padding = new RectOffset(6, 6, 4, 4)
            };
        }

        /// <summary>
        /// A saved profile shown as a tile in the pattern grid, in exactly the preset card's
        /// footprint so the row reads as one set of choices. Preset art would be misleading
        /// here — a named profile is not a material, it is whatever the author configured —
        /// so the tile is a plain dark card carrying the asset's own name, wrapped over as
        /// many lines as it needs at a smaller size than the preset captions.
        /// </summary>
        public static bool NamedProfileChip(string profileName, bool active)
        {
            EnsureStyles();
            if (s_state.s_namedProfileTileStyle == null) BuildNamedProfileTileStyle();

            const float cardWidth = 94f;
            const float cardHeight = 118f;
            Rect rect = GUILayoutUtility.GetRect(cardWidth, cardHeight,
                GUILayout.Width(cardWidth), GUILayout.Height(cardHeight));

            bool clicked = GUI.Button(rect,
                new GUIContent(string.Empty, $"{profileName}: saved Destruction Profile"),
                GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                // Inset by the same transparent margin the preset PNGs carry, so this card
                // lines up with them instead of filling its cell edge to edge.
                Rect card = new Rect(rect.x + 6f, rect.y + 6f,
                    rect.width - 12f, rect.height - 12f);
                DrawRoundedRect(card, 6f, new Color(0.05f, 0.05f, 0.05f, 1f));

                s_state.s_namedProfileTileStyle.normal.textColor = active ? Accent : TextDim;
                GUI.Label(card, profileName, s_state.s_namedProfileTileStyle);

                if (active)
                {
                    const float border = 2f;
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, border), Accent);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), Accent);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, border, rect.height), Accent);
                    EditorGUI.DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), Accent);
                }
            }
            return clicked;
        }

        /// <summary>Filled rectangle with rounded corners, drawn from plain rects: the
        /// corners are stepped in per row, which at this radius reads as a clean curve and
        /// needs no texture asset.</summary>
        static void DrawRoundedRect(Rect rect, float radius, Color color)
        {
            radius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y + radius, rect.width, rect.height - radius * 2f),
                color);
            for (float y = 0f; y < radius; y += 1f)
            {
                // Horizontal inset of the corner at this distance from the flat edge.
                float inset = radius - Mathf.Sqrt(Mathf.Max(0f,
                    radius * radius - (radius - y - 1f) * (radius - y - 1f)));
                EditorGUI.DrawRect(
                    new Rect(rect.x + inset, rect.y + y, rect.width - inset * 2f, 1f), color);
                EditorGUI.DrawRect(
                    new Rect(rect.x + inset, rect.yMax - y - 1f, rect.width - inset * 2f, 1f),
                    color);
            }
        }

        static bool DrawGlyphPresetChip(FracturePattern pattern, string label, bool active)
        {
            EnsurePresetChipStyles();
            float width = Mathf.Max(82f, s_state.ChipBtn.CalcSize(new GUIContent(label)).x + 26f);
            Rect rect = GUILayoutUtility.GetRect(width, 24f, GUILayout.Width(width));
            bool clicked = GUI.Button(rect,
                new GUIContent(string.Empty, $"{label}: {PatternGlyphTooltip(pattern)}"),
                active ? s_state.ChipBtnActive : s_state.ChipBtn);

            if (Event.current.type == EventType.Repaint)
            {
                Rect glyphRect = new Rect(rect.x + 5f, rect.y + 2f, 16f, rect.height - 4f);
                Rect labelRect = new Rect(rect.x + 22f, rect.y + 1f, rect.width - 25f, rect.height - 2f);
                GUI.Label(glyphRect, PatternGlyph(pattern),
                    active ? s_state.s_presetGlyphActiveStyle : s_state.s_presetGlyphStyle);
                GUI.Label(labelRect, label,
                    active ? s_state.s_presetLabelActiveStyle : s_state.s_presetLabelStyle);
            }
            return clicked;
        }

        static Texture2D GetPatternIcon(FracturePattern pattern)
        {
            if (s_patternIcons.TryGetValue(pattern, out var cached) && cached != null)
                return cached;

            string fileName;
            switch (pattern)
            {
                case FracturePattern.RadialGlass: fileName = "QD_Preset_Glass"; break;
                case FracturePattern.DirectionalWood: fileName = "QD_Preset_Wood"; break;
                case FracturePattern.BrickCells: fileName = "QD_Preset_Brick"; break;
                case FracturePattern.LayeredSlabs: fileName = "QD_Preset_Slabs"; break;
                case FracturePattern.ColumnarPillars: fileName = "QD_Preset_Pillars"; break;
                case FracturePattern.ImpactShatter: fileName = "QD_Preset_Impact"; break;
                default: fileName = "QD_Preset_Concrete"; break;
            }

            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/QuickDestruction/Editor/Icons/" + fileName + ".png");
            s_patternIcons[pattern] = icon;
            return icon;
        }

        static void EnsurePresetChipStyles()
        {
            if (s_state.s_presetGlyphStyle != null) return;
            s_state.s_presetGlyphStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = TextFaint }
            };
            s_state.s_presetGlyphActiveStyle = new GUIStyle(s_state.s_presetGlyphStyle)
            {
                normal = { textColor = Accent }
            };
            s_state.s_presetLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TextDim }
            };
            s_state.s_presetLabelActiveStyle = new GUIStyle(s_state.s_presetLabelStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Accent }
            };
        }

        static string PatternGlyph(FracturePattern pattern)
        {
            switch (pattern)
            {
                case FracturePattern.RadialGlass: return "✦";
                case FracturePattern.DirectionalWood: return "≋";
                case FracturePattern.BrickCells: return "▤";
                case FracturePattern.LayeredSlabs: return "≡";
                case FracturePattern.ColumnarPillars: return "▥";
                case FracturePattern.ImpactShatter: return "✹";
                default: return "▦";
            }
        }

        static string PatternGlyphTooltip(FracturePattern pattern)
        {
            switch (pattern)
            {
                case FracturePattern.RadialGlass: return "radial glass shatter";
                case FracturePattern.DirectionalWood: return "directional wood grain";
                case FracturePattern.BrickCells: return "brick-cell grid";
                case FracturePattern.LayeredSlabs: return "layered slab cuts";
                case FracturePattern.ColumnarPillars: return "columnar pillar cuts";
                case FracturePattern.ImpactShatter: return "impact-focused shatter";
                default: return "uniform concrete grid";
            }
        }

        /// <summary>Horizontal progress-style meter with label, used for live values.</summary>
        public static void Meter(string label, float value01, Color color, string rightText = null)
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            DrawFieldLabel(label, GUILayout.Width(LabelWidth));
            Rect r = GUILayoutUtility.GetRect(60f, 10f, GUILayout.ExpandWidth(true));
            r.y += 3f; r.height = 6f;
            GUI.DrawTexture(r, Tex(PanelLight), ScaleMode.StretchToFill);
            var fill = new Rect(r.x, r.y, r.width * Mathf.Clamp01(value01), r.height);
            GUI.DrawTexture(fill, Tex(color), ScaleMode.StretchToFill);
            GUILayout.Label(rightText ?? value01.ToString("0.00"), s_state.ValueLabel, GUILayout.Width(56f));
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }
    }
}
