using UnityEngine;
using PSD.Creator.Inspector.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class CameraShakeRenderer : FieldRenderer
    {
        public override bool Handles(SerializedProperty p) =>
            p.propertyType == SerializedPropertyType.Generic &&
            p.type == nameof(CameraShake.CameraShake);

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            var prop = p.FindPropertyRelative("cameraShake");
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(LabelWidth));
            EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        }
    }
#endif
}