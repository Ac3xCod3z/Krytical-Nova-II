using System.Collections.Generic;
using CTFX2.Creator.Inspector.Rendering;

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    public static partial class FieldRendererRegistry
    {
        private static readonly List<FieldRenderer> Renderers = new()
        {
            new StringRenderer(),
            new RangeRenderer(),
            new MinMaxSliderRenderer(),
            new GradientColorRenderer(),
            new StringArrayRenderer(),
            new ArrayRenderer(),
            new CameraShakeRenderer(),
            new GenericRenderer(),
        };
    }
#endif
}