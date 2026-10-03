using UnityEngine;
using PSD.Creator.Data.Attributes;
using PSD.Creator.Inspector.Utility;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Objects
{
    public abstract class ParticleObject : PSDObject
    {
        [Category("Textures")]
        public Texture2D albedo;
        public Texture2D normal;
        public Texture2D dissolve;

        [Category("Sampling")]
        public Vector2 flipbookSize = Vector2.one * 1;

        [Category("Rendering")]
        [Range(0, 2)] public float normalStrength = 1f;
        [Range(0, 1)] public float cutoff = 0.01f;
        [Range(0, 1)] public float softParticles = 1f;
        [HideInInspector] public bool useDitherFade = true;
        [Optional("useDitherFade"), Range(0, 1)] public float ditherFade = 1;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ParticleObject), true)]
    [CanEditMultipleObjects]
    public class ParticleObjectEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var property = serializedObject.GetIterator().Copy();

            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.name == "m_Script")
                {
                    GUI.enabled = false;
                    EditorGUILayout.PropertyField(property, true);
                    GUI.enabled = true;
                    continue;
                }

                DrawProperty(property);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProperty(SerializedProperty property)
        {
            EditorGUILayout.Space(2);

            bool enabled = GUI.enabled;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty optionalProp = null;
                if (property.GetAttribute(out OptionalAttribute optionalAttr))
                {
                    optionalProp = property.FindRelativeProperty(optionalAttr.EnabledProperty);
                    if (optionalProp != null && optionalProp.propertyType == SerializedPropertyType.Boolean)
                        if (!optionalProp.boolValue) GUI.enabled = false;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(property, true);

                    if (optionalProp != null)
                    {
                        GUI.enabled = enabled;
                        optionalProp.boolValue = GUILayout.Toggle(optionalProp.boolValue, GUIContent.none, GUILayout.Width(EditorGUIUtility.singleLineHeight));
                    }
                }
            }

            EditorGUILayout.Space(2);
        }
    }
#endif
}
