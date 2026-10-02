using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class FarProbeRenderPass : Pass
    {
        private Kernel _kbuild;

        private ComputeBuffer _debug_arguments;

        private FarProbes _far_probes;

        private Material _debug;

        private bool _traces;

        public FarProbeRenderPass(FarProbes far_probes, bool traces)
        {
            _far_probes = far_probes;
            _traces = traces;

            _debug = new Material(Shader.Find("GI/FarProbes"));

            _kbuild = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/RenderArguments"), "Main");

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
            var cmd = pass.Begin("Far Probe Render");

            _far_probes.Parameters(cmd, _kbuild);

            _kbuild.Seti(cmd, P.IndexStart, (int)data.Atlas.DebugMesh.GetIndexStart(0));
            _kbuild.Seti(cmd, P.IndexCount, (int)data.Atlas.DebugMesh.GetIndexCount(0));
            _kbuild.Seti(cmd, P.BaseVertex, (int)data.Atlas.DebugMesh.GetBaseVertex(0));

            _kbuild.Bind(cmd, P.ArgumentsRW, _debug_arguments);

            _kbuild.DispatchOne(cmd);

            _debug.SetTexture(P.FarProbeGatherAtlas, _far_probes.GatherAtlas);
            _debug.SetTexture(P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);

            cmd.DrawMeshInstancedIndirect(data.Atlas.DebugMesh, 0, _debug, _traces ? 1 : 0, _debug_arguments);

            pass.End();
        }
    }
}