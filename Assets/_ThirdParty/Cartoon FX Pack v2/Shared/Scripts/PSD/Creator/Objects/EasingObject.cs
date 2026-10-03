using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Objects
{
    [CreateAssetMenu(fileName = "Easing", menuName = "CTFX2/Easing", order = 1)]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class EasingObject : ScriptableObject
    {
        [Category("Sampling")]
        public AnimationCurve Easing = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EasingObject), true)]
    [CanEditMultipleObjects]
    public class EasingObjectEditor : Editor
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

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                EditorGUILayout.PropertyField(property, true);

            EditorGUILayout.Space(2);
        }
    }
#endif
}