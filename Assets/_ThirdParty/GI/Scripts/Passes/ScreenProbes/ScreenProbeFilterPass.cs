using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ScreenProbeFilterPass : Pass
    {
        private Kernel _kernel;

        private ScreenProbes _screen_probes;

        public ScreenProbeFilterPass(ScreenProbes screen_probes)
        {
            _screen_probes = screen_probes;

            _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Filter"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Screen Probe Filter");

            _screen_probes.Parameters(cmd, _kernel);

            _kernel.BindOnce(cmd, P.ScreenProbeRadianceAtlas, _screen_probes.RadianceAtlas);
            _kernel.BindOnce(cmd, P.ScreenProbeRadianceAtlasRW, _screen_probes.RadianceAtlas2);

            _kernel.Dispatch(cmd, _screen_probes.CalculatedCount);

            pass.End();

            _screen_probes.SwapRadianceAtlas();
        }
    }
}