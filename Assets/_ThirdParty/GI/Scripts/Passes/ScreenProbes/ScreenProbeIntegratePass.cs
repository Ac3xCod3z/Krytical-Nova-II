using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ScreenProbeIntegratePass : Pass
    {
        private ScreenProbes _screen_probes;

        private Material _apply;

        public ScreenProbeIntegratePass(ScreenProbes screen_probes)
        {
            _screen_probes = screen_probes;

            _apply = new Material(Resources.Load<Shader>("Shaders/ScreenProbes/Integrate"));
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

            if (data.Settings.Renderer == RendererChoice.Forward || executor.IsScriptable())
                _apply.EnableKeyword("CAMERA_NORMALS");
            else
                _apply.DisableKeyword("CAMERA_NORMALS");

            //

            var camera = data.Camera;

            var pass = executor.Pass();
            var cmd = pass.Begin("Screen Probe Integrate");

            //

            cmd.SetRenderTarget(_screen_probes.IntegratedDiffuse);
            cmd.SetViewport(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight));

            if (builtin) // eh
            {
                Util.SetCameraParameters(_apply, data.Camera);

                cmd.SetGlobalTexture(P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                cmd.SetGlobalTexture(P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
            }

            _screen_probes.Parameters(_apply);

            _apply.SetTexture(P.ScreenProbeAdaptiveMapping, _screen_probes.Mapping);

            cmd.DrawMesh(Util.Quad, Matrix4x4.identity, _apply);

            //

            pass.End();
        }
    }
}
