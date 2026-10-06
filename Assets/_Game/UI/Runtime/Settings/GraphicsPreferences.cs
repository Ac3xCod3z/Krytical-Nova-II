using System;
using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.UI
{
    public enum GraphicsPreset { Low, Medium, High, Ultra, Custom }
    public enum ShadowQuality { Off, Low, Medium, High, Ultra }
    public enum AntiAliasingChoice { Off, Fxaa, Smaa }
    public enum WindowChoice { Fullscreen, Borderless, Windowed }

    /// <summary>
    /// The player's graphics options, persisted in PlayerPrefs like <see cref="HudPreferences"/>. A preset is a row of
    /// the table below; touching one detail turns the preset into Custom. High is the look the game is authored at,
    /// so a player who never opens Settings sees no difference. <see cref="GraphicsSettingsApplier"/> puts them in force.
    /// </summary>
    public static class GraphicsPreferences
    {
        /// <summary>What one preset sets. Display options (resolution, VSync, frame cap) are not part of a preset.</summary>
        public readonly struct Detail
        {
            public readonly float RenderScale, GrassDensity;
            public readonly ShadowQuality Shadows;
            public readonly AntiAliasingChoice AntiAliasing;
            public readonly bool VolumetricFog, SunShafts, ComicOutlines;

            public Detail(float renderScale, ShadowQuality shadows, AntiAliasingChoice antiAliasing, float grassDensity,
                bool volumetricFog, bool sunShafts, bool comicOutlines)
            {
                RenderScale = renderScale; Shadows = shadows; AntiAliasing = antiAliasing; GrassDensity = grassDensity;
                VolumetricFog = volumetricFog; SunShafts = sunShafts; ComicOutlines = comicOutlines;
            }
        }

        private static readonly Detail[] Presets =
        {
            new Detail(0.7f, ShadowQuality.Low, AntiAliasingChoice.Off, 0.4f, false, false, true),     // Low
            new Detail(0.85f, ShadowQuality.Medium, AntiAliasingChoice.Fxaa, 0.7f, true, false, true), // Medium
            new Detail(1f, ShadowQuality.High, AntiAliasingChoice.Smaa, 1f, true, true, true),         // High: as authored
            new Detail(1f, ShadowQuality.Ultra, AntiAliasingChoice.Smaa, 1f, true, true, true),        // Ultra
        };

        public const float MinRenderScale = 0.5f;
        public const float MinGrassDensity = 0.2f;
        public static readonly int[] FrameCaps = { 30, 60, 120, 144, 240, 0 }; // 0 = no cap

        private static readonly string Prefix = BuildPrefix();

        private static string BuildPrefix()
        {
            string profile = NetworkConnectionManager.LocalProfile();
            return string.IsNullOrEmpty(profile) ? "kn.gfx." : "kn.gfx." + profile + ".";
        }

        /// <summary>Raised after any option changed and was saved.</summary>
        public static event Action Changed;

        public static GraphicsPreset Preset => (GraphicsPreset)Mathf.Clamp(GetInt("preset", (int)GraphicsPreset.High), 0, (int)GraphicsPreset.Custom);

        public static float RenderScale
        {
            get => Mathf.Clamp(GetFloat("renderScale", 1f), MinRenderScale, 1f);
            set => SetDetail("renderScale", Mathf.Clamp(value, MinRenderScale, 1f));
        }

        public static ShadowQuality Shadows
        {
            get => (ShadowQuality)Mathf.Clamp(GetInt("shadows", (int)ShadowQuality.High), 0, (int)ShadowQuality.Ultra);
            set => SetDetail("shadows", (int)value);
        }

        public static AntiAliasingChoice AntiAliasing
        {
            get => (AntiAliasingChoice)Mathf.Clamp(GetInt("aa", (int)AntiAliasingChoice.Smaa), 0, (int)AntiAliasingChoice.Smaa);
            set => SetDetail("aa", (int)value);
        }

        public static float GrassDensity
        {
            get => Mathf.Clamp(GetFloat("grass", 1f), MinGrassDensity, 1f);
            set => SetDetail("grass", Mathf.Clamp(value, MinGrassDensity, 1f));
        }

        public static bool VolumetricFog { get => GetInt("fog", 1) != 0; set => SetDetail("fog", value ? 1 : 0); }
        public static bool SunShafts { get => GetInt("shafts", 1) != 0; set => SetDetail("shafts", value ? 1 : 0); }
        public static bool ComicOutlines { get => GetInt("outlines", 1) != 0; set => SetDetail("outlines", value ? 1 : 0); }

        public static bool VSync { get => GetInt("vsync", 1) != 0; set => Set("vsync", value ? 1 : 0); }

        /// <summary>Frames per second the game is held to with VSync off; 0 = no cap.</summary>
        public static int FrameCap { get => Mathf.Max(0, GetInt("frameCap", 0)); set => Set("frameCap", Mathf.Max(0, value)); }

        public static WindowChoice Window
        {
            get => (WindowChoice)Mathf.Clamp(GetInt("window", (int)WindowChoice.Borderless), 0, (int)WindowChoice.Windowed);
            set => Set("window", (int)value);
        }

        /// <summary>The chosen resolution, or (0, 0) for the display's own.</summary>
        public static Vector2Int Resolution => new Vector2Int(Mathf.Max(0, GetInt("width", 0)), Mathf.Max(0, GetInt("height", 0)));

        public static void SetResolution(int width, int height)
        {
            if (Resolution.x == width && Resolution.y == height) return;
            PlayerPrefs.SetInt(Prefix + "width", width);
            PlayerPrefs.SetInt(Prefix + "height", height);
            Commit();
        }

        /// <summary>Writes a preset's row over the detail options. Custom keeps them as they are.</summary>
        public static void ApplyPreset(GraphicsPreset preset)
        {
            PlayerPrefs.SetInt(Prefix + "preset", (int)preset);
            if (preset != GraphicsPreset.Custom)
            {
                Detail d = Presets[(int)preset];
                PlayerPrefs.SetFloat(Prefix + "renderScale", d.RenderScale);
                PlayerPrefs.SetInt(Prefix + "shadows", (int)d.Shadows);
                PlayerPrefs.SetInt(Prefix + "aa", (int)d.AntiAliasing);
                PlayerPrefs.SetFloat(Prefix + "grass", d.GrassDensity);
                PlayerPrefs.SetInt(Prefix + "fog", d.VolumetricFog ? 1 : 0);
                PlayerPrefs.SetInt(Prefix + "shafts", d.SunShafts ? 1 : 0);
                PlayerPrefs.SetInt(Prefix + "outlines", d.ComicOutlines ? 1 : 0);
            }
            Commit();
        }

        private static int GetInt(string key, int fallback) => PlayerPrefs.GetInt(Prefix + key, fallback);
        private static float GetFloat(string key, float fallback) => PlayerPrefs.GetFloat(Prefix + key, fallback);

        private static void Set(string key, int value)
        {
            if (PlayerPrefs.HasKey(Prefix + key) && PlayerPrefs.GetInt(Prefix + key) == value) return;
            PlayerPrefs.SetInt(Prefix + key, value);
            Commit();
        }

        // A detail changed by hand: the options no longer match a preset.
        private static void SetDetail(string key, int value)
        {
            if (PlayerPrefs.HasKey(Prefix + key) && PlayerPrefs.GetInt(Prefix + key) == value) return;
            PlayerPrefs.SetInt(Prefix + key, value);
            PlayerPrefs.SetInt(Prefix + "preset", (int)GraphicsPreset.Custom);
            Commit();
        }

        private static void SetDetail(string key, float value)
        {
            if (PlayerPrefs.HasKey(Prefix + key) && Mathf.Approximately(PlayerPrefs.GetFloat(Prefix + key), value)) return;
            PlayerPrefs.SetFloat(Prefix + key, value);
            PlayerPrefs.SetInt(Prefix + "preset", (int)GraphicsPreset.Custom);
            Commit();
        }

        private static void Commit()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
