using System;
using UnityEditor;

namespace laplahce.Watermark.Core
{
    public static class WatermarkWindowLauncher
    {
        public static void ShowIfNotDismissed(WatermarkWindowConfig config, Func<WatermarkWindow, WatermarkWindow> build)
        {
            if (EditorPrefs.GetBool(config.DontShowAgainKey, false)) return;
            Show(config, build);
        }

        public static void Show(WatermarkWindowConfig config, Func<WatermarkWindow, WatermarkWindow> build)
        {
            var window = WatermarkWindow.Create(config);
            build(window).Launch();
        }
    }
}
