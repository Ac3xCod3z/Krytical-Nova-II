using System.Collections.Generic;
using KryticalNova.World;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The game's colour bible in code (documented in Game Docs/Rendering-Bible.md, "Colour bible"): the core palette,
    /// the time-of-day fog ramp, weather fog tints, and the building palette (Synty atlas variants plus tinted
    /// background-tower materials). <see cref="ApplyAll"/> writes the fog and sky-body values into the world assets;
    /// the city builder reads <see cref="BuildingMaterials"/> to colour buildings so neighbours never match.
    /// </summary>
    public static class ColorBible
    {
        // ---------------------------------------------------------------- core palette
        public static readonly Color Ink = Hex("14122B");            // outlines, vignette, deepest night
        public static readonly Color ShadowIndigo = Hex("4C52BF");   // comic shadow tint
        public static readonly Color SkyBlue = Hex("1F6ECC");        // day zenith
        public static readonly Color HorizonBlue = Hex("ADE0F2");    // day horizon
        public static readonly Color NeonCyan = Hex("4FE3F0");       // tech accents, exhaust glow, portal
        public static readonly Color NeonMagenta = Hex("E04FB8");    // signage accent
        public static readonly Color SignalYellow = Hex("FFC93C");   // road paint, warnings
        public static readonly Color SunsetCoral = Hex("FF9E66");    // dawn / dusk light
        public static readonly Color Concrete = Hex("8C8A94");       // cool city neutral
        public static readonly Color RuinConcrete = Hex("4A4752");   // broken interiors

        // Time-of-day fog: indigo nights, coral-violet dawn, clear comic blue day, rose-magenta dusk. Distance fades
        // toward the sky's own hue instead of grey, so the skyline reads as air, not smog.
        private static readonly (float Hour, string Hex)[] FogRamp =
        {
            (0f, "141A3D"), (5f, "2E2A5C"), (6f, "E8A08A"), (7.4f, "8FC3E8"),
            (16.6f, "8FC3E8"), (18f, "E07A8C"), (18.8f, "5B3F7A"), (19.2f, "141A3D"),
        };

        // Weather look. Weather mutes the palette but never greys it out: the fog tint is a chromatic blue-lavender
        // with a low pull (alpha), so the time-of-day ramp (coral dawns, rose dusks) still shows through, and
        // saturation drops stay small. Clear skies carry enough fog to read as air at distance.
        private struct WeatherLook
        {
            public string FogTint;          // RRGGBBAA, alpha = pull toward the tint
            public float FogDensity;        // exponential-squared
            public float Saturation, Contrast, SkyDesaturation;
            public float VolumetricFog;     // ground volumetric fog 0-1: only fog / whiteout weather, never rain
            public WeatherLook(string tint, float density, float sat, float con, float skyDesat, float volumetric = 0f)
            { FogTint = tint; FogDensity = density; Saturation = sat; Contrast = con; SkyDesaturation = skyDesat; VolumetricFog = volumetric; }
        }

        private static readonly Dictionary<string, WeatherLook> WeatherLooks = new Dictionary<string, WeatherLook>
        {
            { "WP_Clear",        new WeatherLook("A9C4E000", 0.0022f,   8f,  4f, 0f) },
            { "WP_FewClouds",    new WeatherLook("A9C4E000", 0.0024f,   6f,  3f, 0f) },
            { "WP_PartlyCloudy", new WeatherLook("A9C4E000", 0.0026f,   4f,  2f, 0f) },
            { "WP_MostlyCloudy", new WeatherLook("A4B2CC26", 0.0030f,  -4f,  0f, 0.1f) },
            { "WP_Overcast",     new WeatherLook("9EA8C859", 0.0045f, -10f, -2f, 0.3f, 0f) },
            { "WP_Fog",          new WeatherLook("B4BEDC66", 0.0140f, -12f, -4f, 0.35f, 1f) },
            { "WP_LightRain",    new WeatherLook("98A4C466", 0.0055f, -10f,  0f, 0.35f) },
            { "WP_Rain",         new WeatherLook("7C88B080", 0.0075f, -15f,  4f, 0.4f) },
            { "WP_Thunderstorm", new WeatherLook("5C64908C", 0.0090f, -18f, 10f, 0.5f) },
            { "WP_Snow",         new WeatherLook("D2DAF073", 0.0065f, -12f, -4f, 0.4f) },
            { "WP_Blizzard",     new WeatherLook("DCE2F499", 0.0200f, -20f, -8f, 0.55f, 0.5f) },
        };

        // Sky bodies read farther away (angular radius, radians).
        private const float SunTextureSize = 0.05f;
        private const float MoonTextureSize = 0.075f;
        private const float SunDiscSize = 0.027f;
        private const float MoonDiscSize = 0.023f;

        private const string DayCyclePath = "Assets/_Game/World/Data/Assets/DC_Default.asset";

        [MenuItem("Krytical Nova/World/Apply Colour Bible (Fog + Sky Bodies)")]
        public static void ApplyAll()
        {
            var dc = AssetDatabase.LoadAssetAtPath<DayCycleProfile>(DayCyclePath);
            if (dc != null)
            {
                Undo.RecordObject(dc, "Apply colour bible");
                var keys = new GradientColorKey[FogRamp.Length];
                for (int i = 0; i < FogRamp.Length; i++) keys[i] = new GradientColorKey(Hex(FogRamp[i].Hex), FogRamp[i].Hour / 24f);
                var fog = new Gradient();
                fog.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                dc.fogColor = fog;
                dc.sunTextureSize = SunTextureSize;
                dc.moonTextureSize = MoonTextureSize;
                dc.sunDiscSize = SunDiscSize;
                dc.moonDiscSize = MoonDiscSize;
                EditorUtility.SetDirty(dc);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:WeatherProfile"))
            {
                var w = AssetDatabase.LoadAssetAtPath<WeatherProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (w == null || !WeatherLooks.TryGetValue(w.name, out var look)) continue;
                Undo.RecordObject(w, "Apply colour bible");
                w.fogTint = Hex(look.FogTint);
                w.fogDensity = look.FogDensity;
                w.saturation = look.Saturation;
                w.contrast = look.Contrast;
                w.skyDesaturation = look.SkyDesaturation;
                w.volumetricFog = look.VolumetricFog;
                EditorUtility.SetDirty(w);
            }
            ValidateRainXorFog();
            AssetDatabase.SaveAssets();
            Debug.Log("[ColorBible] Fog ramp, weather looks (fog tint, density, saturation, contrast) and sky-body sizes applied.");
        }

        /// <summary>
        /// Air either rains or fogs: warn if a raining profile asks for volumetric fog (it is ignored at runtime) or
        /// if the weather graph jumps straight between fog and rain without a dry step in between.
        /// </summary>
        private static void ValidateRainXorFog()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:WeatherProfile"))
            {
                var w = AssetDatabase.LoadAssetAtPath<WeatherProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (w == null) continue;
                if (w.RainIntensity > 0f && w.volumetricFog > 0f)
                    Debug.LogWarning($"[ColorBible] {w.name} rains but has volumetricFog {w.volumetricFog}; rain always shows 0.", w);
                foreach (var n in w.next)
                {
                    if (n.profile == null || n.weight <= 0f) continue;
                    bool fogToRain = w.VolumetricFogAmount >= 0.5f && n.profile.RainIntensity > 0f;
                    bool rainToFog = w.RainIntensity > 0f && n.profile.VolumetricFogAmount >= 0.5f;
                    if (fogToRain || rainToFog)
                        Debug.LogWarning($"[ColorBible] {w.name} -> {n.profile.name} jumps between fog and rain; route it through a dry weather.", w);
                }
            }
        }

        // ---------------------------------------------------------------- building palette

        // Synty Sci-Fi City atlas variants that sit inside the bible: warm and cool neutrals carry most of the city,
        // teal / indigo / rose / ochre / sage / plum / brick add variety, orange is a rare accent. Lime, near-black and
        // murky olive variants are left out. Repeats weight the pick.
        private static readonly string[] AtlasPalette =
        {
            "PolygonScifi_01_A", "PolygonScifi_01_A", "PolygonScifi_01_C", "PolygonScifi_01_C", "PolygonScifi_04_A",
            "PolygonScifi_02_C", "PolygonScifi_03_B", "PolygonScifi_03_B", "PolygonScifi_02_D", "PolygonScifi_04_B",
            "PolygonScifi_03_E", "PolygonScifi_01_E", "PolygonScifi_03_C", "PolygonScifi_02_E", "PolygonScifi_04_C",
        };

        // Colour family of each atlas variant: the neighbour rule compares families, so two variants that read as the
        // same colour (e.g. the warm neutrals) never stand side by side.
        private static readonly Dictionary<string, string> AtlasFamily = new Dictionary<string, string>
        {
            { "PolygonScifi_01_A", "neutral" }, { "PolygonScifi_01_C", "neutral" }, { "PolygonScifi_04_A", "graphite" },
            { "PolygonScifi_02_C", "teal" }, { "PolygonScifi_03_B", "indigo" }, { "PolygonScifi_02_D", "rose" },
            { "PolygonScifi_04_B", "ochre" }, { "PolygonScifi_03_E", "sage" }, { "PolygonScifi_01_E", "plum" },
            { "PolygonScifi_03_C", "brick" }, { "PolygonScifi_02_E", "sand" }, { "PolygonScifi_04_C", "orange" },
        };

        // Background towers: the three window sets × strong facade tints (the old pale tints all read as cream).
        // Window set 1 is the dark facade; sets 2 and 3 are light facades that take the tint's hue.
        private static readonly (string Name, string Family, Color Tint)[] TowerTints =
        {
            ("Teal", "teal", Hex("5FB0A8")), ("Indigo", "indigo", Hex("8088D0")), ("Terracotta", "brick", Hex("D08A6A")),
            ("OliveGold", "ochre", Hex("C8B060")), ("Plum", "plum", Hex("A878B0")), ("Slate", "graphite", Hex("8A98B0")),
            ("Clean", "neutral", Color.white),
        };

        private const string AtlasFolder = "Assets/Synty/PolygonSciFiCity/Materials/Alts/";
        private const string MiscFolder = "Assets/Synty/PolygonSciFiCity/Materials/Misc/";
        public const string PaletteFolder = "Assets/_Game/Environment/Buildings/Materials/BuildingPalette";

        /// <summary>
        /// The palette materials that can replace <paramref name="source"/> (empty when the material isn't a building
        /// material the palette covers). Background-tower tints are created on first use.
        /// </summary>
        public static List<Material> BuildingMaterials(Material source)
        {
            var list = new List<Material>();
            if (source == null) return list;
            if (source.name.StartsWith("PolygonScifi_0"))
            {
                foreach (string n in AtlasPalette)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AtlasFolder + n + ".mat");
                    if (m != null) list.Add(m);
                }
            }
            else if (source.name.StartsWith("Buildings_Background_0"))
            {
                for (int set = 1; set <= 3; set++)
                {
                    var baseMat = AssetDatabase.LoadAssetAtPath<Material>($"{MiscFolder}Buildings_Background_0{set}.mat");
                    if (baseMat == null) continue;
                    foreach (var (name, _, tint) in TowerTints) list.Add(TowerTint(baseMat, set, name, tint));
                }
            }
            return list;
        }

        private static Material TowerTint(Material baseMat, int set, string name, Color tint)
        {
            if (tint == Color.white) return baseMat;
            WeatherVfxBuilder.EnsureFolder(PaletteFolder);
            string path = $"{PaletteFolder}/M_Tower_0{set}_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(baseMat);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor") && mat.GetColor("_BaseColor") != tint)
            {
                mat.SetColor("_BaseColor", tint);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        /// <summary>The colour family a palette material belongs to (unknown materials are their own family).</summary>
        public static string FamilyOf(Material m)
        {
            if (m == null) return "";
            if (AtlasFamily.TryGetValue(m.name, out string f)) return f;
            if (m.name.StartsWith("Buildings_Background_01") || m.name.StartsWith("M_Tower_01_")) return "dark";
            if (m.name.StartsWith("Buildings_Background_0")) return "neutral";
            if (m.name.StartsWith("M_Tower_0"))
                foreach (var (name, family, _) in TowerTints)
                    if (m.name.EndsWith("_" + name)) return family;
            return m.name;
        }

        // ---------------------------------------------------------------- grime (triplanar dirt)

        private const string TriplanarSource = AtlasFolder + "PolygonScifi_01_A_TriPlanar.mat";
        private static readonly (string Name, float Dirt)[] GrimeLevels = { ("Light", 0.35f), ("Medium", 0.6f), ("Heavy", 0.9f) };

        /// <summary>Synty's triplanar dirt material at three grime levels (for trash corners and back lots).</summary>
        public static List<Material> GrimeMaterials()
        {
            var list = new List<Material>();
            var source = AssetDatabase.LoadAssetAtPath<Material>(TriplanarSource);
            if (source == null) return list;
            WeatherVfxBuilder.EnsureFolder(PaletteFolder);
            foreach (var (name, dirt) in GrimeLevels)
            {
                string path = $"{PaletteFolder}/M_Grime_{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(source);
                    AssetDatabase.CreateAsset(mat, path);
                }
                if (mat.HasProperty("_Dirt_Amount") && !Mathf.Approximately(mat.GetFloat("_Dirt_Amount"), dirt))
                {
                    mat.SetFloat("_Dirt_Amount", dirt);
                    EditorUtility.SetDirty(mat);
                }
                list.Add(mat);
            }
            return list;
        }

        // ---------------------------------------------------------------- signs

        public const string SignFolder = PaletteFolder + "/Signs";

        /// <summary>A sign board material (URP Lit), optionally emissive (HDR, feeds Bloom) and/or translucent.</summary>
        public static Material SignMaterial(string name, Color color, float emission, bool transparent = false)
        {
            WeatherVfxBuilder.EnsureFolder(SignFolder);
            string path = $"{SignFolder}/M_Sign_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color baseColor = new Color(color.r, color.g, color.b, transparent ? 0.45f : 1f);
            float max = Mathf.Max(baseColor.r, Mathf.Max(baseColor.g, baseColor.b));
            if (max > 1f) { baseColor.r /= max; baseColor.g /= max; baseColor.b /= max; }
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Surface", transparent ? 1f : 0f);
            mat.SetFloat("_Blend", 0f);
            if (emission > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                mat.SetColor("_EmissionColor", new Color(baseColor.r, baseColor.g, baseColor.b) * emission);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            BaseShaderGUI.SetMaterialKeywords(mat, LitGUI.SetMaterialKeywords);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A TMP font material in a given (possibly HDR) face colour, saved as an asset so scenes stay clean.</summary>
        public static Material SignTextMaterial(TMPro.TMP_FontAsset font, Color ink)
        {
            WeatherVfxBuilder.EnsureFolder(SignFolder);
            string path = $"{SignFolder}/M_Text_{font.name.Replace(" ", "")}_{ColorUtility.ToHtmlStringRGB(ink)}_{Mathf.RoundToInt(Mathf.Max(ink.r, Mathf.Max(ink.g, ink.b)) * 10f)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            // Some vendor font assets lose their material reference when TextMeshPro upgrades them, though the
            // material is still inside the asset: take it from there.
            Material source = font.material;
            if (source == null)
                foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(font)))
                    if (sub is Material inside) { source = inside; break; }
            if (source == null)
            {
                Debug.LogWarning($"[ColorBible] Font asset {font.name} has no material; its sign text keeps the default look.");
                return null;
            }
            mat = new Material(source);
            mat.SetColor("_FaceColor", ink);
            if (mat.HasProperty("_OutlineWidth"))
            {
                mat.SetFloat("_OutlineWidth", 0.12f);
                mat.SetColor("_OutlineColor", Ink);
                mat.EnableKeyword("OUTLINE_ON");
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Dark concrete for the faces Mesh Demolisher cuts through a tower.</summary>
        public static Material RuinInteriorMaterial()
        {
            WeatherVfxBuilder.EnsureFolder(PaletteFolder);
            string path = $"{PaletteFolder}/M_Ruin_Interior.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", RuinConcrete);
            mat.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
