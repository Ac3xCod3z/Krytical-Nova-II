using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class FarProbeAtlasPass : Pass
    {
        private Kernel _karguments;
        private Kernel _katlas;

        private FarProbes _far_probes;

        public FarProbeAtlasPass(FarProbes far_probes)
        {
            _far_probes = far_probes;

            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/AtlasArguments"), "Main");
            _katlas = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Atlas"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Far Probe Atlas");

            //

            _far_probes.Parameters(cmd, _karguments);
            _karguments.Bind(cmd, P.ArgumentsRW, _far_probes.AtlasArguments);

            _karguments.DispatchOne(cmd);

            //

            _far_probes.Parameters(cmd, _katlas);

            _katlas.BindOnce(cmd, P.FarProbeGatherAtlas, _far_probes.GatherAtlas);
            _katlas.BindOnce(cmd, P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);

            _katlas.BindOnce(cmd, P.FarProbeRadianceAtlasRW, _far_probes.RadianceAtlas2);

            _katlas.Dispatch(cmd, _far_probes.AtlasArguments);

            //

            pass.End();

            //

            _far_probes.SwapRadianceAtlas();
        }
    }
}