using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World.Water
{
    /// <summary>
    /// Ways over a zone's <see cref="WaterMap"/> (Waterstride): the shortest way between two water cells, eight ways
    /// round, never across a corner of land, then pulled straight wherever a straight line stays over water, so a run
    /// across a lake is one line and one round a headland is a few.
    /// </summary>
    public static class WaterRoute
    {
        private static readonly Vector2Int[] Steps =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        private const float Diagonal = 1.41421356f;

        /// <summary>
        /// Fills <paramref name="into"/> with the way from <paramref name="from"/> to <paramref name="to"/> as points on
        /// the water's surface (the first is <paramref name="from"/>'s cell, the last <paramref name="to"/>'s). False when
        /// no way is found within <paramref name="maxCells"/> cells searched.
        /// </summary>
        public static bool Find(WaterMap map, Vector2Int from, Vector2Int to, int maxCells, List<Vector3> into)
        {
            into.Clear();
            if (map == null || !map.IsWater(from) || !map.IsWater(to)) return false;
            if (from == to) { into.Add(map.Centre(from)); return true; }
            if (Clear(map, map.Centre(from), map.Centre(to)))
            {
                into.Add(map.Centre(from));
                into.Add(map.Centre(to));
                return true;
            }

            var cost = new Dictionary<Vector2Int, float> { [from] = 0f };
            var came = new Dictionary<Vector2Int, Vector2Int>();
            // Ordered by cost, then by when it was added (a cell is not comparable): its cell is kept beside it.
            var open = new SortedSet<(float f, int order)>();
            var cellOf = new Dictionary<int, Vector2Int>();
            int order = 0;
            cellOf[order] = from;
            open.Add((Heuristic(from, to), order++));
            int searched = 0;
            bool found = false;
            while (open.Count > 0)
            {
                var best = open.Min;
                open.Remove(best);
                Vector2Int cell = cellOf[best.order];
                cellOf.Remove(best.order);
                if (cell == to) { found = true; break; }
                if (++searched > maxCells) break;
                float here = cost[cell];
                foreach (Vector2Int step in Steps)
                {
                    Vector2Int next = cell + step;
                    if (!map.IsWater(next)) continue;
                    bool diagonal = step.x != 0 && step.y != 0;
                    // Never across a corner of land: both cells beside a diagonal step must be water too.
                    if (diagonal && (!map.IsWater(new Vector2Int(cell.x + step.x, cell.y)) || !map.IsWater(new Vector2Int(cell.x, cell.y + step.y)))) continue;
                    float g = here + (diagonal ? Diagonal : 1f);
                    if (cost.TryGetValue(next, out float known) && known <= g) continue;
                    cost[next] = g;
                    came[next] = cell;
                    cellOf[order] = next;
                    open.Add((g + Heuristic(next, to), order++));
                }
            }
            if (!found) return false;

            var cells = new List<Vector2Int>();
            for (Vector2Int c = to; ; c = came[c])
            {
                cells.Add(c);
                if (c == from) break;
            }
            cells.Reverse();
            // Pulled straight: from each kept point, the farthest cell still in a straight line over water.
            int at = 0;
            into.Add(map.Centre(cells[0]));
            while (at < cells.Count - 1)
            {
                int far = at + 1;
                for (int k = cells.Count - 1; k > at + 1; k--)
                    if (Clear(map, map.Centre(cells[at]), map.Centre(cells[k]))) { far = k; break; }
                into.Add(map.Centre(cells[far]));
                at = far;
            }
            return true;
        }

        /// <summary>Does a straight line from <paramref name="a"/> to <paramref name="b"/> stay over water all the way?</summary>
        public static bool Clear(WaterMap map, Vector3 a, Vector3 b)
        {
            Vector3 flat = b - a;
            flat.y = 0f;
            float length = flat.magnitude;
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / (map.cellSize * 0.25f)));
            for (int i = 0; i <= samples; i++)
            {
                Vector3 p = a + flat * (i / (float)samples);
                if (!map.IsWater(p)) return false;
            }
            return true;
        }

        /// <summary>The water cell nearest <paramref name="point"/>, within <paramref name="radius"/> metres.</summary>
        public static bool NearestWater(WaterMap map, Vector3 point, float radius, out Vector2Int cell)
        {
            cell = default;
            if (map == null) return false;
            if (map.TryCell(point, out Vector2Int here) && map.IsWater(here)) { cell = here; return true; }
            int span = Mathf.CeilToInt(radius / map.cellSize);
            float best = float.PositiveInfinity;
            map.TryCell(point, out here);
            for (int z = -span; z <= span; z++)
            for (int x = -span; x <= span; x++)
            {
                var c = new Vector2Int(here.x + x, here.y + z);
                if (!map.IsWater(c)) continue;
                Vector3 centre = map.Centre(c);
                float d = new Vector2(centre.x - point.x, centre.z - point.z).sqrMagnitude;
                if (d >= best || d > radius * radius) continue;
                best = d;
                cell = c;
            }
            return best < float.PositiveInfinity;
        }

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dz = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dz) + (Diagonal - 1f) * Mathf.Min(dx, dz);
        }
    }
}
