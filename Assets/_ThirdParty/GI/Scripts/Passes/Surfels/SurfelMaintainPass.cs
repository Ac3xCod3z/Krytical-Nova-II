using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SurfelMaintainPass : Pass
    {
        private Kernel _karguments2;
        private Kernel _karguments;
        private Kernel _kcollect;
        private Kernel _kremove;
        private Kernel _kreset;
        private Kernel _kupdate;

        private Surfels _surfels;

        private Vector4[] _offsets;

        public SurfelMaintainPass(Surfels surfels)
        {
            _surfels = surfels;

            _karguments   = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/MaintainArguments"), "Main");
            _karguments2  = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/MaintainArguments2"), "Main");
            _kcollect     = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/MaintainCollect"), "Main");
            _kremove      = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/MaintainRemove"), "Main");
            _kreset       = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/Reset"), "Main");
            _kupdate      = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/MaintainUpdate"), "Main");

            _offsets = new Vector4[Defines.SURFEL_CASCADE_COUNT];
        }

        public override void Execute(Executor executor, PassData data)
        {
            _surfels.GlobalParameters();

            var pass = executor.Pass();
            var cmd = pass.Begin("Surfel Maintenance");

            var cell_count = Defines.SURFEL_CASCADE_CELL_COUNT * Defines.SURFEL_CASCADE_COUNT;
            var all_count = cell_count > Defines.SURFEL_MAX_ENTRIES ? cell_count : Defines.SURFEL_MAX_ENTRIES;

            if (_surfels.NeedReset)
            {
                _surfels.Parameters(cmd, _kreset);
                _kreset.DispatchEnoughFor(cmd, all_count);

                _surfels.NeedReset = false;
            }


            _surfels.Parameters(cmd, _karguments);
            _karguments.Bind(cmd, P.ArgumentsRW, _surfels.MaintainArguments);
            _karguments.DispatchOne(cmd);

            //

            _surfels.Parameters(cmd, _kupdate);
            _kupdate.Dispatch(cmd, _surfels.MaintainArguments);

            _surfels.Parameters(cmd, _karguments2);
            _karguments2.Bind(cmd, P.ArgumentsRW, _surfels.CollectArguments);
            _karguments2.DispatchOne(cmd);

            //

            _surfels.Parameters(cmd, _kcollect);
            _kcollect.Dispatch(cmd, _surfels.CollectArguments);

            //

            _surfels.Parameters(cmd, _kremove);
            _kremove.Dispatch(cmd, _surfels.CollectArguments, 16);

            //

            pass.End();
        }
    }
}