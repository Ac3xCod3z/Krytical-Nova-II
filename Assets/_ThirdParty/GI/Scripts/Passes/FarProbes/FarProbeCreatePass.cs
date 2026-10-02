using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class FarProbeCreatePass : Pass
    {
        private Kernel _kcreate;
        private Kernel _kcreate_arguments;
        private Kernel _kcreate_clear;
        private Kernel _kquery;
        private Kernel _kquery_screen;

        private Probes _probes;
        private FarProbes _far_probes;
        private ScreenProbes _screen_probes;

        private int _repeat_count;

        public FarProbeCreatePass(FarProbes far_probes, Probes probes, ScreenProbes screen_probes)
        {
            _far_probes = far_probes;
            _probes = probes;
            _screen_probes = screen_probes;

            _kcreate = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Create"), "Main");
            _kcreate_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/CreateArguments"), "Main");
            _kcreate_clear = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/CreateClear"), "Main");
            _kquery = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Query"), "Main");

            if (screen_probes != null)
                _kquery_screen = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/QueryScreen"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Far Probe Create");

            //

            if (data.Settings.Experimental.ScreenProbes)
            {
                _screen_probes.Parameters(cmd, _kquery_screen);
                _far_probes.Parameters(cmd, _kquery_screen);
                _kquery_screen.Dispatch(cmd, _screen_probes.QueryArguments);
            }

            //

            _probes.Parameters(cmd, _kquery);
            _far_probes.Parameters(cmd, _kquery);
            _kquery.Dispatch(cmd, _probes.ProbeArguments, 32); // third argument group

            //

            _probes.Parameters(cmd, _kcreate_arguments);
            _far_probes.Parameters(cmd, _kcreate_arguments);
            _kcreate_arguments.Bind(cmd, P.ArgumentsRW, _far_probes.CreateArguments);
            _kcreate_arguments.DispatchOne(cmd);

            //

            _probes.Parameters(cmd, _kcreate);
            _far_probes.Parameters(cmd, _kcreate);
            _kcreate.Dispatch(cmd, _far_probes.CreateArguments);

            //

            _probes.Parameters(cmd, _kcreate_clear);
            _far_probes.Parameters(cmd, _kcreate_clear);
            _kcreate_clear.Dispatch(cmd, _far_probes.CreateArguments, 16);

            //

            pass.End();
        }
    }
}