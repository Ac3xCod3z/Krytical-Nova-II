using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>The Graphics page of Settings: display options, a quality preset, and the details a preset sets.</summary>
    public sealed class GraphicsSettingsTab
    {
        private static readonly string[] PresetNames = { "LOW", "MEDIUM", "HIGH", "ULTRA", "CUSTOM" };
        private static readonly string[] ShadowNames = { "OFF", "LOW", "MEDIUM", "HIGH", "ULTRA" };
        private static readonly string[] AntiAliasingNames = { "OFF", "FXAA", "SMAA" };
        private static readonly string[] WindowNames = { "FULLSCREEN", "BORDERLESS", "WINDOWED" };
        private static readonly string[] UpscalerQualityNames = { "NATIVE AA", "QUALITY", "BALANCED", "PERFORMANCE", "ULTRA PERFORMANCE" };
        // The upscalers this machine can run, as the row lists them.
        private readonly List<UpscalerChoice> upscalers = new List<UpscalerChoice>();

        private readonly List<Action> refreshers = new List<Action>();
        private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
        private string[] resolutionNames;
        private string[] frameCapNames;

        public void Build(VisualElement page)
        {
            page.Clear();
            refreshers.Clear();
            CollectResolutions();
            frameCapNames = Array.ConvertAll(GraphicsPreferences.FrameCaps, cap => cap <= 0 ? "NO CAP" : cap + " FPS");

            SettingsRows.Header(page, "DISPLAY");
            Add(SettingsRows.Choice(page, "Resolution", resolutionNames, ResolutionIndex,
                index => GraphicsPreferences.SetResolution(resolutions[index].x, resolutions[index].y)));
            Add(SettingsRows.Choice(page, "Window", WindowNames, () => (int)GraphicsPreferences.Window,
                index => GraphicsPreferences.Window = (WindowChoice)index));
            Add(SettingsRows.Toggle(page, "Vertical sync", () => GraphicsPreferences.VSync, value => GraphicsPreferences.VSync = value));
            Add(SettingsRows.Choice(page, "Frame cap (vertical sync off)", frameCapNames,
                () => Mathf.Max(0, Array.IndexOf(GraphicsPreferences.FrameCaps, GraphicsPreferences.FrameCap)),
                index => GraphicsPreferences.FrameCap = GraphicsPreferences.FrameCaps[index]));
            if (Application.isEditor)
                SettingsRows.Help(page, "Resolution and window mode take effect in the built game, not in the Editor's Game view.");

            SettingsRows.Header(page, "UPSCALING");
            upscalers.Clear();
            upscalers.Add(UpscalerChoice.Off);
            if (GraphicsSettingsApplier.DlssAvailable) upscalers.Add(UpscalerChoice.Dlss);
            if (GraphicsSettingsApplier.Fsr2Available) upscalers.Add(UpscalerChoice.Fsr2);
            string[] upscalerNames = upscalers.ConvertAll(choice => choice == UpscalerChoice.Dlss ? "DLSS" : choice == UpscalerChoice.Fsr2 ? "FSR 2" : "OFF").ToArray();
            Add(SettingsRows.Choice(page, "Upscaler", upscalerNames,
                () => Mathf.Max(0, upscalers.IndexOf(GraphicsSettingsApplier.UpscalerInForce)),
                index => GraphicsPreferences.Upscaler = upscalers[index]));
            Add(SettingsRows.Choice(page, "Upscaler quality", UpscalerQualityNames, () => (int)GraphicsPreferences.UpscalerQuality,
                index => GraphicsPreferences.UpscalerQuality = (UpscalerQuality)index));
            SettingsRows.Help(page, UpscalerHelp());

            SettingsRows.Header(page, "QUALITY");
            Add(SettingsRows.Choice(page, "Preset", PresetNames, () => (int)GraphicsPreferences.Preset,
                index => { GraphicsPreferences.ApplyPreset((GraphicsPreset)index); Refresh(); }));
            Add(SettingsRows.Slider(page, "Render scale", GraphicsPreferences.MinRenderScale, 1f,
                () => GraphicsPreferences.RenderScale, value => Detail(() => GraphicsPreferences.RenderScale = value)));
            Add(SettingsRows.Choice(page, "Shadows", ShadowNames, () => (int)GraphicsPreferences.Shadows,
                index => Detail(() => GraphicsPreferences.Shadows = (ShadowQuality)index)));
            Add(SettingsRows.Choice(page, "Anti-aliasing", AntiAliasingNames, () => (int)GraphicsPreferences.AntiAliasing,
                index => Detail(() => GraphicsPreferences.AntiAliasing = (AntiAliasingChoice)index)));
            Add(SettingsRows.Slider(page, "Grass density", GraphicsPreferences.MinGrassDensity, 1f,
                () => GraphicsPreferences.GrassDensity, value => Detail(() => GraphicsPreferences.GrassDensity = value)));
            Add(SettingsRows.Toggle(page, "Volumetric fog and mist", () => GraphicsPreferences.VolumetricFog,
                value => Detail(() => GraphicsPreferences.VolumetricFog = value)));
            Add(SettingsRows.Toggle(page, "Sun shafts", () => GraphicsPreferences.SunShafts,
                value => Detail(() => GraphicsPreferences.SunShafts = value)));
            Add(SettingsRows.Toggle(page, "Comic ink outlines", () => GraphicsPreferences.ComicOutlines,
                value => Detail(() => GraphicsPreferences.ComicOutlines = value)));

            SettingsRows.Header(page, "PERFORMANCE");
            Add(SettingsRows.Toggle(page, "Performance readout", () => GraphicsPreferences.PerformanceReadout,
                value => GraphicsPreferences.PerformanceReadout = value));
            SettingsRows.Help(page, "Shows how the game is running, on the left edge of the screen: frames a second and the slowest of them, where each frame's time goes, how much is being drawn, memory, and your connection.");
        }

        /// <summary>Shows every stored value again.</summary>
        public void Refresh()
        {
            foreach (Action refresh in refreshers) refresh();
        }

        private void Add(Action refresh) => refreshers.Add(refresh);

        private static string UpscalerHelp()
        {
            string what = "An upscaler draws the game smaller and rebuilds the full picture from several frames: more frames a second for a little softness. "
                + "Quality loses least, Performance gains most; Native AA draws at full size and only smooths edges. "
                + "While one is on it sets the render scale and does the anti-aliasing, so those two options below wait.";
            if (GraphicsSettingsApplier.DlssAvailable) return what;
            return what + (GraphicsSettingsApplier.Fsr2Available
                ? " DLSS needs an NVIDIA RTX graphics card, which this PC does not have: FSR 2 works on any card."
                : " No upscaler is available on this graphics card.");
        }

        // A detail changed by hand turns the preset into Custom: the preset row must say so.
        private void Detail(Action change)
        {
            GraphicsPreset before = GraphicsPreferences.Preset;
            change();
            if (GraphicsPreferences.Preset != before) Refresh();
        }

        private int ResolutionIndex()
        {
            Vector2Int chosen = GraphicsPreferences.Resolution;
            int index = resolutions.IndexOf(chosen);
            return index >= 0 ? index : 0;
        }

        /// <summary>The display's own resolution first ("NATIVE"), then every size it offers, largest first.</summary>
        private void CollectResolutions()
        {
            resolutions.Clear();
            resolutions.Add(Vector2Int.zero);
            Resolution[] offered = Screen.resolutions;
            for (int i = offered.Length - 1; i >= 0; i--)
            {
                var size = new Vector2Int(offered[i].width, offered[i].height);
                if (size.x >= 1024 && !resolutions.Contains(size)) resolutions.Add(size);
            }
            resolutionNames = resolutions.ConvertAll(size => size == Vector2Int.zero ? "NATIVE" : $"{size.x} x {size.y}").ToArray();
        }
    }
}
