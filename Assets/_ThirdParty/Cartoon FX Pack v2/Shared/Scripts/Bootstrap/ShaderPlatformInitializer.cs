using UnityEngine;

namespace CTFX2.Bootstrap
{
    public static class ShaderPlatformInitializer
    {
        const string MOBILE_KEYWORD = "CTFX_IS_MOBILE_SHADER";
        const string WEBGL_KEYWORD = "CTFX_IS_WEBGL_SHADER";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Init()
        {
            if (Application.isMobilePlatform)
                Shader.EnableKeyword(MOBILE_KEYWORD);
            else
                Shader.DisableKeyword(MOBILE_KEYWORD);

            if (Application.platform == RuntimePlatform.WebGLPlayer)
                Shader.EnableKeyword(WEBGL_KEYWORD);
            else
                Shader.DisableKeyword(WEBGL_KEYWORD);
        }
    }
}