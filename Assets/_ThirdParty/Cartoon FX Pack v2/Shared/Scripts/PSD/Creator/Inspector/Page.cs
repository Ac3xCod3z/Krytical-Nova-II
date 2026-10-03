using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using PSD.Creator.Objects;
using PSD.Creator.Inspector.Utility;
using PSD.Preferences.Theming;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector
{
#if UNITY_EDITOR
    public abstract class Page
    {
        protected abstract string Settings { get; }

        protected abstract bool Toggleable { get; }

        protected ReferencesObject GetReferences(SerializedObject target)
            => target.FindProperty("reference.references")?.objectReferenceValue as ReferencesObject;

        public abstract string Title { get; }
        public virtual string Subtitle => string.Empty;

        public abstract string Category { get; }

        private Vector2 scrollPosition;

        public bool IsEnabled(SerializedObject target) => GetProperty(target, "enabled").boolValue;

        public abstract void DrawContent(SerializedObject target);

        protected virtual bool RequiresValidation => false;
        public bool Validate(SerializedObject target) => !RequiresValidation || GetValidationStates(target).All(e => e.valid);
        protected virtual List<(string name, bool valid)> GetValidationStates(SerializedObject target) => new List<(string, bool)>();

        protected SerializedProperty GetProperty(SerializedObject target, string name) => target.FindProperty($"{Settings}.{name}");
        protected void DrawProperty(SerializedObject target, string name) => InspectorUtility.DrawProperty(target, $"{Settings}.{name}", name);

        protected void DrawHeader(string title, string subtitle = null) => InspectorUtility.DrawHeader(title, subtitle);
        protected void DrawSubHeader(string title) => InspectorUtility.DrawSubHeader(title);

        protected void BeginBox(string label) => InspectorUtility.BeginBox(label);
        protected void EndBox() => InspectorUtility.EndBox();

        protected void Separate() => InspectorUtility.Separate();

        private void DrawHeader()
        {
            EditorGUILayout.Space(8);

            bool hasSubtitle = !string.IsNullOrEmpty(Subtitle);
            float boxHeight = hasSubtitle ? 58 : 40;

            Rect boxRect = EditorGUILayout.GetControlRect(false, boxHeight);
            EditorGUI.DrawRect(boxRect, Theme.Background);

            Rect accentRect = new Rect(boxRect.x, boxRect.y, 3, boxRect.height);
            EditorGUI.DrawRect(accentRect, Theme.Accent);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Theme.TitleText }
            };

            float textX = boxRect.x + 12;
            float textW = boxRect.width - 14;

            if (!hasSubtitle)
            {
                Rect titleRect = new Rect(textX, boxRect.y, textW, boxRect.height);
                EditorGUI.LabelField(titleRect, Title, titleStyle);
            }
            else
            {
                Rect titleRect = new Rect(textX, boxRect.y + 6, textW, 22);
                EditorGUI.LabelField(titleRect, Title, titleStyle);

                var subtitleStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    normal = { textColor = Theme.SubtitleText }
                };

                Rect subtitleRect = new Rect(textX, boxRect.y + 30, textW, 20);
                EditorGUI.LabelField(subtitleRect, Subtitle, subtitleStyle);
            }

            EditorGUILayout.Space(6);
        }

        protected virtual void DrawValidationStatus(SerializedObject target)
        {
            if (!RequiresValidation) return;

            bool enabled = GetProperty(target, "enabled").boolValue;
            var errors = GetValidationStates(target);
            var isValid = errors.All(e => e.valid);

            if (enabled && !isValid)
            {
                Rect stripRect = EditorGUILayout.GetControlRect(false, 2);
                EditorGUI.DrawRect(stripRect, Theme.ErrorStrip);

                EditorGUILayout.Space(4);

                Rect errBoxRect = EditorGUILayout.GetControlRect(false, 22);
                EditorGUI.DrawRect(errBoxRect, Theme.ErrorBackground);

                Rect errAccent = new Rect(errBoxRect.x, errBoxRect.y, 3, errBoxRect.height);
                EditorGUI.DrawRect(errAccent, Theme.ErrorAccent);

                var errLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Theme.ErrorText }
                };

                EditorGUI.LabelField(new Rect(errBoxRect.x + 10, errBoxRect.y, errBoxRect.width - 12, errBoxRect.height), "⚠  Validation errors", errLabelStyle);

                EditorGUILayout.Space(3);

                foreach ((string name, bool valid) in errors)
                {
                    if (valid) continue;
                    var fieldStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = Theme.ErrorFieldText }
                    };
                    EditorGUILayout.LabelField($"  • {name} is not assigned.", fieldStyle);
                }
                EditorGUILayout.Space(6);
            }
            else if (enabled && isValid)
            {
                Rect okRect = EditorGUILayout.GetControlRect(false, 22);
                EditorGUI.DrawRect(okRect, Theme.SuccessBackground);

                Rect okAccent = new Rect(okRect.x, okRect.y, 3, okRect.height);
                EditorGUI.DrawRect(okAccent, Theme.SuccessAccent);

                var okStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Theme.SuccessText }
                };

                EditorGUI.LabelField(new Rect(okRect.x + 10, okRect.y, okRect.width - 12, okRect.height), "✓  All required fields completed", okStyle);

                EditorGUILayout.Space(6);
            }
        }

        private bool DrawEffectToggle(SerializedObject target)
        {
            var enabledProperty = GetProperty(target, "enabled");
            if (enabledProperty != null)
            {
                EditorGUILayout.Space(5);

                var boxStyle = new GUIStyle("box")
                {
                    padding = new RectOffset(10, 10, 8, 8)
                };

                EditorGUILayout.BeginVertical(boxStyle);
                EditorGUILayout.BeginHorizontal();

                string icon = enabledProperty.boolValue ? "✓" : "✗";
                var iconColor = enabledProperty.boolValue ? Color.green : Color.red;

                var iconStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = iconColor },
                    fontSize = 14
                };

                EditorGUILayout.LabelField(icon, iconStyle, GUILayout.Width(20));

                enabledProperty.boolValue = EditorGUILayout.ToggleLeft(
                    enabledProperty.boolValue ? "Enabled" : "Disabled",
                    enabledProperty.boolValue,
                    EditorStyles.boldLabel,
                    GUILayout.ExpandWidth(true)
                );

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(8);

            }
            return enabledProperty.boolValue;
        }

        private float contentHeight = 400f;

        public void DrawPage(SerializedObject target)
        {
            DrawHeader();
            DrawValidationStatus(target);
            if (Toggleable && !DrawEffectToggle(target)) return;

            DrawTitle(Title);
            EditorGUILayout.Space(5);

            float scrollHeight = Mathf.Min(contentHeight, 400f);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(scrollHeight));

            EditorGUILayout.BeginVertical();
            DrawContent(target);
            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint)
            {
                float h = GUILayoutUtility.GetLastRect().height;
                if (Mathf.Abs(h - contentHeight) > 1f) contentHeight = h;
            }
            EditorGUILayout.EndScrollView();

            Rect scrollRect = GUILayoutUtility.GetLastRect();
            for (int i = 3; i >= 1; i--)
            {
                Color shadow = Theme.Shadow.WithAlpha(0.06f * i);
                EditorGUI.DrawRect(new Rect(scrollRect.x - i, scrollRect.y - i, scrollRect.width + i * 2, 1), shadow); // top
                EditorGUI.DrawRect(new Rect(scrollRect.x - i, scrollRect.yMax + i - 1, scrollRect.width + i * 2, 1), shadow); // bottom
                EditorGUI.DrawRect(new Rect(scrollRect.x - i, scrollRect.y - i, 1, scrollRect.height + i * 2), shadow); // left
                EditorGUI.DrawRect(new Rect(scrollRect.xMax + i - 1, scrollRect.y - i, 1, scrollRect.height + i * 2), shadow); // right
            }

            EditorGUI.DrawRect(new Rect(scrollRect.x, scrollRect.y, scrollRect.width, 1), Theme.AccentBorder); // top
            EditorGUI.DrawRect(new Rect(scrollRect.x, scrollRect.yMax, scrollRect.width, 1), Theme.AccentBorder); // bottom
            EditorGUI.DrawRect(new Rect(scrollRect.x, scrollRect.y, 1, scrollRect.height), Theme.AccentBorder); // left
            EditorGUI.DrawRect(new Rect(scrollRect.xMax - 1, scrollRect.y, 1, scrollRect.height), Theme.AccentBorder); // right

            EditorGUILayout.Space(6);
        }

        protected void DrawTitle(string title)
        {
            Rect titleRect = EditorGUILayout.GetControlRect(false, 26);

            EditorGUI.DrawRect(titleRect, Theme.ContentBackground);

            Rect accent = new Rect(titleRect.x, titleRect.y, 3, titleRect.height);
            EditorGUI.DrawRect(accent, Theme.AccentSubtle);

            Rect underline = new Rect(titleRect.x, titleRect.yMax, titleRect.width, 2);
            EditorGUI.DrawRect(underline, Theme.AccentBorder);

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Theme.TitleText }
            };

            EditorGUI.LabelField(new Rect(titleRect.x + 10, titleRect.y, titleRect.width - 12, titleRect.height), title, style);

            EditorGUILayout.Space(4);
        }
    }
#endif
}