using UnityEditor;
using UnityEngine;

namespace laplahce.Watermark.Theming
{
    public sealed class ButtonStyleFactory
    {
        private readonly GradientTextureCache textureCache;

        public ButtonStyleFactory(GradientTextureCache textureCache)
        {
            this.textureCache = textureCache;
        }

        public GUIStyle Build(Color leftNormal, Color rightNormal, Color leftHover, Color rightHover, Color leftActive, Color rightActive, int fontSize = 11, float fixedHeight = 0f, float fixedWidth = 0f)
        {
            var normal = textureCache.Get(leftNormal, rightNormal);
            var hover = textureCache.Get(leftHover, rightHover);
            var active = textureCache.Get(leftActive, rightActive);

            return new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                fixedHeight = fixedHeight,
                fixedWidth = fixedWidth,
                normal = { background = normal, textColor = Color.white },
                hover = { background = hover, textColor = Color.white },
                active = { background = active, textColor = Color.white }
            };
        }

        public GUIStyle BuildPrimary(int fontSize = 11, float fixedHeight = 0f, float fixedWidth = 0f, float exposure = 1) => Build(
            WatermarkColors.BtnGradientLeftNormal.WithExposure(exposure), WatermarkColors.BtnGradientRightNormal.WithExposure(exposure),
            WatermarkColors.BtnGradientLeftHover.WithExposure(exposure), WatermarkColors.BtnGradientRightHover.WithExposure(exposure),
            WatermarkColors.BtnGradientLeftActive.WithExposure(exposure), WatermarkColors.BtnGradientRightActive.WithExposure(exposure),
            fontSize, fixedHeight, fixedWidth);

        public GUIStyle BuildPipeline(int fontSize = 11, float fixedHeight = 0f, float fixedWidth = 0f, float exposure = 1) => Build(
            WatermarkColors.PipelineBtnLeftNormal.WithExposure(exposure), WatermarkColors.PipelineBtnRightNormal.WithExposure(exposure),
            WatermarkColors.PipelineBtnLeftHover.WithExposure(exposure), WatermarkColors.PipelineBtnRightHover.WithExposure(exposure),
            WatermarkColors.PipelineBtnLeftActive.WithExposure(exposure), WatermarkColors.PipelineBtnRightActive.WithExposure(exposure),
            fontSize, fixedHeight, fixedWidth);

        public Rect DrawShadow(Rect rect, bool pipeline, float border = 3, int fontSize = 11, float fixedHeight = 0f, float fixedWidth = 0f)
        {
            GUIStyle style;
            if (pipeline) style = BuildPipeline(fontSize, fixedHeight, fixedWidth, 1.15f);
            else style = BuildPrimary(fontSize, fixedHeight, fixedWidth, 1.15f);

            GUI.Label(rect, "", style);

            EditorGUI.DrawRect(new Rect(rect.x + 3, rect.y + 4, rect.width, rect.height), WatermarkColors.ButtonShadowHeavy);
            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 3, rect.width, rect.height), WatermarkColors.ButtonShadowMid);

            rect.xMin += border;
            rect.yMin += border;
            rect.xMax -= border;
            rect.yMax -= border;
            return rect;
        }
    }
}
