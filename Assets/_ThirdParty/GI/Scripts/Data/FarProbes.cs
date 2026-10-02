using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class FarProbes
    {
        public bool NeedReset;
        public ProbeChoice Choice;

        public float CellSize = 1;
        public int CascadeCount = 8;

        public ComputeBuffer Arrays;
        public ComputeBuffer Counts;
        public ComputeBuffer Entries;
        public ComputeBuffer Grid;

        public ComputeBuffer RequestMap;

        public ComputeBuffer SurfelMap;

        public ComputeBuffer AcceptArguments;
        public ComputeBuffer AtlasArguments;
        public ComputeBuffer CollectArguments;
        public ComputeBuffer CreateArguments;
        public ComputeBuffer DebugArguments;
        public ComputeBuffer MaintainArguments;
        public ComputeBuffer PositionFindArguments;
        public ComputeBuffer ProbeArguments;
        public ComputeBuffer RadianceArguments;
        public ComputeBuffer RaySetupArguments;
        public ComputeBuffer RequestArguments;
        public ComputeBuffer TraceArguments;

        public RenderTexture RadianceAtlas;
        public RenderTexture RadianceAtlas2;
        public RenderTexture GatherAtlas;

        public Vector3 ReferencePosition;

        private Vector4[] _cascade_origins;
        private Vector4[] _cascade_offsets;

        public FarProbes(float cell, int cascade_count, bool surfel_mapping)
        {
            CellSize = cell * Defines.FAR_PROBE_SPACING;
            CascadeCount = cascade_count;

            var cell_count = cascade_count * Defines.FAR_PROBE_CASCADE_CELL_COUNT;

            Arrays = new ComputeBuffer(Defines.FAR_PROBE_ARRAY_ALL, sizeof(uint), ComputeBufferType.Raw);
            Counts = new ComputeBuffer(Defines.FAR_PROBE_COUNT_ALL, sizeof(uint), ComputeBufferType.Raw);
            Entries = new ComputeBuffer(Defines.FAR_PROBE_MAX_ENTRIES, Defines.FAR_PROBE_ENTRY_STRIDE, ComputeBufferType.Raw);
            Grid = new ComputeBuffer(cell_count, sizeof(uint), ComputeBufferType.Raw);
            RequestMap = new ComputeBuffer(cell_count, sizeof(uint), ComputeBufferType.Raw);

            if (surfel_mapping)
                SurfelMap = new ComputeBuffer(Defines.FAR_PROBE_MAX_RAYS, Defines.SURFEL_MAP_STRIDE, ComputeBufferType.Raw);

            AcceptArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            AtlasArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            CollectArguments = new ComputeBuffer(8, sizeof(uint), ComputeBufferType.IndirectArguments);
            CreateArguments = new ComputeBuffer(8, sizeof(uint), ComputeBufferType.IndirectArguments);
            DebugArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            MaintainArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            ProbeArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            RadianceArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            RaySetupArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            RequestArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            TraceArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);

            {
                var desc = new RenderTextureDescriptor();
                desc.width = Defines.FAR_PROBE_ATLAS_WIDTH;
                desc.height = Defines.FAR_PROBE_ATLAS_HEIGHT;
                desc.volumeDepth = 1;
                desc.dimension = TextureDimension.Tex2D;
                desc.msaaSamples = 1;
                desc.enableRandomWrite = true;
                desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;

                GatherAtlas = new RenderTexture(desc);

                // make sure FAR_PROBE_RES is PROBE_RES * 2
                desc.width = Defines.FAR_PROBE_ATLAS_WIDTH / 2;
                desc.height = Defines.FAR_PROBE_ATLAS_HEIGHT / 2;
                RadianceAtlas = new RenderTexture(desc);
                RadianceAtlas2 = new RenderTexture(desc);
            }

            NeedReset = true;

            _cascade_origins = new Vector4[Defines.FAR_PROBE_MAX_CASCADES];
            _cascade_offsets = new Vector4[Defines.FAR_PROBE_MAX_CASCADES];
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
            Shader.SetGlobalFloat(P.FarProbeCell, CellSize);
            Shader.SetGlobalInt(P.FarProbeCascadeCount, CascadeCount);
            Shader.SetGlobalVectorArray(P.FarProbeCascadeOffsets, _cascade_offsets);
            Shader.SetGlobalVectorArray(P.FarProbeCascadeOrigins, _cascade_origins);

            Shader.SetGlobalBuffer(P.FarProbeArrays, Arrays);
            Shader.SetGlobalBuffer(P.FarProbeCounts, Counts);
            Shader.SetGlobalBuffer(P.FarProbeEntries, Entries);
            Shader.SetGlobalBuffer(P.FarProbeGrid, Grid);
            Shader.SetGlobalBuffer(P.FarProbeRequestMap, RequestMap);

            if (SurfelMap != null)
                Shader.SetGlobalBuffer(P.FarProbeSurfelMap, SurfelMap);

            Shader.SetGlobalBuffer(P.FarProbeArraysRW, Arrays);
            Shader.SetGlobalBuffer(P.FarProbeCountsRW, Counts);
            Shader.SetGlobalBuffer(P.FarProbeEntriesRW, Entries);
            Shader.SetGlobalBuffer(P.FarProbeGridRW, Grid);
            Shader.SetGlobalBuffer(P.FarProbeRequestMapRW, RequestMap);

            if (SurfelMap != null)
                Shader.SetGlobalBuffer(P.FarProbeSurfelMapRW, SurfelMap);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeFloatParam(kernel.Shader, P.FarProbeCell, CellSize);
            cmd.SetComputeIntParam(kernel.Shader, P.FarProbeCascadeCount, CascadeCount);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.FarProbeCascadeOffsets, _cascade_offsets);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.FarProbeCascadeOrigins, _cascade_origins);
            cmd.SetComputeVectorParam(kernel.Shader, P.ReferencePosition, ReferencePosition);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeArrays, Arrays);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeCounts, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeEntries, Entries);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeGrid, Grid);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeRequestMap, RequestMap);

            if (SurfelMap != null)
                cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeSurfelMap, SurfelMap);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeArraysRW, Arrays);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeCountsRW, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeEntriesRW, Entries);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeGridRW, Grid);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeRequestMapRW, RequestMap);

            if (SurfelMap != null)
                cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.FarProbeSurfelMapRW, SurfelMap);
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

            var origin = new Vector3Int(center.x - Defines.FAR_PROBE_CASCADE_DIM / 2,
                                        center.y - Defines.FAR_PROBE_CASCADE_DIM / 2,
                                        center.z - Defines.FAR_PROBE_CASCADE_DIM / 2);

            return origin;
        }

        public void SwapRadianceAtlas()
        {
            var t = RadianceAtlas;
            RadianceAtlas = RadianceAtlas2;
            RadianceAtlas2 = t;
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

            if (AcceptArguments != null)
            {
                AcceptArguments.Release();
                AcceptArguments = null;
            }

            if (AtlasArguments != null)
            {
                AtlasArguments.Release();
                AtlasArguments = null;
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
        }
    }
}