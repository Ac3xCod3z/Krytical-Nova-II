using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ScreenProbeCreatePass : Pass
    {
        private Kernel _kadaptive;
        private Kernel _karguments;
        private Kernel _korigin;
        private Kernel _kplace;
        private Kernel _ksetup;

        private Frame _frame;
        private GDF _gdf;
        private Probes _probes;
        private ScreenProbes _screen_probes;
        private SDFGrid _sdf_grid;
        private Traces _traces;

        public ScreenProbeCreatePass(Frame frame, ScreenProbes screen_probes, Probes probes, Traces traces, GDF gdf, SDFGrid sdf_grid)
        {
            _frame = frame;
            _gdf = gdf;
            _probes = probes;
            _screen_probes = screen_probes;
            _sdf_grid = sdf_grid;
            _traces = traces;

            _kadaptive = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Adaptive"), "Main");
            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/QueryArguments"), "Main");
            _korigin = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Origin"), "Main");
            _kplace = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Place"), "Main");
            _ksetup = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Adaptive"), "Setup");
        }

        public override int InputRequirements(GISettings settings)
        {
            if (settings.Renderer == RendererChoice.Forward)
                return Requirements.Depth | Requirements.Normals;
            else
                return Requirements.GBuffer;
        }

        public override void Execute(Executor executor, PassData data)
        {
            var builtin = !executor.IsScriptable();

            //

            _kplace.Keyword("CAMERA_NORMALS", data.Settings.Renderer == RendererChoice.Forward || executor.IsScriptable());
            _kplace.Keyword("_GBUFFER_NORMALS_OCT", data.Settings.Renderer == RendererChoice.DeferredAccurateNormals);

            _kadaptive.Keyword("CAMERA_NORMALS", data.Settings.Renderer == RendererChoice.Forward || executor.IsScriptable());
            _kadaptive.Keyword("_GBUFFER_NORMALS_OCT", data.Settings.Renderer == RendererChoice.DeferredAccurateNormals);

            _korigin.Keyword("LOCAL_TRACING", data.Settings.LocalTracing);

            //

            var camera = data.Camera;
            _screen_probes.Update(camera.pixelWidth, camera.pixelHeight);

            _traces.EnsureScreen(_screen_probes.CalculatedCount * Defines.SCREEN_PROBE_RAY_COUNT);

            //

            {
                var clear_pass = executor.Pass();
                var clear_cmd = clear_pass.Begin("Screen Probe Clear");
                clear_cmd.SetRenderTarget(_screen_probes.Mapping);
                clear_cmd.ClearRenderTarget(false, true, Color.black);
                clear_pass.End();
            }

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Screen Probe Create");

            //

            _kplace.Seti(cmd, P.Viewport, camera.pixelWidth, camera.pixelHeight);

            _frame.Parameters(cmd, _kplace);
            _screen_probes.Parameters(cmd, _kplace);

            if (builtin)
            {
                Util.SetCameraParameters(cmd, _kplace, data.Camera);

                _kplace.BindRT(cmd, P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                _kplace.BindRT(cmd, P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
            }

            _kplace.DispatchEnoughFor(cmd, _screen_probes.CountX, _screen_probes.CountY);

            //

            _screen_probes.Parameters(cmd, _ksetup);
            _ksetup.DispatchOne(cmd);


            //

            _kadaptive.Seti(cmd, P.Viewport, camera.pixelWidth, camera.pixelHeight);

            _frame.Parameters(cmd, _kadaptive);
            _screen_probes.Parameters(cmd, _kadaptive);

            if (builtin)
            {
                Util.SetCameraParameters(cmd, _kadaptive, data.Camera);

                _kadaptive.BindRT(cmd, P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                _kadaptive.BindRT(cmd, P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
            }

            _kadaptive.BindOnce(cmd, P.ScreenProbeAdaptiveMappingRW, _screen_probes.Mapping);

            int adaptive_dim = Defines.SCREEN_PROBE_DIM / 2;

            while (adaptive_dim >= Defines.SCREEN_PROBE_ADAPTIVE_DIM)
            {
                int count_x = (camera.pixelWidth + adaptive_dim - 1) / adaptive_dim;
                int count_y = (camera.pixelHeight + adaptive_dim - 1) / adaptive_dim;

                _kadaptive.Seti(cmd, P.AdaptiveDim, adaptive_dim);
                _kadaptive.DispatchEnoughFor(cmd, count_x, count_y);

                adaptive_dim /= 2;
            }

            //

            _screen_probes.Parameters(cmd, _karguments);

            _karguments.Bind(cmd, P.ArgumentsRW, _screen_probes.QueryArguments);

            _karguments.DispatchOne(cmd);

            //

            {
                if (data.Atlas.Terrain != null && data.Atlas.Terrain.IsEnabled)
                {
                    _korigin.Keyword("TERRAIN", true);
                    Terrains.Parameters(cmd, _korigin, data.Atlas.Terrain);
                }
                else
                    _korigin.Keyword("TERRAIN", false);

                _screen_probes.Parameters(cmd, _korigin);

                _gdf.Parameters(cmd, _korigin);
                _gdf.Textures(cmd, _korigin);
                _sdf_grid.Parameters(cmd, _korigin);

                _korigin.BindOnce(cmd, P.SDFAtlas, data.Atlas.SDF);
                _korigin.Bind(cmd, P.SDFAssets, data.Atlas.Assets);
                _korigin.Bind(cmd, P.SDFBricks, data.Atlas.Bricks);

                _korigin.Bind(cmd, P.DFInstances, data.Atlas.Buffer);

                _korigin.Dispatch(cmd, _screen_probes.QueryArguments);
            }

            //

            pass.End();
        }
    }
}
