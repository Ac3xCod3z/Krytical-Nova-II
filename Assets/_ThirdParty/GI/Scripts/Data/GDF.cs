using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class GDF
    {
        public int CascadeCount;

        public bool IsDirty;

        public RenderTexture SDF;
        public RenderTexture Map;

        public ComputeBuffer UpdateCount;
        public ComputeBuffer Updates;
        public ComputeBuffer Changes;
        public ComputeBuffer Hashes2;
        public ComputeBuffer Hashes;
        public ComputeBuffer Arguments;

        public Vector3Int[] CascadeOffsets;

        public int[] CascadeIndices;
        public int ScrollIndex;

        public Vector3 ReferencePosition;

        private Vector4[] _cascade_origins;
        private Vector4[] _cascade_positions;

        private Vector3Int[] _previous_cascade_origins;

        private bool _r32f;

        public GDF(int cascade_count)
        {
            // assume Android does not handle R8_Unorm with random writes...
            if (SystemInfo.graphicsDeviceVersion.StartsWith("OpenGL ES 3."))
                _r32f = true;
            else
                _r32f = false;

            CascadeCount = cascade_count;

            CascadeOffsets = new Vector3Int[cascade_count];
            IsDirty = true;

            CascadeIndices = new int[Defines.GDF_MAX_CASCADES]; // use MAX_CASCADES for easier read for shader parameters

            for (int i = 0; i < CascadeIndices.Length; i++)
                CascadeIndices[i] = i;

            ScrollIndex = cascade_count;

            var cell_count = Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM;

            Arguments = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
            Changes = new ComputeBuffer(cell_count, 4, ComputeBufferType.Default);
            Hashes = new ComputeBuffer(cell_count, 4, ComputeBufferType.Default);
            Hashes2 = new ComputeBuffer(cell_count, 4, ComputeBufferType.Default);
            UpdateCount = new ComputeBuffer(1, 4, ComputeBufferType.Raw);
            Updates = new ComputeBuffer(Defines.MAX_GDF_UPDATES, 4, ComputeBufferType.Raw);

            _previous_cascade_origins = new Vector3Int[cascade_count];

            _cascade_origins = new Vector4[Defines.GDF_MAX_CASCADES];
            _cascade_positions = new Vector4[Defines.GDF_MAX_CASCADES];

            {
                var desc = new RenderTextureDescriptor();
                desc.width = Defines.GDF_CASCADE_DIM * (CascadeCount + 1); // +1 for scrolling
                desc.height = Defines.GDF_CASCADE_DIM;
                desc.volumeDepth = Defines.GDF_CASCADE_DIM;
                desc.dimension = TextureDimension.Tex3D;
                desc.msaaSamples = 1;
                desc.enableRandomWrite = true;

                if (_r32f)
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat;
                else
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;

                SDF = new RenderTexture(desc);
            }

            {
                var desc = new RenderTextureDescriptor();
                desc.width = Defines.GDF_CASCADE_DIM * (CascadeCount + 1); // +1 for scrolling
                desc.height = Defines.GDF_CASCADE_DIM;
                desc.volumeDepth = Defines.GDF_CASCADE_DIM;
                desc.dimension = TextureDimension.Tex3D;
                desc.msaaSamples = 1;
                desc.enableRandomWrite = true;
                desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt;

                Map = new RenderTexture(desc);
            }
        }

        public void Release()
        {
            if (UpdateCount != null)
            {
                UpdateCount.Release();
                UpdateCount = null;
            }

            if (Updates != null)
            {
                Updates.Release();
                Updates = null;
            }

            if (Changes != null)
            {
                Changes.Release();
                Changes = null;
            }

            if (Hashes2 != null)
            {
                Hashes2.Release();
                Hashes2 = null;
            }

            if (Hashes != null)
            {
                Hashes.Release();
                Hashes = null;
            }

            if (Arguments != null)
            {
                Arguments.Release();
                Arguments = null;
            }

            if (SDF != null)
            {
                Object.DestroyImmediate(SDF);
                SDF = null;
            }

            if (Map != null)
            {
                Object.DestroyImmediate(Map);
                Map = null;
            }
        }

        public void RequestScroll(int cascade, out int write, out int read)
        {
            write = ScrollIndex;
            read = CascadeIndices[cascade];

            CascadeIndices[cascade] = write;
            ScrollIndex = read;
        }

        public void Update(Vector3 reference_position)
        {
            for (int i = 0; i < CascadeCount; i++)
            {
                var origin = Origin(reference_position, i);
                _cascade_origins[i] = new Vector4(origin.x, origin.y, origin.z);

                CascadeOffsets[i] = origin - _previous_cascade_origins[i];
                _previous_cascade_origins[i] = origin;

                float cell_size = Defines.GDF_SDF_CELL_DIM * (1 << i);

                _cascade_positions[i] = new Vector4(origin.x * cell_size, origin.y * cell_size, origin.z * cell_size);
            }

            ReferencePosition = reference_position;

            {
                var t = Hashes;
                Hashes = Hashes2;
                Hashes2 = t;
            }
        }

        public void Parameters(Material material)
        {
            material.SetInt(P.GDFCascadeCount, CascadeCount);
            material.SetVectorArray(P.GDFCascadeOrigins, _cascade_origins);
            material.SetVectorArray(P.GDFCascadePositions, _cascade_positions);
            material.SetVector(P.GDFCascades, new Vector4(CascadeIndices[0], CascadeIndices[1], CascadeIndices[2], CascadeIndices[3]));
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeVectorParam(kernel.Shader, P.ReferencePosition, ReferencePosition);

            cmd.SetComputeIntParam(kernel.Shader, P.GDFCascadeCount, CascadeCount);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.GDFCascadeOrigins, _cascade_origins);
            cmd.SetComputeVectorArrayParam(kernel.Shader, P.GDFCascadePositions, _cascade_positions);
            cmd.SetComputeIntParams(kernel.Shader, P.GDFCascades, CascadeIndices[0], CascadeIndices[1], CascadeIndices[2], CascadeIndices[3]);
        }

        public void Textures(Material material)
        {
            material.SetTexture(P.GDFSDF, SDF);
            material.SetTexture(P.GDFMap, Map);
        }

        public void Textures(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeTextureParam(kernel.Shader, kernel.Index, P.GDFSDF, SDF);
            cmd.SetComputeTextureParam(kernel.Shader, kernel.Index, P.GDFMap, Map);
        }

        public Vector3Int Origin(Vector3 reference_position, int cascade)
        {
            float cell_size = Defines.GDF_SDF_CELL_DIM * (1 << cascade);

            var center = new Vector3Int(Mathf.FloorToInt(reference_position.x / cell_size),
                                        Mathf.FloorToInt(reference_position.y / cell_size),
                                        Mathf.FloorToInt(reference_position.z / cell_size));

            var origin = new Vector3Int(center.x - Defines.GDF_CASCADE_DIM / 2,
                                        center.y - Defines.GDF_CASCADE_DIM / 2,
                                        center.z - Defines.GDF_CASCADE_DIM / 2);

            return origin;
        }
    }
}