using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SurfelLightProbePass : Pass
    {
        private Kernel _arguments;
        private Kernel _kernel;

        private Probes _probes;
        private Surfels _surfels;

        public SurfelLightProbePass(Probes probes, Surfels surfels)
        {
            _probes = probes;
            _surfels = surfels;

            _arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/Arguments"), "Main");
            _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/LightProbe"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            _kernel.Keyword("MAINTAIN_PROBES", data.Settings.SurfelsMaintainProbes);
            _kernel.Keyword("PROBES_SH3", data.Settings.Probes == ProbeChoice.SH3);
            _kernel.Keyword("PROBES_OCCLUSION", data.Settings.ProbeOcclusion);

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Surfel Light Probe");

            _surfels.Parameters(cmd, _arguments);

            _arguments.Seti(cmd, P.GroupSize, _kernel.GroupX);
            _arguments.Bind(cmd, P.ArgumentsRW, _surfels.UpdateArguments);

            _arguments.DispatchOne(cmd);

            //

            _probes.Parameters(cmd, _kernel);
            _surfels.Parameters(cmd, _kernel);

            if (_probes.DepthAtlas != null)
                _kernel.BindOnce(cmd, P.ProbeDepthAtlas, _probes.DepthAtlas);

            _kernel.Dispatch(cmd, _surfels.UpdateArguments);

            //

            pass.End();
        }
    }
}