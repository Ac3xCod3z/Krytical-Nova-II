using UnityEngine;
using PSD.Creator.Data.Attributes;
using PSD.Creator.Inspector.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Utility
{
#if UNITY_EDITOR
    public static class InspectorUtility
    {
        public static Texture2D MakeGradientTex(int width, int height, Color colA, Color colB)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pix = new Color32[width * height];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float t = (float)x / (width - 1);
                    Color col = Color.Lerp(colA, colB, t);
                    if (y < 2) col = Color.Lerp(col, Color.black, 0.35f);
                    if (y == height - 1) col = Color.Lerp(col, Color.white, 0.15f);
                    pix[y * width + x] = col;
                }

            tex.SetPixels32(pix);
            tex.Apply();
            return tex;
        }

        public static void DrawProperty(SerializedObject target, string propertyPath, string name) => DrawProperty(target.FindProperty(propertyPath), target.FindProperty(propertyPath), name);
        public static void DrawProperty(SerializedProperty property, SerializedProperty owner, string name, PropertyOverride? overrides = null)
        {
            bool enabled = GUI.enabled;

            if (!PassesGates(property)) return;

            string label = (overrides.HasValue && !string.IsNullOrEmpty(overrides.Value.Label)) ? overrides.Value.Label
                : (property.GetAttribute(out LabelAttribute labelAttr)
                    ? labelAttr.Label
                    : ObjectNames.NicifyVariableName(name));

            SerializedProperty optionalProp = null;
            if (property.GetAttribute(out OptionalAttribute optionalAttr))
            {
                optionalProp = property.FindRelativeProperty(optionalAttr.EnabledProperty);
                if (optionalProp != null && optionalProp.propertyType == SerializedPropertyType.Boolean)
                    if (!optionalProp.boolValue) GUI.enabled = false;
            }

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUILayout.HorizontalScope())
            {
                FieldRenderer renderer;
                if (overrides.HasValue)
                {
                    switch (overrides.Value.Override)
                    {
                        case PropertyOverride.EOverride.Range:
                            renderer = new RangeRenderer(overrides.Value.Min, overrides.Value.Max);
                            break;
                        case PropertyOverride.EOverride.MinMaxRange:
                            renderer = new MinMaxSliderRenderer(overrides.Value.Min, overrides.Value.Max);
                            break;
                        case PropertyOverride.EOverride.Default:
                        default:
                            renderer = FieldRendererRegistry.For(property);
                            break;
                    }
                }
                else
                    renderer = FieldRendererRegistry.For(property);
                renderer.Render(property, owner, new GUIContent(label, property.tooltip), enabled);

                if (optionalProp != null)
                {
                    GUI.enabled = enabled;
                    optionalProp.boolValue = GUILayout.Toggle(optionalProp.boolValue, GUIContent.none, GUILayout.Width(EditorGUIUtility.singleLineHeight));
                }
            }

            if (property.GetAttributes(out RequiredAttribute[] validations))
            {
                foreach (var validation in validations)
                    if (!validation.Validate(property, out var message, out var type))
                        EditorGUILayout.HelpBox(message, type);
            }

            EditorGUILayout.Space(2);
            GUI.enabled = enabled;
        }

        public static void DrawHeader(string title, string subtitle = null)
        {
            EditorGUILayout.Separator();
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
                if (!string.IsNullOrEmpty(subtitle))
                    EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel);
            }
        }

        public static void DrawSubHeader(string title)
        {
            EditorGUI.indentLevel++;
            using (new EditorGUILayout.VerticalScope("box"))
                EditorGUILayout.LabelField(title, EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        public static void BeginBox(string label)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(2);
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.LabelField(label, EditorStyles.whiteBoldLabel);
        }

        public static void EndBox()
        {
            EditorGUILayout.EndVertical();
        }

        public static void Separate()
        {
            EditorGUILayout.Separator();
            EditorGUILayout.Separator();
        }

        private static bool PassesGates(SerializedProperty property)
        {
            if (property.GetAttribute(out ShowIfAttribute showIf) && !showIf.condition.Show(property))
                return false;
            if (property.GetAttribute(out NonNullableCheckAttribute nonNull) && !nonNull.Show(property))
                return false;
            return true;
        }
    }

    public readonly struct PropertyOverride
    {
        public readonly string Label;
        public enum EOverride { Default, Range, MinMaxRange }
        public readonly EOverride Override;
        public readonly float Min, Max;

        public PropertyOverride(string label, EOverride o, float min, float max)
        { Label = label; Override = o; Min = min; Max = max; }

        public static PropertyOverride WithLabel(string label) => new(label, EOverride.Default, 0, 0);
        public static PropertyOverride WithRange(float min, float max, string label = null) => new(label, EOverride.Range, min, max);
        public static PropertyOverride WithMinMaxRange(float min, float max, string label = null) => new(label, EOverride.MinMaxRange, min, max);

        public static implicit operator PropertyOverride(string label) => WithLabel(label);
    }
#endif
}