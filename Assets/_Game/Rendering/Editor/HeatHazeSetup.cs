using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Summer heat haze (item 21 part 3): makes sure the PC renderer carries <see cref="KryticalNova.Rendering.HeatHazeFeature"/>
    /// with its shader referenced (so a build keeps it). It does nothing at runtime while the season presenter publishes no haze
    /// (no season, night, a cool day, World Effects Low). The mobile renderer is left without it.
    /// </summary>
    public static class HeatHazeSetup
    {
        const string PcRendererPath = "Assets/_Game/Rendering/Settings/PC_Renderer.asset";
        const string ShaderPath = "Assets/_Game/Rendering/Shaders/Seasons/HeatHaze.shader";

        [MenuItem("Krytical Nova/Rendering/Ensure Heat Haze")]
        public static void EnsureHeatHaze()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (renderer == null) { Debug.LogError($"[HeatHaze] Missing {PcRendererPath}"); return; }
            RendererFeatureUtil.Ensure<KryticalNova.Rendering.HeatHazeFeature>(renderer, "HeatHaze", f =>
            {
                var feature = new SerializedObject(f);
                SerializedProperty shader = feature.FindProperty("shader");
                if (shader.objectReferenceValue != null) return;
                shader.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                feature.ApplyModifiedPropertiesWithoutUndo();
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[HeatHaze] PC renderer carries the heat haze pass.");
        }
    }
}
