using System;
using KryticalNova.Player.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// The bodies a swing could catch, found by place (perf clean-ups round 2, chunk 7). <see cref="MeleeResolveServerSystem"/> used to
    /// gather every body of the world for every due swing; now it fills this once per tick (on the first due swing) and each swing asks
    /// only the cells around the attacker. Each body is filed under the ground box of its whole position ring (every sample the swing
    /// may test, <see cref="PositionHistory"/>), widened by its capsule's radius, so no body the blade could reach is ever left out:
    /// <see cref="MeleeRules.Resolve"/> still makes the exact test on what is gathered. Indexes come back in the order the bodies were
    /// added, so the hits land in the same order as before.
    /// </summary>
    public sealed class MeleeBystanderGrid : IDisposable
    {
        /// <summary>The cell side, mm: a little more than the longest reach plus a capsule, so a swing reads about 4 cells.</summary>
        public const int CellMm = 4000;
        /// <summary>A reach wider than this many cells per side reads every box instead (never in the game today).</summary>
        const int MaxCellSpan = 16;

        NativeParallelMultiHashMap<int2, int> m_Cells;
        NativeList<int4> m_Boxes;     // min x, min z, max x, max z (mm), widened by the capsule's radius
        NativeList<bool> m_Filed;     // false: an empty ring (never testable, never gathered)
        NativeList<int> m_Stamp;
        int m_StampNow;

        public MeleeBystanderGrid(int capacity = 64)
        {
            m_Cells = new NativeParallelMultiHashMap<int2, int>(capacity * 2, Allocator.Persistent);
            m_Boxes = new NativeList<int4>(capacity, Allocator.Persistent);
            m_Filed = new NativeList<bool>(capacity, Allocator.Persistent);
            m_Stamp = new NativeList<int>(capacity, Allocator.Persistent);
        }

        /// <summary>Bodies added since the last <see cref="Clear"/>.</summary>
        public int Count => m_Boxes.Length;

        public void Clear()
        {
            m_Cells.Clear();
            m_Boxes.Clear();
            m_Filed.Clear();
            m_Stamp.Clear();
            m_StampNow = 0;
        }

        /// <summary>Files the next body (its index is <see cref="Count"/> before the call) by its ring and capsule.</summary>
        public void Add(in PositionHistory ring, HitProxy proxy)
        {
            int index = m_Boxes.Length;
            m_Stamp.Add(0);
            int n = ring.Samples.Length;
            if (n == 0) { m_Boxes.Add(default); m_Filed.Add(false); return; }
            int3 f = ring.Samples[0].FeetMm;
            int4 box = new int4(f.x, f.z, f.x, f.z);
            for (int i = 1; i < n; i++)
            {
                f = ring.Samples[i].FeetMm;
                box = new int4(math.min(box.x, f.x), math.min(box.y, f.z), math.max(box.z, f.x), math.max(box.w, f.z));
            }
            int r = math.max(0, proxy.RadiusMm);
            box = new int4(Widen(box.x, -r), Widen(box.y, -r), Widen(box.z, r), Widen(box.w, r));
            m_Boxes.Add(box);
            m_Filed.Add(true);
            int2 lo = Cell(box.x, box.y), hi = Cell(box.z, box.w);
            if (hi.x - lo.x > MaxCellSpan || hi.y - lo.y > MaxCellSpan) { m_Cells.Add(new int2(int.MinValue, int.MinValue), index); return; }
            for (int cx = lo.x; cx <= hi.x; cx++)
                for (int cz = lo.y; cz <= hi.y; cz++)
                    m_Cells.Add(new int2(cx, cz), index);
        }

        /// <summary>
        /// Every body whose box comes within <paramref name="reachMm"/> of the attacker's feet on the ground plane (a superset of what
        /// <see cref="PositionHistoryRules.WithinReach"/> can pass), ascending, into <paramref name="into"/> (cleared first).
        /// </summary>
        public void Gather(int3 attackerFeetMm, int reachMm, NativeList<int> into)
        {
            into.Clear();
            int reach = math.max(0, reachMm);
            if (++m_StampNow == int.MaxValue) { for (int i = 0; i < m_Stamp.Length; i++) m_Stamp[i] = 0; m_StampNow = 1; }
            int ax = attackerFeetMm.x, az = attackerFeetMm.z;
            int2 lo = Cell(Widen(ax, -reach), Widen(az, -reach)), hi = Cell(Widen(ax, reach), Widen(az, reach));
            if (hi.x - lo.x > MaxCellSpan || hi.y - lo.y > MaxCellSpan)
            {
                for (int i = 0; i < m_Boxes.Length; i++) if (m_Filed[i] && Near(m_Boxes[i], ax, az, reach)) into.Add(i);
                return;
            }
            for (int cx = lo.x; cx <= hi.x; cx++)
                for (int cz = lo.y; cz <= hi.y; cz++)
                    Take(new int2(cx, cz), ax, az, reach, into);
            Take(new int2(int.MinValue, int.MinValue), ax, az, reach, into);   // the bodies too spread out for cells
            into.Sort();
        }

        void Take(int2 cell, int ax, int az, int reach, NativeList<int> into)
        {
            if (!m_Cells.TryGetFirstValue(cell, out int i, out NativeParallelMultiHashMapIterator<int2> it)) return;
            do
            {
                if (m_Stamp[i] == m_StampNow) continue;
                m_Stamp[i] = m_StampNow;
                if (Near(m_Boxes[i], ax, az, reach)) into.Add(i);
            } while (m_Cells.TryGetNextValue(out i, ref it));
        }

        static bool Near(int4 box, int ax, int az, int reach) =>
            (long)ax >= (long)box.x - reach && (long)ax <= (long)box.z + reach && (long)az >= (long)box.y - reach && (long)az <= (long)box.w + reach;

        static int Widen(int v, int by) => (int)math.clamp((long)v + by, int.MinValue + 1, int.MaxValue - 1);

        static int2 Cell(int x, int z) => new int2(FloorDiv(x, CellMm), FloorDiv(z, CellMm));

        static int FloorDiv(int v, int d) => (int)math.floor((double)v / d);

        /// <summary>Perf round 4: the grid for a parallel job (read only; <see cref="ReadOnlyView.Gather"/> needs no stamps).</summary>
        public ReadOnlyView AsReadOnly() => new ReadOnlyView { Cells = m_Cells, Boxes = m_Boxes.AsArray(), Filed = m_Filed.AsArray() };

        /// <summary>
        /// <see cref="Gather"/> without writing anything: the same bodies, ascending (a body filed in several cells is met several
        /// times and kept once, where <see cref="Gather"/> skips it by its stamp).
        /// </summary>
        public struct ReadOnlyView
        {
            [ReadOnly] public NativeParallelMultiHashMap<int2, int> Cells;
            [ReadOnly] public NativeArray<int4> Boxes;
            [ReadOnly] public NativeArray<bool> Filed;

            public void Gather(int3 attackerFeetMm, int reachMm, NativeList<int> into)
            {
                into.Clear();
                int reach = math.max(0, reachMm);
                int ax = attackerFeetMm.x, az = attackerFeetMm.z;
                int2 lo = Cell(Widen(ax, -reach), Widen(az, -reach)), hi = Cell(Widen(ax, reach), Widen(az, reach));
                if (hi.x - lo.x > MaxCellSpan || hi.y - lo.y > MaxCellSpan)
                {
                    for (int i = 0; i < Boxes.Length; i++) if (Filed[i] && Near(Boxes[i], ax, az, reach)) into.Add(i);
                    return;
                }
                for (int cx = lo.x; cx <= hi.x; cx++)
                    for (int cz = lo.y; cz <= hi.y; cz++)
                        Take(new int2(cx, cz), ax, az, reach, into);
                Take(new int2(int.MinValue, int.MinValue), ax, az, reach, into);
                into.Sort();
                int kept = 0;
                for (int i = 0; i < into.Length; i++) if (i == 0 || into[i] != into[i - 1]) into[kept++] = into[i];
                into.ResizeUninitialized(kept);
            }

            void Take(int2 cell, int ax, int az, int reach, NativeList<int> into)
            {
                if (!Cells.TryGetFirstValue(cell, out int i, out NativeParallelMultiHashMapIterator<int2> it)) return;
                do { if (Near(Boxes[i], ax, az, reach)) into.Add(i); } while (Cells.TryGetNextValue(out i, ref it));
            }
        }

        public void Dispose()
        {
            if (m_Cells.IsCreated) m_Cells.Dispose();
            if (m_Boxes.IsCreated) m_Boxes.Dispose();
            if (m_Filed.IsCreated) m_Filed.Dispose();
            if (m_Stamp.IsCreated) m_Stamp.Dispose();
        }
    }
}
