using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class FarProbeMaintainPass : Pass
    {
        private Kernel _karguments2;
        private Kernel _karguments;
        private Kernel _kcollect;
        private Kernel _kremove;
        private Kernel _kreset;
        private Kernel _kupdate;

        private FarProbes _far_probes;

        public FarProbeMaintainPass(FarProbes probes)
        {
            _far_probes = probes;

            _karguments   = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/MaintainArguments"), "Main");
            _karguments2  = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/MaintainArguments2"), "Main");
            _kcollect     = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/MaintainCollect"), "Main");
            _kremove      = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/MaintainRemove"), "Main");
            _kreset       = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Reset"), "Main");
            _kupdate      = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Maintain"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            _far_probes.GlobalParameters();

            var pass = executor.Pass();
            var cmd = pass.Begin("Far Probe Maintenance");

            if (_far_probes.NeedReset)
            {
                _far_probes.Parameters(cmd, _kreset);

                var cell_count = Defines.FAR_PROBE_CASCADE_CELL_COUNT * _far_probes.CascadeCount;
                _kreset.DispatchEnoughFor(cmd, cell_count > Defines.FAR_PROBE_MAX_ENTRIES ? cell_count : Defines.FAR_PROBE_MAX_ENTRIES, 1, 1);

                _far_probes.NeedReset = false;
            }

            //

            _far_probes.Parameters(cmd, _karguments);
            _karguments.Bind(cmd, P.ArgumentsRW, _far_probes.MaintainArguments);
            _karguments.DispatchOne(cmd);

            //

            _far_probes.Parameters(cmd, _kupdate);
            _kupdate.Dispatch(cmd, _far_probes.MaintainArguments);

            //

            _far_probes.Parameters(cmd, _karguments2);
            _karguments2.Bind(cmd, P.ArgumentsRW, _far_probes.CollectArguments);
            _karguments2.DispatchOne(cmd);

            //

            _far_probes.Parameters(cmd, _kcollect);
            _kcollect.Dispatch(cmd, _far_probes.CollectArguments);

            //

            _far_probes.Parameters(cmd, _kremove);
            _kremove.Dispatch(cmd, _far_probes.CollectArguments, 16);

            //

            pass.End();
        }
    }
}