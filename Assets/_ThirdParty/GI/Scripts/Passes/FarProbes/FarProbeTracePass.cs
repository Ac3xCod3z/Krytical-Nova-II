using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class FarProbeTracePass : Pass
    {
        private Kernel _kray_setup;
        private Kernel _kray_setup_arguments;
        private Kernel _ktrace_arguments;
        private Kernel _ktrace_global;
        private Kernel _ktrace_large;
        private Kernel _ktrace_terrain;

        private FarProbes _far_probes;
        private Frame _frame;
        private GDF _gdf;
        private Surfels _surfels;
        private Traces _traces;

        public FarProbeTracePass(Frame frame, Traces traces, GDF gdf, FarProbes far_probes, Surfels surfels)
        {
            _far_probes = far_probes;
            _frame = frame;
            _gdf = gdf;
            _surfels = surfels;
            _traces = traces;

            _kray_setup = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/RaySetup"), "Main");
            _kray_setup_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/RaySetupArguments"), "Main");
            _ktrace_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/TraceArguments"), "Main");
            _ktrace_global = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/TraceGlobal"), "Main");
            _ktrace_large = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/TraceLarge"), "Main");
            _ktrace_terrain = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/TraceTerrain"), "Main");
        }

        public override void Execute(Executor executor, PassData data)
        {
            _kray_setup.Keyword("RETRACE_ALL", data.Settings.Retrace == RetraceChoice.All);
            _kray_setup.Keyword("RETRACE_PARTIAL", data.Settings.Retrace == RetraceChoice.Partial);

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Far Probe Trace");

            //

            _far_probes.Parameters(cmd, _kray_setup_arguments);
            _traces.Parameters(cmd, _kray_setup_arguments);

            _kray_setup_arguments.Bind(cmd, P.ArgumentsRW, _far_probes.RaySetupArguments);

            _kray_setup_arguments.DispatchOne(cmd);

            //

            _frame.Parameters(cmd, _kray_setup);
            _far_probes.Parameters(cmd, _kray_setup);
            _surfels.Parameters(cmd, _kray_setup);
            _traces.Parameters(cmd, _kray_setup);

            _kray_setup.Dispatch(cmd, _far_probes.RaySetupArguments);

            //

            _traces.Parameters(cmd, _ktrace_arguments);
            _ktrace_arguments.Bind(cmd, P.ArgumentsRW, _far_probes.TraceArguments);

            _ktrace_arguments.DispatchOne(cmd);

            //

            _frame.Parameters(cmd, _ktrace_global);
            _far_probes.Parameters(cmd, _ktrace_global);
            _surfels.Parameters(cmd, _ktrace_global);
            _gdf.Parameters(cmd, _ktrace_global);
            _gdf.Textures(cmd, _ktrace_global);
            _traces.Parameters(cmd, _ktrace_global);

            _ktrace_global.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);

            _ktrace_global.Dispatch(cmd, _far_probes.TraceArguments);

            //

            if (data.Atlas.Terrain != null && data.Atlas.Terrain.IsEnabled)
            {
                _frame.Parameters(cmd, _ktrace_terrain);
                _far_probes.Parameters(cmd, _ktrace_terrain);
                _surfels.Parameters(cmd, _ktrace_terrain);
                _traces.Parameters(cmd, _ktrace_terrain);

                Terrains.Parameters(cmd, _ktrace_terrain, data.Atlas.Terrain);

                _ktrace_terrain.Dispatch(cmd, _far_probes.TraceArguments);
            }

            //

            if (data.Atlas.HasAnyLarge)
            {
                _frame.Parameters(cmd, _ktrace_large);
                _far_probes.Parameters(cmd, _ktrace_large);
                _surfels.Parameters(cmd, _ktrace_large);
                _traces.Parameters(cmd, _ktrace_large);

                _ktrace_large.Bind(cmd, P.Large, data.Atlas.Large);
                _ktrace_large.Bind(cmd, P.SDFAssets, data.Atlas.Assets);
                _ktrace_large.Bind(cmd, P.DFInstances, data.Atlas.Buffer);
                _ktrace_large.Bind(cmd, P.SDFBricks, data.Atlas.Bricks);
                _ktrace_large.BindOnce(cmd, P.SDFAtlas, data.Atlas.SDF);
                _ktrace_large.BindOnce(cmd, P.VoxelAtlas, data.Atlas.Voxels);

                _ktrace_large.Dispatch(cmd, _far_probes.TraceArguments);
            }

            //

            pass.End();
        }
    }
}