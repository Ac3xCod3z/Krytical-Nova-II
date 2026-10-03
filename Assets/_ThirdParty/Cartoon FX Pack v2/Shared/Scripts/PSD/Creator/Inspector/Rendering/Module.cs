using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class ModuleRenderer : FieldRenderer
    {
        public override bool Handles(SerializedProperty p) => false;

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            EditorGUILayout.PropertyField(p, GUIContent.none, true);
        }
    }
#endif
}