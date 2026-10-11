using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// What DLSS and FSR 2 need from the project, in one step that is safe to run again. The render pipeline has both
    /// behind its upscaler framework, which is compiled in only with the scripting define ENABLE_UPSCALER_FRAMEWORK
    /// and the engine's NVIDIA and AMD modules (Packages/manifest.json: com.unity.modules.nvidia, .amd). Each upscaler
    /// keeps its options in an asset inside the pipeline asset; the game's Graphics settings change those at run time
    /// (<c>GraphicsSettingsApplier</c>), so they have to be there, in the Editor and in a build.
    /// </summary>
    public static class UpscalerSetup
    {
        private const string Define = "ENABLE_UPSCALER_FRAMEWORK";
        private const string PipelineAssetPath = "Assets/_Game/Rendering/Settings/PC_RPAsset.asset";

        [MenuItem("Krytical Nova/Lighting/Set Up Upscalers (DLSS, FSR 2)")]
        public static void Run()
        {
            NamedBuildTarget target = NamedBuildTarget.Standalone;
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            if (!defines.Contains(Define))
            {
                PlayerSettings.SetScriptingDefineSymbols(target, string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);
                Debug.Log($"[Upscalers] Added the scripting define {Define}. Run this again once the scripts have compiled.");
                return;
            }

#if ENABLE_UPSCALER_FRAMEWORK
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (asset == null) { Debug.LogError($"[Upscalers] No pipeline asset at {PipelineAssetPath}."); return; }
            var serialized = new SerializedObject(asset);
            SerializedProperty list = serialized.FindProperty("m_UpscalerOptions");
            if (list == null) { Debug.LogError("[Upscalers] This pipeline version keeps no upscaler options on its asset."); return; }
            bool changed = UpscalerOptions.ValidateSerializedUpscalerOptionReferencesWithinRPAsset(asset, list);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (changed)
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            string found = string.Empty;
            foreach (UpscalerOptions options in asset.upscalerOptions)
                if (options != null) found += (found.Length > 0 ? ", " : string.Empty) + options.upscalerName;
            Debug.Log($"[Upscalers] {asset.name} holds options for: {(found.Length > 0 ? found : "none")}." + (changed ? " Saved." : string.Empty));
#endif
        }
    }
}
