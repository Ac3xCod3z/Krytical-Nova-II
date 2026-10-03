using UnityEngine;

namespace laplahce.Watermark.Core
{
    public sealed class WatermarkWindowConfig
    {
        public string PackName { get; set; }
        public string Slug { get; set; }
        public string PackVersion { get; set; }
        public string Tagline { get; set; }
        public string DontShowAgainKey { get; set; }
        public string MenuPath { get; set; }
        public int MenuPriority { get; set; } = 1;
        public Vector2 Size { get; set; } = new Vector2(380f, 520f);
    }
}
