using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class ScreenProbeDebugPass : Pass
    {
        private Material _mrender;

        private ScreenProbes _screen_probes;

        public ScreenProbeDebugPass(ScreenProbes screen_probes)
        {
            _screen_probes = screen_probes;

            _mrender = new Material(Shader.Find("GI/ScreenTraceDebug"));
        }

        public override void Release()
        {
            if (_mrender != null)
            {
                Object.DestroyImmediate(_mrender);
                _mrender = null;
            }
        }

        public override void Execute(Executor executor, PassData data)
        {
            var pass = executor.Pass();
            var cmd = pass.Begin("GDF Render");

            _screen_probes.Parameters(_mrender);

            _mrender.SetTexture(P.ScreenProbeRadianceAtlas, _screen_probes.RadianceAtlas);

            if (data.ColorRenderHandle != null)
                cmd.SetRenderTarget(data.ColorRenderHandle);

            cmd.DrawProcedural(Matrix4x4.identity, _mrender, 0, MeshTopology.Triangles, 6);

            pass.End();
        }
    }
}