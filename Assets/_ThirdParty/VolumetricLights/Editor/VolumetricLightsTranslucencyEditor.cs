using UnityEditor;

namespace VolumetricLights {

    [CustomEditor(typeof(VolumetricLightsTranslucency))]
    public class VolumetricLightsTranslucencyEditor : Editor {

        SerializedProperty preserveOriginalShader;
        SerializedProperty intensityMultiplier;
        SerializedProperty baseTexturePropertyName;
        SerializedProperty baseColorPropertyName;

        private void OnEnable () {
            preserveOriginalShader = serializedObject.FindProperty("preserveOriginalShader");
            intensityMultiplier = serializedObject.FindProperty("intensityMultiplier");
            baseTexturePropertyName = serializedObject.FindProperty("baseTexturePropertyName");
            baseColorPropertyName = serializedObject.FindProperty("baseColorPropertyName");
        }

        public override void OnInspectorGUI () {

            serializedObject.Update();
            EditorGUILayout.PropertyField(preserveOriginalShader);

            if (!preserveOriginalShader.boolValue) {
                EditorGUILayout.PropertyField(intensityMultiplier);
                EditorGUILayout.PropertyField(baseTexturePropertyName);
                EditorGUILayout.PropertyField(baseColorPropertyName);
            }

            serializedObject.ApplyModifiedProperties();
        }

    }
}