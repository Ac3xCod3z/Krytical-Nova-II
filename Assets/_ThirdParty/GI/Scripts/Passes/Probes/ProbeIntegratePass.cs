using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ProbeIntegratePass : Pass
    {
        private Probes _probes;

        private Material _apply;

        public ProbeIntegratePass(Probes probes)
        {
            _probes = probes;

            _apply = new Material(Resources.Load<Shader>("Shaders/Apply/ProbesIntegrate"));
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

            if (data.Settings.ProbeOcclusion)
                _apply.EnableKeyword("PROBES_OCCLUSION");
            else
                _apply.DisableKeyword("PROBES_OCCLUSION");

            switch (data.Settings.Probes)
            {
                case ProbeChoice.Simple:
                    _apply.EnableKeyword("PROBES_SIMPLE");
                    _apply.DisableKeyword("PROBES_SH3");
                    break;

                case ProbeChoice.SH2:
                    _apply.DisableKeyword("PROBES_SIMPLE");
                    _apply.DisableKeyword("PROBES_SH3");
                    break;

                case ProbeChoice.SH3:
                    _apply.DisableKeyword("PROBES_SIMPLE");
                    _apply.EnableKeyword("PROBES_SH3");
                    break;
            }

            //

            var camera = data.Camera;
            _probes.EnsureIntegrated(camera.pixelWidth, camera.pixelHeight);

            var pass = executor.Pass();
            var cmd = pass.Begin("Probes Integrate");

            cmd.SetRenderTarget(_probes.IntegratedDiffuse);
            cmd.SetViewport(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight));

            if (builtin) // eh
            {
                Util.SetCameraParameters(_apply, data.Camera);

                cmd.SetGlobalTexture(P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                cmd.SetGlobalTexture(P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
            }

            _apply.SetTexture(P.ProbeDepthAtlas, _probes.DepthAtlas);

            cmd.DrawMesh(Util.Quad, Matrix4x4.identity, _apply);

            //

            pass.End();
        }
    }
}
