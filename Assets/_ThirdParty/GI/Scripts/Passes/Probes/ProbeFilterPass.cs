using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeFilterPass : Pass
    {
        private Kernel _kernel;

        private Probes _probes;

        public ProbeFilterPass(Probes probes)
        {
            _probes = probes;

            _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Filter"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Filter");

            _probes.Parameters(cmd, _kernel);

            _kernel.BindOnce(cmd, P.ProbeGatherAtlas, _probes.GatherAtlas);

            _kernel.BindOnce(cmd, P.ProbeFilterAtlasRW, _probes.FilterAtlas);

            _kernel.Dispatch(cmd, _probes.ProbeArguments, 16);

            pass.End();
        }
    }
}