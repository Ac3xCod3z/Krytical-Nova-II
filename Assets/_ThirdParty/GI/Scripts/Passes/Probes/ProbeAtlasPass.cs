using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeAtlasPass : Pass
    {
        private Kernel _kernel;

        private Probes _probes;

        public ProbeAtlasPass(Probes probes)
        {
            _probes = probes;

            _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Atlas"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            _kernel.Keyword("PROBES_OCCLUSION", data.Settings.ProbeOcclusion);

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Atlas");

            _probes.Parameters(cmd, _kernel);

            if (data.Settings.ProbeFiltering)
                _kernel.BindOnce(cmd, P.ProbeGatherAtlas, _probes.FilterAtlas);
            else
                _kernel.BindOnce(cmd, P.ProbeGatherAtlas, _probes.GatherAtlas);

            _kernel.BindOnce(cmd, P.ProbeRadianceAtlas, _probes.RadianceAtlas);

            _kernel.BindOnce(cmd, P.ProbeRadianceAtlasRW, _probes.RadianceAtlas2);

            if (_probes.DepthAtlas != null)
                _kernel.BindOnce(cmd, P.ProbeDepthAtlasRW, _probes.DepthAtlas);

            _kernel.Dispatch(cmd, _probes.ProbeArguments, 16);

            pass.End();

            _probes.SwapRadianceAtlas();
        }
    }
}