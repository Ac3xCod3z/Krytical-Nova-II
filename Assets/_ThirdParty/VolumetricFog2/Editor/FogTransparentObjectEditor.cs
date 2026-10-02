using UnityEngine;
using UnityEditor;

namespace VolumetricFogAndMist2 {

    [CustomEditor(typeof(FogTransparentObject))]
    [CanEditMultipleObjects]
    public class FogTransparentObjectEditor : Editor {

        SerializedProperty autoFogVolume, fogVolume;

        private void OnEnable() {
            autoFogVolume = serializedObject.FindProperty("autoFogVolume");
            fogVolume = serializedObject.FindProperty("fogVolume");
        }


        public override void OnInspectorGUI() {

            serializedObject.Update();

            EditorGUILayout.PropertyField(autoFogVolume);
            if (!autoFogVolume.boolValue || autoFogVolume.hasMultipleDifferentValues) {
                EditorGUILayout.PropertyField(fogVolume);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

}
