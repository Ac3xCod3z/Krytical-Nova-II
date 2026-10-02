using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class Probes
    {
        public bool NeedReset;
        public ProbeChoice Choice;

        public float CellSize = 1;
        public int CascadeCount = 8;

        public int BufferWidth;
        public int BufferHeight;

        public ComputeBuffer Arrays;
        public ComputeBuffer Counts;
        public ComputeBuffer Entries;
        public ComputeBuffer Grid;
        public ComputeBuffer Colors;
        public ComputeBuffer RequestMap;
        public ComputeBuffer SurfelMap;
        public ComputeBuffer SH;

        public ComputeBuffer AcceptArguments;
        public ComputeBuffer CollectArguments;
        public ComputeBuffer CreateArguments;
        public ComputeBuffer DebugArguments;
        public ComputeBuffer MaintainArguments;
        public ComputeBuffer PositionArguments;
        public ComputeBuffer ProbeArguments;
        public ComputeBuffer RadianceArguments;
        public ComputeBuffer TraceArguments;
        public ComputeBuffer RaySetupArguments;
        public ComputeBuffer RequestArguments;

        public RenderTexture RadianceAtlas;
        public RenderTexture RadianceAtlas2;
        public RenderTexture GatherAtlas;
        public RenderTexture FilterAtlas;
        public RenderTexture DepthAtlas;

        public RenderTexture IntegratedDiffuse;

        public Vector3 ReferencePosition;

        private Vector4[] _cascade_origins;
        private Vector4[] _cascade_offsets;

        public Probes(float cell, int cascade_count, bool color_buffer, bool probe_occlusion, bool filter, bool sh3, bool surfel_mapping)
        {
            CellSize = cell;
            CascadeCount = cascade_count;

            var cell_count = cascade_count * Defines.PROBE_CASCADE_CELL_COUNT;

            Arrays = new ComputeBuffer(Defines.PROBE_ARRAY_ALL, sizeof(uint), ComputeBufferType.Raw);
            Counts = new ComputeBuffer(Defines.PROBE_COUNT_ALL, sizeof(uint), ComputeBufferType.Raw);
            Entries = new ComputeBuffer(Defines.PROBE_MAX_ENTRIES * Defines.PROBE_ENTRY_STRIDE / 4, 4, ComputeBufferType.Raw);
            Grid = new ComputeBuffer(cell_count, sizeof(uint), ComputeBufferType.Raw);
            RequestMap = new ComputeBuffer(cell_count, sizeof(uint), ComputeBufferType.Raw);

            if (surfel_mapping)
                SurfelMap = new ComputeBuffer(Defines.PROBE_MAX_RAYS, Defines.SURFEL_MAP_STRIDE, ComputeBufferType.Raw);

            var sh_stride = sh3 ? Defines.SH3_STRIDE : Defines.SH2_STRIDE;
            SH = new ComputeBuffer(Defines.PROBE_MAX_ENTRIES, sh_stride, ComputeBufferType.Raw);

            if (color_buffer)
                Colors = new ComputeBuffer(Defines.PROBE_MAX_ENTRIES, Defines.RADIANCE_STRIDE, ComputeBufferType.Raw);

            AcceptArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            CollectArguments = new ComputeBuffer(8, sizeof(uint), ComputeBufferType.IndirectArguments);
            CreateArguments = new ComputeBuffer(8, sizeof(uint), ComputeBufferType.IndirectArguments);
            DebugArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            MaintainArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            PositionArguments = new ComputeBuffer(8, sizeof(uint), ComputeBufferType.IndirectArguments);
            ProbeArguments = new ComputeBuffer(12, sizeof(uint), ComputeBufferType.IndirectArguments);
            RadianceArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            RaySetupArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            RequestArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            TraceArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);

            {
                var desc = new RenderTextureDescriptor();
                desc.width = Defines.PROBE_ATLAS_WIDTH;
                desc.height = Defines.PROBE_ATLAS_HEIGHT;
                desc.volumeDepth = 1;
                desc.dimension = TextureDimension.Tex2D;
                desc.msaaSamples = 1;
                desc.enableRandomWrite = true;
                desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

                RadianceAtlas = new RenderTexture(desc);
                RadianceAtlas2 = new RenderTexture(desc);
                GatherAtlas = new RenderTexture(desc);

                if (filter)
                    FilterAtlas = new RenderTexture(desc);
            }

            if (probe_occlusion)
            {
                var desc = new RenderTextureDescriptor();
                desc.width = Defines.PROBE_DEPTH_ATLAS_WIDTH;
                desc.height = Defines.PROBE_DEPTH_ATLAS_HEIGHT;
                desc.volumeDepth = 1;
                desc.dimension = TextureDimension.Tex2D;
                desc.msaaSamples = 1;
                desc.enableRandomWrite = true;
                desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

                DepthAtlas = new RenderTexture(desc);
            }

            NeedReset = true;

            _cascade_origins = new Vector4[Defines.PROBE_MAX_CASCADES];
            _cascade_offsets = new Vector4[Defines.PROBE_MAX_CASCADES];
        }

        public void Update(Vector3 reference_position)
        {
            for (int i = 0; i < CascadeCount; i++)
            {
                var origin = Origin(reference_position, i);
                _cascade_origins[i] = new Vector4(origin.x, origin.y, origin.z);

                var previous = Origin(ReferencePosition, i);

                _cascade_offsets[i] = new Vector4(previous.x - origin.x,
                                                  previous.y - origin.y,
                                                  previous.z - origin.z,
                                                  0);
            }

            ReferencePosition = reference_position;
        }

        public void GlobalParameters()
        {
            Shader.SetGlobalFloat(P.ProbeCell, CellSize);
            Shader.SetGlobalInt(P.ProbeCascadeCount, CascadeCount);
            Shader.SetGlobalVector(P.ReferencePosition, ReferencePosition);
            Shader.SetGlobalVectorArray(P.ProbeCascadeOffsets, _cascade_offsets);
            Shader.SetGlobalVectorArray(P.ProbeCascadeOrigins, _cascade_origins);

            Shader.SetGlobalBuffer(P.ProbeArrays, Arrays);
            Shader.SetGlobalBuffer(P.ProbeCounts, Counts);
            Shader.SetGlobalBuffer(P.ProbeEntries, Entries);
            Shader.SetGlobalBuffer(P.ProbeGrid, Grid);
            Shader.SetGlobalBuffer(P.ProbeRequestMap, RequestMap);
            Shader.SetGlobalBuffer(P.ProbeSH, SH);

            if (SurfelMap != null)
                Shader.SetGlobalBuffer(P.ProbeSurfelMap, SurfelMap);

            Shader.SetGlobalBuffer(P.ProbeArraysRW, Arrays);
            Shader.SetGlobalBuffer(P.ProbeCountsRW, Counts);
            Shader.SetGlobalBuffer(P.ProbeEntriesRW, Entries);
            Shader.SetGlobalBuffer(P.ProbeGridRW, Grid);
            Shader.SetGlobalBuffer(P.ProbeRequestMapRW, RequestMap);
            Shader.SetGlobalBuffer(P.ProbeSHRW, SH);

            if (SurfelMap != null)
                Shader.SetGlobalBuffer(P.ProbeSurfelMapRW, SurfelMap);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeFloatParam(kernel.Shader, P.ProbeCell, CellSize);
            cmd.SetComputeIntParam(kernel.Shader, P.ProbeCascadeCount, CascadeCount);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.ProbeCascadeOffsets, _cascade_offsets);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.ProbeCascadeOrigins, _cascade_origins);
            cmd.SetComputeVectorParam(kernel.Shader, P.ReferencePosition, ReferencePosition);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeArrays, Arrays);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeCounts, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeEntries, Entries);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeGrid, Grid);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeRequestMap, RequestMap);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeSH, SH);

            if (SurfelMap != null)
                cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeSurfelMap, SurfelMap);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeArraysRW, Arrays);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeCountsRW, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeEntriesRW, Entries);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeGridRW, Grid);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeRequestMapRW, RequestMap);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeSHRW, SH);

            if (SurfelMap != null)
                cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.ProbeSurfelMapRW, SurfelMap);
        }

        public void SwapRadianceAtlas()
        {
            var t = RadianceAtlas;
            RadianceAtlas = RadianceAtlas2;
            RadianceAtlas2 = t;
        }

        public Vector3Int Cell(Vector3 position, int cascade)
        {
            float cell_size = CellSize * (1 << cascade);

            return new Vector3Int(Mathf.FloorToInt(position.x / cell_size),
                                  Mathf.FloorToInt(position.x / cell_size),
                                  Mathf.FloorToInt(position.x / cell_size));
        }

        public Vector3Int Origin(Vector3 reference_position, int cascade)
        {
            float cell_size = CellSize * (1 << cascade);

            var center = new Vector3Int(Mathf.FloorToInt(reference_position.x / cell_size),
                                        Mathf.FloorToInt(reference_position.y / cell_size),
                                        Mathf.FloorToInt(reference_position.z / cell_size));

            var origin = new Vector3Int(center.x - Defines.PROBE_CASCADE_DIM / 2,
                                        center.y - Defines.PROBE_CASCADE_DIM / 2,
                                        center.z - Defines.PROBE_CASCADE_DIM / 2);

            return origin;
        }

        public void EnsureIntegrated(int width, int height)
        {
            var buffer_width = width;
            var buffer_height = height;

            if (buffer_width < BufferWidth) buffer_width = BufferWidth;
            if (buffer_height < BufferHeight) buffer_height = BufferHeight;

            if (IntegratedDiffuse == null ||
                buffer_width > BufferWidth || buffer_height > BufferHeight)
            {
                if (IntegratedDiffuse != null)
                {
                    IntegratedDiffuse.Release();
                    IntegratedDiffuse = null;
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

                    IntegratedDiffuse = new RenderTexture(desc);
                }

                BufferWidth = buffer_width;
                BufferHeight = buffer_height;
            }
        }

        public void Release()
        {
            if (Arrays != null)
            {
                Arrays.Release();
                Arrays = null;
            }

            if (Counts != null)
            {
                Counts.Release();
                Counts = null;
            }

            if (Entries != null)
            {
                Entries.Release();
                Entries = null;
            }

            if (Grid != null)
            {
                Grid.Release();
                Grid = null;
            }

            if (Colors != null)
            {
                Colors.Release();
                Colors = null;
            }

            if (RequestMap != null)
            {
                RequestMap.Release();
                RequestMap = null;
            }

            if (SurfelMap != null)
            {
                SurfelMap.Release();
                SurfelMap = null;
            }

            if (SH != null)
            {
                SH.Release();
                SH = null;
            }

            if (AcceptArguments != null)
            {
                AcceptArguments.Release();
                AcceptArguments = null;
            }

            if (PositionArguments != null)
            {
                PositionArguments.Release();
                PositionArguments = null;
            }

            if (CollectArguments != null)
            {
                CollectArguments.Release();
                CollectArguments = null;
            }

            if (CreateArguments != null)
            {
                CreateArguments.Release();
                CreateArguments = null;
            }

            if (DebugArguments != null)
            {
                DebugArguments.Release();
                DebugArguments = null;
            }

            if (MaintainArguments != null)
            {
                MaintainArguments.Release();
                MaintainArguments = null;
            }

            if (ProbeArguments != null)
            {
                ProbeArguments.Release();
                ProbeArguments = null;
            }

            if (RadianceArguments != null)
            {
                RadianceArguments.Release();
                RadianceArguments = null;
            }

            if (TraceArguments != null)
            {
                TraceArguments.Release();
                TraceArguments = null;
            }

            if (RaySetupArguments != null)
            {
                RaySetupArguments.Release();
                RaySetupArguments = null;
            }

            if (RequestArguments != null)
            {
                RequestArguments.Release();
                RequestArguments = null;
            }

            if (RadianceAtlas != null)
            {
                RadianceAtlas.Release();
                RadianceAtlas = null;
            }

            if (RadianceAtlas2 != null)
            {
                RadianceAtlas2.Release();
                RadianceAtlas2 = null;
            }

            if (GatherAtlas != null)
            {
                GatherAtlas.Release();
                GatherAtlas = null;
            }

            if (FilterAtlas != null)
            {
                FilterAtlas.Release();
                FilterAtlas = null;
            }

            if (DepthAtlas != null)
            {
                DepthAtlas.Release();
                DepthAtlas = null;
            }

            if (IntegratedDiffuse != null)
            {
                IntegratedDiffuse.Release();
                IntegratedDiffuse = null;
            }
        }
    }
}