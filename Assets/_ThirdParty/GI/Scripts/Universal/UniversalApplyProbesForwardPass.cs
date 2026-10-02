using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering.Universal;

namespace GI
{
    class UniversalApplyProbesForwardPass : RenderObjectsPass
    {
        public UniversalApplyProbesForwardPass()
            : base("Apply Probes Forward",
                   RenderPassEvent.AfterRenderingOpaques,
                   null,
                   RenderQueueType.Opaque,
                   int.MaxValue,
                   new RenderObjects.CustomCameraSettings())
        {
            overrideShader = Resources.Load<Shader>("Shaders/Apply/ProbesForward");
            SetDepthState(false, CompareFunction.Equal);
        }
    }
}
