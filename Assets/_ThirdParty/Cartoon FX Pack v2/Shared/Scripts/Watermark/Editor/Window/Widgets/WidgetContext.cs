using System;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Widgets
{
    public sealed class WidgetContext
    {
        public GradientTextureCache TextureCache { get; }
        public ButtonStyleFactory ButtonStyles { get; }

        public Action<string> NavigateToTab { get; }

        public WidgetContext(GradientTextureCache textureCache, ButtonStyleFactory buttonStyles, Action<string> navigateToTab = null)
        {
            TextureCache = textureCache;
            ButtonStyles = buttonStyles;
            NavigateToTab = navigateToTab;
        }
    }
}