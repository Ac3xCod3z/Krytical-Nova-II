using PSD.Creator.Objects;
using UnityEngine;

#if UNITY_EDITOR   
using UnityEditor;
#endif

namespace PSD.Preferences.Theming
{
    [CreateAssetMenu(fileName = "Theme", menuName = "CTFX2/Settings/Theming/Theme")]
    public class ThemeObject : ScriptableObject
    {
        [Category("Backgrounds")]
        [ThemeColor, Label("Default")] public Color background = new Color(0.15f, 0.10f, 0.25f, 0.25f);
        [ThemeColor, Label("Content")] public Color contentBackground = new Color(0.12f, 0.08f, 0.20f, 0.4f);
        [ThemeColor, Label("Error")] public Color errorBackground = new Color(0.30f, 0.08f, 0.08f, 0.4f);
        [ThemeColor, Label("Success")] public Color successBackground = new Color(0.08f, 0.25f, 0.12f, 0.4f);

        [Category("Text")]
        [ThemeColor, Label("Title")] public Color titleText = new Color(0.92f, 0.88f, 1.00f);
        [ThemeColor, Label("Subtitle")] public Color subtitleText = new Color(0.65f, 0.60f, 0.80f);
        [ThemeColor, Label("Content")] public Color contentText = new Color(0.80f, 0.75f, 0.95f);
        [ThemeColor, Label("Subtle")] public Color subtleText = new Color(0.5f, 0.5f, 0.5f);

        [Category("Accents")]
        [ThemeColor, Label("Primary")] public Color accent = new Color(0.55f, 0.30f, 0.95f, 1f);
        [ThemeColor, Label("Border")] public Color accentBorder = new Color(0.55f, 0.30f, 0.95f, 0.4f);
        [ThemeColor, Label("Subtle")] public Color accentSubtle = new Color(0.55f, 0.30f, 0.95f, 0.6f);
        [ThemeColor, Label("Border Subtle")] public Color accentBorderSubtle = new Color(0.55f, 0.30f, 0.95f, 0.3f);

        [Category("Error")]
        [ThemeColor, Label("Accent")] public Color errorAccent = new Color(0.90f, 0.25f, 0.25f, 1f);
        [ThemeColor, Label("Strip")] public Color errorStrip = new Color(0.90f, 0.30f, 0.30f, 0.6f);
        [ThemeColor, Label("Text")] public Color errorText = new Color(1.00f, 0.55f, 0.55f);
        [ThemeColor, Label("Field Text")] public Color errorFieldText = new Color(0.85f, 0.50f, 0.50f);

        [Category("Success")]
        [ThemeColor, Label("Accent")] public Color successAccent = new Color(0.25f, 0.80f, 0.40f, 1f);
        [ThemeColor, Label("Text")] public Color successText = new Color(0.45f, 0.90f, 0.55f);

        [Category("Shadow")]
        [ThemeColor, Label("Default")] public Color shadow = new Color(0.20f, 0.05f, 0.40f);
        [ThemeColor, Label("Heavy")] public Color buttonShadowHeavy = new Color(0f, 0f, 0f, 0.30f);
        [ThemeColor, Label("Medium")] public Color buttonShadowMid = new Color(0f, 0f, 0f, 0.18f);
        [ThemeColor, Label("Light")] public Color buttonShadowLight = new Color(0f, 0f, 0f, 0.08f);

        [Category("Gradient Button")]
        [ThemeColor, Label("Left Normal")] public Color btnGradientLeftNormal = new Color(0.38f, 0.35f, 0.78f);
        [ThemeColor, Label("Left Hover")] public Color btnGradientLeftHover = new Color(0.45f, 0.42f, 0.88f);
        [ThemeColor, Label("Left Active")] public Color btnGradientLeftActive = new Color(0.30f, 0.28f, 0.65f);
        [ThemeColor, Label("Right Normal")] public Color btnGradientRightNormal = new Color(0.114f, 0.62f, 0.459f);
        [ThemeColor, Label("Right Hover")] public Color btnGradientRightHover = new Color(0.15f, 0.72f, 0.53f);
        [ThemeColor, Label("Right Active")] public Color btnGradientRightActive = new Color(0.09f, 0.52f, 0.38f);

        [Category("Navigation")]
        [ThemeColor, Label("Separator")] public Color navigationSeparator = new Color(0.5f, 0.5f, 0.5f, 0.15f);
        [ThemeColor, Label("Default")] public Color separator = new Color(0.5f, 0.5f, 0.5f, 0.2f);
        [ThemeColor, Label("Selected")] public Color categoryTabSelected = new Color(0.35f, 0.35f, 0.35f);
        [ThemeColor, Label("Underline")] public Color categoryUnderline = Color.white;
        [ThemeColor, Label("Category Text")] public Color categoryTabText = new Color(0.7f, 0.7f, 0.7f);

        [Category("Page Pills")]
        [ThemeColor, Label("Active")] public Color pillActive = new Color(0.5f, 0.7f, 1f);
        [ThemeColor, Label("Active Invalid")] public Color pillActiveInvalid = new Color(1f, 0.6f, 0.4f);
        [ThemeColor, Label("Invalid")] public Color pillInvalid = new Color(1f, 0.85f, 0.85f);
        [ThemeColor, Label("Disabled Content")] public Color pillDisabledContent = new Color(0.7f, 0.7f, 0.7f, 0.7f);

        [Category("Badge")]
        [ThemeColor, Label("Text")] public Color badgeText = new Color(0.498f, 0.467f, 0.867f);
        [ThemeColor, Label("Background")] public Color badgeBackground = new Color(0.235f, 0.204f, 0.718f, 0.15f);
    }

    public class ThemeColorAttribute : PropertyAttribute { }

    public class LabelAttribute : PropertyAttribute
    {
        public string Text;
        public LabelAttribute(string text) => Text = text;
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ThemeColorAttribute))]
    public class ThemeColorDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            LabelAttribute labelAttr = (LabelAttribute)System.Attribute.GetCustomAttribute(fieldInfo, typeof(LabelAttribute));
            GUIContent displayLabel = labelAttr != null ? new GUIContent(labelAttr.Text) : label;

            EditorGUI.BeginProperty(position, displayLabel, property);

            Rect boxRect = new Rect(position.x, position.y, position.width, position.height - 2f);
            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

            Vector2 padding = new Vector2(4f, 2f);
            float labelWidth = EditorGUIUtility.labelWidth / 2;
            float colorWidth = position.width - labelWidth - padding.x * 3f;

            Rect labelRect = new Rect(position.x + padding.x, position.y + padding.y, labelWidth, position.height - 2f);
            Rect colorRect = new Rect(position.x + labelWidth + padding.x * 2f, position.y + padding.y, colorWidth, position.height - 2f - padding.y * 2f);

            EditorGUI.LabelField(labelRect, displayLabel, EditorStyles.miniBoldLabel);
            EditorGUI.PropertyField(colorRect, property, GUIContent.none);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + 6f;
        }
    }
#endif
}