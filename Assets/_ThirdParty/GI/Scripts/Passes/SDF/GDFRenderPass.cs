using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class GDFRenderPass : Pass
    {
        private Material _mrender;

        private GDF _gdf;

        public GDFRenderPass(GDF gdf)
        {
            _gdf = gdf;

            _mrender = new Material(Shader.Find("GI/GDF"));
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

            if (!executor.IsScriptable())
                Util.SetCameraParameters(_mrender, data.Camera);

            _gdf.Parameters(_mrender);
            _gdf.Textures(_mrender);

            _mrender.SetTexture(P.VoxelAtlas, data.Atlas.Voxels);

            if (data.ColorRenderHandle != null)
                cmd.SetRenderTarget(data.ColorRenderHandle);

            cmd.DrawProcedural(Matrix4x4.identity, _mrender, 0, MeshTopology.Triangles, 6);

            pass.End();
        }
    }
}