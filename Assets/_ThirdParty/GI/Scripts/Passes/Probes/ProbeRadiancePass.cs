using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeRadiancePass : Pass
    {
        private Kernel _karguments;
        private Kernel _kmapped_black;
        private Kernel _kmapped_sky;
        private Kernel _ktraces_black;
        private Kernel _ktraces_sky;

        private Probes _probes;
        private FarProbes _far_probes;
        private Surfels _surfels;
        private Traces _traces;

        public ProbeRadiancePass(Traces traces, Probes probes, FarProbes far_probes, Surfels surfels)
        {
            _traces = traces;
            _probes = probes;
            _far_probes = far_probes;
            _surfels = surfels;

            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/RadianceArguments"), "Main");
            _kmapped_black = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Radiance"), "MappedBlack");
            _kmapped_sky = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Radiance"), "MappedSky");
            _ktraces_black = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Radiance"), "TracesBlack");
            _ktraces_sky = new Kernel(Resources.Load<ComputeShader>("Shaders/Probes/Radiance"), "TracesSky");

            _kmapped_black.Keyword("FAR_PROBES", far_probes != null);
            _kmapped_sky.Keyword("FAR_PROBES", far_probes != null);
            _ktraces_black.Keyword("FAR_PROBES", far_probes != null);
            _ktraces_sky.Keyword("FAR_PROBES", far_probes != null);
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Probe Radiance");

            //

            Texture environment = null;

            var sky_probe = GameObject.FindObjectOfType<SkyProbe>();

            if (sky_probe != null)
                environment = sky_probe.Texture;

            //

            if (data.Settings.Experimental.SurfelMapping)
            {
                _probes.Parameters(cmd, _karguments);
                _karguments.Bind(cmd, P.ArgumentsRW, _probes.RadianceArguments);

                _karguments.DispatchOne(cmd);

                //

                if (environment != null)
                {
                    if (environment != null)
                        _kmapped_sky.BindOnce(cmd, P.Sky, environment);

                    if (_far_probes != null)
                    {
                        _far_probes.Parameters(cmd, _kmapped_sky);
                        _kmapped_sky.BindOnce(cmd, P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);
                    }

                    _kmapped_sky.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _probes.Parameters(cmd, _kmapped_sky);
                    _surfels.Parameters(cmd, _kmapped_sky);

                    _kmapped_sky.BindOnce(cmd, P.ProbeGatherAtlasRW, _probes.GatherAtlas);

                    _kmapped_sky.Dispatch(cmd, _probes.RadianceArguments);
                }
                else
                {
                    if (_far_probes != null)
                    {
                        _far_probes.Parameters(cmd, _kmapped_black);
                        _kmapped_black.BindOnce(cmd, P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);
                    }

                    _kmapped_black.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _probes.Parameters(cmd, _kmapped_black);
                    _surfels.Parameters(cmd, _kmapped_black);

                    _kmapped_black.BindOnce(cmd, P.ProbeGatherAtlasRW, _probes.GatherAtlas);

                    _kmapped_black.Dispatch(cmd, _probes.RadianceArguments);
                }
            }
            else
            {
                if (environment != null)
                {
                    if (environment != null)
                        _ktraces_sky.BindOnce(cmd, P.Sky, environment);

                    if (_far_probes != null)
                    {
                        _far_probes.Parameters(cmd, _ktraces_sky);
                        _ktraces_sky.BindOnce(cmd, P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);
                    }

                    _ktraces_sky.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _probes.Parameters(cmd, _ktraces_sky);
                    _surfels.Parameters(cmd, _ktraces_sky);
                    _traces.Parameters(cmd, _ktraces_sky);

                    _ktraces_sky.BindOnce(cmd, P.ProbeGatherAtlasRW, _probes.GatherAtlas);

                    _ktraces_sky.Dispatch(cmd, _probes.TraceArguments);
                }
                else
                {
                    if (_far_probes != null)
                    {
                        _far_probes.Parameters(cmd, _ktraces_black);
                        _ktraces_black.BindOnce(cmd, P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);
                    }

                    _ktraces_black.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _probes.Parameters(cmd, _ktraces_black);
                    _surfels.Parameters(cmd, _ktraces_black);
                    _traces.Parameters(cmd, _ktraces_black);

                    _ktraces_black.BindOnce(cmd, P.ProbeGatherAtlasRW, _probes.GatherAtlas);

                    _ktraces_black.Dispatch(cmd, _probes.TraceArguments);
                }
            }

            //

            pass.End();
        }
    }
}