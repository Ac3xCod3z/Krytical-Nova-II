using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeMaintainPass : Pass
    {
        private Kernel _karguments2;
        private Kernel _karguments;
        private Kernel _kcollect;
        private Kernel _kneighbours;
        private Kernel _kremove;
        private Kernel _kreset;
        private Kernel _kreset_atlas;
        private Kernel _kupdate;

        private Probes _probes;

        public ProbeMaintainPass(Probes probes)
        {
            _probes = probes;

            _karguments   = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/MaintainArguments"), "Main");
            _karguments2  = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/MaintainArguments2"), "Main");
            _kcollect     = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/MaintainCollect"), "Main");
            _kneighbours  = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/MaintainNeighbours"), "Main");
            _kremove      = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/MaintainRemove"), "Main");
            _kreset       = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Reset"), "Main");
            _kreset_atlas = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/ResetAtlas"), "Main");
            _kupdate      = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Maintain"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            _probes.GlobalParameters();

            _kreset_atlas.Keyword("PROBES_OCCLUSION", data.Settings.ProbeOcclusion);

            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Maintenance");

            if (_probes.NeedReset)
            {
                _probes.Parameters(cmd, _kreset);

                var cell_count = Defines.PROBE_CASCADE_CELL_COUNT * _probes.CascadeCount;
                _kreset.DispatchEnoughFor(cmd, cell_count > Defines.PROBE_MAX_ENTRIES ? cell_count : Defines.PROBE_MAX_ENTRIES, 1, 1);

                _kreset_atlas.BindOnce(cmd, P.ProbeRadianceAtlasRW, _probes.RadianceAtlas);

                if (_probes.DepthAtlas != null)
                {
                    _kreset_atlas.BindOnce(cmd, P.ProbeDepthAtlasRW, _probes.DepthAtlas);
                    _kreset_atlas.DispatchEnoughFor(cmd, _probes.DepthAtlas.width, _probes.DepthAtlas.height);
                }
                else
                    _kreset_atlas.DispatchEnoughFor(cmd, _probes.RadianceAtlas.width, _probes.RadianceAtlas.height);

                _probes.NeedReset = false;
            }

            //

            _probes.Parameters(cmd, _karguments);
            _karguments.Bind(cmd, P.ArgumentsRW, _probes.MaintainArguments);
            _karguments.DispatchOne(cmd);

            //

            _probes.Parameters(cmd, _kneighbours);
            _kneighbours.Dispatch(cmd, _probes.MaintainArguments);

            //

            _probes.Parameters(cmd, _kupdate);
            _kupdate.Dispatch(cmd, _probes.MaintainArguments);

            //

            _probes.Parameters(cmd, _karguments2);
            _karguments2.Bind(cmd, P.ArgumentsRW, _probes.CollectArguments);
            _karguments2.DispatchOne(cmd);

            //

            _probes.Parameters(cmd, _kcollect);
            _kcollect.Dispatch(cmd, _probes.CollectArguments);

            //

            _probes.Parameters(cmd, _kremove);
            _kremove.Dispatch(cmd, _probes.CollectArguments, 16);

            //

            pass.End();
        }
    }
}