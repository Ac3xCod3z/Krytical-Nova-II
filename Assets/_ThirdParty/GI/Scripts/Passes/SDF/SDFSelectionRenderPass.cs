using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    class SDFSelectionRenderPass : Pass
    {
        private Material _mrender;

        private ComputeBuffer _buffer;
        private int _buffer_size;

        public SDFSelectionRenderPass()
        {
            _mrender = new Material(Shader.Find("GI/SDFManual"));
        }

        public override void Release()
        {
            if (_mrender != null)
            {
                Object.DestroyImmediate(_mrender);
                _mrender = null;
            }

            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }
        }

        public override void Execute(Executor executor, PassData data)
        {
            if (data.Atlas.Selection.Count == 0)
                return;

            if (_buffer == null || _buffer_size != data.Atlas.Selection.Count)
            {
                if (_buffer != null)
                    _buffer.Release();

                _buffer_size = data.Atlas.Selection.Count;
                _buffer = new ComputeBuffer(_buffer_size, 4);
            }

            _buffer.SetData(data.Atlas.Selection);

            var pass = executor.Pass();
            var cmd = pass.Begin("SDF Selection Render");

            if (!executor.IsScriptable())
                Util.SetCameraParameters(_mrender, data.Camera);

            _mrender.SetFloat(P.FarClipPlane, data.Camera.farClipPlane);

            _mrender.SetBuffer(P.SDFAssets, data.Atlas.Assets);
            _mrender.SetBuffer(P.SDFBricks, data.Atlas.Bricks);
            _mrender.SetTexture(P.SDFAtlas, data.Atlas.SDF);
            _mrender.SetTexture(P.VoxelAtlas, data.Atlas.Voxels);

            _mrender.SetInt(P.BufferCount, data.Atlas.Selection.Count);
            _mrender.SetBuffer(P.IndexBuffer, _buffer);

            _mrender.SetBuffer(P.DFInstances, data.Atlas.Buffer);

            if (data.ColorRenderHandle != null)
                cmd.SetRenderTarget(data.ColorRenderHandle);

            cmd.DrawProcedural(Matrix4x4.identity, _mrender, 0, MeshTopology.Triangles, 6);

            pass.End();
        }
    }
}