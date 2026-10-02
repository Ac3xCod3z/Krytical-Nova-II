using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace GI
{
    class TemporalPass : Pass
    {
        private Kernel _ktemporal;

        private Probes _probes;
        private ScreenProbes _screen_probes;
        private Temporal _temporal;

        private RenderTexture _last_depth;

        public TemporalPass(Temporal temporal, ScreenProbes screen_probes, Probes probes)
        {
            _temporal = temporal;
            _screen_probes = screen_probes;
            _probes = probes;

            _ktemporal = new Kernel(Resources.Load<ComputeShader>("Shaders/Apply/Temporal"), "Main");
        }

        public override void Release()
        {
            if (_last_depth != null)
            {
                _last_depth.Release();
                _last_depth = null;
            }
        }

        public override int InputRequirements(GISettings settings)
        {
            return Requirements.Depth | Requirements.Motion;
        }

        public override void Execute(Executor executor, PassData data)
        {
            var builtin = !executor.IsScriptable();

            var camera = data.Camera;
            _temporal.Update(camera, camera.pixelWidth, camera.pixelHeight);

            _ktemporal.Keyword("MOTION_VECTORS", _temporal.GameMode && data.Settings.TemporalAccumulation == TemporalChoice.MotionVectors);

            var pass = executor.Pass();
            var cmd = pass.Begin("Temporal");

            if (!builtin)
            {
                var width = camera.pixelWidth;
                var height = camera.pixelHeight;

                if (_last_depth == null || _last_depth.width != width || _last_depth.height != height)
                {
                    if (_last_depth != null)
                    {
                        _last_depth.Release();
                        _last_depth = null;
                    }

                    var desc = new RenderTextureDescriptor();
                    desc.width = width;
                    desc.height = height;
                    desc.volumeDepth = 1;
                    desc.dimension = TextureDimension.Tex2D;
                    desc.msaaSamples = 1;
                    desc.enableRandomWrite = true;
                    desc.graphicsFormat = GraphicsFormat.R32_SFloat;

                    _last_depth = new RenderTexture(desc);
                    _last_depth.name = "GI_LastCameraDepthTexture";
                    _last_depth.Create();
                }
            }

            _ktemporal.Seti(cmd, P.Viewport, camera.pixelWidth, camera.pixelHeight);
            _ktemporal.Setf(cmd, P.ViewportScale, (float)camera.pixelWidth / (float)_temporal.BufferWidth, (float)camera.pixelHeight / (float)_temporal.BufferHeight);

            _temporal.Parameters(cmd, _ktemporal);

            if (builtin)
            {
                Util.SetCameraParameters(cmd, _ktemporal, data.Camera);

                _ktemporal.BindRT(cmd, P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                _ktemporal.BindRT(cmd, P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
            }

            if (!builtin && _last_depth != null)
                _ktemporal.BindRT(cmd, P._LastCameraDepthTexture, _last_depth);

            if (data.Settings.Experimental.ScreenProbes)
                _ktemporal.BindOnce(cmd, P.DiffuseBuffer, _screen_probes.IntegratedDiffuse);
            else
                _ktemporal.BindOnce(cmd, P.DiffuseBuffer, _probes.IntegratedDiffuse);

            // "current" here means the previous frame
            _ktemporal.BindOnce(cmd, P.PreviousDiffuse, _temporal.CurrentDiffuse);
            _ktemporal.BindOnce(cmd, P.Convergence, _temporal.CurrentConvergence);

            // "previous" will be swapped in Swap to become "current"
            _ktemporal.BindOnce(cmd, P.CurrentDiffuseRW, _temporal.PreviousDiffuse);
            _ktemporal.BindOnce(cmd, P.ConvergenceRW, _temporal.PreviousConvergence);

            _ktemporal.DispatchEnoughFor(cmd, camera.pixelWidth, camera.pixelHeight);

            if (!builtin && _last_depth != null)
                cmd.Blit(data.DepthRenderHandle, _last_depth);

            pass.End();

            //

            _temporal.SwapConvergence();
            _temporal.SwapDiffuse();

            Shader.SetGlobalTexture(P.TemporalDiffuseBuffer, _temporal.CurrentDiffuse);
        }
    }
}
