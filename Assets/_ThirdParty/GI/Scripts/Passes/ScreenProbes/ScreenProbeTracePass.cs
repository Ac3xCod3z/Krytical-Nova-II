using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ScreenProbeTracePass : Pass
    {
        private Kernel _kray_setup;
        private Kernel _kray_setup_arguments;
        private Kernel _ktrace_arguments;
        private Kernel _ktrace_global;
        private Kernel _ktrace_local;
        private Kernel _ktrace_terrain;

        private FarProbes _far_probes;
        private Frame _frame;
        private GDF _gdf;
        private ScreenProbes _screen_probes;
        private SDFGrid _sdf_grid;
        private Surfels _surfels;
        private Traces _traces;

        public ScreenProbeTracePass(Frame frame, Traces traces, GDF gdf, ScreenProbes screen_probes, FarProbes far_probes, Surfels surfels, SDFGrid sdf_grid)
        {
            _far_probes = far_probes;
            _frame = frame;
            _gdf = gdf;
            _screen_probes = screen_probes;
            _sdf_grid = sdf_grid;
            _surfels = surfels;
            _traces = traces;

            _kray_setup = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/RaySetup"), "Main");
            _kray_setup_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/RaySetupArguments"), "Main");
            _ktrace_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/TraceArguments"), "Main");
            _ktrace_global = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/TraceGlobal"), "Main");
            _ktrace_local = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/TraceLocal"), "Main");
            _ktrace_terrain = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/TraceTerrain"), "Main");

            _ktrace_global.Keyword("FAR_PROBES", far_probes != null);
            _ktrace_local.Keyword("FAR_PROBES", far_probes != null);
            _ktrace_terrain.Keyword("FAR_PROBES", far_probes != null);
        }

        public override void Execute(Executor executor, PassData data)
        {
            _ktrace_global.Keyword("LOCAL_TRACING", data.Settings.LocalTracing);

            var pass = executor.Pass();
            var cmd = pass.Begin("Screen Probe Trace");

            //

            {
                _screen_probes.Parameters(cmd, _kray_setup_arguments);
                _traces.Parameters(cmd, _kray_setup_arguments);

                _kray_setup_arguments.Bind(cmd, P.ArgumentsRW, _screen_probes.RaySetupArguments);

                _kray_setup_arguments.DispatchOne(cmd);

                //

                _screen_probes.Parameters(cmd, _kray_setup);
                _traces.Parameters(cmd, _kray_setup);
                _surfels.Parameters(cmd, _kray_setup);

                _kray_setup.Dispatch(cmd, _screen_probes.RaySetupArguments);
            }

            //

            {
                _traces.Parameters(cmd, _ktrace_arguments);

                _ktrace_arguments.Bind(cmd, P.ArgumentsRW, _screen_probes.TraceArguments);

                _ktrace_arguments.DispatchOne(cmd);

                //

                if (data.Settings.LocalTracing)
                {
                    _frame.Parameters(cmd, _ktrace_local);
                    _screen_probes.Parameters(cmd, _ktrace_local);
                    _surfels.Parameters(cmd, _ktrace_local);
                    _sdf_grid.Parameters(cmd, _ktrace_local);
                    _traces.Parameters(cmd, _ktrace_local);

                    if (_far_probes != null) _far_probes.Parameters(cmd, _ktrace_local);

                    _ktrace_local.Bind(cmd, P.SDFAssets, data.Atlas.Assets);
                    _ktrace_local.Bind(cmd, P.DFInstances, data.Atlas.Buffer);
                    _ktrace_local.Bind(cmd, P.SDFBricks, data.Atlas.Bricks);
                    _ktrace_local.BindOnce(cmd, P.SDFAtlas, data.Atlas.SDF);
                    _ktrace_local.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);

                    _ktrace_local.Dispatch(cmd, _screen_probes.TraceArguments);
                }

                //

                _frame.Parameters(cmd, _ktrace_global);
                _screen_probes.Parameters(cmd, _ktrace_global);
                _surfels.Parameters(cmd, _ktrace_global);
                _gdf.Parameters(cmd, _ktrace_global);
                _gdf.Textures(cmd, _ktrace_global);

                _ktrace_global.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);

                _traces.Parameters(cmd, _ktrace_global);

                if (_far_probes != null) _far_probes.Parameters(cmd, _ktrace_global);

                _ktrace_global.Dispatch(cmd, _screen_probes.TraceArguments);

                //

                if (data.Atlas.Terrain != null && data.Atlas.Terrain.IsEnabled)
                {
                    _frame.Parameters(cmd, _ktrace_terrain);
                    _screen_probes.Parameters(cmd, _ktrace_terrain);
                    _surfels.Parameters(cmd, _ktrace_terrain);
                    _traces.Parameters(cmd, _ktrace_terrain);

                    if (_far_probes != null) _far_probes.Parameters(cmd, _ktrace_terrain);

                    Terrains.Parameters(cmd, _ktrace_terrain, data.Atlas.Terrain);

                    _ktrace_terrain.Dispatch(cmd, _screen_probes.TraceArguments);
                }
            }

            //

            pass.End();
        }
    }
}