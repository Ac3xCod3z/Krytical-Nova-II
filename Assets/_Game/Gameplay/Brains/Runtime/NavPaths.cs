using System;
using Unity.AI.Navigation.LowLevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Brains
{
    /// <summary>
    /// Which path backend the server's brains use (singleton; none = the straight line, as in the tests). NavWorld (M5.2 pick A, M5.4b)
    /// searches the map's baked NavMesh: <see cref="AgentTypeId"/> is the NavMesh agent type, <see cref="Extents"/> the half size of the
    /// box a point is mapped onto the mesh with (at least 2 m tall: M5.2).
    /// </summary>
    public struct PathBackend : IComponentData
    {
        public byte Kind;
        public int AgentTypeId;
        public float3 Extents;
    }

    public static class PathBackendKind
    {
        public const byte StraightLine = 0, NavWorld = 1;
    }

    /// <summary>
    /// Path searches on the NavMesh with Unity's NavWorld (M5.4b), as proven by the M5.2 spike: map both ends, search the polygon
    /// corridor within a node budget, turn it into corners with a funnel that also unfolds NavMeshLinks. One job does a batch of
    /// searches with its own query buffer and scratch; <see cref="NavPaths"/> owns them (create once, dispose with the system).
    /// </summary>
    public struct NavPaths : IDisposable
    {
        public const int MaxPath = 2048, MaxCorners = 128;
        NavQueryBuffer m_Buffer;
        NativeArray<NavNode> m_Path;
        NativeArray<float3> m_L, m_R, m_UL, m_UR;
        NativeArray<byte> m_Kind, m_CSide;
        NativeArray<int> m_CIdx;

        public bool IsCreated => m_Path.IsCreated;

        public static NavPaths Create(NavWorld world, int nodeBudget)
        {
            return new NavPaths
            {
                m_Buffer = new NavQueryBuffer(world, Allocator.Persistent, math.max(64, nodeBudget)),
                m_Path = new NativeArray<NavNode>(MaxPath, Allocator.Persistent),
                m_L = new NativeArray<float3>(MaxPath + 1, Allocator.Persistent),
                m_R = new NativeArray<float3>(MaxPath + 1, Allocator.Persistent),
                m_UL = new NativeArray<float3>(MaxPath + 1, Allocator.Persistent),
                m_UR = new NativeArray<float3>(MaxPath + 1, Allocator.Persistent),
                m_Kind = new NativeArray<byte>(MaxPath + 1, Allocator.Persistent),
                m_CSide = new NativeArray<byte>(MaxCorners, Allocator.Persistent),
                m_CIdx = new NativeArray<int>(MaxCorners, Allocator.Persistent),
            };
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            m_Buffer.Dispose();
            m_Path.Dispose(); m_L.Dispose(); m_R.Dispose(); m_UL.Dispose(); m_UR.Dispose();
            m_Kind.Dispose(); m_CSide.Dispose(); m_CIdx.Dispose();
        }

        /// <summary>
        /// Searches every pair <c>Starts[i]</c> to <c>Ends[i]</c>: <c>Counts[i]</c> corners from <c>Corners[i * MaxCorners]</c> (the
        /// first is the start), 0 when there is no path within <paramref name="nodeBudget"/> nodes or an end is off the mesh. Runs as a
        /// Burst job and waits for it.
        /// </summary>
        public void Search(NavWorld world, in PathBackend backend, int nodeBudget, NativeArray<float3> starts, NativeArray<float3> ends,
                           NativeArray<float3> corners, NativeArray<int> counts)
        {
            var job = new NavPathJob
            {
                World = world, Buffer = m_Buffer, AgentTypeId = backend.AgentTypeId, Extents = backend.Extents, NodeBudget = nodeBudget,
                Starts = starts, Ends = ends, Corners = corners, CornerCounts = counts,
                Path = m_Path, L = m_L, R = m_R, UL = m_UL, UR = m_UR, Kind = m_Kind, CSide = m_CSide, CIdx = m_CIdx,
            };
            JobHandle handle = job.Schedule();
            world.AddDependency(handle);
            handle.Complete();
        }
    }

    /// <summary>The M5.2 spike's path job (its final pass), with a node budget: a search that needs more nodes fails.</summary>
    [BurstCompile]
    public struct NavPathJob : IJob
    {
        const float ColinearEps = 1e-3f;
        [ReadOnly] public NavWorld World;
        public NavQueryBuffer Buffer;
        public int AgentTypeId;
        public float3 Extents;
        public int NodeBudget;
        [ReadOnly] public NativeArray<float3> Starts, Ends;
        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> Corners;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int> CornerCounts;
        public NativeArray<NavNode> Path;
        public NativeArray<float3> L, R, UL, UR;
        public NativeArray<byte> Kind, CSide;
        public NativeArray<int> CIdx;

        public void Execute()
        {
            for (int q = 0; q < Starts.Length; q++) Run(q);
        }

        void Run(int q)
        {
            CornerCounts[q] = 0;
            NavLocation s = World.MapLocation(Starts[q], Extents, AgentTypeId);
            NavLocation e = World.MapLocation(Ends[q], Extents, AgentTypeId);
            if (!World.IsValid(s) || !World.IsValid(e)) return;
            NavQueryStatus st = World.BeginFindPath(Buffer, s, e);
            int visited = 0;
            while ((st & NavQueryStatus.InProgress) != 0 && visited < NodeBudget)
            {
                st = World.ContinueFindPath(Buffer, math.min(256, NodeBudget - visited), out int step);
                visited += math.max(1, step);
            }
            if ((st & NavQueryStatus.Success) == 0 || (st & NavQueryStatus.PartialResult) != 0) return;
            // a corridor that stops short of the end (the end is cut off from the start) is no path: joining it to the end would
            // walk through the wall (found by the M5.4b probe)
            NavQueryStatus end = World.EndFindPath(Buffer, out int pathSize);
            if ((end & NavQueryStatus.Success) == 0 || (end & NavQueryStatus.PartialResult) != 0) return;
            int n = World.GetResultFromFindPath(Buffer, Path.Slice(0, math.min(pathSize, NavPaths.MaxPath)));
            if (n == 0 || Path[n - 1] != e.node) return;
            // portals: 0 = start point, k = between Path[k-1] and Path[k], n = end point
            L[0] = s.position; R[0] = s.position;
            for (int k = 1; k < n; k++)
            {
                World.GetPortalPoints(Path[k - 1], Path[k], out Vector3 l, out Vector3 r);
                L[k] = l; R[k] = r;
            }
            L[n] = e.position; R[n] = e.position;
            int cc = 0;
            RunUnfolded(n, q * NavPaths.MaxCorners, ref cc);
            CornerCounts[q] = cc;
        }

        // A NavMeshLink is a lane: the corridor after a link is slid (x, z) so the exit edge lies straight ahead of the entry edge at the
        // link's 3D length; one funnel runs over the unfolded strip; where a straight stretch crosses a link's entry and exit edge becomes
        // a corner (M5.2: exact on polygons, 22/40 link pairs within 0.1 m of CalculatePath).
        void RunUnfolded(int n, int cBase, ref int cc)
        {
            for (int k = 0; k <= n; k++) Kind[k] = 0;
            for (int k = 1; k + 1 < n; k++)
            {
                if (World.GetNodeType(Path[k]) != NavNodeType.Link) continue;
                Kind[k] = 1; Kind[k + 1] = 2;
                float3 me = (L[k] + R[k]) * 0.5f, mx = (L[k + 1] + R[k + 1]) * 0.5f, dir = mx - me;
                if (math.lengthsq(dir.xz) < 1e-8f) dir = (L[k + 2] + R[k + 2]) * 0.5f - (L[k - 1] + R[k - 1]) * 0.5f;
                if (Cross2(dir, L[k] - me) < 0) { float3 t = L[k]; L[k] = R[k]; R[k] = t; }
                float same = math.distance(L[k + 1].xz, L[k].xz) + math.distance(R[k + 1].xz, R[k].xz);
                float swap = math.distance(L[k + 1].xz, R[k].xz) + math.distance(R[k + 1].xz, L[k].xz);
                if (swap < same) { float3 t = L[k + 1]; L[k + 1] = R[k + 1]; R[k + 1] = t; }
            }
            float2 off = float2.zero;
            for (int k = 0; k <= n; k++)
            {
                if (Kind[k] == 2)
                {
                    float3 me = (L[k - 1] + R[k - 1]) * 0.5f, mx = (L[k] + R[k]) * 0.5f;
                    float2 d = mx.xz - me.xz;
                    float len = math.length(mx - me);
                    d = math.lengthsq(d) > 1e-8f ? math.normalize(d) : float2.zero;
                    off = me.xz + off + d * len - mx.xz;
                }
                UL[k] = new float3(L[k].x + off.x, 0, L[k].z + off.y);
                UR[k] = new float3(R[k].x + off.x, 0, R[k].z + off.y);
            }
            // simple stupid funnel (Mononen / Detour findStraightPath) over the unfolded portals -> (portal index, side)
            int m = 0; CIdx[m] = 0; CSide[m] = 0; m++;
            float3 apex = UL[0], pl = UL[0], pr = UR[0];
            int apexIndex = 0, leftIndex = 0, rightIndex = 0;
            for (int i = 1; i <= n; i++)
            {
                float3 left = UL[i], right = UR[i];
                if (TriArea2D(apex, pr, right) <= 0f)
                {
                    if (Eq(apex, pr) || TriArea2D(apex, pl, right) > -ColinearEps) { pr = right; rightIndex = i; }
                    else
                    {
                        apex = pl; apexIndex = leftIndex;
                        if (m < NavPaths.MaxCorners) { CIdx[m] = leftIndex; CSide[m] = 0; m++; }
                        pl = apex; pr = apex; rightIndex = apexIndex; i = apexIndex; continue;
                    }
                }
                if (TriArea2D(apex, pl, left) >= 0f)
                {
                    if (Eq(apex, pl) || TriArea2D(apex, pr, left) < ColinearEps) { pl = left; leftIndex = i; }
                    else
                    {
                        apex = pr; apexIndex = rightIndex;
                        if (m < NavPaths.MaxCorners) { CIdx[m] = rightIndex; CSide[m] = 1; m++; }
                        pl = apex; pr = apex; leftIndex = apexIndex; i = apexIndex; continue;
                    }
                }
            }
            if (m < NavPaths.MaxCorners) { CIdx[m] = n; CSide[m] = 0; m++; }
            // back to real positions, adding the link edge crossings
            for (int j = 0; j < m; j++)
            {
                int ia = CIdx[j];
                Add(cBase, ref cc, CSide[j] == 0 ? L[ia] : R[ia]);
                if (j + 1 >= m) break;
                int ib = CIdx[j + 1];
                float3 pa = CSide[j] == 0 ? UL[ia] : UR[ia], pb = CSide[j + 1] == 0 ? UL[ib] : UR[ib];
                for (int k = ia; k < ib; k++)
                {
                    if (Kind[k] != 1) continue;
                    if (k != ia) Add(cBase, ref cc, EdgeCrossing(pa, pb, k));
                    if (k + 1 != ib) Add(cBase, ref cc, EdgeCrossing(pa, pb, k + 1));
                }
            }
        }

        float3 EdgeCrossing(float3 p, float3 q, int k)
        {
            float3 r = q - p, e = UR[k] - UL[k];
            float den = Cross2(e, r);
            float t = math.abs(den) < 1e-9f ? 0.5f : Cross2(p - UL[k], r) / den;
            return math.lerp(L[k], R[k], math.saturate(t));
        }

        void Add(int cBase, ref int cc, float3 p)
        {
            if (cc > 0 && math.distancesq(Corners[cBase + cc - 1], p) < 1e-8f) return;
            if (cc < NavPaths.MaxCorners) { Corners[cBase + cc] = p; cc++; }
        }

        static float Cross2(float3 u, float3 v) => u.x * v.z - u.z * v.x;
        static float TriArea2D(float3 a, float3 b, float3 c) => (c.x - a.x) * (b.z - a.z) - (b.x - a.x) * (c.z - a.z);
        static bool Eq(float3 a, float3 b) => math.distancesq(a, b) < 1e-12f;
    }
}
