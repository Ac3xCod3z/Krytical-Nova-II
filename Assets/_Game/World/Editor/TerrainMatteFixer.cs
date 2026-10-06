using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Makes terrain read as matte, to match the comic/toon look.
    /// Synty diffuse textures carry an opaque alpha, and URP TerrainLit reads smoothness from that alpha by default,
    /// so every layer rendered at smoothness 1 (bright specular sheen). This switches every TerrainLayer in the
    /// project to a constant low smoothness and gives open-scene terrains a project-owned TerrainLit material.
    /// </summary>
    public static class TerrainMatteFixer
    {
        private const string MaterialFolder = "Assets/_Game/World/Terrain/Materials";
        private const string MaterialPath = MaterialFolder + "/M_TerrainMatte.mat";
        private const string TerrainLitShader = "Universal Render Pipeline/Terrain/Lit";
        private const float MatteSmoothness = 0.05f;

        public static void Run()
        {
            int layers = FixLayers();
            var material = GetOrCreateMaterial();
            int terrains = AssignMaterial(material);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TerrainMatteFixer] {layers} terrain layer(s) set to constant smoothness {MatteSmoothness}; " +
                      $"{terrains} terrain(s) in open scenes now use {MaterialPath}. Save the scene to keep the material change.");
        }

        private static int FixLayers()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:TerrainLayer", new[] { "Assets" }))
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(guid));
                if (layer == null) continue;
                // Synty and other third-party folders are left alone; only project-owned layers are edited.
                if (AssetDatabase.GetAssetPath(layer).StartsWith("Assets/Synty")) continue;
                Undo.RecordObject(layer, "Make Terrain Matte");
                layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;
                layer.smoothness = MatteSmoothness;
                layer.metallic = 0f;
                layer.specular = Color.black;
                EditorUtility.SetDirty(layer);
                count++;
            }
            return count;
        }

        private static Material GetOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            var shader = Shader.Find(TerrainLitShader);
            if (shader == null)
            {
                Debug.LogError($"[TerrainMatteFixer] Shader '{TerrainLitShader}' not found.");
                return null;
            }
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                Directory.CreateDirectory(MaterialFolder);
                AssetDatabase.Refresh();
            }
            material = new Material(shader) { name = "M_TerrainMatte", enableInstancing = true };
            if (material.HasProperty("_EnableInstancedPerPixelNormal"))
            {
                material.SetFloat("_EnableInstancedPerPixelNormal", 1f);
                material.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            }
            for (int i = 0; i < 4; i++)
            {
                if (material.HasProperty($"_Smoothness{i}")) material.SetFloat($"_Smoothness{i}", MatteSmoothness);
                if (material.HasProperty($"_Metallic{i}")) material.SetFloat($"_Metallic{i}", 0f);
            }
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static int AssignMaterial(Material material)
        {
            if (material == null) return 0;
            int count = 0;
            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                if (terrain.materialTemplate == material) continue;
                Undo.RecordObject(terrain, "Assign Matte Terrain Material");
                terrain.materialTemplate = material;
                EditorUtility.SetDirty(terrain);
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                count++;
            }
            return count;
        }
    }
}
