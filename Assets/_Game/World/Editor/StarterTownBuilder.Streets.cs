using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The street plan, modelled on the Synty demo: 10 m two-lane streets with 5 m kerbed sidewalks on a grid of lines
    /// (0, ±35, ±65, ±100 in both axes), varied by a curated segment table. Some segments are removed so blocks merge
    /// into big lots, some become pedestrian market alleys, and four blocks get a special use. Each road knows its
    /// street type (main / side / wall road), which drives its dressing. A flood fill checks that the plaza and every
    /// gate stay connected.
    /// </summary>
    public static partial class StarterTownBuilder
    {
        private enum TileKind { Block, Road, Junction, Sidewalk, Plaza, WallStrip, Alley }
        private enum StreetType { None, Main, Side, Wall }
        private enum Segment { Road, Removed, Alley, None }

        private static TileKind[,] tiles;
        private static bool[,] roadAlongZ;       // Road / Alley tiles: runs north-south
        private static StreetType[,] streetType; // Road / Junction tiles

        // Road centre lines; a road covers |coord − line| < RoadHalf.
        private static readonly float[] Lines = { -100f, -65f, -35f, 0f, 35f, 65f, 100f };
        private const float RoadHalf = 5f;

        // Curated variation. Keys: (vertical line index, span index) for lines of constant x spanning z between
        // Lines[span] and Lines[span + 1]; horizontal lines are the same with x and z swapped.
        // The ±65 ring only survives as short road and alley segments near the central cross, so the outer bands form
        // big 45-55 m lots (towers, courtyards, the training yard and the Exchange) instead of tiny boxed-in blocks.
        private static readonly Dictionary<(int Line, int Span), Segment> VerticalSegments = new Dictionary<(int, int), Segment>
        {
            { (3, 2), Segment.None }, { (3, 3), Segment.None },   // no x=0 street through the plaza
            { (1, 0), Segment.Removed }, { (1, 1), Segment.Removed }, { (1, 2), Segment.Alley }, { (1, 4), Segment.Removed }, { (1, 5), Segment.Removed },
            { (5, 0), Segment.Removed }, { (5, 1), Segment.Alley }, { (5, 3), Segment.Alley }, { (5, 4), Segment.Removed }, { (5, 5), Segment.Removed },
            { (2, 0), Segment.Removed },  // SW: industrial yard lot
            { (4, 4), Segment.Alley },    // NE market alley off the plaza
        };
        private static readonly Dictionary<(int Line, int Span), Segment> HorizontalSegments = new Dictionary<(int, int), Segment>
        {
            { (3, 2), Segment.None }, { (3, 3), Segment.None },   // no z=0 street through the plaza
            { (1, 0), Segment.Removed }, { (1, 1), Segment.Removed }, { (1, 2), Segment.Alley }, { (1, 4), Segment.Removed }, { (1, 5), Segment.Removed },
            { (5, 0), Segment.Removed }, { (5, 1), Segment.Removed }, { (5, 3), Segment.Alley }, { (5, 4), Segment.Alley }, { (5, 5), Segment.Removed },
            { (2, 0), Segment.Removed },  // W merged lot
        };

        // Blocks with a special use, by a point inside them.
        private enum SpecialBlock { None, Park, Parking, RaisedDeck, Construction }
        private static readonly (Vector2 Point, SpecialBlock Use)[] SpecialBlocks =
        {
            (new Vector2(-17.5f, 50f), SpecialBlock.Park),
            (new Vector2(50f, -17.5f), SpecialBlock.Parking),
            (new Vector2(17.5f, -50f), SpecialBlock.RaisedDeck),
            (new Vector2(82.5f, 17.5f), SpecialBlock.Construction),
        };

        // Synty road tiles carry an edge line on both X edges at yaw 0, so two side by side read as edge lines plus a
        // double centre line. Sidewalk tiles have their kerb on +Z; corner kerbs curve toward +X/+Z.
        private const string RoadLines = "SM_Env_Road_Lines_01_SF";
        private const string RoadCrossing = "SM_Env_Road_Crossing_01_SF";
        private const string RoadArrow = "SM_Env_Road_Arrow_01_SF";
        private const string RoadBareHalf = "SM_Env_Road_Bare_Half_01_SF";
        private const string SidewalkCorner = "SM_Env_Sidewalk_Corner_01_SF";
        private const string SidewalkPlain = "SM_Env_Sidewalk_01_SF";
        private static readonly string[] SidewalkStraight =
            { "SM_Env_Sidewalk_Straight_01_SF", "SM_Env_Sidewalk_Straight_01_SF", "SM_Env_Sidewalk_Straight_02_SF", "SM_Env_Sidewalk_Straight_03_SF" };
        private const string SidewalkGrate = "SM_Env_Sidewalk_Straight_04_SF";

        private static readonly Vector2Int[] Dirs = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

        private static float CellMin(int i) => -WallHalf + i * Tile;
        private static float CellCenter(int i) => -WallHalf + (i + 0.5f) * Tile;
        private static int CellIndex(float coord) => Mathf.Clamp(Mathf.FloorToInt((coord + WallHalf) / Tile), 0, Cells - 1);
        private static bool InGrid(int i, int j) => i >= 0 && j >= 0 && i < Cells && j < Cells;
        private static bool IsRoad(int i, int j) => InGrid(i, j) && (tiles[i, j] == TileKind.Road || tiles[i, j] == TileKind.Junction);
        private static bool IsStreet(int i, int j) => IsRoad(i, j) || (InGrid(i, j) && tiles[i, j] == TileKind.Alley);

        // ---------------------------------------------------------------- classification

        private static void ClassifyTiles()
        {
            tiles = new TileKind[Cells, Cells];
            roadAlongZ = new bool[Cells, Cells];
            streetType = new StreetType[Cells, Cells];
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
                tiles[i, j] = Classify(CellCenter(i), CellCenter(j), out roadAlongZ[i, j], out streetType[i, j]);

            // Block cells beside a road or alley become sidewalk; the plaza edge and the wall strip keep their kind.
            var sidewalks = new List<Vector2Int>();
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
                if (tiles[i, j] == TileKind.Block && StreetDirections(i, j).Count > 0) sidewalks.Add(new Vector2Int(i, j));
            foreach (var c in sidewalks) tiles[c.x, c.y] = TileKind.Sidewalk;

            CheckConnectivity();
        }

        private static int LineAt(float coord)
        {
            for (int i = 0; i < Lines.Length; i++)
                if (Mathf.Abs(coord - Lines[i]) < RoadHalf) return i;
            return -1;
        }

        private static int SpanAt(float coord)
        {
            for (int k = 0; k + 1 < Lines.Length; k++)
                if (coord >= Lines[k] && coord < Lines[k + 1]) return k;
            return -1;
        }

        private static Segment SegmentOf(bool vertical, int line, int span)
        {
            if (line < 0 || span < 0 || span >= Lines.Length - 1) return Segment.None;
            var table = vertical ? VerticalSegments : HorizontalSegments;
            return table.TryGetValue((line, span), out var s) ? s : Segment.Road;
        }

        private static StreetType TypeOfLine(int line)
        {
            // Main: the gate avenues and the plaza ring (the busy streets). Side: the ±65 remnants. Wall: the ring road.
            float l = Mathf.Abs(Lines[line]);
            return l < 1f || Mathf.Approximately(l, 35f) ? StreetType.Main : Mathf.Approximately(l, 100f) ? StreetType.Wall : StreetType.Side;
        }

        private static TileKind Classify(float x, float z, out bool alongZ, out StreetType type)
        {
            float ax = Mathf.Abs(x), az = Mathf.Abs(z), m = Mathf.Max(ax, az), n = Mathf.Min(ax, az);
            alongZ = false;
            type = StreetType.None;

            if (m > RingOut)
            {
                // Gate openings continue the avenues through the wall line.
                if (n < RoadHalf) { alongZ = az > ax; type = StreetType.Main; return TileKind.Road; }
                return TileKind.WallStrip;
            }
            if (m < PlazaHalf) return TileKind.Plaza;

            int vLine = LineAt(x), hLine = LineAt(z);
            if (vLine >= 0 && hLine >= 0)
            {
                // Junction square: count the arms that are real roads.
                float lineX = Lines[vLine], lineZ = Lines[hLine];
                var up = SegmentOf(true, vLine, SpanAt(lineZ + RoadHalf + 0.1f));
                var down = SegmentOf(true, vLine, SpanAt(lineZ - RoadHalf - 0.1f));
                var right = SegmentOf(false, hLine, SpanAt(lineX + RoadHalf + 0.1f));
                var left = SegmentOf(false, hLine, SpanAt(lineX - RoadHalf - 0.1f));
                bool u = up == Segment.Road, d = down == Segment.Road, r = right == Segment.Road, l = left == Segment.Road;
                int arms = (u ? 1 : 0) + (d ? 1 : 0) + (r ? 1 : 0) + (l ? 1 : 0);
                if (arms == 0)
                {
                    bool alley = up == Segment.Alley || down == Segment.Alley || right == Segment.Alley || left == Segment.Alley;
                    alongZ = up == Segment.Alley || down == Segment.Alley;
                    return alley ? TileKind.Alley : TileKind.Block;
                }
                type = (StreetType)Mathf.Max((int)TypeOfLine(vLine), (int)TypeOfLine(hLine));
                if (arms == 2 && u && d) { alongZ = true; type = TypeOfLine(vLine); return TileKind.Road; }
                if (arms == 2 && l && r) { alongZ = false; type = TypeOfLine(hLine); return TileKind.Road; }
                if (arms == 1) { alongZ = u || d; type = alongZ ? TypeOfLine(vLine) : TypeOfLine(hLine); return TileKind.Road; }
                return TileKind.Junction;
            }
            if (vLine >= 0)
            {
                var seg = SegmentOf(true, vLine, SpanAt(z));
                alongZ = true;
                if (seg == Segment.Road) { type = TypeOfLine(vLine); return TileKind.Road; }
                return seg == Segment.Alley ? TileKind.Alley : TileKind.Block;
            }
            if (hLine >= 0)
            {
                var seg = SegmentOf(false, hLine, SpanAt(x));
                alongZ = false;
                if (seg == Segment.Road) { type = TypeOfLine(hLine); return TileKind.Road; }
                return seg == Segment.Alley ? TileKind.Alley : TileKind.Block;
            }
            return TileKind.Block;
        }

        /// <summary>Directions (unit grid steps) from a cell to its 4-neighbour road tiles.</summary>
        private static List<Vector2Int> RoadDirections(int i, int j)
        {
            var list = new List<Vector2Int>(2);
            foreach (var d in Dirs)
                if (IsRoad(i + d.x, j + d.y)) list.Add(d);
            return list;
        }

        /// <summary>Like <see cref="RoadDirections"/> but alleys count too (buildings front onto both).</summary>
        private static List<Vector2Int> StreetDirections(int i, int j)
        {
            var list = new List<Vector2Int>(2);
            foreach (var d in Dirs)
                if (IsStreet(i + d.x, j + d.y)) list.Add(d);
            return list;
        }

        /// <summary>Every walkable street tile must reach the plaza; every gate must reach it too.</summary>
        private static void CheckConnectivity()
        {
            bool Walkable(int i, int j) => InGrid(i, j) && tiles[i, j] != TileKind.Block;
            var seen = new bool[Cells, Cells];
            var queue = new Queue<Vector2Int>();
            var start = new Vector2Int(CellIndex(0f), CellIndex(0f));
            queue.Enqueue(start);
            seen[start.x, start.y] = true;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Dirs)
                {
                    int ni = c.x + d.x, nj = c.y + d.y;
                    if (!Walkable(ni, nj) || seen[ni, nj]) continue;
                    seen[ni, nj] = true;
                    queue.Enqueue(new Vector2Int(ni, nj));
                }
            }
            int unreachable = 0;
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
                if ((tiles[i, j] == TileKind.Road || tiles[i, j] == TileKind.Junction || tiles[i, j] == TileKind.Alley) && !seen[i, j]) unreachable++;
            foreach (var gate in new[] { new Vector2(0f, 107f), new Vector2(0f, -107f), new Vector2(107f, 0f), new Vector2(-107f, 0f) })
                if (!seen[CellIndex(gate.x), CellIndex(gate.y)]) Debug.LogError($"[StarterTown] Gate at {gate} is not connected to the plaza.");
            if (unreachable > 0) Debug.LogError($"[StarterTown] {unreachable} street tiles are cut off from the plaza; fix the segment table.");
        }

        private static SpecialBlock SpecialAt(Vector2 p)
        {
            foreach (var (point, use) in SpecialBlocks)
                if (SameBlock(point, p)) return use;
            return SpecialBlock.None;
        }

        /// <summary>True when two points sit in the same block (same span between street lines in both axes).</summary>
        private static bool SameBlock(Vector2 a, Vector2 b) => SpanAt(a.x) == SpanAt(b.x) && SpanAt(a.y) == SpanAt(b.y)
                                                               && LineAt(a.x) < 0 && LineAt(a.y) < 0 && LineAt(b.x) < 0 && LineAt(b.y) < 0;

        // ---------------------------------------------------------------- tiles

        private static void BuildStreets(Transform parent)
        {
            var roads = Group(parent, "Roads");
            var walks = Group(parent, "Sidewalks");
            var lots = Group(parent, "Block Paving");
            var alleys = Group(parent, "Alley Paving");
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                float x = CellCenter(i), z = CellCenter(j);
                switch (tiles[i, j])
                {
                    case TileKind.Junction:
                        // No full-size bare tile exists: two bare halves make a plain junction square.
                        Paving(roads, RoadBareHalf, x, z - Tile * 0.25f, 0f);
                        Paving(roads, RoadBareHalf, x, z + Tile * 0.25f, 0f);
                        break;
                    case TileKind.Road:
                        RoadTile(roads, i, j, x, z);
                        break;
                    case TileKind.Alley:
                        Paving(alleys, Chance(0.6) ? "SM_Env_Ground_Tile_02" : "SM_Env_Ground_Tile_03", x, z, rng.Next(4) * 90f);
                        break;
                    case TileKind.Sidewalk:
                    case TileKind.WallStrip:
                        SidewalkTile(walks, i, j, x, z);
                        break;
                    case TileKind.Block:
                        // Block ground is paved too (buildings sit on it); the quarters and special blocks lay their own.
                        var p = new Vector2(x, z);
                        if (!TrainingYard.Contains(p) && !ExchangeSquare.Contains(p) && SpecialAt(p) == SpecialBlock.None)
                            Paving(lots, Chance(0.7) ? SidewalkPlain : "SM_Env_Sidewalk_03_SF", x, z, rng.Next(4) * 90f, PavingHeight - 0.01f);
                        break;
                    case TileKind.Plaza:
                        // The plaza's outer ring is kerbed like a sidewalk; the inside is laid out by BuildPlaza.
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > PlazaHalf - Tile) SidewalkTile(walks, i, j, x, z);
                        break;
                }
            }
        }

        private static void RoadTile(Transform parent, int i, int j, float x, float z)
        {
            bool alongZ = roadAlongZ[i, j];
            float yaw = alongZ ? 0f : 90f;
            // Zebra crossings on the tile right before a junction (along the lanes).
            Vector2Int step = alongZ ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
            if (IsJunction(i + step.x, j + step.y) || IsJunction(i - step.x, j - step.y))
            {
                Paving(parent, RoadCrossing, x, z, yaw);
                return;
            }

            // Lane arrows now and then on the main streets, right-hand traffic.
            int along = alongZ ? j : i;
            if (streetType[i, j] == StreetType.Main && along % 6 == 2)
            {
                float lineCoord = Lines[LineAt(alongZ ? x : z)];
                float side = (alongZ ? x : z) - lineCoord;
                float arrowYaw = alongZ ? (side > 0f ? 0f : 180f) : (side > 0f ? 270f : 90f);
                Paving(parent, RoadArrow, x, z, arrowYaw);
                return;
            }
            Paving(parent, RoadLines, x, z, yaw);
        }

        private static bool IsJunction(int i, int j) => InGrid(i, j) && tiles[i, j] == TileKind.Junction;

        private static void SidewalkTile(Transform parent, int i, int j, float x, float z)
        {
            var dirs = RoadDirections(i, j);
            if (dirs.Count == 1)
            {
                string prefab = Chance(0.08) ? SidewalkGrate : Pick(SidewalkStraight);
                Paving(parent, prefab, x, z, YawOf(dirs[0]));
            }
            else if (dirs.Count == 2 && dirs[0] + dirs[1] != Vector2Int.zero)
            {
                // Corner kerb: local (+1, +1) turned toward the diagonal between the two roads.
                Vector2Int d = dirs[0] + dirs[1];
                float yaw = d.x > 0 ? (d.y > 0 ? 0f : 90f) : (d.y < 0 ? 180f : 270f);
                Paving(parent, SidewalkCorner, x, z, yaw);
            }
            else
            {
                Paving(parent, SidewalkPlain, x, z, 0f);
            }
        }

        /// <summary>
        /// Flat paving over the terrain. Tiles stand ~8 cm proud of the terrain, so each gets mesh colliders matching its
        /// surface: the NavMesh, click hits, foot IK and ground rings then sit on the visible surface, not inside it.
        /// </summary>
        private static GameObject Paving(Transform parent, string prefab, float x, float z, float yaw, float y = PavingHeight)
        {
            var go = Place(parent, prefab, x, z, yaw, y, 1f, true, collider: false);
            if (go != null)
            {
                SetLayerRecursively(go, groundLayer);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                AddPavingCollider(go);
            }
            return go;
        }

        /// <summary>Exact (non-convex) mesh colliders on the tile's meshes, so curbs and lips stay true; no-op if it has one.</summary>
        internal static bool AddPavingCollider(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() != null) return false;
            bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                any = true;
            }
            return any;
        }

        /// <summary>Patches an already-built town: adds the paving colliders without a rebuild. Rebake the NavMesh after.</summary>
        [MenuItem("Krytical Nova/Zone/Add Paving Colliders (Open Scene)")]
        public static void AddPavingCollidersInScene()
        {
            int ground = LayerMask.NameToLayer("Ground");
            int added = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var go = t.gameObject;
                if (go.layer != ground || !PrefabUtility.IsAnyPrefabInstanceRoot(go) || go.GetComponent<Terrain>() != null) continue;
                string n = go.name;
                if (!n.StartsWith("SM_Env_Ground_Tile") && !n.StartsWith("SM_Env_Sidewalk") && !n.StartsWith("SM_Env_Road")) continue;
                if (AddPavingCollider(go)) { added++; EditorUtility.SetDirty(go); }
            }
            if (added > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[StarterTown] Added {added} paving colliders.");
        }

        // ---------------------------------------------------------------- wall

        private const double WallBreachChance = 0.18;     // share of wall spans swapped for a baked breach / rubble
        private const double WallTowerRuinChance = 0.3;   // share of mid-wall towers standing broken

        private static bool RChance(double p) => ruinRng.NextDouble() < p;

        private static Vector2 WallOutward(int side) =>
            side == 0 ? Vector2.up : side == 1 ? Vector2.down : side == 2 ? Vector2.right : Vector2.left;

        /// <summary>
        /// Swaps an intact wall piece for a baked Quick Destruction ruin of the same source at the same transform (both
        /// share the Synty pivot). The ruin is turned so its fallen chunks spill outside the city, keeping the Wall Ring
        /// road clear, then the chunks are settled onto the ground.
        /// </summary>
        private static void ReplaceWithRuin(Transform parent, GameObject intact, string source, RuinBaker.Tier tier, Vector2 outward)
        {
            if (!TryPickRuin(source, tier, out var prefab)) return;
            var t = intact.transform;
            TryGetBounds(intact, out var intactBounds);
            var ruin = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            ruin.transform.SetPositionAndRotation(t.position, t.rotation);
            ruin.transform.localScale = t.localScale;
            ruin.name = intact.name + " (Ruined)";

            var fallen = ruin.transform.Find(RuinBaker.FallenName);
            if (fallen != null && fallen.childCount > 0 && TryGetBounds(fallen.gameObject, out var fb))
            {
                Vector3 d = fb.center - intactBounds.center;
                if (Vector2.Dot(new Vector2(d.x, d.z), outward) < 0f)
                    ruin.transform.RotateAround(intactBounds.center, Vector3.up, 180f);
            }
            SettleFallen(ruin, 0.2f);
            GameObjectUtility.SetStaticEditorFlags(ruin, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            Object.DestroyImmediate(intact);
        }

        private static void BuildWalls(Transform parent)
        {
            const string wall = "SM_Bld_City_Wall_01";
            const string tower = "SM_Bld_City_Wall_Tower_01";
            float wallLength = Footprint(wall).size.x;
            float towerHalf = Footprint(tower).size.x * 0.5f;
            float[] towerAt = { GateTowerAcross, 56f, WallHalf };
            float gateTowerHalf = towerHalf * GateTowerScale;

            for (int side = 0; side < 4; side++)
            {
                // 0 = north, 1 = south, 2 = east, 3 = west
                bool alongX = side < 2;
                float line = side == 0 || side == 2 ? WallHalf : -WallHalf;
                float yaw = alongX ? 0f : 90f;
                Vector3 Point(float along) => alongX ? new Vector3(along, 0f, line) : new Vector3(line, 0f, along);

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    for (int t = 0; t + 1 < towerAt.Length; t++)
                    {
                        // Wall spans between towers, scaled along their length to fit exactly.
                        float a = towerAt[t] + (t == 0 ? gateTowerHalf : towerHalf), b = towerAt[t + 1] - towerHalf;
                        int count = Mathf.CeilToInt((b - a) / wallLength);
                        float segment = (b - a) / count;
                        for (int k = 0; k < count; k++)
                        {
                            var span = Spawn(parent, wall, Point(sign * (a + segment * (k + 0.5f))), Quaternion.Euler(0f, yaw, 0f),
                                  new Vector3(segment / wallLength, 1f, 1f));
                            // Breached spans: never the one beside a gate tower, so every gate keeps its frame.
                            bool besideGate = t == 0 && k == 0;
                            if (span != null && !besideGate && RChance(WallBreachChance))
                                ReplaceWithRuin(parent, span, RuinBaker.Wall, PickTier(0.15f, 0.6f, 0.25f), WallOutward(side));
                        }
                    }
                    for (int t = 1; t + 1 < towerAt.Length; t++) // t = 0: gate towers come from BuildGates
                    {
                        var tw = Spawn(parent, tower, Point(sign * towerAt[t]), Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                        // Mid-wall towers (not gate towers) sometimes stand broken.
                        if (tw != null && RChance(WallTowerRuinChance))
                            ReplaceWithRuin(parent, tw, RuinBaker.WallTower, PickTier(0.2f, 0.5f, 0.3f), WallOutward(side));
                    }
                }
            }

            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Spawn(parent, tower, new Vector3(sx * WallHalf, 0f, sz * WallHalf), Quaternion.identity, Vector3.one * 1.25f);

            // Gatehouses (towers, lintel, booths, fences, nameplates) on all four gates; beacons on the main (north) gate.
            BuildGates(parent);

            var beacon = AssetDatabase.LoadAssetAtPath<GameObject>(PortalVfxBuilder.GateBeaconPath);
            if (beacon != null)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(beacon, parent);
                    go.transform.position = new Vector3(sign * towerAt[0], 13.4f * GateTowerScale, WallHalf);
                    go.transform.localScale = Vector3.one * 2f;
                }
        }
    }
}
