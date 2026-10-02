using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ApplyTemporalDeferredPass : Pass
    {
        private Material _apply;

        private ScreenProbes _screen_probes;
        private Probes _probes;
        private Temporal _temporal;

        public ApplyTemporalDeferredPass(ScreenProbes screen_probes, Probes probes, Temporal temporal)
        {
            _screen_probes = screen_probes;
            _probes = probes;
            _temporal = temporal;

            _apply = new Material(Resources.Load<Shader>("Shaders/Apply/TemporalDeferred"));
        }

        public override int InputRequirements(GISettings settings)
        {
            return Requirements.Depth | Requirements.Motion;
        }

        public override void Execute(Executor executor, PassData data)
        {
            var builtin = !executor.IsScriptable();

            var pass = executor.Pass();
            var cmd = pass.Begin("Apply Temporal Deferred");

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

            if (builtin)
                _apply.EnableKeyword("BUILTIN_GBUFFER");
            else
                _apply.DisableKeyword("BUILTIN_GBUFFER");

            var camera = data.Camera;
            _apply.SetVector(P.ViewportScale, new Vector2((float)camera.pixelWidth / (float)_temporal.BufferWidth, (float)camera.pixelHeight / (float)_temporal.BufferHeight));

            if (_temporal != null)
                _apply.SetTexture(P.DiffuseBuffer, _temporal.CurrentDiffuse);
            else if (_screen_probes != null)
                _apply.SetTexture(P.DiffuseBuffer, _screen_probes.IntegratedDiffuse);

            _apply.SetFloat(P.DiffuseMultiplier, data.Settings.DiffuseMultiplier);

            if (builtin)
            {
                Util.SetCameraParameters(_apply, data.Camera);
                cmd.SetGlobalTexture(P._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
            }

            cmd.DrawMesh(Util.Quad, Matrix4x4.identity, _apply);

            pass.End();
        }
    }
}