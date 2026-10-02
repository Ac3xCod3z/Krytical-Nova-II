using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class Transparents
    {
        public RenderTexture Target;

        public void Release()
        {
            destroyTarget();
        }

        private void destroyTarget()
        {
            if (Target != null)
            {
                Target.Release();
                Object.DestroyImmediate(Target);
                Target = null;
            }
        }

        public void EnsureTarget(int camera_width, int camera_height)
        {
            var target_width = camera_width / 4;
            var target_height = camera_height / 4;

            if (Target == null || Target.width != target_width || Target.height != target_height)
            {
                destroyTarget();

                RenderTextureDescriptor desc = new RenderTextureDescriptor(target_width, target_height, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
                desc.autoGenerateMips = false;

                Target = new RenderTexture(desc);
                Target.Create();
            }
        }
    }
}