using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class Traces
    {
        public ComputeBuffer Array;
        public ComputeBuffer Counts;
        public ComputeBuffer Hits;

        private int _ensured_screen;

        public Traces(bool screen_probes)
        {
            if (screen_probes)
                _ensured_screen = 1024 * 1024;

            var total_count = Defines.TRACE_ARRAY_SCREEN + _ensured_screen;

            Array = new ComputeBuffer(total_count, sizeof(uint), ComputeBufferType.Raw);
            Counts = new ComputeBuffer(Defines.TRACE_COUNT_ALL, sizeof(uint), ComputeBufferType.Raw);
            Hits = new ComputeBuffer(total_count, Defines.HIT_STRIDE, ComputeBufferType.Raw);
        }

        public void Parameters(CommandBuffer cmd, Kernel kernel)
        {
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.TraceArray, Array);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.TraceCounts, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.TraceHits, Hits);

            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.TraceArrayRW, Array);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.TraceCountsRW, Counts);
            cmd.SetComputeBufferParam(kernel.Shader, kernel.Index, P.TraceHitsRW, Hits);
        }

        public void EnsureScreen(int count)
        {
            if (count <= _ensured_screen)
                return;

            if (Array != null)
            {
                Array.Release();
                Array = null;
            }

            if (Hits != null)
            {
                Hits.Release();
                Hits = null;
            }

            _ensured_screen = count;

            var total_count = Defines.TRACE_ARRAY_SCREEN + _ensured_screen;

            Array = new ComputeBuffer(total_count, sizeof(uint), ComputeBufferType.Raw);
            Hits = new ComputeBuffer(total_count, Defines.HIT_STRIDE, ComputeBufferType.Raw);
        }

        public void Release()
        {
            if (Array != null)
            {
                Array.Release();
                Array = null;
            }

            if (Counts != null)
            {
                Counts.Release();
                Counts = null;
            }

            if (Hits != null)
            {
                Hits.Release();
                Hits = null;
            }
        }
    }
}