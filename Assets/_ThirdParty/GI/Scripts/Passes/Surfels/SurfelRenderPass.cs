using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SurfelRenderPass : Pass
    {
        private Kernel _arguments;

        private ComputeBuffer _debug_arguments;

        private Surfels _surfels;

        private Material _debug;

        public SurfelRenderPass(Surfels surfels)
        {
            _surfels = surfels;

            _debug = new Material(Shader.Find("GI/Surfels"));

            _arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/RenderArguments"), "Main");

            _debug_arguments = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        }

        public override void Release()
        {
            if (_debug_arguments != null)
            {
                _debug_arguments.Release();
                _debug_arguments = null;
            }
        }

        public override void Execute(Executor executor, PassData data)
        {
            if (data.Atlas.DebugMesh == null)
                return;

            var pass = executor.Pass();
            var cmd = pass.Begin("Surfel Render");

            _arguments.Seti(cmd, P.IndexStart, (int)data.Atlas.DebugMesh.GetIndexStart(0));
            _arguments.Seti(cmd, P.IndexCount, (int)data.Atlas.DebugMesh.GetIndexCount(0));
            _arguments.Seti(cmd, P.BaseVertex, (int)data.Atlas.DebugMesh.GetBaseVertex(0));

            _surfels.Parameters(cmd, _arguments);

            _arguments.Bind(cmd, P.ArgumentsRW, _debug_arguments);

            _arguments.DispatchOne(cmd);

            cmd.DrawMeshInstancedIndirect(data.Atlas.DebugMesh, 0, _debug, 0, _debug_arguments);

            pass.End();
        }
    }
}