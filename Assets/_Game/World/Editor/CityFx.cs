using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// URP-safe copies of Synty Sci-Fi City ambient effects used by the city builder (the originals use legacy
    /// Built-in particle shaders, which render magenta in URP). Built on first use.
    /// </summary>
    public static class CityFx
    {
        private const string Source = "Assets/Synty/PolygonSciFiCity/Prefabs/FX/";
        public const string Folder = "Assets/_Game/VFX/Environment/City";
        public const string SteamPath = Folder + "/PFX_Vent_Steam.prefab";

        /// <summary>Soft vent steam for alleys: slow, sparse, cheap.</summary>
        public static GameObject Steam()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(SteamPath);
            if (existing != null) return existing;
            WeatherVfxBuilder.EnsureFolder(Folder);
            WeatherVfxBuilder.Build(Source + "FX_Steam_01.prefab", SteamPath, root =>
            {
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Min(main.maxParticles, 40);
                    main.playOnAwake = true;
                    main.loop = true;
                }
            });
            return AssetDatabase.LoadAssetAtPath<GameObject>(SteamPath);
        }
    }
}
