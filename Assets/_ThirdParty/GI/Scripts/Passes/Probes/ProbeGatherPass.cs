using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeGatherPass : Pass
    {
        private Kernel _kernel;

        private Probes _probes;

        public ProbeGatherPass(Probes probes, ProbeChoice choice)
        {
            _probes = probes;

            switch (choice)
            {
                case ProbeChoice.SH2: _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Gather"), "MainSH2"); break;
                case ProbeChoice.SH3: _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Gather"), "MainSH3"); break;
                case ProbeChoice.Simple: _kernel = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Gather"), "MainSimple"); break;
            }
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Gather");

            var octahedral_solid_angle = OctahedralSolidAngle.Get(Defines.PROBE_RES);
            _kernel.BindOnce(cmd, P.OctahedralSolidAngle, octahedral_solid_angle);

            _kernel.BindOnce(cmd, P.ProbeRadianceAtlas, _probes.RadianceAtlas);

            _probes.Parameters(cmd, _kernel);

            _kernel.Dispatch(cmd, _probes.ProbeArguments);

            pass.End();
        }
    }
}