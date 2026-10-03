using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class GenericRenderer : FieldRenderer
    {
        public override bool Handles(SerializedProperty p) => p.propertyType == SerializedPropertyType.Generic && !p.isArray;

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(p, label, true);
            EditorGUILayout.BeginHorizontal();
        }
    }
#endif
}