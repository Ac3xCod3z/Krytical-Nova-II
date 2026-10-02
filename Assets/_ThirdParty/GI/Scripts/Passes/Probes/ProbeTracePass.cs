using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeTracePass : Pass
    {
        private Kernel _kray_setup;
        private Kernel _kray_setup_arguments;
        private Kernel _ksurfel_arguments;
        private Kernel _ksurfel_probe;
        private Kernel _ksurfel_probe_query;
        private Kernel _ktrace_arguments;
        private Kernel _ktrace_global;
        private Kernel _ktrace_large;
        private Kernel _ktrace_local;
        private Kernel _ktrace_terrain;

        private FarProbes _far_probes;
        private Frame _frame;
        private GDF _gdf;
        private Probes _probes;
        private SDFGrid _sdf_grid;
        private Surfels _surfels;
        private Traces _traces;

        public ProbeTracePass(Frame frame, Traces traces, GDF gdf, Probes probes, FarProbes far_probes, Surfels surfels, SDFGrid sdf_grid)
        {
            _far_probes = far_probes;
            _frame = frame;
            _gdf = gdf;
            _probes = probes;
            _sdf_grid = sdf_grid;
            _surfels = surfels;
            _traces = traces;

            _kray_setup = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/RaySetup"), "Main");
            _kray_setup_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/RaySetupArguments"), "Main");
            _ktrace_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/TraceArguments"), "Main");
            _ktrace_global = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/TraceGlobal"), "Main");
            _ktrace_large = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/TraceLarge"), "Main");
            _ktrace_local = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/TraceLocal"), "Main");
            _ktrace_terrain = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/TraceTerrain"), "Main");

            _ktrace_global.Keyword("FAR_PROBES", far_probes != null);
            _ktrace_local.Keyword("FAR_PROBES", far_probes != null);
            _ktrace_terrain.Keyword("FAR_PROBES", far_probes != null);
        }

        public override void Execute(Executor executor, PassData data)
        {
            _kray_setup.Keyword("RETRACE_ALL", data.Settings.Retrace == RetraceChoice.All);
            _kray_setup.Keyword("RETRACE_PARTIAL", data.Settings.Retrace == RetraceChoice.Partial);

            _ktrace_global.Keyword("LOCAL_TRACING", data.Settings.LocalTracing);

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Trace");

            {
                _probes.Parameters(cmd, _kray_setup_arguments);
                _traces.Parameters(cmd, _kray_setup_arguments);

                _kray_setup_arguments.Bind(cmd, P.ArgumentsRW, _probes.RaySetupArguments);

                _kray_setup_arguments.DispatchOne(cmd);

                //

                _frame.Parameters(cmd, _kray_setup);
                _probes.Parameters(cmd, _kray_setup);
                _surfels.Parameters(cmd, _kray_setup);
                _traces.Parameters(cmd, _kray_setup);

                _kray_setup.Dispatch(cmd, _probes.RaySetupArguments);
            }

            {
                _traces.Parameters(cmd, _ktrace_arguments);

                _ktrace_arguments.Bind(cmd, P.ArgumentsRW, _probes.TraceArguments);

                _ktrace_arguments.DispatchOne(cmd);

                //

                if (data.Settings.LocalTracing)
                {
                    _frame.Parameters(cmd, _ktrace_local);
                    _probes.Parameters(cmd, _ktrace_local);
                    _surfels.Parameters(cmd, _ktrace_local);
                    _sdf_grid.Parameters(cmd, _ktrace_local);
                    _traces.Parameters(cmd, _ktrace_local);

                    if (_far_probes != null) _far_probes.Parameters(cmd, _ktrace_local);

                    _ktrace_local.Bind(cmd, P.SDFAssets, data.Atlas.Assets);
                    _ktrace_local.Bind(cmd, P.DFInstances, data.Atlas.Buffer);
                    _ktrace_local.Bind(cmd, P.SDFBricks, data.Atlas.Bricks);
                    _ktrace_local.BindOnce(cmd, P.SDFAtlas, data.Atlas.SDF);
                    _ktrace_local.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);

                    _ktrace_local.Dispatch(cmd, _probes.TraceArguments);
                }

                //

                _frame.Parameters(cmd, _ktrace_global);
                _probes.Parameters(cmd, _ktrace_global);
                _surfels.Parameters(cmd, _ktrace_global);
                _gdf.Parameters(cmd, _ktrace_global);
                _gdf.Textures(cmd, _ktrace_global);

                _ktrace_global.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);

                _traces.Parameters(cmd, _ktrace_global);

                if (_far_probes != null) _far_probes.Parameters(cmd, _ktrace_global);

                _ktrace_global.Dispatch(cmd, _probes.TraceArguments);

                //

                if (data.Atlas.Terrain != null && data.Atlas.Terrain.IsEnabled)
                {
                    _frame.Parameters(cmd, _ktrace_terrain);
                    _probes.Parameters(cmd, _ktrace_terrain);
                    _surfels.Parameters(cmd, _ktrace_terrain);
                    _traces.Parameters(cmd, _ktrace_terrain);

                    if (_far_probes != null) _far_probes.Parameters(cmd, _ktrace_terrain);

                    Terrains.Parameters(cmd, _ktrace_terrain, data.Atlas.Terrain);

                    _ktrace_terrain.Dispatch(cmd, _probes.TraceArguments);
                }

                //

                if (_far_probes == null && data.Atlas.HasAnyLarge)
                {
                    _frame.Parameters(cmd, _ktrace_large);
                    _probes.Parameters(cmd, _ktrace_large);
                    _surfels.Parameters(cmd, _ktrace_large);
                    _traces.Parameters(cmd, _ktrace_large);

                    _ktrace_large.Bind(cmd, P.Large, data.Atlas.Large);
                    _ktrace_large.Bind(cmd, P.SDFAssets, data.Atlas.Assets);
                    _ktrace_large.Bind(cmd, P.DFInstances, data.Atlas.Buffer);
                    _ktrace_large.Bind(cmd, P.SDFBricks, data.Atlas.Bricks);
                    _ktrace_large.BindOnce(cmd, P.SDFAtlas, data.Atlas.SDF);
                    _ktrace_large.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);

                    _ktrace_large.Dispatch(cmd, _probes.TraceArguments);
                }
            }

            pass.End();
        }
    }
}