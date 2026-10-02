using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class Frame
    {
        public int FrameIndex;

        public void Update()
        {
            FrameIndex = (FrameIndex + 1) % 16384;
            Shader.SetGlobalInt(P.FrameIndex, FrameIndex);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeIntParam(kernel.Shader, P.FrameIndex, FrameIndex);
        }
    }
}
