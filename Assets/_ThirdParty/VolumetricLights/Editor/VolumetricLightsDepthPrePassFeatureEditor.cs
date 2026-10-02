using UnityEngine;
using UnityEditor;

namespace VolumetricLights
{

    [CustomEditor(typeof(VolumetricLightsDepthPrePassFeature))]
    public class VolumetricLightsDepthPrePassFeatureEditor : Editor
    {

        SerializedProperty transparentLayerMask, useOptimizedDepthOnlyShader, transparentCullMode;
        SerializedProperty alphaCutoutLayerMask, alphaCutOff, semiTransparentCullMode;
        SerializedProperty ignoreReflectionProbes, ignoreOverlayCamera;

        private void OnEnable()
        {
            transparentLayerMask = serializedObject.FindProperty("transparentLayerMask");
            useOptimizedDepthOnlyShader = serializedObject.FindProperty("useOptimizedDepthOnlyShader");
            transparentCullMode = serializedObject.FindProperty("transparentCullMode");
            alphaCutoutLayerMask = serializedObject.FindProperty("alphaCutoutLayerMask");
            alphaCutOff = serializedObject.FindProperty("alphaCutOff");
            semiTransparentCullMode = serializedObject.FindProperty("semiTransparentCullMode");
            ignoreReflectionProbes = serializedObject.FindProperty("ignoreReflectionProbes");
            ignoreOverlayCamera = serializedObject.FindProperty("ignoreOverlayCamera");
        }

        public override void OnInspectorGUI() {

            serializedObject.Update();

            EditorGUILayout.PropertyField(transparentLayerMask);
            if (transparentLayerMask.intValue != 0) {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(useOptimizedDepthOnlyShader, new GUIContent("Use Optimized Shader"));
                if (useOptimizedDepthOnlyShader.boolValue) {
                    EditorGUILayout.PropertyField(transparentCullMode, new GUIContent("Cull Mode"));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(alphaCutoutLayerMask);
            if (alphaCutoutLayerMask.intValue != 0) {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(alphaCutOff);
                EditorGUILayout.PropertyField(semiTransparentCullMode, new GUIContent("Cull Mode Fallback"));
                EditorGUI.indentLevel--;
            }

            if (transparentLayerMask.intValue == 0 && alphaCutoutLayerMask.intValue == 0) {
                EditorGUILayout.HelpBox("Both layer masks are set to Nothing. The depth pre-pass will not render anything.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(ignoreReflectionProbes);
            EditorGUILayout.PropertyField(ignoreOverlayCamera);

            serializedObject.ApplyModifiedProperties();
        }

    }
}
