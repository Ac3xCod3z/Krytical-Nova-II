using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>Adds / finds ScriptableRendererFeatures on a renderer data asset (sub-asset + feature map kept in sync).</summary>
    public static class RendererFeatureUtil
    {
        /// <summary>Returns the existing feature of type T, or creates it at the end of the list. <paramref name="configure"/> runs on both.</summary>
        public static T Ensure<T>(ScriptableRendererData renderer, string name, Action<T> configure) where T : ScriptableRendererFeature
        {
            var feature = renderer.rendererFeatures.OfType<T>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<T>();
                feature.name = name;
                AssetDatabase.AddObjectToAsset(feature, renderer);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var so = new SerializedObject(renderer);
                var features = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            configure?.Invoke(feature);
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            return feature;
        }

        /// <summary>Enables or disables an existing feature of type T (no-op when absent).</summary>
        public static void SetActive<T>(ScriptableRendererData renderer, bool active) where T : ScriptableRendererFeature
        {
            foreach (var f in renderer.rendererFeatures.OfType<T>())
            {
                f.SetActive(active);
                EditorUtility.SetDirty(f);
            }
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }
    }
}
