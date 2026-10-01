using System.Linq;
using KryticalNova.Items.Visuals;
using KryticalNova.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Project + scene setup for the equipment page. Idempotent.
    ///  - "UIPreview" layer (preview camera renders only this) and a named rendering layer for the preview lights.
    ///  - UIPreview_Renderer: a copy of the PC renderer with no renderer features (no SSAO / comic passes), appended to
    ///    every URP asset so the preview camera can use it by name.
    ///  - Adds GearUIController to the HUD in the open scene (via HudSetup).
    /// </summary>
    public static class EquipmentPreviewSetup
    {
        private const string SourceRenderer = "Assets/Settings/PC_Renderer.asset";
        private const string PreviewRenderer = "Assets/Settings/UIPreview_Renderer.asset";
        private const int PreviewLayerIndex = 8;
        private const int PreviewRenderingLayerIndex = 7;
        private const string PreviewRenderingLayerName = "UI Preview";

        [MenuItem("Krytical Nova/Items/4. Set Up Equipment Preview + HUD")]
        public static void Run()
        {
            EnsureLayers();
            var renderer = EnsurePreviewRenderer();
            if (renderer != null)
            {
                EnsureComicFeature(renderer);
                AddToPipelineAssets(renderer);
            }
            AssetDatabase.SaveAssets();
            HudSetup.SetUpOpenScene();
            Debug.Log("[Items] Equipment preview ready. Press I / E in Play mode.");
        }

        private static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            int existing = -1;
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == CharacterPreviewStage.PreviewLayerName) existing = i;
            if (existing < 0)
            {
                var slot = layers.GetArrayElementAtIndex(PreviewLayerIndex);
                if (!string.IsNullOrEmpty(slot.stringValue))
                    Debug.LogWarning($"[Items] Layer {PreviewLayerIndex} is '{slot.stringValue}'; set a free layer named '{CharacterPreviewStage.PreviewLayerName}' by hand.");
                else slot.stringValue = CharacterPreviewStage.PreviewLayerName;
            }

            var renderingLayers = tagManager.FindProperty("m_RenderingLayers");
            if (renderingLayers != null && renderingLayers.arraySize > PreviewRenderingLayerIndex)
                renderingLayers.GetArrayElementAtIndex(PreviewRenderingLayerIndex).stringValue = PreviewRenderingLayerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ScriptableRendererData EnsurePreviewRenderer()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PreviewRenderer);
            if (existing != null) return existing;
            if (!AssetDatabase.CopyAsset(SourceRenderer, PreviewRenderer))
            {
                Debug.LogError($"[Items] Could not copy {SourceRenderer}.");
                return null;
            }

            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PreviewRenderer);
            // Strip every renderer feature: the preview needs plain lit shading and an intact alpha channel.
            foreach (var feature in AssetDatabase.LoadAllAssetsAtPath(PreviewRenderer).OfType<ScriptableRendererFeature>().ToArray())
            {
                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, true);
            }
            var so = new SerializedObject(data);
            so.FindProperty("m_RendererFeatures").ClearArray();
            so.FindProperty("m_RendererFeatureMap").ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.ImportAsset(PreviewRenderer);
            return data;
        }

        /// <summary>
        /// Gives the preview renderer the same comic look as the world (outlines + Ben-Day dots), tuned for a small,
        /// close-up, shadow-less render and immune to day/night modifiers. Existing settings are kept on re-runs.
        /// </summary>
        private static void EnsureComicFeature(ScriptableRendererData renderer)
        {
            if (renderer.rendererFeatures.OfType<ComicStyleFeature>().Any()) return;
            var source = AssetDatabase.LoadAllAssetsAtPath(SourceRenderer).OfType<ComicStyleFeature>().FirstOrDefault();
            var feature = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<ComicStyleFeature>();
            feature.name = "ComicStyle";
            feature.applyEnvironmentModifiers = false;
            feature.shadows.enabled = false;            // no shadow map in the preview
            feature.halftone.postProcessDots = true;    // Synty/Sidekick are plain Lit: dots from shading
            feature.halftone.luminanceThreshold = 0.08f; // linear luminance (~31% sRGB): shaded areas only
            feature.halftone.dotSize = 12f;             // dots scale with target height; previews are ~500px tall
            feature.outlines.thickness = 1.1f;
            feature.outlines.thinStartDistance = 10f;   // the doll is always 1.5-4 m away
            feature.outlines.creaseFadeStart = 10f;
            feature.outlines.creaseFadeEnd = 20f;

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
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }

        private static void AddToPipelineAssets(ScriptableRendererData renderer)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                var list = so.FindProperty("m_RendererDataList");
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) present = true;
                if (present) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = renderer;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }
    }
}
