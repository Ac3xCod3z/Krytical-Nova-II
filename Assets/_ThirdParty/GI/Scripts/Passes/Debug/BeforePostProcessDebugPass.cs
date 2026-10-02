using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class BeforePostProcessDebugPass : Pass
    {
        private GDF _gdf;
        private Probes _probes;
        private Surfels _surfels;
        private FarProbes _far_probes;

        private Pass _debug_sdf;
        private Pass _debug_sdf_selection;
        private Pass _debug_gdf;
        private Pass _debug_probes;
        private Pass _debug_traces;
        private Pass _debug_depths;
        private Pass _debug_surfels;
        private Pass _debug_far_probes;
        private Pass _debug_far_probe_traces;

        public BeforePostProcessDebugPass(GDF gdf, Probes probes, Surfels surfels, FarProbes far_probes)
        {
            _gdf = gdf;
            _probes = probes;
            _surfels = surfels;
            _far_probes = far_probes;

            _debug_sdf_selection = new SDFSelectionRenderPass();
        }

        public override int InputRequirements(GISettings settings)
        {
            int result = 0;

            if (_debug_sdf != null) result |= _debug_sdf.InputRequirements(settings);
            if (_debug_sdf_selection != null) result |= _debug_sdf_selection.InputRequirements(settings);
            if (_debug_gdf != null) result |= _debug_gdf.InputRequirements(settings);
            if (_debug_probes != null) result |= _debug_probes.InputRequirements(settings);
            if (_debug_traces != null) result |= _debug_traces.InputRequirements(settings);
            if (_debug_depths != null) result |= _debug_depths.InputRequirements(settings);
            if (_debug_surfels != null) result |= _debug_surfels.InputRequirements(settings);
            if (_debug_far_probes != null) result |= _debug_far_probes.InputRequirements(settings);
            if (_debug_far_probe_traces != null) result |= _debug_far_probe_traces.InputRequirements(settings);

            return result;
        }

        public override void Release()
        {
            if (_debug_sdf != null)
            {
                _debug_sdf.Release();
                _debug_sdf = null;
            }

            if (_debug_sdf_selection != null)
            {
                _debug_sdf_selection.Release();
                _debug_sdf_selection = null;
            }

            if (_debug_gdf != null)
            {
                _debug_gdf.Release();
                _debug_gdf = null;
            }

            if (_debug_probes != null)
            {
                _debug_probes.Release();
                _debug_probes = null;
            }

            if (_debug_traces != null)
            {
                _debug_traces.Release();
                _debug_traces = null;
            }

            if (_debug_depths != null)
            {
                _debug_depths.Release();
                _debug_depths = null;
            }

            if (_debug_surfels != null)
            {
                _debug_surfels.Release();
                _debug_surfels = null;
            }

            if (_debug_far_probes != null)
            {
                _debug_far_probes.Release();
                _debug_far_probes = null;
            }

            if (_debug_far_probe_traces != null)
            {
                _debug_far_probe_traces.Release();
                _debug_far_probe_traces = null;
            }
        }

        public override void Execute(Executor executor, PassData data)
        {
            _debug_sdf_selection.Execute(executor, data);

            switch (data.Atlas.Debug)
            {
                case DebugChoice.None:
                    break;

                case DebugChoice.SDF:
                    if (_debug_sdf == null)
                        _debug_sdf = new SDFRenderPass();

                    _debug_sdf.Execute(executor, data);
                    break;

                case DebugChoice.GDF:
                    if (_debug_gdf == null)
                        _debug_gdf = new GDFRenderPass(_gdf);

                    _debug_gdf.Execute(executor, data);
                    break;

                case DebugChoice.Probes:
                    if (_debug_probes == null)
                        _debug_probes = new ProbeRenderPass(_probes, DebugChoice.Probes);

                    _debug_probes.Execute(executor, data);
                    break;

                case DebugChoice.ProbeTexels:
                    if (_debug_traces == null)
                        _debug_traces = new ProbeRenderPass(_probes, DebugChoice.ProbeTexels);

                    _debug_traces.Execute(executor, data);
                    break;

                case DebugChoice.Depths:
                    if (_debug_depths == null)
                        _debug_depths = new ProbeRenderPass(_probes, DebugChoice.Depths);

                    _debug_depths.Execute(executor, data);
                    break;

                case DebugChoice.Surfels:
                    if (_debug_surfels == null)
                        _debug_surfels = new SurfelRenderPass(_surfels);

                    _debug_surfels.Execute(executor, data);
                    break;

                case DebugChoice.FarProbeTexels:
                    if (data.Settings.FarProbes)
                    {
                        if (_debug_far_probes == null)
                            _debug_far_probes = new FarProbeRenderPass(_far_probes, false);

                        _debug_far_probes.Execute(executor, data);
                    }
                    break;

                case DebugChoice.FarProbeTraces:
                    if (data.Settings.FarProbes)
                    {
                        if (_debug_far_probe_traces == null)
                            _debug_far_probe_traces = new FarProbeRenderPass(_far_probes, true);

                        _debug_far_probe_traces.Execute(executor, data);
                    }
                    break;
            }
        }
    }
}