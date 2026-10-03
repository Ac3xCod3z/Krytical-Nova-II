using UnityEngine;

namespace laplahce.Watermark.Theming
{
    public static class ColorExtensions
    {
        public static Color WithAlpha(this Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
        public static Color WithExposure(this Color color, float exposure) => new Color(
            Mathf.Clamp01(color.r * exposure),
            Mathf.Clamp01(color.g * exposure),
            Mathf.Clamp01(color.b * exposure),
            color.a
        );
    }

    public static class WatermarkColors
    {
        public static readonly Color Accent = new Color(0.42f, 0.39f, 1.00f);
        public static readonly Color AccentSubtle = new Color(0.42f, 0.39f, 1.00f, 0.55f);

        public static readonly Color ContentBackground = new Color(0.11f, 0.08f, 0.18f, 0.40f);
        public static readonly Color TitleText = new Color(0.93f, 0.90f, 1.00f);
        public static readonly Color SubtitleText = new Color(0.60f, 0.55f, 0.78f);
        public static readonly Color ContentText = new Color(0.78f, 0.73f, 0.94f);

        public static readonly Color SubtleText = new Color(0.50f, 0.50f, 0.50f);
        public static readonly Color Separator = new Color(0.50f, 0.50f, 0.50f, 0.18f);
        public static readonly Color BadgeText = new Color(0.50f, 0.46f, 0.87f);
        public static readonly Color BadgeBackground = new Color(0.24f, 0.20f, 0.72f, 0.12f);

        public static readonly Color ButtonShadowHeavy = new Color(0f, 0f, 0f, 0.28f);
        public static readonly Color ButtonShadowMid = new Color(0f, 0f, 0f, 0.14f);
        public static readonly Color ButtonShadowLight = new Color(0f, 0f, 0f, 0.06f);

        public static readonly Color BtnGradientLeftNormal = new Color(0.36f, 0.33f, 0.80f);
        public static readonly Color BtnGradientRightNormal = new Color(0.10f, 0.62f, 0.46f);
        public static readonly Color BtnGradientLeftHover = new Color(0.44f, 0.41f, 0.90f);
        public static readonly Color BtnGradientRightHover = new Color(0.14f, 0.72f, 0.54f);
        public static readonly Color BtnGradientLeftActive = new Color(0.28f, 0.26f, 0.65f);
        public static readonly Color BtnGradientRightActive = new Color(0.08f, 0.50f, 0.36f);

        public static readonly Color PipelineBtnLeftNormal = new Color(0.20f, 0.52f, 0.64f);
        public static readonly Color PipelineBtnRightNormal = new Color(0.18f, 0.32f, 0.54f);
        public static readonly Color PipelineBtnLeftHover = new Color(0.28f, 0.64f, 0.78f);
        public static readonly Color PipelineBtnRightHover = new Color(0.24f, 0.40f, 0.66f);
        public static readonly Color PipelineBtnLeftActive = new Color(0.16f, 0.42f, 0.52f);
        public static readonly Color PipelineBtnRightActive = new Color(0.14f, 0.26f, 0.44f);
    }
}
