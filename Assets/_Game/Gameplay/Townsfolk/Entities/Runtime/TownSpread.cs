using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// Life over the whole district (owner 14 Nov 2026: "I want it to seem like the whole area has life", not only by the Sky Lift arrival).
    /// Points spread evenly over every walkable floor of an area (its NavMesh triangles: plazas, streets, stairs, upper tiers, edges), then
    /// sorted into what each part of the district is like: rim points look out (viewpoints, strollers), back streets far from every
    /// hand-placed spot get a few quiet places to lean (loners), the rest are walk points with some places to stand and look round. Pure and
    /// deterministic from the area's seed (the server calls it once when the area is built); the numbers are in <see cref="TownsfolkSettings"/>.
    /// </summary>
    public static class TownSpread
    {
        public enum Use : byte { Walk = 0, Rim = 1, BackStreet = 2, Stand = 3 }

        public struct Point
        {
            public Vector3 Pos;
            public Use Use;
            /// <summary>Degrees the place faces (a rim point looks out from the middle).</summary>
            public float Yaw;
        }

        /// <summary>
        /// Even points over the triangles (<paramref name="verts"/>, <paramref name="tris"/>) whose middle lies within <paramref name="radius"/>
        /// of <paramref name="middle"/> across and <paramref name="height"/> up or down: about one per <paramref name="spacing"/> square
        /// metres, never two nearer than <paramref name="spacing"/>, at most <paramref name="most"/>.
        /// </summary>
        public static void Sample(Vector3[] verts, int[] tris, Vector3 middle, float radius, float height, float spacing, int most, uint seed, List<Vector3> into)
        {
            into.Clear();
            if (verts == null || tris == null || spacing <= 0.1f || most <= 0) return;
            var rng = new Unity.Mathematics.Random(seed | 1u);
            var candidates = new List<Vector3>();
            float cellArea = spacing * spacing;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                Vector3 mid = (a + b + c) / 3f;
                if (Mathf.Abs(mid.y - middle.y) > height) continue;
                if (new Vector2(mid.x - middle.x, mid.z - middle.z).sqrMagnitude > radius * radius) continue;
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                // a few tries per cell's worth of floor (small triangles get one by chance), so big plazas and thin stairs both fill
                float tries = area / cellArea * 3f;
                int n = (int)tries + (rng.NextFloat() < tries - (int)tries ? 1 : 0);
                for (int k = 0; k < n; k++)
                {
                    float r1 = rng.NextFloat(), r2 = rng.NextFloat();
                    if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                    candidates.Add(a + (b - a) * r1 + (c - a) * r2);
                }
            }
            // shuffled, so a cap never favours the NavMesh's first triangles (one corner)
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            var grid = new Dictionary<long, List<Vector3>>();
            float inv = 1f / spacing, minSq = spacing * spacing;
            foreach (Vector3 p in candidates)
            {
                if (into.Count >= most) break;
                int cx = Mathf.FloorToInt(p.x * inv), cz = Mathf.FloorToInt(p.z * inv);
                bool clear = true;
                for (int dx = -1; dx <= 1 && clear; dx++)
                    for (int dz = -1; dz <= 1 && clear; dz++)
                        if (grid.TryGetValue(Key(cx + dx, cz + dz), out List<Vector3> near))
                            foreach (Vector3 q in near)
                                if ((q - p).sqrMagnitude < minSq && Mathf.Abs(q.y - p.y) < 3f) { clear = false; break; }
                if (!clear) continue;
                long key = Key(cx, cz);
                if (!grid.TryGetValue(key, out List<Vector3> cell)) grid[key] = cell = new List<Vector3>(2);
                cell.Add(p);
                into.Add(p);
            }
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>
        /// What each point is for. <paramref name="edgeDistance"/> = metres from each point to the NavMesh's nearest edge (a rail or a
        /// wall), <paramref name="edgeAt"/> = that edge's point; <paramref name="handSpots"/> = the area's hand-placed spots.
        /// </summary>
        public static void Sort(List<Vector3> points, float[] edgeDistance, Vector3[] edgeAt, Vector3 middle, IReadOnlyList<Vector3> handSpots,
                                TownsfolkSettings s, uint seed, List<Point> into)
        {
            into.Clear();
            float far = 0f;
            foreach (Vector3 p in points) far = Mathf.Max(far, Across(p, middle));
            var rng = new Unity.Mathematics.Random((seed ^ 0xA5A5u) | 1u);
            float backSq = s.backStreetFrom * s.backStreetFrom;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                float roll = rng.NextFloat();
                Vector3 out_ = p - middle;
                float outYaw = Mathf.Atan2(out_.x, out_.z) * Mathf.Rad2Deg;
                var point = new Point { Pos = p, Use = Use.Walk, Yaw = rng.NextFloat(0f, 360f) };
                if (Across(p, middle) >= far - s.spreadRimBand && edgeDistance[i] <= s.spreadRimEdge)
                {
                    point.Use = Use.Rim;
                    point.Yaw = outYaw;
                }
                else if (NearestSq(p, handSpots) >= backSq)
                {
                    if (roll < s.backStreetStandShare)
                    {
                        point.Use = Use.BackStreet;
                        Vector3 away = p - edgeAt[i];   // the back to the wall it leans on
                        away.y = 0f;
                        if (away.sqrMagnitude > 1e-4f) point.Yaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
                    }
                }
                else if (roll < s.spreadStandShare) point.Use = Use.Stand;
                into.Add(point);
            }
        }

        static float Across(Vector3 p, Vector3 middle) => new Vector2(p.x - middle.x, p.z - middle.z).magnitude;

        static float NearestSq(Vector3 p, IReadOnlyList<Vector3> spots)
        {
            float best = float.MaxValue;
            for (int i = 0; i < spots.Count; i++) best = Mathf.Min(best, (spots[i] - p).sqrMagnitude);
            return best;
        }

        /// <summary>
        /// Whether a new townsperson may step out at <paramref name="at"/>: not near the arrival while the crowd there is already its most
        /// share (<paramref name="nearArrival"/> of <paramref name="out_"/> out).
        /// </summary>
        public static bool MayStepOutAt(float3 at, bool hasArrival, float3 arrival, float arrivalRadius, int nearArrival, int out_, float mostShare)
        {
            if (!hasArrival || math.distancesq(at.xz, arrival.xz) > arrivalRadius * arrivalRadius) return true;
            return nearArrival < math.max(1f, mostShare * math.max(out_, 1));
        }

        /// <summary>Whether the arrival holds more than its most share of the crowd (its spots then score less).</summary>
        public static bool ArrivalCrowded(int nearArrival, int out_, float mostShare) => out_ > 0 && nearArrival > mostShare * out_;
    }
}
