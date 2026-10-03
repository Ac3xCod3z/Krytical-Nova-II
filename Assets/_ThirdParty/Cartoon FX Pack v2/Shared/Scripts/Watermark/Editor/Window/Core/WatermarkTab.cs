using System.Collections.Generic;
using laplahce.Watermark.Widgets;

namespace laplahce.Watermark.Core
{
    public sealed class WatermarkTab
    {
        public string Label { get; }
        public List<IWidget> Widgets { get; } = new List<IWidget>();

        public WatermarkTab(string label)
        {
            Label = label;
        }
    }
}
