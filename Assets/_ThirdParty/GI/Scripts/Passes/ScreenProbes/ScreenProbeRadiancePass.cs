using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ScreenProbeRadiancePass : Pass
    {
        private Kernel _kernel_sky;
        private Kernel _kernel_black;

        private ScreenProbes _screen_probes;
        private FarProbes _far_probes;
        private Surfels _surfels;
        private Traces _traces;

        public ScreenProbeRadiancePass(ScreenProbes screen_probes, FarProbes far_probes, Traces traces, Surfels surfels)
        {
            _screen_probes = screen_probes;
            _far_probes = far_probes;
            _surfels = surfels;
            _traces = traces;

            _kernel_black = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Radiance"), "MainBlack");
            _kernel_sky = new Kernel(Resources.Load<ComputeShader>("Shaders/ScreenProbes/Radiance"), "MainSky");

            _kernel_black.Keyword("FAR_PROBES", far_probes != null);
            _kernel_sky.Keyword("FAR_PROBES", far_probes != null);
        }

        public override void Execute(Executor executor, PassData data)
        {
            if (data.Atlas.Debug == DebugChoice.ScreenTraces)
            {
                var clear_pass = executor.Pass();
                var clear = clear_pass.Begin("Screen Probe Clear");
                clear.SetRenderTarget(_screen_probes.RadianceAtlas);
                clear.ClearRenderTarget(false, true, Color.black);
                clear_pass.End();
            }

            //

            var pass = executor.Pass();
            var cmd = pass.Begin("Screen Probe Radiance");

            //

            Texture environment = null;

            var sky_probe = GameObject.FindObjectOfType<SkyProbe>();

            if (sky_probe != null)
                environment = sky_probe.Texture;

            cmd.SetRenderTarget(_screen_probes.RadianceAtlas);
            cmd.ClearRenderTarget(false, true, Color.black);

            //

            _kernel_black.Keyword("FAR_PROBES", _far_probes != null);
            _kernel_sky.Keyword("FAR_PROBES", _far_probes != null);

            if (environment != null)
            {
                if (environment != null)
                    _kernel_sky.BindOnce(cmd, P.Sky, environment);

                if (_far_probes != null)
                {
                    _far_probes.Parameters(cmd, _kernel_sky);
                    _kernel_sky.BindOnce(cmd, P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);
                }

                _kernel_sky.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                _screen_probes.Parameters(cmd, _kernel_sky);
                _surfels.Parameters(cmd, _kernel_sky);
                _traces.Parameters(cmd, _kernel_sky);

                _kernel_sky.BindOnce(cmd, P.ScreenProbeRadianceAtlasRW, _screen_probes.RadianceAtlas);

                _kernel_sky.Dispatch(cmd, _screen_probes.TraceArguments);
            }
            else
            {
                if (_far_probes != null)
                {
                    _far_probes.Parameters(cmd, _kernel_black);
                    _kernel_black.BindOnce(cmd, P.FarProbeRadianceAtlas, _far_probes.RadianceAtlas);
                }

                _kernel_black.Setf(cmd, P.BounceStrength, data.Settings.BounceStrength);

                _screen_probes.Parameters(cmd, _kernel_black);
                _surfels.Parameters(cmd, _kernel_black);
                _traces.Parameters(cmd, _kernel_black);

                _kernel_black.BindOnce(cmd, P.ScreenProbeRadianceAtlasRW, _screen_probes.RadianceAtlas);

                _kernel_black.Dispatch(cmd, _screen_probes.TraceArguments);
            }

            //

            pass.End();
        }
    }
}