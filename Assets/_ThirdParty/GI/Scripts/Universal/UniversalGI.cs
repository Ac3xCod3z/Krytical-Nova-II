using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering.Universal;

#if UNITY_EDITOR

    using UnityEditor;

#endif

namespace GI
{
    public class GIState
    {
        private RenderObjectsPass _apply; // make sure passes here don't have Release()
        private UniversalTransparentDepthPass _transparent_depth;

        private UniversalPassManager _passes;

        private FarProbes _far_probes;
        private Frame _frame;
        private GDF _gdf;
        private Lights _lights;
        private Probes _probes;
        private ScreenProbes _screen_probes;
        private SDFGrid _sdf_grid;
        private Surfels _surfels;
        private Temporal _temporal;
        private Traces _traces;
        private Transparents _transparents;

        private UniversalGI _gi;

        public GIState(UniversalGI gi)
        {
            _gi = gi;
        }

        public void Create(GISettings settings)
        {
            Shader.SetKeyword(GlobalKeyword.Create("PROBES_SH3"), settings.Probes == ProbeChoice.SH3);
            Shader.SetKeyword(GlobalKeyword.Create("PROBES_SIMPLE"), settings.Probes == ProbeChoice.Simple);
            Shader.SetKeyword(GlobalKeyword.Create("PROBES_OCCLUSION"), settings.ProbeOcclusion);

            _frame = new Frame();
            _gdf = new GDF(settings.GDFCascades);
            _lights = new Lights();
            _probes = new Probes(settings.Cell, settings.Cascades, settings.Probes == ProbeChoice.Simple, settings.ProbeOcclusion, settings.ProbeFiltering, settings.Probes == ProbeChoice.SH3, settings.Experimental.SurfelMapping);
            _sdf_grid = new SDFGrid();
            _surfels = new Surfels();
            _traces = new Traces(settings.Experimental.ScreenProbes);

            var temporal_accumulation = settings.TemporalAccumulation != TemporalChoice.None;

            if (temporal_accumulation || settings.Experimental.ScreenProbes)
                _temporal = new Temporal();

            if (settings.FarProbes)
                _far_probes = new FarProbes(settings.Cell, settings.Cascades, settings.Experimental.SurfelMapping);

            if (settings.Experimental.ScreenProbes)
                _screen_probes = new ScreenProbes(settings.Experimental.MaxAdaptiveProbes);

            //

            _passes = new UniversalPassManager();
            _passes.Settings = _gi.Settings;
            _passes.RenderEvent = (settings.Renderer == RendererChoice.Forward) ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.AfterRenderingGbuffer;

            //s

            if (settings.TransparentQueries)
            {
                _transparents = new Transparents();
                _transparent_depth = new UniversalTransparentDepthPass(_transparents, _passes.RenderEvent);
            }

            //

            var before_render = new PassList();

            before_render.Passes.Add(new SDFGridUpdatePass(_sdf_grid));
            before_render.Passes.Add(new GDFUpdatePass(_gdf, _sdf_grid));
            before_render.Passes.Add(new ProbeMaintainPass(_probes));
            before_render.Passes.Add(new SurfelMaintainPass(_surfels));

            if (settings.FarProbes)
                before_render.Passes.Add(new FarProbeMaintainPass(_far_probes));

            _passes.Add(before_render, Queue.BeforeRender);

            //

            var render = new PassList();

            if (settings.Experimental.ScreenProbes)
                render.Passes.Add(new ScreenProbeCreatePass(_frame, _screen_probes, _probes, _traces, _gdf, _sdf_grid));

            render.Passes.Add(new ProbeCreatePass(_frame, _gdf, _sdf_grid, _probes, _surfels, 2, _transparents));

            if (settings.FarProbes)
                render.Passes.Add(new FarProbeCreatePass(_far_probes, _probes, _screen_probes));

            render.Passes.Add(new ProbeTracePass(_frame, _traces, _gdf, _probes, _far_probes, _surfels, _sdf_grid));

            if (settings.FarProbes)
                render.Passes.Add(new FarProbeTracePass(_frame, _traces, _gdf, _far_probes, _surfels));

            if (settings.Experimental.ScreenProbes)
                render.Passes.Add(new ScreenProbeTracePass(_frame, _traces, _gdf, _screen_probes, _far_probes, _surfels, _sdf_grid));

            //

            // order matters, Probe pass must be first before other light passes
            render.Passes.Add(new SurfelCreatePass(_probes, _screen_probes, _far_probes, _surfels, _traces));
            render.Passes.Add(new SurfelLightProbePass(_probes, _surfels));
            render.Passes.Add(new SurfelLightSunPass(_gdf, _probes, _surfels, _lights));
            render.Passes.Add(new SurfelLightPass(_gdf, _probes, _surfels, _lights));

            //

            if (settings.FarProbes)
            {
                render.Passes.Add(new FarProbeRadiancePass(_far_probes, _surfels, _traces));
                render.Passes.Add(new FarProbeAtlasPass(_far_probes));
            }

            render.Passes.Add(new ProbeRadiancePass(_traces, _probes, _far_probes, _surfels));

            if (settings.Experimental.ScreenProbes)
                render.Passes.Add(new ScreenProbeRadiancePass(_screen_probes, _far_probes, _traces, _surfels));

            //

            if (settings.ProbeFiltering)
                render.Passes.Add(new ProbeFilterPass(_probes));

            render.Passes.Add(new ProbeAtlasPass(_probes));
            render.Passes.Add(new ProbeGatherPass(_probes, _gi.Settings.Probes));

            //

            if (settings.Experimental.ScreenProbes)
            {
                render.Passes.Add(new ScreenProbeFilterPass(_screen_probes));
                render.Passes.Add(new ScreenProbeGatherPass(_screen_probes));

                if (settings.Apply)
                    render.Passes.Add(new ScreenProbeIntegratePass(_screen_probes));
            }

            //

            if (settings.Apply && (temporal_accumulation || settings.Experimental.ScreenProbes))
            {
                if (!settings.Experimental.ScreenProbes)
                    render.Passes.Add(new ProbeIntegratePass(_probes));

                render.Passes.Add(new TemporalPass(_temporal, _screen_probes, _probes));
            }

            //

            _passes.Add(render, Queue.Render);

            //

            if (settings.Apply)
            {
                if (settings.Renderer == RendererChoice.Forward)
                {
                    if (temporal_accumulation || settings.Experimental.ScreenProbes)
                        _apply = new UniversalApplyTemporalForwardPass();
                    else
                        _apply = new UniversalApplyProbesForwardPass();
                }
                else
                {
                    if (temporal_accumulation || settings.Experimental.ScreenProbes)
                    {
                        var apply = new ApplyTemporalDeferredPass(_screen_probes, _probes, _temporal);
                        _passes.Add(apply, Queue.Apply);
                    }
                    else
                    {
                        var apply = new ApplyProbesDeferredPass(_probes);
                        _passes.Add(apply, Queue.Apply);
                    }
                }
            }

            //

            _passes.Add(new BeforePostProcessDebugPass(_gdf, _probes, _surfels, _far_probes), Queue.BeforePostProcess);
            _passes.Add(new AfterRenderDebugPass(_screen_probes), Queue.AfterRender);
        }

        public void Release()
        {
            if (_far_probes != null)
            {
                _far_probes.Release();
                _far_probes = null;
            }

            // if (_frame != null)
            // {
            //     _frame.Release();
            //     _frame = null;
            // }

            if (_gdf != null)
            {
                _gdf.Release();
                _gdf = null;
            }

            // if (_lights != null)
            // {
            //     _lights.Release();
            //     _lights = null;
            // }

            if (_probes != null)
            {
                _probes.Release();
                _probes = null;
            }

            if (_screen_probes != null)
            {
                _screen_probes.Release();
                _screen_probes = null;
            }

            if (_sdf_grid != null)
            {
                _sdf_grid.Release();
                _sdf_grid = null;
            }

            if (_surfels != null)
            {
                _surfels.Release();
                _surfels = null;
            }

            if (_temporal != null)
            {
                _temporal.Release();
                _temporal = null;
            }

            if (_transparents != null)
            {
                _transparents.Release();
                _transparents = null;
            }

            if (_traces != null)
            {
                _traces.Release();
                _traces = null;
            }

            //

            // if (_apply != null)
            // {
            //     _apply.Release();
            //     _apply = null;
            // }

            if (_passes != null)
            {
                _passes.Release();
                _passes = null;
            }

            // if (_transparent_depth != null)
            // {
            //     _transparent_depth.Release();
            //     _transparent_depth = null;
            // }
        }

        public void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var atlas = GameObject.FindObjectOfType<SDFAtlas>();

            if (atlas != null && atlas.IsValid)
            {
                Shader.SetKeyword(GlobalKeyword.Create("PROBES_DIFFUSE_ONLY"), atlas.Debug == DebugChoice.Diffuse);

                var camera = renderingData.cameraData.camera;

                bool recreate = false;

                // Unity can destroy resources without recreating the render feature. In such case recreate GI.
                // _gdf.SDF was the first null encountered during development, that's why it's checked, could be any other resource.
                if (_gdf.SDF == null)
                    recreate = true;

                if (recreate)
                    Create(_gi.Settings);

                if (_probes.Choice != _gi.Settings.Probes)
                {
                    _probes.Choice = _gi.Settings.Probes;
                    _probes.NeedReset = true;
                }

                if (_temporal != null)
                    _temporal.GameMode = camera.cameraType == CameraType.Game;

                _frame.Update();
                _lights.Update();

                if (_transparents != null)
                    _transparents.EnsureTarget(camera.pixelWidth, camera.pixelHeight);

                if (atlas.Terrain != null)
                    atlas.Terrain.AutoUpdate(camera.transform.position);

                _probes.Update(camera.transform.position);
                _surfels.Update(camera.transform.position);
                _gdf.Update(camera.transform.position);
                _sdf_grid.Update(camera.transform.position);

                Shader.SetGlobalFloat(P.DiffuseMultiplier, _gi.Settings.DiffuseMultiplier);

                if (_far_probes != null)
                    _far_probes.Update(camera.transform.position);

                //

                if (_gi.Settings.TransparentQueries && _transparent_depth != null)
                    renderer.EnqueuePass(_transparent_depth);

                _passes.ScriptableRenderer = renderer;
                _passes.Run();

                if (_apply != null)
                {
                    // hack, this is not clean
                    _apply.overrideShaderPassIndex = atlas.Debug == DebugChoice.Diffuse ? 1 : 0;

                    renderer.EnqueuePass(_apply);
                }
            }
        }
    }

    public class UniversalGI : ScriptableRendererFeature
    {
        public GISettings Settings = new GISettings();

        private Dictionary<Camera, GIState> _states = new Dictionary<Camera, GIState>();

        public override void Create()
        {
            foreach (GIState state in _states.Values)
                state.Release();

            _states.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            foreach (GIState state in _states.Values)
                state.Release();

            _states.Clear();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData rendering_data)
        {
            var camera = rendering_data.cameraData.camera;

            #if UNITY_EDITOR

            // only the main window

            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                return;

            if (EditorApplication.isCompiling)
                return;

            if (BuildPipeline.isBuildingPlayer)
                return;

            #endif

            if (!_states.ContainsKey(camera))
            {
                var state = new GIState(this);
                state.Create(Settings);

                _states[camera] = state;
            }

            _states[camera].AddRenderPasses(renderer, ref rendering_data);
        }
    }
}