using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SDFRenderPass : Pass
    {
        private Material _mrender;

        public SDFRenderPass()
        {
            _mrender = new Material(Shader.Find("GI/SDF"));
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
            var cmd = pass.Begin("SDF Render");

            if (!executor.IsScriptable())
                Util.SetCameraParameters(_mrender, data.Camera);

            _mrender.SetFloat(P.FarClipPlane, data.Camera.farClipPlane);

            _mrender.SetBuffer(P.SDFAssets, data.Atlas.Assets);
            _mrender.SetBuffer(P.SDFBricks, data.Atlas.Bricks);
            _mrender.SetTexture(P.SDFAtlas, data.Atlas.SDF);
            _mrender.SetTexture(P.VoxelAtlas, data.Atlas.Voxels);

            _mrender.SetInt(P.BufferCount, data.Atlas.Cursor);

            _mrender.SetBuffer(P.DFInstances, data.Atlas.Buffer);

            if (data.ColorRenderHandle != null)
                cmd.SetRenderTarget(data.ColorRenderHandle);

            cmd.DrawProcedural(Matrix4x4.identity, _mrender, 0, MeshTopology.Triangles, 6);

            pass.End();
        }
    }
}