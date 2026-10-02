using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class Surfels
    {
        public bool NeedReset;

        public ComputeBuffer Arrays;
        public ComputeBuffer Counts;
        public ComputeBuffer Entries;
        public ComputeBuffer Grid;
        public ComputeBuffer Radiance;
        public ComputeBuffer RequestMap;
        public ComputeBuffer Requests;

        public ComputeBuffer AcceptArguments;
        public ComputeBuffer CollectArguments;
        public ComputeBuffer CreateArguments;
        public ComputeBuffer MaintainArguments;
        public ComputeBuffer UpdateArguments;

        public Vector3 ReferencePosition;

        private Vector4[] _cascade_origins;
        private Vector4[] _cascade_offsets;

        public Surfels()
        {
            Arrays = new ComputeBuffer(Defines.SURFEL_ARRAY_ALL, sizeof(uint), ComputeBufferType.Raw);
            Counts = new ComputeBuffer(Defines.SURFEL_COUNT_ALL, sizeof(uint), ComputeBufferType.Raw);
            Entries = new ComputeBuffer(Defines.SURFEL_MAX_ENTRIES, Defines.SURFEL_ENTRY_STRIDE, ComputeBufferType.Raw);
            Grid = new ComputeBuffer(Defines.SURFEL_CELL_COUNT, sizeof(uint), ComputeBufferType.Raw);
            Radiance = new ComputeBuffer(Defines.SURFEL_MAX_ENTRIES, Defines.RADIANCE_STRIDE, ComputeBufferType.Raw);
            RequestMap = new ComputeBuffer(Defines.SURFEL_CELL_COUNT, sizeof(uint), ComputeBufferType.Raw);
            Requests = new ComputeBuffer(Defines.SURFEL_MAX_REQUESTS, sizeof(uint), ComputeBufferType.Raw);

            AcceptArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            CollectArguments = new ComputeBuffer(8, sizeof(uint), ComputeBufferType.IndirectArguments);
            CreateArguments = new ComputeBuffer(8, sizeof(uint), ComputeBufferType.IndirectArguments);
            MaintainArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            UpdateArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);

            NeedReset = true;

            _cascade_origins = new Vector4[Defines.SURFEL_CASCADE_COUNT];
            _cascade_offsets = new Vector4[Defines.SURFEL_CASCADE_COUNT];
        }

        public void Update(Vector3 reference_position)
        {
            for (int i = 0; i < Defines.SURFEL_CASCADE_COUNT; i++)
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
            Shader.SetGlobalFloat(P.SurfelCell, Defines.SURFEL_CELL_DIM);
            Shader.SetGlobalInt(P.SurfelCascadeCount, Defines.SURFEL_CASCADE_COUNT);
            Shader.SetGlobalVectorArray(P.SurfelCascadeOffsets, _cascade_offsets);
            Shader.SetGlobalVectorArray(P.SurfelCascadeOrigins, _cascade_origins);

            Shader.SetGlobalBuffer(P.SurfelArrays, Arrays);
            Shader.SetGlobalBuffer(P.SurfelCounts, Counts);
            Shader.SetGlobalBuffer(P.SurfelEntries, Entries);
            Shader.SetGlobalBuffer(P.SurfelGrid, Grid);
            Shader.SetGlobalBuffer(P.SurfelRadiance, Radiance);
            Shader.SetGlobalBuffer(P.SurfelRequestMap, RequestMap);
            Shader.SetGlobalBuffer(P.SurfelRequests, Requests);

            Shader.SetGlobalBuffer(P.SurfelArraysRW, Arrays);
            Shader.SetGlobalBuffer(P.SurfelCountsRW, Counts);
            Shader.SetGlobalBuffer(P.SurfelEntriesRW, Entries);
            Shader.SetGlobalBuffer(P.SurfelGridRW, Grid);
            Shader.SetGlobalBuffer(P.SurfelRadianceRW, Radiance);
            Shader.SetGlobalBuffer(P.SurfelRequestMapRW, RequestMap);
            Shader.SetGlobalBuffer(P.SurfelRequestsRW, Requests);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeFloatParam(kernel.Shader, P.SurfelCell, Defines.SURFEL_CELL_DIM);
            cmd.SetComputeIntParam(kernel.Shader, P.SurfelCascadeCount, Defines.SURFEL_CASCADE_COUNT);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.SurfelCascadeOffsets, _cascade_offsets);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.SurfelCascadeOrigins, _cascade_origins);
            cmd.SetComputeVectorParam(kernel.Shader, P.ReferencePosition, ReferencePosition);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelArrays, Arrays);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelCounts, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelEntries, Entries);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelGrid, Grid);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelRadiance, Radiance);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelRequestMap, RequestMap);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelRequests, Requests);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelArraysRW, Arrays);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelCountsRW, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelEntriesRW, Entries);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelGridRW, Grid);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelRadianceRW, Radiance);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelRequestMapRW, RequestMap);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SurfelRequestsRW, Requests);
        }

        public Vector3Int Cell(Vector3 position, int cascade)
        {
            float cell_size = Defines.SURFEL_CELL_DIM * (1 << cascade);

            return new Vector3Int(Mathf.FloorToInt(position.x / cell_size),
                                  Mathf.FloorToInt(position.x / cell_size),
                                  Mathf.FloorToInt(position.x / cell_size));
        }

        public Vector3Int Origin(Vector3 reference_position, int cascade)
        {
            float cell_size = Defines.SURFEL_CELL_DIM * (1 << cascade);

            var center = new Vector3Int(Mathf.FloorToInt(reference_position.x / cell_size),
                                        Mathf.FloorToInt(reference_position.y / cell_size),
                                        Mathf.FloorToInt(reference_position.z / cell_size));

            var origin = new Vector3Int(center.x - Defines.SURFEL_CASCADE_DIM / 2,
                                        center.y - Defines.SURFEL_CASCADE_DIM / 2,
                                        center.z - Defines.SURFEL_CASCADE_DIM / 2);

            return origin;
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

            if (Radiance != null)
            {
                Radiance.Release();
                Radiance = null;
            }

            if (RequestMap != null)
            {
                RequestMap.Release();
                RequestMap = null;
            }

            if (Requests != null)
            {
                Requests.Release();
                Requests = null;
            }

            if (AcceptArguments != null)
            {
                AcceptArguments.Release();
                AcceptArguments = null;
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

            if (MaintainArguments != null)
            {
                MaintainArguments.Release();
                MaintainArguments = null;
            }

            if (UpdateArguments != null)
            {
                UpdateArguments.Release();
                UpdateArguments = null;
            }
        }
    }
}