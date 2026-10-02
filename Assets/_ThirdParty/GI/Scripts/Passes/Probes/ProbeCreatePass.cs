using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeCreatePass : Pass
    {
        private Kernel _karguments;
        private Kernel _kcreate;
        private Kernel _kcreate_arguments;
        private Kernel _kcreate_clear;
        private Kernel _knormals;
        private Kernel _kposition_arguments;
        private Kernel _kpositions_push;
        private Kernel _kpositions_update;
        private Kernel _kpositions_visibility;
        private Kernel _kquery_normals;
        private Kernel _kquery_opaque;
        private Kernel _kquery_transparent;

        private Frame _frame;
        private GDF _gdf;
        private Probes _probes;
        private SDFGrid _sdf_grid;
        private Surfels _surfels;

        private Transparents _transparents;

        private int _repeat_count;

        public ProbeCreatePass(Frame frame, GDF gdf, SDFGrid sdf_grid, Probes probes, Surfels surfels, int repeat_count, Transparents transparents)
        {
            _frame = frame;
            _gdf = gdf;
            _probes = probes;
            _sdf_grid = sdf_grid;
            _surfels = surfels;

            _transparents = transparents;

            _repeat_count = repeat_count;

            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Arguments"), "Main");
            _kcreate = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Create"), "Main");
            _kcreate_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/CreateArguments"), "Main");
            _kcreate_clear = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/CreateClear"), "Main");
            _knormals = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Normals"), "Main");
            _kposition_arguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Positions"), "Arguments");
            _kpositions_push = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Positions"), "Push");
            _kpositions_update = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Positions"), "Update");
            _kpositions_visibility = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Positions"), "Visibility");
            _kquery_normals = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/QueryNormals"), "Main");
            _kquery_opaque = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Query"), "MainOpaque");

            if (transparents != null)
                _kquery_transparent = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Query"), "MainTransparent");
        }

        public override int InputRequirements(GISettings settings)
        {
            if (settings.Renderer == RendererChoice.Forward)
                return Requirements.Depth | Requirements.Normals;
            else
                return Requirements.GBuffer;
        }

        private void assign(CommandBuffer cmd, SDFAtlas atlas, Kernel kernel)
        {
            kernel.BindOnce(cmd, P.SDFAtlas, atlas.SDF);
            kernel.Bind(cmd, P.SDFAssets, atlas.Assets);
            kernel.Bind(cmd, P.SDFBricks, atlas.Bricks);

            kernel.Bind(cmd, P.DFInstances, atlas.Buffer);
        }

        public override void Execute(Executor executor, PassData data)
        {
            var builtin = !executor.IsScriptable();

            _kquery_opaque.Keyword("CAMERA_NORMALS", data.Settings.Renderer == RendererChoice.Forward || executor.IsScriptable());
            _kquery_opaque.Keyword("_GBUFFER_NORMALS_OCT", data.Settings.Renderer == RendererChoice.DeferredAccurateNormals);

            _kquery_normals.Keyword("CAMERA_NORMALS", data.Settings.Renderer == RendererChoice.Forward || executor.IsScriptable());
            _kquery_normals.Keyword("_GBUFFER_NORMALS_OCT", data.Settings.Renderer == RendererChoice.DeferredAccurateNormals);

            _kpositions_visibility.Keyword("LOCAL_TRACING", data.Settings.LocalTracing);

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Create");

            var camera = data.Camera;

            {
                int dispatch_width = (int)((_repeat_count * camera.pixelWidth + Defines.PROBE_SCREEN_CELL_DIM - 1) / Defines.PROBE_SCREEN_CELL_DIM);
                int dispatch_height = (int)((camera.pixelHeight + Defines.PROBE_SCREEN_CELL_DIM - 1) / Defines.PROBE_SCREEN_CELL_DIM);

                _kquery_opaque.Keyword("REDFASTGI_VR", data.StereoMode);

                _kquery_opaque.Seti(cmd, P.Repeats, _repeat_count);
                _kquery_opaque.Seti(cmd, P.RepeatWidth, camera.pixelWidth);
                _kquery_opaque.Seti(cmd, P.Viewport, camera.pixelWidth, camera.pixelHeight);

                if (builtin)
                {
                    Util.SetCameraParameters(cmd, _kquery_opaque, data.Camera);

                    _kquery_opaque.BindRT(cmd, P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                    _kquery_opaque.BindRT(cmd, P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
                }

                _frame.Parameters(cmd, _kquery_opaque);
                _probes.Parameters(cmd, _kquery_opaque);

                _kquery_opaque.DispatchEnoughFor(cmd, dispatch_width, dispatch_height);
            }

            //

            if (_transparents != null)
            {
                int dispatch_width = _transparents.Target.width;
                int dispatch_height = _transparents.Target.height;

                _kquery_transparent.Keyword("REDFASTGI_VR", data.StereoMode);

                _kquery_transparent.BindOnce(cmd, P.TransparentDepth, _transparents.Target);
                _kquery_transparent.Seti(cmd, P.Viewport, dispatch_width, dispatch_height);

                _frame.Parameters(cmd, _kquery_transparent);
                _probes.Parameters(cmd, _kquery_transparent);

                _kquery_transparent.DispatchEnoughFor(cmd, dispatch_width, dispatch_height);
            }

            //

            _probes.Parameters(cmd, _kcreate_arguments);
            _kcreate_arguments.Bind(cmd, P.ArgumentsRW, _probes.CreateArguments);
            _kcreate_arguments.DispatchOne(cmd);

            //

            _probes.Parameters(cmd, _kcreate);
            _kcreate.Dispatch(cmd, _probes.CreateArguments);

            //

            _probes.Parameters(cmd, _kcreate_clear);
            _kcreate_clear.Dispatch(cmd, _probes.CreateArguments, 16);

            //

            {
                int dispatch_width = (int)((_repeat_count * camera.pixelWidth + Defines.PROBE_SCREEN_CELL_DIM - 1) / Defines.PROBE_SCREEN_CELL_DIM);
                int dispatch_height = (int)((camera.pixelHeight + Defines.PROBE_SCREEN_CELL_DIM - 1) / Defines.PROBE_SCREEN_CELL_DIM);

                _kquery_normals.Keyword("REDFASTGI_VR", data.StereoMode);

                _kquery_normals.Seti(cmd, P.Repeats, _repeat_count);
                _kquery_normals.Seti(cmd, P.RepeatWidth, camera.pixelWidth);
                _kquery_normals.Seti(cmd, P.Viewport, camera.pixelWidth, camera.pixelHeight);

                if (builtin)
                {
                    Util.SetCameraParameters(cmd,_kquery_normals, data.Camera);

                    _kquery_normals.BindRT(cmd, P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                    _kquery_normals.BindRT(cmd, P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
                }

                _frame.Parameters(cmd, _kquery_normals);
                _probes.Parameters(cmd, _kquery_normals);

                _kquery_normals.DispatchEnoughFor(cmd, dispatch_width, dispatch_height);
            }

            //

            _probes.Parameters(cmd, _karguments);
            _karguments.Bind(cmd, P.ArgumentsRW, _probes.ProbeArguments);

            _karguments.DispatchOne(cmd);

            //

            _frame.Parameters(cmd, _knormals);
            _probes.Parameters(cmd, _knormals);

            _knormals.Dispatch(cmd, _probes.ProbeArguments, 32);

            //

            if (data.Atlas.Terrain != null && data.Atlas.Terrain.IsEnabled)
            {
                _kpositions_update.Keyword("TERRAIN", true);
                Terrains.Parameters(cmd, _kpositions_update, data.Atlas.Terrain);
            }
            else
                _kpositions_update.Keyword("TERRAIN", false);

            _frame.Parameters(cmd, _kpositions_update);
            _probes.Parameters(cmd, _kpositions_update);
            _gdf.Parameters(cmd, _kpositions_update);
            _gdf.Textures(cmd, _kpositions_update);
            _sdf_grid.Parameters(cmd, _kpositions_update);
            assign(cmd, data.Atlas, _kpositions_update);

            _kpositions_update.Dispatch(cmd, _probes.ProbeArguments, 32);

            //

            _probes.Parameters(cmd, _kposition_arguments);
            _kposition_arguments.Bind(cmd, P.ArgumentsRW, _probes.PositionArguments);

            _kposition_arguments.DispatchOne(cmd);

            //

            if (data.Atlas.Terrain != null && data.Atlas.Terrain.IsEnabled)
            {
                _kpositions_push.Keyword("TERRAIN", true);
                Terrains.Parameters(cmd, _kpositions_push, data.Atlas.Terrain);
            }
            else
                _kpositions_push.Keyword("TERRAIN", false);

            _frame.Parameters(cmd, _kpositions_push);
            _probes.Parameters(cmd, _kpositions_push);
            _gdf.Parameters(cmd, _kpositions_push);
            _gdf.Textures(cmd, _kpositions_push);
            _sdf_grid.Parameters(cmd, _kpositions_push);
            assign(cmd, data.Atlas, _kpositions_push);

            _kpositions_push.Dispatch(cmd, _probes.PositionArguments);

            //

            if (data.Atlas.Terrain != null && data.Atlas.Terrain.IsEnabled)
            {
                _kpositions_visibility.Keyword("TERRAIN", true);
                Terrains.Parameters(cmd, _kpositions_visibility, data.Atlas.Terrain);
            }
            else
                _kpositions_visibility.Keyword("TERRAIN", false);

            _frame.Parameters(cmd, _kpositions_visibility);
            _probes.Parameters(cmd, _kpositions_visibility);
            _gdf.Parameters(cmd, _kpositions_visibility);
            _gdf.Textures(cmd, _kpositions_visibility);
            _sdf_grid.Parameters(cmd, _kpositions_visibility);
            assign(cmd, data.Atlas, _kpositions_visibility);

            _kpositions_visibility.Dispatch(cmd, _probes.PositionArguments, 16);

            //

            pass.End();
        }
    }
}
