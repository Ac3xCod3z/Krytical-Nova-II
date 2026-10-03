using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class StringRenderer : FieldRenderer
    {
        public override bool Handles(SerializedProperty p) => p.propertyType == SerializedPropertyType.String;

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(LabelWidth));
            p.stringValue = EditorGUILayout.DelayedTextField(p.stringValue);
        }
    }
#endif
}