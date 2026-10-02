using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace GI
{
    public struct Kernel
    {
        public ComputeShader Shader;
        public int Index;

        public int GroupX;
        public int GroupY;
        public int GroupZ;

        public Kernel(ComputeShader shader, string name)
        {
            Shader = shader;

            Index = shader.FindKernel(name);

            uint x, y, z;
            shader.GetKernelThreadGroupSizes(Index, out x, out y, out z);

            GroupX = (int)x;
            GroupY = (int)y;
            GroupZ = (int)z;
        }

        public Kernel Copy()
        {
            var kernel = new Kernel();
            kernel.Shader = GameObject.Instantiate(Shader);
            kernel.Index = Index;
            kernel.GroupX = GroupX;
            kernel.GroupY = GroupY;
            kernel.GroupZ = GroupZ;
            return kernel;
        }

        public void Keyword(string name, bool value)
        {
            if (value)
                Shader.EnableKeyword(name);
            else
                Shader.DisableKeyword(name);

            // Assert.AreEqual(Shader.IsKeywordEnabled(name), value);
        }

        public void BindRT(CommandBuffer cmd, int parameter, RenderTargetIdentifier rt)
        {
            cmd.SetComputeTextureParam(Shader, Index, parameter, rt);
        }

        public void BindOnce(CommandBuffer cmd, int parameter, RenderTexture texture)
        {
            Shader.SetTexture(Index, parameter, texture);
        }

        public void BindOnce(CommandBuffer cmd, int parameter, Texture2D texture)
        {
            Shader.SetTexture(Index, parameter, texture);
        }

        public void BindOnce(CommandBuffer cmd, int parameter, Texture3D texture)
        {
            Shader.SetTexture(Index, parameter, texture);
        }

        public void BindOnce(CommandBuffer cmd, int parameter, Texture texture)
        {
            Shader.SetTexture(Index, parameter, texture);
        }

        public void Bind(CommandBuffer cmd, int parameter, ComputeBuffer buffer)
        {
            cmd.SetComputeBufferParam(Shader, Index, parameter, buffer);
        }

        public void Bind(CommandBuffer cmd, int parameter, GraphicsBuffer buffer)
        {
            cmd.SetComputeBufferParam(Shader, Index, parameter, buffer);
        }

        public void Set(CommandBuffer cmd, int parameter, Matrix4x4 matrix)
        {
            cmd.SetComputeMatrixParam(Shader, parameter, matrix);
        }

        public void Set(CommandBuffer cmd, int parameter, params Matrix4x4[] matrices)
        {
            cmd.SetComputeMatrixArrayParam(Shader, parameter, matrices);
        }

        public void Set(CommandBuffer cmd, int parameter, params Vector4[] values)
        {
            cmd.SetComputeVectorArrayParam(Shader, parameter, values);
        }

        public void Set(CommandBuffer cmd, int parameter, Vector4 value)
        {
            cmd.SetComputeVectorParam(Shader, parameter, value);
        }

        public void Seti(CommandBuffer cmd, int parameter, params int[] values)
        {
            cmd.SetComputeIntParams(Shader, parameter, values);
        }

        public void Seti(CommandBuffer cmd, int parameter, int value)
        {
            cmd.SetComputeIntParam(Shader, parameter, value);
        }

        public void Setf(CommandBuffer cmd, int parameter, params float[] values)
        {
            cmd.SetComputeFloatParams(Shader, parameter, values);
        }

        public void Setf(CommandBuffer cmd, int parameter, float value)
        {
            cmd.SetComputeFloatParam(Shader, parameter, value);
        }

        public void DispatchOne(CommandBuffer buffer)
        {
            buffer.DispatchCompute(Shader, Index, 1, 1, 1);
        }

        public void Dispatch(CommandBuffer buffer, int width, int height = 1, int depth = 1)
        {
            if (width == 0 || height == 0 || depth == 0)
                return;

            buffer.DispatchCompute(Shader,
                                   Index,
                                   width,
                                   height,
                                   depth);
        }

        public void DispatchEnoughFor(CommandBuffer buffer, int width, int height = 1, int depth = 1)
        {
            if (width == 0 || height == 0 || depth == 0)
                return;

            buffer.DispatchCompute(Shader,
                                   Index,
                                   (int)((width  + GroupX - 1) / GroupX),
                                   (int)((height + GroupY - 1) / GroupY),
                                   (int)((depth  + GroupZ - 1) / GroupZ));
        }

        public void Dispatch(CommandBuffer buffer, ComputeBuffer arguments, int offset = 0)
        {
            buffer.DispatchCompute(Shader, Index, arguments, (uint)offset);
        }
    }
}