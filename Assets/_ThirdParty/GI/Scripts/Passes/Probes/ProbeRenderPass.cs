using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeRenderPass : Pass
    {
        private Kernel _kbuild;

        private ComputeBuffer _debug_arguments;
        private ComputeBuffer _counter;

        private Probes _probes;

        private Material _debug;

        public ProbeRenderPass(Probes probes, DebugChoice choice)
        {
            _probes = probes;

            _debug = new Material(Shader.Find("GI/Probes"));

            switch (choice)
            {
                case DebugChoice.Probes:
                    _debug.DisableKeyword("RADIANCE");
                    _debug.DisableKeyword("DEPTHS");
                    break;

                case DebugChoice.ProbeTexels:
                    _debug.EnableKeyword("RADIANCE");
                    _debug.DisableKeyword("DEPTHS");
                    break;

                case DebugChoice.Depths:
                    _debug.DisableKeyword("RADIANCE");
                    _debug.EnableKeyword("DEPTHS");
                    break;
            }

            _kbuild = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/RenderArguments"), "Main");

            _debug_arguments = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            _counter = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.Raw);
        }

        public override void Release()
        {
            if (_debug_arguments != null)
            {
                _debug_arguments.Release();
                _debug_arguments = null;
            }

            if (_counter != null)
            {
                _counter.Release();
                _counter = null;
            }
        }

        public override void Execute(Executor executor, PassData data)
        {
            if (data.Atlas.DebugMesh == null)
                return;

            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Render");

            switch (data.Settings.Probes)
            {
                case ProbeChoice.Simple:
                    _debug.DisableKeyword("PROBES_SH3");
                    _debug.EnableKeyword("PROBES_SIMPLE");
                    break;

                case ProbeChoice.SH2:
                    _debug.DisableKeyword("PROBES_SH3");
                    _debug.DisableKeyword("PROBES_SIMPLE");
                    break;

                case ProbeChoice.SH3:
                    _debug.EnableKeyword("PROBES_SH3");
                    _debug.DisableKeyword("PROBES_SIMPLE");
                    break;
            }

            _probes.Parameters(cmd, _kbuild);

            _kbuild.Seti(cmd, P.IndexStart, (int)data.Atlas.DebugMesh.GetIndexStart(0));
            _kbuild.Seti(cmd, P.IndexCount, (int)data.Atlas.DebugMesh.GetIndexCount(0));
            _kbuild.Seti(cmd, P.BaseVertex, (int)data.Atlas.DebugMesh.GetBaseVertex(0));

            _kbuild.Bind(cmd, P.ArgumentsRW, _debug_arguments);

            _kbuild.DispatchOne(cmd);

            if (!executor.IsScriptable())
                Util.SetCameraParameters(_debug, data.Camera);

            _debug.SetTexture(P.ProbeRadianceAtlas, _probes.RadianceAtlas);

            cmd.DrawMeshInstancedIndirect(data.Atlas.DebugMesh, 0, _debug, 0, _debug_arguments);

            pass.End();
        }
    }
}