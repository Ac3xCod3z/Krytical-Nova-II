using UnityEngine;
using PSD.Creator.Inspector.Utility;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class RangeRenderer : FieldRenderer
    {
        private readonly float? Min, Max;
        public RangeRenderer() { }
        public RangeRenderer(float min, float max) { Min = min; Max = max; }

        public override bool Handles(SerializedProperty p) =>
            (p.propertyType == SerializedPropertyType.Float ||
             p.propertyType == SerializedPropertyType.Integer)
            && p.GetAttribute(out RangeAttribute _);

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            p.GetAttribute(out RangeAttribute range);
            bool isInt = p.propertyType == SerializedPropertyType.Integer;
            float current = isInt ? p.intValue : p.floatValue;

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(LabelWidth));

            EditorGUI.BeginChangeCheck();
            float newVal = DragBuffer.Slider(current, Min ?? range.min, Max ?? range.max);
            if (EditorGUI.EndChangeCheck())
            {
                if (isInt) p.intValue = Mathf.RoundToInt(newVal);
                else p.floatValue = newVal;
                Apply(p, false);
            }
        }
    }
#endif
}