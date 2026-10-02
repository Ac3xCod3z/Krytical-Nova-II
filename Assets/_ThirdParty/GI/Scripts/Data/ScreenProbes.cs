using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class ScreenProbes
    {
        public int AtlasWidth;
        public int AtlasHeight;
        public int BufferWidth;
        public int BufferHeight;

        public ComputeBuffer Counts;
        public ComputeBuffer Data;
        public ComputeBuffer SH;

        public RenderTexture RadianceAtlas;
        public RenderTexture RadianceAtlas2;
        public RenderTexture IntegratedDiffuse;
        public RenderTexture Mapping;

        public ComputeBuffer RaySetupArguments;
        public ComputeBuffer TraceArguments;
        public ComputeBuffer QueryArguments;

        public int CountX;
        public int CountY;
        public int MaxAdaptiveCount;

        public int CalculatedCount { get { return _calculated_count; } }

        private int _calculated_count;
        private int _calculated_adaptive_count;

        public ScreenProbes(int max_adaptive_count)
        {
            MaxAdaptiveCount = max_adaptive_count;

            Counts = new ComputeBuffer(Defines.SCREEN_PROBE_COUNT_ALL, sizeof(uint), ComputeBufferType.Raw);
            RaySetupArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            TraceArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            QueryArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        }

        public void Release()
        {
            if (Data != null)
            {
                Data.Release();
                Data = null;
            }

            if (Counts != null)
            {
                Counts.Release();
                Counts = null;
            }

            if (RaySetupArguments != null)
            {
                RaySetupArguments.Release();
                RaySetupArguments = null;
            }

            if (TraceArguments != null)
            {
                TraceArguments.Release();
                TraceArguments = null;
            }

            if (QueryArguments != null)
            {
                QueryArguments.Release();
                QueryArguments = null;
            }

            if (RadianceAtlas != null)
            {
                RadianceAtlas.Release();
                RadianceAtlas = null;
            }

            if (IntegratedDiffuse != null)
            {
                IntegratedDiffuse.Release();
                IntegratedDiffuse = null;
            }

            if (Mapping != null)
            {
                Mapping.Release();
                Mapping = null;
            }
        }

        public void Update(int width, int height)
        {
            CountX = (width + Defines.SCREEN_PROBE_DIM - 1) / Defines.SCREEN_PROBE_DIM;
            CountY = (height + Defines.SCREEN_PROBE_DIM - 1) / Defines.SCREEN_PROBE_DIM;

            var count = CountX * (CountY + (MaxAdaptiveCount + CountX - 1) / CountX);

            _calculated_adaptive_count = count - CountX * CountY;

            if (Data == null || SH == null || count != _calculated_count)
            {
                if (Data != null) Data.Release();
                if (SH != null) SH.Release();

                Data = new ComputeBuffer(count, Defines.SCREEN_PROBE_STRIDE, ComputeBufferType.Raw);
                SH = new ComputeBuffer(count, Defines.SH2_STRIDE, ComputeBufferType.Raw);

                _calculated_count = count;
            }

            var atlas_width = CountX * Defines.SCREEN_PROBE_RES;
            var atlas_height = CountY * Defines.SCREEN_PROBE_RES;

            atlas_height += ((_calculated_adaptive_count + CountX - 1) / CountX) * Defines.SCREEN_PROBE_RES;

            if (RadianceAtlas == null ||
                RadianceAtlas2 == null ||
                atlas_width != AtlasWidth || atlas_height != AtlasHeight)
            {
                if (RadianceAtlas != null) RadianceAtlas.Release();
                if (RadianceAtlas2 != null) RadianceAtlas2.Release();

                {
                    var desc = new RenderTextureDescriptor();
                    desc.width = atlas_width;
                    desc.height = atlas_height;
                    desc.volumeDepth = 1;
                    desc.dimension = TextureDimension.Tex2D;
                    desc.msaaSamples = 1;
                    desc.enableRandomWrite = true;
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

                    RadianceAtlas = new RenderTexture(desc);
                    RadianceAtlas2 = new RenderTexture(desc);
                }

                AtlasWidth = atlas_width;
                AtlasHeight = atlas_height;
            }

            //

            var buffer_width = width;
            var buffer_height = height;

            if (IntegratedDiffuse == null ||
                Mapping == null ||
                buffer_width != BufferWidth || buffer_height != BufferHeight)
            {
                if (IntegratedDiffuse != null) IntegratedDiffuse.Release();
                if (Mapping != null) Mapping.Release();

                {
                    var desc = new RenderTextureDescriptor();
                    desc.width = buffer_width;
                    desc.height = buffer_height;
                    desc.volumeDepth = 1;
                    desc.dimension = TextureDimension.Tex2D;
                    desc.msaaSamples = 1;
                    desc.enableRandomWrite = true;
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

                    IntegratedDiffuse = new RenderTexture(desc);
                }

                {
                    var desc = new RenderTextureDescriptor();
                    desc.width = (width + Defines.SCREEN_PROBE_ADAPTIVE_DIM - 1) / Defines.SCREEN_PROBE_ADAPTIVE_DIM;
                    desc.height = (height + Defines.SCREEN_PROBE_ADAPTIVE_DIM - 1) / Defines.SCREEN_PROBE_ADAPTIVE_DIM;
                    desc.volumeDepth = 1;
                    desc.dimension = TextureDimension.Tex2D;
                    desc.msaaSamples = 1;
                    desc.enableRandomWrite = true;
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UInt;

                    Mapping = new RenderTexture(desc);
                }

                BufferWidth = buffer_width;
                BufferHeight = buffer_height;
            }

            //

            Shader.SetGlobalInt(P.ScreenProbeCountX, CountX);
            Shader.SetGlobalInt(P.ScreenProbeCountY, CountY);

            Shader.SetGlobalBuffer(P.ScreenProbes, Data);

            Shader.SetGlobalBuffer(P.ScreenProbesRW, Data);
        }

        public void Parameters(Material material)
        {
            material.SetInt(P.ScreenProbeCountX, CountX);
            material.SetInt(P.ScreenProbeCountY, CountY);
            material.SetInt(P.MaxAdaptiveCount, _calculated_adaptive_count);

            material.SetBuffer(P.ScreenProbes, Data);
            material.SetBuffer(P.ScreenProbeSH, SH);
            material.SetBuffer(P.ScreenProbeCounts, Counts);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeIntParam(kernel.Shader, P.ScreenProbeCountX, CountX);
            cmd.SetComputeIntParam(kernel.Shader, P.ScreenProbeCountY, CountY);
            cmd.SetComputeIntParam(kernel.Shader, P.MaxAdaptiveCount, _calculated_adaptive_count);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ScreenProbeCounts, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ScreenProbes, Data);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ScreenProbeSH, SH);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ScreenProbeCountsRW, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ScreenProbeSHRW, SH);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ScreenProbesRW, Data);
        }

        public void SwapRadianceAtlas()
        {
            var t = RadianceAtlas;
            RadianceAtlas = RadianceAtlas2;
            RadianceAtlas2 = t;
        }
    }
}