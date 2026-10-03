using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Creates "Minimap_Renderer" (a feature-less Forward+ renderer, copied from Mobile_Renderer, that draws opaques only) and
    /// registers it on the PC pipeline asset, where MinimapLiveCamera finds it by name. Not added to the Mobile asset,
    /// so the Mobile tier keeps the baked minimap image. Re-runnable.
    /// </summary>
    public static class MinimapRendererSetup
    {
        private const string Folder = "Assets/_Game/Rendering/Settings/";
        private const string SourcePath = Folder + "Mobile_Renderer.asset";
        public const string RendererPath = Folder + "Minimap_Renderer.asset";
        private const string PcPipelinePath = Folder + "PC_RPAsset.asset";

        [MenuItem("Krytical Nova/Lighting/Setup Live Minimap Renderer")]
        public static void Setup()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                if (!AssetDatabase.CopyAsset(SourcePath, RendererPath)) { Debug.LogError($"[Minimap] Could not copy {SourcePath}"); return; }
                data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            }

            var so = new SerializedObject(data);
            so.FindProperty("m_TransparentLayerMask").intValue = 0; // no fog volumes, particles or glass on the map
            // Forward+ like PC_Renderer: a plain Forward renderer anywhere in the asset disables GPU Resident Drawer.
            so.FindProperty("m_RenderingMode").intValue = (int)RenderingMode.ForwardPlus;
            so.FindProperty("m_RendererFeatures").ClearArray();
            so.FindProperty("m_RendererFeatureMap").ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcPipelinePath);
            var pso = new SerializedObject(pipeline);
            var list = pso.FindProperty("m_RendererDataList");
            bool present = Enumerable.Range(0, list.arraySize).Any(i => list.GetArrayElementAtIndex(i).objectReferenceValue == data);
            if (!present)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
                pso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Minimap] Minimap_Renderer ready on PC_RPAsset.");
        }
    }
}
