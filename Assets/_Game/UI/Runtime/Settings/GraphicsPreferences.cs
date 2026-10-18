using System;
using KryticalNova.Networking;
using KryticalNova.World;
using UnityEngine;

namespace KryticalNova.UI
{
    public enum GraphicsPreset { Low, Medium, High, Ultra, Custom }
    public enum ShadowQuality { Off, Low, Medium, High, Ultra }
    public enum AntiAliasingChoice { Off, Fxaa, Smaa }
    public enum WindowChoice { Fullscreen, Borderless, Windowed }
    /// <summary>Draws the game at a lower resolution and rebuilds the full picture from several frames. DLSS needs an NVIDIA RTX card; FSR 2 runs on any.</summary>
    public enum UpscalerChoice { Off, Dlss, Fsr2 }
    /// <summary>How much lower: Native AA draws at full size (anti-aliasing only), each step after it draws less and runs faster.</summary>
    public enum UpscalerQuality { NativeAA, Quality, Balanced, Performance, UltraPerformance }

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
            public readonly VolumetricQuality Fog;
            public readonly bool SunShafts, ComicOutlines;

            public Detail(float renderScale, ShadowQuality shadows, AntiAliasingChoice antiAliasing, float grassDensity,
                VolumetricQuality fog, bool sunShafts, bool comicOutlines)
            {
                RenderScale = renderScale; Shadows = shadows; AntiAliasing = antiAliasing; GrassDensity = grassDensity;
                Fog = fog; SunShafts = sunShafts; ComicOutlines = comicOutlines;
            }
        }

        private static readonly Detail[] Presets =
        {
            new Detail(0.7f, ShadowQuality.Low, AntiAliasingChoice.Off, 0.4f, VolumetricQuality.Off, false, true),        // Low
            new Detail(0.85f, ShadowQuality.Medium, AntiAliasingChoice.Fxaa, 0.7f, VolumetricQuality.Medium, false, true), // Medium
            new Detail(1f, ShadowQuality.High, AntiAliasingChoice.Smaa, 1f, VolumetricQuality.High, true, true),          // High: as authored
            new Detail(1f, ShadowQuality.Ultra, AntiAliasingChoice.Smaa, 1f, VolumetricQuality.Ultra, true, true),        // Ultra
        };

        public const float MinRenderScale = 0.5f;
        public const float MinGrassDensity = 0.2f;
        /// <summary>The frame cap that follows the screen: as many frames a second as the display can show at its fastest.</summary>
        public const int FrameCapOfScreen = -1;
        public static readonly int[] FrameCaps = { FrameCapOfScreen, 30, 60, 120, 144, 240, 0 }; // 0 = no cap

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

        /// <summary>
        /// Volumetric fog, mist and the lamps' light cones: Off, or how finely they are drawn. Stored since 16 Oct 2026
        /// under a key of its own; a choice stored before, when it was on or off, reads as High or Off.
        /// </summary>
        public static VolumetricQuality Fog
        {
            get => (VolumetricQuality)Mathf.Clamp(GetInt(FogKey, GetInt("fog", 1) != 0 ? (int)VolumetricQuality.High : (int)VolumetricQuality.Off),
                0, (int)VolumetricQuality.Ultra);
            set => SetDetail(FogKey, (int)value);
        }

        private const string FogKey = "fogQuality";

        public static bool SunShafts { get => GetInt("shafts", 1) != 0; set => SetDetail("shafts", value ? 1 : 0); }
        public static bool ComicOutlines { get => GetInt("outlines", 1) != 0; set => SetDetail("outlines", value ? 1 : 0); }

        /// <summary>
        /// Off as the game ships (owner, 11 Oct 2026): the frame rate is held by the frame cap, at the highest the
        /// screen is made for, not by waiting for the screen.
        /// </summary>
        public static bool VSync { get => GetInt("vsync", 0) != 0; set => Set("vsync", value ? 1 : 0); }

        /// <summary>
        /// The upscaler. Not part of a preset: which one a player can use depends on their graphics card. While one
        /// is on it decides the render scale and does the anti-aliasing, so those two options wait.
        /// </summary>
        public static UpscalerChoice Upscaler
        {
            get => (UpscalerChoice)Mathf.Clamp(GetInt("upscaler", (int)UpscalerChoice.Off), 0, (int)UpscalerChoice.Fsr2);
            set => Set("upscaler", (int)value);
        }

        public static UpscalerQuality UpscalerQuality
        {
            get => (UpscalerQuality)Mathf.Clamp(GetInt("upscalerQuality", (int)UpscalerQuality.Quality), 0, (int)UpscalerQuality.UltraPerformance);
            set => Set("upscalerQuality", (int)value);
        }

        /// <summary>
        /// The performance readout is on screen: frame rate, where the frame's time goes, what is drawn, memory and
        /// the connection (<see cref="PerformanceReadout"/>). Not part of a preset.
        /// </summary>
        public static bool PerformanceReadout { get => GetInt("readout", 0) != 0; set => Set("readout", value ? 1 : 0); }

        /// <summary>
        /// Frames per second the game is held to with VSync off: <see cref="FrameCapOfScreen"/> (what the game ships
        /// with) holds it to the highest rate the screen is made for, 0 is no cap. Frames the screen cannot show are
        /// work for nothing, and a PC that is not flat out has room for a frame that takes longer.
        /// <para>Kept under a key of its own since 11 Oct 2026: what was stored before (no cap, as the game then
        /// shipped) is not taken for a choice.</para>
        /// </summary>
        public static int FrameCap
        {
            get => Mathf.Max(FrameCapOfScreen, GetInt(FrameCapKey, FrameCapOfScreen));
            set => Set(FrameCapKey, Mathf.Max(FrameCapOfScreen, value));
        }

        private const string FrameCapKey = "frameLimit";

        /// <summary>
        /// The highest refresh rate the screen is made for, as a whole number of frames a second: the fastest of the
        /// modes it offers, and never less than the one it is running at (60 when the display does not say).
        /// </summary>
        public static int ScreenRate
        {
            get
            {
                double rate = Screen.currentResolution.refreshRateRatio.value;
                foreach (Resolution mode in Screen.resolutions)
                    if (mode.refreshRateRatio.value > rate) rate = mode.refreshRateRatio.value;
                return rate >= 1.0 ? Mathf.RoundToInt((float)rate) : 60;
            }
        }

        /// <summary>The cap in force with VSync off, in frames a second; 0 = none.</summary>
        public static int FrameCapInForce => FrameCap == FrameCapOfScreen ? ScreenRate : FrameCap;

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
                PlayerPrefs.SetInt(Prefix + FogKey, (int)d.Fog);
                PlayerPrefs.SetInt(Prefix + "shafts", d.SunShafts ? 1 : 0);
                PlayerPrefs.SetInt(Prefix + "outlines", d.ComicOutlines ? 1 : 0);
            }
            Commit();
        }

        /// <summary>
        /// The preset a Multiplayer Play Mode clone (the Player 2 Editor) starts on, every time (owner, 18 Oct 2026: not
        /// Ultra or High). It is a test client: the GPU and memory are left to the main Editor.
        /// </summary>
        public const GraphicsPreset ClonePreset = GraphicsPreset.Low;
        public const int CloneFrameCap = 60;

        /// <summary>
        /// On a Multiplayer Play Mode clone, puts its test-client options in force: <see cref="ClonePreset"/>, a 60 fps
        /// cap and no upscaler. Its options are its own (the profile prefix), so the main Editor's stay as they are; what
        /// is changed in its Settings lasts until it starts again. False, and nothing touched, anywhere else.
        /// </summary>
        public static bool ApplyCloneDefaults()
        {
            if (!Application.isEditor || NetworkConnectionManager.EditorCloneProfile() == null) return false;
            PlayerPrefs.SetInt(Prefix + FrameCapKey, CloneFrameCap);
            PlayerPrefs.SetInt(Prefix + "upscaler", (int)UpscalerChoice.Off);
            ApplyPreset(ClonePreset);
            return true;
        }

        /// <summary>Video memory (MB) below which a card is taken for integrated graphics (they report a small shared slice).</summary>
        public const int IntegratedBelowMb = 2048;
        /// <summary>Video memory (MB) a card needs, besides being of a recent family, to start on High.</summary>
        public const int HighFromMb = 6000;
        /// <summary>Processor threads at or below which the game starts on Low whatever the card.</summary>
        public const int LowAtThreads = 4;

        /// <summary>
        /// The preset a first launch starts on (owner, 18 Oct 2026: Low reaches laptops and integrated graphics, the
        /// mid tier and up keep High): integrated graphics, a Steam Deck or four threads or fewer → Low; an older family
        /// (GTX, Radeon RX 400 / 500 / 5000) or less than <see cref="HighFromMb"/> → Medium; anything else → High.
        /// </summary>
        public static GraphicsPreset PresetForHardware(string deviceName, int videoMemoryMb, int processorThreads)
        {
            string n = (deviceName ?? string.Empty).ToUpperInvariant();
            bool integrated = videoMemoryMb < IntegratedBelowMb || n.Contains("UHD") || n.Contains("IRIS") || n.Contains("INTEL(R) HD")
                              || n.Contains("RADEON(TM) GRAPHICS") || n.Contains("RADEON GRAPHICS") || n.Contains("VEGA") || n.Contains("CUSTOM GPU 0405");
            if (integrated || processorThreads <= LowAtThreads) return GraphicsPreset.Low;
            bool older = n.Contains("GTX") || n.Contains("RX 4") || n.Contains("RX 5") || videoMemoryMb < HighFromMb;
            return older ? GraphicsPreset.Medium : GraphicsPreset.High;
        }

        /// <summary>
        /// On the first launch of this profile (no preset stored yet), puts <see cref="PresetForHardware"/> in force; on
        /// Low also a 60 fps cap and an upscaler (DLSS on an RTX card, else FSR 2, none where neither runs). Null when a
        /// preset was already stored: the player's own choice is never overridden.
        /// </summary>
        public static GraphicsPreset? ApplyHardwarePresetOnFirstLaunch()
        {
            if (PlayerPrefs.HasKey(Prefix + "preset")) return null;
            GraphicsPreset preset = PresetForHardware(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, SystemInfo.processorCount);
            if (preset == GraphicsPreset.Low)
            {
                PlayerPrefs.SetInt(Prefix + FrameCapKey, 60);
                PlayerPrefs.SetInt(Prefix + "upscaler", (int)UpscalerChoice.Dlss);
                PlayerPrefs.SetInt(Prefix + "upscalerQuality", (int)UpscalerQuality.Quality);
            }
            ApplyPreset(preset);
            return preset;
        }

        /// <summary>Every graphics option back to what the game ships with (the High preset, the display's own size).</summary>
        public static void ResetToDefaults()
        {
            foreach (string key in new[] { "preset", "renderScale", "shadows", "aa", "grass", "fog", FogKey, "shafts", "outlines", "vsync", FrameCapKey, "window", "width", "height", "readout", "upscaler", "upscalerQuality" })
                PlayerPrefs.DeleteKey(Prefix + key);
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
