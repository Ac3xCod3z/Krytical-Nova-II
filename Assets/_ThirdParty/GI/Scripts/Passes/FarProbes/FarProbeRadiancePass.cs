using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class FarProbeRadiancePass : Pass
    {
        private Kernel _karguments;
        private Kernel _kmapped_black;
        private Kernel _kmapped_sky;
        private Kernel _ktraces_black;
        private Kernel _ktraces_sky;

        private FarProbes _far_probes;
        private Surfels _surfels;
        private Traces _traces;

        public FarProbeRadiancePass(FarProbes far_probes, Surfels surfels, Traces traces)
        {
            _far_probes = far_probes;
            _surfels = surfels;
            _traces = traces;

            _karguments = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/RadianceArguments"), "Main");
            _kmapped_black = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Radiance"), "MappedBlack");
            _kmapped_sky = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Radiance"), "MappedSky");
            _ktraces_black = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Radiance"), "TracesBlack");
            _ktraces_sky = new Kernel(Resources.Load<ComputeShader>("Shaders/FarProbes/Radiance"), "TracesSky");
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("Far Probe Radiance");

            Texture environment = null;

            var sky_probe = GameObject.FindObjectOfType<SkyProbe>();

            if (sky_probe != null)
                environment = sky_probe.Texture;

            //

            if (data.Settings.Experimental.SurfelMapping)
            {
                _far_probes.Parameters(cmd, _karguments);
                _karguments.Bind(cmd, P.ArgumentsRW, _far_probes.RadianceArguments);

                _karguments.DispatchOne(cmd);

                //

                if (environment != null)
                {
                    if (environment != null)
                        _kmapped_sky.BindOnce(cmd, P.Sky, environment);

                    _kmapped_sky.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _far_probes.Parameters(cmd, _kmapped_sky);
                    _surfels.Parameters(cmd, _kmapped_sky);

                    _kmapped_sky.BindOnce(cmd, P.FarProbeGatherAtlasRW, _far_probes.GatherAtlas);

                    _kmapped_sky.Dispatch(cmd, _far_probes.RadianceArguments);
                }
                else
                {
                    _kmapped_black.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _far_probes.Parameters(cmd, _kmapped_black);
                    _surfels.Parameters(cmd, _kmapped_black);

                    _kmapped_black.BindOnce(cmd, P.FarProbeGatherAtlasRW, _far_probes.GatherAtlas);

                    _kmapped_black.Dispatch(cmd, _far_probes.RadianceArguments);
                }
            }
            else
            {
                if (environment != null)
                {
                    if (environment != null)
                        _ktraces_sky.BindOnce(cmd, P.Sky, environment);

                    _ktraces_sky.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _far_probes.Parameters(cmd, _ktraces_sky);
                    _surfels.Parameters(cmd, _ktraces_sky);
                    _traces.Parameters(cmd, _ktraces_sky);

                    _ktraces_sky.BindOnce(cmd, P.FarProbeGatherAtlasRW, _far_probes.GatherAtlas);

                    _ktraces_sky.Dispatch(cmd, _far_probes.TraceArguments);
                }
                else
                {
                    _ktraces_black.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                    _far_probes.Parameters(cmd, _ktraces_black);
                    _surfels.Parameters(cmd, _ktraces_black);
                    _traces.Parameters(cmd, _ktraces_black);

                    _ktraces_black.BindOnce(cmd, P.FarProbeGatherAtlasRW, _far_probes.GatherAtlas);

                    _ktraces_black.Dispatch(cmd, _far_probes.TraceArguments);
                }
            }

            //

            pass.End();
        }
    }
}