using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ScreenProbeGatherPass : Pass
    {
        private Kernel _kernel;

        private ScreenProbes _screen_probes;

        public ScreenProbeGatherPass(ScreenProbes screen_probes)
        {
            _screen_probes = screen_probes;

            _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Gather"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Screen Probe Gather");

            var octahedral_solid_angle = OctahedralSolidAngle.Get(Defines.SCREEN_PROBE_RES);
            _kernel.BindOnce(cmd, P.OctahedralSolidAngle, octahedral_solid_angle);

            _kernel.BindOnce(cmd, P.ScreenProbeRadianceAtlas, _screen_probes.RadianceAtlas);

            _screen_probes.Parameters(cmd, _kernel);

            _kernel.Dispatch(cmd, _screen_probes.RaySetupArguments);

            pass.End();
        }
    }
}