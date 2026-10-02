using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering.Universal;

namespace GI
{
    class UniversalApplyTemporalForwardPass : RenderObjectsPass
    {
        public UniversalApplyTemporalForwardPass()
            : base("Apply Temporal Forward",
                   RenderPassEvent.AfterRenderingOpaques,
                   null,
                   RenderQueueType.Opaque,
                   int.MaxValue,
                   new RenderObjects.CustomCameraSettings())
        {
            overrideShader = Resources.Load<Shader>("Shaders/Apply/TemporalForward");
        }
    }
}