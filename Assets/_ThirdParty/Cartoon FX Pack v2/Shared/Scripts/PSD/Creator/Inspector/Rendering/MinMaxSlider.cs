using UnityEngine;
using PSD.Creator.Inspector.Utility;
using PSD.Creator.Data.Attributes;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class MinMaxSliderRenderer : FieldRenderer
    {
        private readonly float? Min, Max;
        public MinMaxSliderRenderer() { }
        public MinMaxSliderRenderer(float min, float max) { Min = min; Max = max; }

        public override bool Handles(SerializedProperty p) =>
            (p.propertyType == SerializedPropertyType.Vector2 ||
             p.propertyType == SerializedPropertyType.Vector2Int)
            && (Min.HasValue || p.GetAttribute(out MinMaxSliderAttribute _));

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            float min, max;
            if (Min.HasValue)
            {
                min = Min.Value;
                max = Max.Value;
            }
            else
            {
                p.GetAttribute(out MinMaxSliderAttribute attr);
                min = attr.Range.x;
                max = attr.Range.y;
            }

            bool isInt = p.propertyType == SerializedPropertyType.Vector2Int;
            float currentMin = isInt ? p.vector2IntValue.x : p.vector2Value.x;
            float currentMax = isInt ? p.vector2IntValue.y : p.vector2Value.y;
            var currentVec = new Vector2(currentMin, currentMax);

            float fieldW = EditorGUIUtility.fieldWidth;
            var fieldWidth = GUILayout.Width(fieldW / 2 * (EditorGUI.indentLevel + 1));

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(EditorGUIUtility.labelWidth / 2));

            EditorGUI.BeginChangeCheck();
            float newMin = isInt
                ? EditorGUILayout.DelayedIntField((int)currentMin, fieldWidth)
                : EditorGUILayout.DelayedFloatField((float)Math.Round(currentMin, 2), fieldWidth);
            if (EditorGUI.EndChangeCheck())
            {
                newMin = Mathf.Clamp(newMin, min, max);
                CustomApply(new Vector2(newMin, newMin > currentMax ? newMin : currentMax));
                return;
            }

            EditorGUI.BeginChangeCheck();
            Vector2 committed = DragBuffer.MinMaxSlider(currentVec, min, max, out Vector2 display);
            if (EditorGUI.EndChangeCheck()) { CustomApply(committed); return; }

            EditorGUI.BeginChangeCheck();
            float newMax = isInt
                ? EditorGUILayout.DelayedIntField((int)display.y, fieldWidth)
                : EditorGUILayout.DelayedFloatField((float)Math.Round(display.y, 2), fieldWidth);
            if (EditorGUI.EndChangeCheck())
            {
                newMax = Mathf.Clamp(newMax, min, max);
                CustomApply(new Vector2(newMax < currentMin ? newMax : currentMin, newMax));
                return;
            }

            void CustomApply(Vector2 v)
            {
                if (isInt) p.vector2IntValue = new Vector2Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));
                else p.vector2Value = v;
                Apply(p, false);
            }
        }
    }
#endif
}