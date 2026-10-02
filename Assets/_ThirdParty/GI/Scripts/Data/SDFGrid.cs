using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class SDFGrid
    {
        public ComputeBuffer Arguments;
        public ComputeBuffer Cells;
        public ComputeBuffer Cursor;
        public ComputeBuffer Writes;

        public Vector3Int Offset;

        private Vector3 _base_position;
        private bool _has_previous;
        private Vector3Int _previous_origin;

        public SDFGrid()
        {
            var cell_count = Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM * Defines.GDF_GRID_DIM;

            Arguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            Cells = new ComputeBuffer(Defines.SDF_GRID_SIZE / 4, 4, ComputeBufferType.Raw);
            Cursor = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            Writes = new ComputeBuffer(Defines.SDF_MAX_INSTANCES, sizeof(uint), ComputeBufferType.Raw);

            Offset = new Vector3Int(0, 0, 0);
        }

        public void Release()
        {
            if (Arguments != null)
            {
                Arguments.Release();
                Arguments = null;
            }

            if (Cells != null)
            {
                Cells.Release();
                Cells = null;
            }

            if (Cursor != null)
            {
                Cursor.Release();
                Cursor = null;
            }

            if (Writes != null)
            {
                Writes.Release();
                Writes = null;
            }
        }

        public void Update(Vector3 reference_position)
        {
            var origin = Util.GridOriginCoord(reference_position, Defines.GDF_CELL_DIM, Defines.GDF_GRID_DIM);

            if (_has_previous)
                Offset = origin - _previous_origin;
            else
                _has_previous = true;

            _previous_origin = origin;

            _base_position = Util.GridCoordToPosition(origin, Defines.GDF_CELL_DIM);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeVectorParam(kernel.Shader, P.GDFBasePosition, _base_position);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SDFGridCells, Cells);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SDFGridCursor, Cursor);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SDFGridWrites, Writes);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SDFGridCellsRW, Cells);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SDFGridCursorRW, Cursor);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.SDFGridWritesRW, Writes);
        }
    }
}