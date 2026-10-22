using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    public static partial class GameFeelBuilder
    {
        public const string BeatRingSettingsPath = "Assets/_Game/GameFeel/Data/Assets/BeatRingSettings.asset";
        private const string BeatRingMaterialPath = MaterialFolder + "/M_BeatRing.mat";

        /// <summary>
        /// The Beat Ring (owner, 22 Oct 2026): its settings, its additive material, and the ring itself on the game feel
        /// prefab every map holds (<see cref="KryticalNova.GameFeel.BeatRing"/>). A full Build Game Feel puts it back too.
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Build Beat Ring")]
        public static void BuildBeatRing()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                AttachBeatRing(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Beat Ring on {PrefabPath}. Tune it in {BeatRingSettingsPath}.");
        }

        private static void AttachBeatRing(GameObject root)
        {
            var ring = root.GetComponent<KryticalNova.GameFeel.BeatRing>();
            if (ring == null) ring = root.AddComponent<KryticalNova.GameFeel.BeatRing>();
            var so = new SerializedObject(ring);
            so.FindProperty("settings").objectReferenceValue = BeatRingSettings();
            so.FindProperty("material").objectReferenceValue = BeatRingMaterial();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static KryticalNova.GameFeel.BeatRingSettings BeatRingSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<KryticalNova.GameFeel.BeatRingSettings>(BeatRingSettingsPath);
            if (settings != null) return settings;
            WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(BeatRingSettingsPath).Replace('\\', '/'));
            settings = ScriptableObject.CreateInstance<KryticalNova.GameFeel.BeatRingSettings>();
            AssetDatabase.CreateAsset(settings, BeatRingSettingsPath);
            return settings;
        }

        // Unlit, additive and reading vertex colours, so each segment glows in its own colour and never hides the pet.
        private static Material BeatRingMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(BeatRingMaterialPath);
            if (material == null)
            {
                WeatherVfxBuilder.EnsureFolder(MaterialFolder);
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "M_BeatRing" };
                AssetDatabase.CreateAsset(material, BeatRingMaterialPath);
            }
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_Cull", 0f); // both faces: a bar seen from behind still shows
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ADD");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
