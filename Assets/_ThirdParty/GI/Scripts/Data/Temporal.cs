using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class Temporal
    {
        public int BufferWidth;
        public int BufferHeight;

        public bool GameMode;

        public RenderTexture CurrentDiffuse;
        public RenderTexture PreviousDiffuse;
        public RenderTexture CurrentConvergence;
        public RenderTexture PreviousConvergence;

        public bool HasPrevious;
        public Matrix4x4 PreviousViewProjection;
        public Matrix4x4 PreviousViewProjectionInverse;
        public Matrix4x4 CurrentViewProjection;
        public Matrix4x4 CurrentViewProjectionInverse;

        public Temporal()
        {
        }

        public void Release()
        {
            if (CurrentDiffuse != null)
            {
                CurrentDiffuse.Release();
                CurrentDiffuse = null;
            }

            if (PreviousDiffuse != null)
            {
                PreviousDiffuse.Release();
                PreviousDiffuse = null;
            }

            if (CurrentConvergence != null)
            {
                CurrentConvergence.Release();
                CurrentConvergence = null;
            }

            if (PreviousConvergence != null)
            {
                PreviousConvergence.Release();
                PreviousConvergence = null;
            }
        }

        public void Update(Camera camera, int width, int height)
        {
            var buffer_width = width;
            var buffer_height = height;

            if (buffer_width < BufferWidth) buffer_width = BufferWidth;
            if (buffer_height < BufferHeight) buffer_height = BufferHeight;

            if (CurrentDiffuse == null ||
                PreviousDiffuse == null ||
                CurrentConvergence == null ||
                PreviousConvergence == null ||
                buffer_width > BufferWidth || buffer_height > BufferHeight)
            {
                if (CurrentDiffuse != null)
                {
                    CurrentDiffuse.Release();
                    CurrentDiffuse = null;
                }

                if (PreviousDiffuse != null)
                {
                    PreviousDiffuse.Release();
                    PreviousDiffuse = null;
                }

                if (CurrentConvergence != null)
                {
                    CurrentConvergence.Release();
                    CurrentConvergence = null;
                }

                if (PreviousConvergence != null)
                {
                    PreviousConvergence.Release();
                    PreviousConvergence = null;
                }

                {
                    var desc = new RenderTextureDescriptor();
                    desc.width = buffer_width;
                    desc.height = buffer_height;
                    desc.volumeDepth = 1;
                    desc.dimension = TextureDimension.Tex2D;
                    desc.msaaSamples = 1;
                    desc.enableRandomWrite = true;
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

                    CurrentDiffuse = new RenderTexture(desc);
                    PreviousDiffuse = new RenderTexture(desc);
                }

                {
                    var desc = new RenderTextureDescriptor();
                    desc.width = buffer_width;
                    desc.height = buffer_height;
                    desc.volumeDepth = 1;
                    desc.dimension = TextureDimension.Tex2D;
                    desc.msaaSamples = 1;
                    desc.enableRandomWrite = true;
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;

                    CurrentConvergence = new RenderTexture(desc);
                    PreviousConvergence = new RenderTexture(desc);
                }

                BufferWidth = buffer_width;
                BufferHeight = buffer_height;
            }

            //

            PreviousViewProjection = CurrentViewProjection;
            PreviousViewProjectionInverse = CurrentViewProjectionInverse;

            CurrentViewProjection = Util.ViewProjection(camera);
            CurrentViewProjectionInverse = Util.ViewProjectionInverse(camera);
            // CurrentViewProjection = Shader.GetGlobalMatrix("unity_MatrixVP");
            // CurrentViewProjectionInverse = Shader.GetGlobalMatrix("unity_MatrixInvVP");

            if (!HasPrevious)
            {
                HasPrevious = true;
                PreviousViewProjection = CurrentViewProjection;
                PreviousViewProjectionInverse = CurrentViewProjectionInverse;
            }

            Shader.SetGlobalVector(P.TemporalViewportScale, new Vector2((float)camera.pixelWidth / (float)BufferWidth, (float)camera.pixelHeight / (float)BufferHeight));
        }

        public void Parameters(Material material)
        {
            material.SetMatrix(P.ViewProjection, CurrentViewProjection);
            material.SetMatrix(P.ViewProjectionInverse, CurrentViewProjectionInverse);
            material.SetMatrix(P.PreviousViewProjection, PreviousViewProjection);
            material.SetMatrix(P.PreviousViewProjectionInverse, PreviousViewProjectionInverse);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeMatrixParam(kernel.Shader, P.ViewProjection, CurrentViewProjection);
            cmd.SetComputeMatrixParam(kernel.Shader, P.ViewProjectionInverse, CurrentViewProjectionInverse);
            cmd.SetComputeMatrixParam(kernel.Shader, P.PreviousViewProjection, PreviousViewProjection);
            cmd.SetComputeMatrixParam(kernel.Shader, P.PreviousViewProjectionInverse, PreviousViewProjectionInverse);
        }

        public void SwapConvergence()
        {
            var t = CurrentConvergence;
            CurrentConvergence = PreviousConvergence;
            PreviousConvergence = t;
        }

        public void SwapDiffuse()
        {
            var t = CurrentDiffuse;
            CurrentDiffuse = PreviousDiffuse;
            PreviousDiffuse = t;
        }
    }
}