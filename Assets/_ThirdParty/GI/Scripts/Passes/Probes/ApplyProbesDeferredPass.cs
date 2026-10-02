using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ApplyProbesDeferredPass : Pass
    {
        private Probes _probes;

        private Material _apply;

        public ApplyProbesDeferredPass(Probes probes)
        {
            _probes = probes;

            _apply = new Material(Resources.Load<Shader>("Shaders/Apply/ProbesDeferred"));
        }

        public override int InputRequirements(GISettings settings)
        {
            return Requirements.GBuffer;
        }

        public override void Execute(Executor executor, PassData data)
        {
            var builtin = !executor.IsScriptable();

            var pass = executor.Pass();
            var cmd = pass.Begin("Apply Probes Deferred");

            if (builtin)
                _apply.EnableKeyword("BUILTIN_GBUFFER");
            else
                _apply.DisableKeyword("BUILTIN_GBUFFER");

            if (data.Settings.ProbeOcclusion)
                _apply.EnableKeyword("PROBES_OCCLUSION");
            else
                _apply.DisableKeyword("PROBES_OCCLUSION");

            if (data.Atlas.Debug == DebugChoice.Diffuse)
            {
                _apply.EnableKeyword("PROBES_DIFFUSE_ONLY");
                _apply.SetInt("ApplyDest", 0);
            }
            else
            {
                _apply.DisableKeyword("PROBES_DIFFUSE_ONLY");
                _apply.SetInt("ApplyDest", 1);
            }

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

            _apply.SetTexture(P.ProbeDepthAtlas, _probes.DepthAtlas);

            _apply.SetFloat(P.DiffuseMultiplier, data.Settings.DiffuseMultiplier);

            if (builtin) // eh
            {
                Util.SetCameraParameters(_apply, data.Camera);

                cmd.SetGlobalTexture(P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
                cmd.SetGlobalTexture(P._GBuffer2, BuiltinRenderTextureType.GBuffer2);
            }

            cmd.DrawMesh(Util.Quad, Matrix4x4.identity, _apply);

            pass.End();
        }
    }
}