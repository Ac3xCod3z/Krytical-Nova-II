using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class DefaultRenderer : FieldRenderer
    {
        public override bool Handles(SerializedProperty p) => true;

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(LabelWidth));
            EditorGUILayout.PropertyField(p, GUIContent.none, true);
        }
    }
#endif
}