using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SurfelCreatePass : Pass
    {
        private Kernel _karguments;
        private Kernel _kcreate;
        private Kernel _kcreate_clear;

        private Kernel _kaccept;
        private Kernel _kquery;
        private Kernel _kupdate;

        private Kernel _kfar_accept;
        private Kernel _kfar_query;
        private Kernel _kfar_update;

        private Kernel _kscreen_query;
        private Kernel _kscreen_accept;

        private Probes _probes;
        private ScreenProbes _screen_probes;
        private FarProbes _far_probes;
        private Surfels _surfels;
        private Traces _traces;

        public SurfelCreatePass(Probes probes, ScreenProbes screen_probes, FarProbes far_probes, Surfels surfels, Traces traces)
        {
            _probes = probes;
            _screen_probes = screen_probes;
            _far_probes = far_probes;
            _surfels = surfels;
            _traces = traces;

            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/CreateArguments"), "Main");
            _kcreate = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/Create"), "Main");
            _kcreate_clear = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/CreateClear"), "Main");

            _kaccept = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/Accept"), "Main");
            _kquery = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/Query"), "Main");
            _kupdate = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/Update"), "Main");

            _kfar_accept = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/AcceptFar"), "Main");
            _kfar_query = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/QueryFar"), "Main");
            _kfar_update = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/UpdateFar"), "Main");

            _kscreen_accept = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/AcceptScreen"), "Main");
            _kscreen_query = new Kernel(Resources.Load<ComputeShader>("Shaders/Surfels/QueryScreen"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            _kaccept.Keyword("SURFEL_MAPPING", data.Settings.Experimental.SurfelMapping);
            _kfar_accept.Keyword("SURFEL_MAPPING", data.Settings.Experimental.SurfelMapping);

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Surfel Create");

            //

            if (data.Settings.Experimental.ScreenProbes)
            {
                _screen_probes.Parameters(cmd, _kscreen_query);
                _surfels.Parameters(cmd, _kscreen_query);
                _traces.Parameters(cmd, _kscreen_query);

                _kscreen_query.Dispatch(cmd, _screen_probes.TraceArguments);
            }

            //

            if (data.Settings.FarProbes)
            {
                _far_probes.Parameters(cmd, _kfar_query);
                _surfels.Parameters(cmd, _kfar_query);
                _traces.Parameters(cmd, _kfar_query);

                _kfar_query.Dispatch(cmd, _far_probes.TraceArguments);
            }

            //

            _probes.Parameters(cmd, _kquery);
            _surfels.Parameters(cmd, _kquery);
            _traces.Parameters(cmd, _kquery);

            _kquery.Dispatch(cmd, _probes.TraceArguments);

            //

            _surfels.Parameters(cmd, _karguments);
            _karguments.Bind(cmd, P.ArgumentsRW, _surfels.CreateArguments);
            _karguments.DispatchOne(cmd);

            //

            _surfels.Parameters(cmd, _kcreate);
            _traces.Parameters(cmd, _kcreate);

            _kcreate.Dispatch(cmd, _surfels.CreateArguments);

            //

            _surfels.Parameters(cmd, _kcreate_clear);
            _kcreate_clear.Dispatch(cmd, _surfels.CreateArguments, 16);

            //

            if (data.Settings.Experimental.ScreenProbes)
            {
                _screen_probes.Parameters(cmd, _kscreen_accept);
                _surfels.Parameters(cmd, _kscreen_accept);
                _traces.Parameters(cmd, _kscreen_accept);

                _kscreen_accept.Dispatch(cmd, _screen_probes.TraceArguments);
            }

            //

            _probes.Parameters(cmd, _kaccept);
            _surfels.Parameters(cmd, _kaccept);
            _traces.Parameters(cmd, _kaccept);

            _kaccept.Dispatch(cmd, _probes.TraceArguments);

            //

            if (_far_probes != null)
            {
                _far_probes.Parameters(cmd, _kfar_accept);
                _surfels.Parameters(cmd, _kfar_accept);
                _traces.Parameters(cmd, _kfar_accept);

                _kfar_accept.Dispatch(cmd, _far_probes.TraceArguments);
            }

            //

            if (data.Settings.Experimental.SurfelMapping)
            {
                _probes.Parameters(cmd, _kupdate);
                _surfels.Parameters(cmd, _kupdate);
                _kupdate.Dispatch(cmd, _probes.RaySetupArguments);

                //

                if (_far_probes != null)
                {
                    _far_probes.Parameters(cmd, _kfar_update);
                    _surfels.Parameters(cmd, _kfar_update);
                    _kfar_update.Dispatch(cmd, _far_probes.RaySetupArguments);
                }
            }

            //

            pass.End();
        }
    }
}