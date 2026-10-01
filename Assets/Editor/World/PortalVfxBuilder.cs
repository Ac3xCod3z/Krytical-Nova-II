using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// URP-safe copies of the Synty portal and beacon effects for Zone01 (the pack's legacy particle
    /// materials render magenta in URP). Same rules as the weather VFX: no lights, no collision,
    /// capped particle counts. Synty folders are never modified.
    /// </summary>
    public static class PortalVfxBuilder
    {
        public const string OutputFolder = "Assets/Prefabs/Environment/Portals";
        public const string PortalPath = OutputFolder + "/PFX_Portal_Zone01.prefab";
        public const string TownBeaconPath = OutputFolder + "/PFX_TownBeacon.prefab";
        public const string GateBeaconPath = OutputFolder + "/PFX_GateBeacon.prefab";

        private const string ParticleFx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        private const string CityFx = "Assets/Synty/PolygonSciFiCity/Prefabs/FX/";
        private const string MaterialFolder = "Assets/GameData/World/Materials/VFX";
        private static readonly Color PortalTint = new Color(0.35f, 0.85f, 1f, 1f);

        [MenuItem("Krytical Nova/Zone/Build Portal + Beacon VFX")]
        public static void BuildAll()
        {
            WeatherVfxBuilder.EnsureFolder(OutputFolder);
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);

            WeatherVfxBuilder.Build(ParticleFx + "FX_Portal_Round_01.prefab", PortalPath, root =>
            {
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Min(main.maxParticles, 300);
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    var source = renderer.sharedMaterial;
                    // "Multiply (Double)" has no URP particle equivalent; the swirl reads best as a glow.
                    if (source != null && source.shader.name.Contains("Multiply"))
                        renderer.sharedMaterial = Additive("M_FX_Portal_Swirl_URP", source.mainTexture, PortalTint);
                }
            });

            WeatherVfxBuilder.Build(CityFx + "FX_Hologram_Base_01.prefab", TownBeaconPath, root =>
            {
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Min(main.maxParticles, 200);
                }
            });

            WeatherVfxBuilder.Build(CityFx + "FX_Beacon_01.prefab", GateBeaconPath, root =>
            {
                var main = root.GetComponent<ParticleSystem>().main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            });

            AssetDatabase.SaveAssets();
            Debug.Log($"[PortalVfxBuilder] Built portal and beacon VFX into {OutputFolder}.");
        }

        private static Material Additive(string name, Texture texture, Color tint)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            WeatherVfxBuilder.SetupUrpParticle(mat, texture, tint, true);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
