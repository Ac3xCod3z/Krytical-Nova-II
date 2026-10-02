using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Street-scale detail in the style of the Synty demo: modular shopfronts stacked from Section modules, stacked
    /// towers, billboards and signs mounted on real facades (raycast against the building, never floating), cable
    /// crossing trusses and strung cables over main streets, power lines along side streets, lantern alleys, grimy
    /// trash corners (Synty triplanar dirt material) and the special blocks (park, parking, raised deck, construction).
    /// </summary>
    public static partial class StarterTownBuilder
    {
        private const float SectionHeight = 3f;

        private static readonly string[] GroundFronts =
        {
            "SM_Bld_Section_Door_01", "SM_Bld_Section_Door_02", "SM_Bld_Section_Door_04", "SM_Bld_Section_Door_05",
            "SM_Bld_Section_Door_06", "SM_Bld_Section_Door_07", "SM_Bld_Section_Window_01", "SM_Bld_Section_Industrial_01",
            "SM_Bld_Section_Industrial_02", "SM_Bld_Section_Industrial_03"
        };
        private static readonly string[] UpperFronts =
        {
            "SM_Bld_Section_Window_01", "SM_Bld_Section_Window_02", "SM_Bld_Section_Window_03", "SM_Bld_Section_Window_02",
            "SM_Bld_Section_Wall_01", "SM_Bld_Section_Wall_03", "SM_Bld_Section_Wall_04", "SM_Bld_Section_Grid_01"
        };
        private static readonly string[] BackFronts = { "SM_Bld_Section_Wall_01", "SM_Bld_Section_Wall_02", "SM_Bld_Section_Wall_05", "SM_Bld_Section_Window_03" };
        private static readonly string[] RoofProps =
        {
            "SM_Prop_AirConditioningUnit_02", "SM_Prop_AirConditioningUnit_01", "SM_Prop_SatDish_01", "SM_Prop_SatDish_02",
            "SM_Prop_SatDish_03", "SM_Prop_Antenna", "SM_Prop_Cylinder_02", "SM_Prop_Vent_02"
        };
        private static readonly string[] ShopSigns = { "SM_Sign_01", "SM_Sign_02", "SM_Sign_03", "SM_Sign_04", "SM_Sign_05", "SM_Sign_06", "SM_Sign_07", "SM_Sign_08", "SM_Sign_09" };
        private static readonly string[] BladeSigns =
        {
            "SM_Sign_Neon_01", "SM_Sign_Neon_02", "SM_Sign_Neon_03", "SM_Sign_Neon_04", "SM_Sign_Neon_05", "SM_Sign_Neon_06",
            "SM_Sign_Neon_07", "SM_Sign_Neon_08", "SM_Sign_Ad_01", "SM_Sign_Ad_05", "SM_Sign_Ad_09", "SM_Sign_Ad_14", "SM_Sign_Ad_22", "SM_Sign_Ad_26"
        };
        private static readonly string[] Posters = { "SM_Prop_Posters_01", "SM_Prop_Posters_02", "SM_Prop_Posters_03" };
        private static readonly string[] ParkedCars = { "SM_Veh_Classic_01", "SM_Veh_Retro_01", "SM_Veh_Future_01", "SM_Veh_Future_Taxi_01", "SM_Veh_Garbage_01", "SM_Veh_Sweepo_01" };

        // ---------------------------------------------------------------- modular shopfronts

        /// <summary>
        /// A shop/apartment block of Section modules, front facing local +Z: <paramref name="bays"/> 5 m bays wide,
        /// <paramref name="depth"/> bays deep, <paramref name="floors"/> 3 m floors, with roof clutter and facade dressing.
        /// </summary>
        private static GameObject BuildModularShop(Transform parent, Vector2 center, float yaw, int bays, int depth, int floors)
        {
            var root = new GameObject($"Shop {bays}x{floors}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(center.x, 0f, center.y), Quaternion.Euler(0f, yaw, 0f));
            float w = bays * Tile, d = depth * Tile;

            // One look per building: the same ground/upper piece per floor reads as designed, not random.
            string[] upperByFloor = new string[floors];
            for (int f = 0; f < floors; f++) upperByFloor[f] = Pick(UpperFronts);
            for (int b = 0; b < bays; b++)
            for (int r = 0; r < depth; r++)
            for (int f = 0; f < floors; f++)
            {
                bool front = r == 0;
                string piece = front ? (f == 0 ? Pick(GroundFronts) : upperByFloor[f]) : (r == depth - 1 ? Pick(BackFronts) : "SM_Bld_Section_Wall_01");
                float lx = -w * 0.5f + Tile * (b + 0.5f), lz = d * 0.5f - Tile * (r + 0.5f);
                Vector2 p = center + Rotate(new Vector2(lx, lz), yaw);
                // Back rows turn round so the rear face also has a facade.
                float pieceYaw = front || depth == 1 ? yaw : yaw + 180f;
                Place(root.transform, piece, p.x, p.y, pieceYaw, f * SectionHeight);
            }

            float top = floors * SectionHeight;
            int props = rng.Next(1, 4);
            for (int k = 0; k < props; k++)
            {
                Vector2 lp = new Vector2(Range(-w * 0.5f + 1f, w * 0.5f - 1f), Range(-d * 0.5f + 1f, d * 0.5f - 1f));
                Vector2 p = center + Rotate(lp, yaw);
                Place(root.transform, Pick(RoofProps), p.x, p.y, yaw + rng.Next(4) * 90f, top, 1f, true, false);
            }

            DressShopFacade(root.transform, center, yaw, w, d, floors);
            return root;
        }

        /// <summary>Awnings, signs, blade signs, posters and AC units on the modular facade plane (local z = depth/2).</summary>
        private static void DressShopFacade(Transform root, Vector2 center, float yaw, float w, float d, int floors)
        {
            float face = d * 0.5f;
            Vector2 Local(float lx, float lz) => center + Rotate(new Vector2(lx, lz), yaw);
            int bays = Mathf.RoundToInt(w / Tile);
            for (int b = 0; b < bays; b++)
            {
                float lx = -w * 0.5f + Tile * (b + 0.5f);
                double roll = rng.NextDouble();
                if (roll < 0.45)
                {
                    // Awning over the shop door with a sign above it (signs face +Z, flat on the wall).
                    var p = Local(lx, face);
                    Place(root, "SM_Prop_Overhang_01", p.x, p.y, yaw, 2.7f, 1f, false, false);
                    var s = Local(lx, face + 0.04f);
                    Place(root, Pick(ShopSigns), s.x, s.y, yaw, 3.6f, 1.8f, false, false);
                }
                else if (roll < 0.7)
                {
                    var s = Local(lx, face + 0.04f);
                    Place(root, Pick(ShopSigns), s.x, s.y, yaw, 2.9f, 2f, false, false);
                }
                else if (roll < 0.85)
                {
                    // Poster stuck on the wall (the prop lies flat; stand it up facing the street).
                    var s = Local(lx + Range(-1.2f, 1.2f), face + 0.03f);
                    Spawn(root, Pick(Posters), new Vector3(s.x, Range(1.3f, 1.9f), s.y), Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f),
                          Vector3.one, false, false, false);
                }

                // Upper floors: AC units and the odd satellite dish hung on the facade.
                for (int f = 1; f < floors; f++)
                    if (Chance(0.3))
                    {
                        var p = Local(lx + Range(-1.5f, 1.5f), face);
                        Place(root, "SM_Prop_AirConditioningUnit_01", p.x, p.y, yaw, f * SectionHeight + 0.6f, 1f, false, false);
                    }
            }

            // Neon blade sign off one bay edge, perpendicular to the street (readable both ways), on taller shops.
            if (floors >= 2 && Chance(0.75))
            {
                float edge = (Chance(0.5) ? -1f : 1f) * (w * 0.5f - 0.3f);
                var p = Local(edge, face + 0.7f);
                Place(root, Pick(BladeSigns), p.x, p.y, yaw + 90f, Range(3.8f, floors * SectionHeight - 1.2f), 2.2f, false, false);
            }
        }

        // ---------------------------------------------------------------- tower stacks

        private static readonly string[] StackMids = { "SM_Bld_Large_Part_04", "SM_Bld_Large_Part_05", "SM_Bld_Large_Part_06" };
        private static readonly string[] StackTops = { "SM_Bld_Large_Part_02", "SM_Bld_Large_Part_03", null };

        private static List<string> StackRecipe(int seed)
        {
            var r = new System.Random(seed);
            var parts = new List<string> { "SM_Bld_Large_Part_01" };
            int mids = 1 + r.Next(3);
            for (int i = 0; i < mids; i++) parts.Add(StackMids[r.Next(StackMids.Length)]);
            string top = StackTops[r.Next(StackTops.Length)];
            if (top != null) parts.Add(top);
            return parts;
        }

        private static float TowerStackHeight(int seed)
        {
            float h = 0f;
            foreach (string p in StackRecipe(seed)) h += Footprint(p).size.y;
            return h;
        }

        /// <summary>A tower stacked from Large_Part pieces (base, 1-3 middles, optional spire) for silhouettes no single prefab has.</summary>
        private static GameObject BuildTowerStack(Transform parent, Vector2 center, float yaw, int seed)
        {
            var root = new GameObject("Tower Stack");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(center.x, 0f, center.y);
            float y = 0f;
            var r = new System.Random(seed + 1);
            foreach (string part in StackRecipe(seed))
            {
                Place(root.transform, part, center.x, center.y, yaw + r.Next(4) * 90f, y);
                y += Footprint(part).size.y;
            }
            return root;
        }

        // ---------------------------------------------------------------- facade billboards and signs

        /// <summary>
        /// Large billboards on tall buildings, mostly on the face that looks toward the plaza. The facade is found by
        /// raycasting against the building's own colliders, so every billboard sits on a wall.
        /// </summary>
        private static void BuildFacadeBillboards(Transform parent)
        {
            int placed = 0, dropped = 0;
            foreach (var b in buildings)
            {
                if (b.Go == null || b.Height < 12f || !Chance(b.Height > 20f ? 0.55 : 0.3)) continue;
                Vector2 toPlaza = -b.Center;
                Vector2 n = Chance(0.75) && toPlaza.sqrMagnitude > 1f ? Dominant(toPlaza) : b.Front;
                float h = b.Height * Range(0.45f, 0.75f);
                if (!FacadeHit(b.Go, b.Center, n, h, out Vector3 hit)) { dropped++; continue; }

                // Wide enough face for a large board? Otherwise a tall bordered one.
                float faceWidth = Mathf.Abs(n.x) > 0.5f ? b.Rect.height : b.Rect.width;
                bool large = faceWidth > 6.5f;
                string sign = large ? "SM_Sign_Billboard_Large_0" + rng.Next(1, 5) : "SM_Sign_Billboard_Border_Tall_0" + rng.Next(1, 5);
                float scale = large ? Range(1.5f, 1.9f) : Range(2.6f, 3.2f);
                Vector3 normal = new Vector3(n.x, 0f, n.y);
                // Billboard art faces local -Z: point -Z out of the wall.
                var go = Spawn(parent, sign, hit + normal * 0.15f, Quaternion.Euler(0f, YawOf(-n), 0f), Vector3.one * scale, false, false, false);
                if (go == null) continue;
                // Brackets back to the wall at the top corners.
                if (TryGetBounds(go, out var bb))
                {
                    Vector3 side = Vector3.Cross(Vector3.up, normal).normalized * (bb.extents.x * 0.7f + bb.extents.z * 0.7f);
                    for (int s = -1; s <= 1; s += 2)
                        Spawn(parent, "SM_Sign_Billboard_Attachmentl_01", new Vector3(hit.x, bb.max.y - 0.2f, hit.z) + side * s + normal * 0.07f,
                              Quaternion.Euler(0f, YawOf(n), 0f), Vector3.one * 3f, false, false, false);
                }
                placed++;
            }
            Debug.Log($"[StarterTown] Facade billboards: {placed} placed, {dropped} skipped (no wall found).");
        }

        /// <summary>Street-level blade signs on whole-prefab buildings (modular shops dress their own facades).</summary>
        private static void BuildStreetSigns(Transform parent)
        {
            foreach (var b in buildings)
            {
                if (b.Go == null || b.Modular || b.Height < 5f || !Chance(0.55)) continue;
                float h = Range(3.2f, Mathf.Min(6f, b.Height - 1f));
                if (!FacadeHit(b.Go, b.Center, b.Front, h, out Vector3 hit)) continue;
                Vector3 normal = new Vector3(b.Front.x, 0f, b.Front.y);
                Vector3 along = Vector3.Cross(Vector3.up, normal).normalized * Range(-1.5f, 1.5f);
                Spawn(parent, Pick(BladeSigns), hit + normal * 0.7f + along, Quaternion.Euler(0f, YawOf(b.Front) + 90f, 0f), Vector3.one * 2.2f, false, false, false);
            }
        }

        private static Vector2 Dominant(Vector2 v) => Mathf.Abs(v.x) > Mathf.Abs(v.y) ? new Vector2(Mathf.Sign(v.x), 0f) : new Vector2(0f, Mathf.Sign(v.y));

        /// <summary>Casts from outside the building back along -normal at height h against its own colliders.</summary>
        private static bool FacadeHit(GameObject building, Vector2 center, Vector2 normal, float h, out Vector3 point)
        {
            point = default;
            var origin = new Vector3(center.x + normal.x * 40f, h, center.y + normal.y * 40f);
            var ray = new Ray(origin, new Vector3(-normal.x, 0f, -normal.y));
            float best = float.MaxValue;
            foreach (var col in building.GetComponentsInChildren<Collider>())
                if (col.Raycast(ray, out RaycastHit hit, 60f) && hit.distance < best) { best = hit.distance; point = hit.point; }
            return best < float.MaxValue && best > 25f; // a hit closer than 25 m means we hit something sticking far out
        }

        // ---------------------------------------------------------------- street dressing

        private static void BuildStreetDressing(Transform parent)
        {
            var lights = Group(parent, "Streetlights");
            var poles = Group(parent, "Power Lines");
            var trusses = Group(parent, "Cable Crossings");
            var cables = Group(parent, "Overhead Cables");
            var furniture = Group(parent, "Street Furniture");
            var cars = Group(parent, "Parked Cars");
            var grime = Group(parent, "Trash Corners");
            var poleRuns = new Dictionary<(int Line, bool AlongZ, int Side), List<Vector3>>();

            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                var kind = tiles[i, j];
                if (kind != TileKind.Sidewalk && kind != TileKind.WallStrip) continue;
                var dirs = RoadDirections(i, j);
                if (dirs.Count != 1) continue;
                Vector2Int dirI = dirs[0];
                Vector2 dir = dirI;
                var road = new Vector2Int(i + dirI.x, j + dirI.y);
                var type = streetType[road.x, road.y];
                Vector2 c = new Vector2(CellCenter(i), CellCenter(j));
                bool streetAlongZ = dirI.x != 0;
                int along = streetAlongZ ? j : i;
                float lineCoord = streetAlongZ ? CellCenter(road.x) - dirI.x * 2.5f : CellCenter(road.y) - dirI.y * 2.5f;
                int line = LineAt(lineCoord);
                int side = streetAlongZ ? dirI.x : dirI.y; // which side of the street this walk is on (-1 / +1)
                float face = YawOf(dir);

                // Lights: main streets both sides every 3rd tile; side streets one side every 4th; wall road inner side.
                bool lightSide = type == StreetType.Main || (line >= 0 && (line % 2 == 0 ? side > 0 : side < 0));
                int lightEvery = type == StreetType.Main ? 3 : 4;
                if (lightSide && along % lightEvery == 1)
                {
                    Vector2 p = c + dir * 1.9f;
                    Place(lights, "SM_Prop_Streetlight_01", p.x, p.y, face, 0f, 1f, alignToBounds: false);
                    continue;
                }

                // Power poles on the other side of side streets and the wall road, collected per run for cabling.
                if ((type == StreetType.Side || type == StreetType.Wall) && !lightSide && along % 4 == 3)
                {
                    Vector2 p = c + dir * 1.9f;
                    var pole = Place(poles, Chance(0.5) ? "SM_Prop_Powerpoll_01" : "SM_Prop_Powerpoll_02", p.x, p.y, face + 90f, 0f, 1f, false);
                    if (pole != null && line >= 0)
                    {
                        var key = (line, streetAlongZ, side);
                        if (!poleRuns.TryGetValue(key, out var run)) poleRuns[key] = run = new List<Vector3>();
                        run.Add(new Vector3(p.x, 4.8f, p.y));
                    }
                    continue;
                }
                if (kind == TileKind.WallStrip)
                {
                    if (Chance(0.2)) Place(furniture, Pick(new[] { "SM_Prop_Barrier_02", "SM_Prop_Barrier_03", "SM_Prop_Crate_Military_01", "SM_Prop_Dumpster_01" }),
                                           c.x - dir.x * 1.2f, c.y - dir.y * 1.2f, face + rng.Next(2) * 90f);
                    continue;
                }

                // Furniture on the building side of the walk, with the odd trash corner.
                double roll = rng.NextDouble();
                Vector2 back = c - dir * 1.5f;
                if (roll < 0.06) Place(furniture, "SM_Prop_Bench_01", back.x, back.y, face);
                else if (roll < 0.1) Place(furniture, "SM_Prop_TrashCan_01", c.x + dir.x * 1.6f, c.y + dir.y * 1.6f, face);
                else if (roll < 0.14) Place(furniture, Chance(0.5) ? "SM_Prop_VendingMachine_01" : "SM_Prop_VendingMachine_Soda_01", back.x, back.y, face);
                else if (roll < 0.17) Place(furniture, "SM_Prop_Advertisement_Pillar_01", c.x, c.y, face);
                else if (roll < 0.2 && type == StreetType.Side) Place(furniture, "SM_Prop_Meter_01", c.x + dir.x * 2f, c.y + dir.y * 2f, face);
                else if (roll < 0.23) Place(furniture, "SM_Prop_Sidewalk_Barrier_01", c.x + dir.x * 2.1f, c.y + dir.y * 2.1f, face);
                else if (roll < 0.29) GrimeCorner(grime, back, -dir);

                // Parked cars along side-street kerbs.
                if (type == StreetType.Side && Chance(0.07))
                {
                    Vector2 p = c + dir * (Tile * 0.5f + 1.6f);
                    Place(cars, Pick(ParkedCars), p.x, p.y, face + 90f + (Chance(0.5) ? 180f : 0f));
                }
            }

            // Cables pole to pole along each run.
            foreach (var run in poleRuns.Values)
            {
                run.Sort((a, b) => (a.x + a.z).CompareTo(b.x + b.z));
                for (int k = 0; k + 1 < run.Count; k++)
                    if (Vector3.Distance(run[k], run[k + 1]) < 26f) StringCable(cables, run[k], run[k + 1]);
            }

            BuildCrossings(trusses, cables);
        }

        /// <summary>
        /// Main streets: lattice gantries every 25 m (the demo's recipe: Pillar_02 supports, Beams_01 lattice girders,
        /// neon and ad signs hung from them, a cable-crossing machine) and cables strung facade to facade in between.
        /// </summary>
        private static void BuildCrossings(Transform trusses, Transform cables)
        {
            float pillarH = Footprint("SM_Prop_Pillar_02").size.y;
            const float beamLen = 5f;
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                if (tiles[i, j] != TileKind.Road || streetType[i, j] != StreetType.Main) continue;
                bool alongZ = roadAlongZ[i, j];
                float x = CellCenter(i), z = CellCenter(j);
                int line = LineAt(alongZ ? x : z);
                if (line < 0) continue;
                float lineCoord = Lines[line];
                // Once per cross-section: only the tile on the low side of the line.
                if ((alongZ ? x : z) > lineCoord) continue;
                int along = alongZ ? j : i;
                float a = alongZ ? z : x;
                Vector2 Across(float offset) => alongZ ? new Vector2(lineCoord + offset, a) : new Vector2(a, lineCoord + offset);
                Vector2 acrossDir = alongZ ? Vector2.right : Vector2.up;

                if (along % 5 == 0 && !IsNearJunction(i, j, alongZ))
                {
                    var gantry = new GameObject("Gantry").transform;
                    gantry.SetParent(trusses, false);
                    float half = RoadHalf + 0.9f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var p = Across(s * half);
                        Place(gantry, "SM_Prop_Pillar_02", p.x, p.y, YawOf(acrossDir * -s));
                    }
                    // Beams run along local +Z from their pivot: two girder rows end to end across the road on the pillar tops.
                    float span = half * 2f;
                    int count = Mathf.CeilToInt(span / beamLen);
                    float len = span / count;
                    Vector2 alongDir = new Vector2(acrossDir.y, acrossDir.x);
                    for (int row = -1; row <= 1; row += 2)
                    for (int k = 0; k < count; k++)
                    {
                        var p = Across(-half + k * len) + alongDir * (row * 0.45f);
                        Spawn(gantry, "SM_Prop_Beams_01", new Vector3(p.x, pillarH - 0.4f + (row > 0 ? 0.8f : 0f), p.y), Quaternion.Euler(0f, YawOf(acrossDir), 0f),
                              new Vector3(1f, 1f, len / beamLen), false, false);
                    }
                    // Signs hang under the girders, readable from both directions along the street.
                    int signs = rng.Next(1, 4);
                    for (int k = 0; k < signs; k++)
                    {
                        var p = Across(Range(-half + 1.2f, half - 1.2f));
                        string sign = Chance(0.6) ? Pick(BladeSigns) : "SM_Sign_Billboard_Border_0" + rng.Next(1, 5);
                        var go = Spawn(gantry, sign, new Vector3(p.x, pillarH - 1.6f, p.y), Quaternion.Euler(0f, YawOf(alongDir), 0f), Vector3.one * 2.2f, false, false, false);
                        if (go != null && TryGetBounds(go, out var sb)) go.transform.position += new Vector3(0f, pillarH - 0.9f - sb.max.y, 0f);
                    }
                    if (Chance(0.5))
                    {
                        var m = Across(Range(-2f, 2f));
                        Place(gantry, "SM_Prop_Cable_Crossing_Machine_01", m.x, m.y, YawOf(acrossDir), pillarH - 1.2f, 1f, true, false);
                    }
                }
                else if (along % 5 == 2 || along % 5 == 3)
                {
                    // Strung cables facade to facade, only where buildings stand on both sides.
                    float faceOff = RoadHalf + Tile + Setback + 0.2f;
                    var a0 = Across(-faceOff);
                    var a1 = Across(faceOff);
                    if (IsBuildingAt(a0 - acrossDir * 0.8f) && IsBuildingAt(a1 + acrossDir * 0.8f))
                    {
                        int n = rng.Next(1, 3);
                        for (int k = 0; k < n; k++)
                        {
                            float y = Range(6.5f, 10f);
                            StringCable(cables, new Vector3(a0.x, y, a0.y), new Vector3(a1.x, y + Range(-0.8f, 0.8f), a1.y));
                        }
                    }
                }
            }
        }

        private static bool IsNearJunction(int i, int j, bool alongZ)
        {
            Vector2Int step = alongZ ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
            for (int k = -1; k <= 1; k++)
            {
                int ni = i + step.x * k, nj = j + step.y * k;
                if (!InGrid(ni, nj) || tiles[ni, nj] != TileKind.Road) return true;
            }
            return false;
        }

        /// <summary>Synty cables hang 7.3 m along local -X from the pivot: stretch one between two points.</summary>
        private static void StringCable(Transform parent, Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float flat = new Vector2(d.x, d.z).magnitude;
            if (flat < 1f) return;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 90f; // local -X → toward 'to'
            float pitch = Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg;
            string prefab = Pick(new[] { "SM_Prop_Cables_01", "SM_Prop_Cables_02", "SM_Prop_Cables_03" });
            Spawn(parent, prefab, from, Quaternion.Euler(0f, yaw, -pitch), new Vector3(flat / 7.3f, 1f, 1f), false, false, false);
        }

        // ---------------------------------------------------------------- alleys

        /// <summary>Pedestrian market alleys: lantern strings overhead, stalls down the middle, vents with steam.</summary>
        private static void BuildAlleys(Transform parent)
        {
            var steam = CityFx.Steam();
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                if (tiles[i, j] != TileKind.Alley) continue;
                bool alongZ = roadAlongZ[i, j];
                float x = CellCenter(i), z = CellCenter(j);
                int line = LineAt(alongZ ? x : z);
                if (line < 0) continue;
                float lineCoord = Lines[line];
                if ((alongZ ? x : z) > lineCoord) continue; // once per cross-section
                int along = alongZ ? j : i;
                float a = alongZ ? z : x;
                Vector2 Across(float offset) => alongZ ? new Vector2(lineCoord + offset, a) : new Vector2(a, lineCoord + offset);
                Vector2 acrossDir = alongZ ? Vector2.right : Vector2.up;
                float streetYaw = alongZ ? 0f : 90f;

                // Lantern string across, facade to facade.
                if (along % 2 == 0)
                {
                    float faceOff = RoadHalf + Tile + Setback + 0.2f;
                    var a0 = Across(-faceOff);
                    var a1 = Across(faceOff);
                    float y = Range(4.6f, 5.4f);
                    StringCable(parent, new Vector3(a0.x, y, a0.y), new Vector3(a1.x, y, a1.y));
                    for (int k = -3; k <= 3; k++)
                    {
                        var p = Across(k * 2.6f + Range(-0.4f, 0.4f));
                        Place(parent, Chance(0.5) ? "SM_Prop_Paper_Lantern_01" : "SM_Prop_Paper_Lantern_02", p.x, p.y, 0f, y - 0.35f - 0.4f * (1f - Mathf.Abs(k) / 3f), 1f, false, false);
                    }
                }

                // Market stalls alternate down the middle; crates and vents along the sides.
                var mid = Across(0f);
                if (along % 3 == 1)
                {
                    Place(parent, "SM_Prop_MarketCover_0" + rng.Next(2, 6), mid.x, mid.y, streetYaw + (Chance(0.5) ? 90f : 270f));
                    Place(parent, "SM_Prop_MarketTable_0" + rng.Next(1, 6), mid.x, mid.y, streetYaw + 90f);
                    if (Chance(0.5)) Place(parent, Pick(new[] { "SM_Prop_Hologram_Noodles_01", "SM_Prop_Hologram_Pizza_01", "SM_Prop_Hologram_Burger_03" }),
                                           mid.x + acrossDir.y * 1.5f, mid.y + acrossDir.x * 1.5f, streetYaw, 0f, 0.6f, true, false);
                }
                var edge = Across((Chance(0.5) ? -1f : 1f) * (RoadHalf - 0.8f));
                if (Chance(0.35))
                {
                    var vent = Place(parent, "SM_Prop_Vent_0" + rng.Next(1, 5), edge.x, edge.y, streetYaw);
                    if (vent != null && steam != null)
                    {
                        var fx = (GameObject)PrefabUtility.InstantiatePrefab(steam, parent);
                        fx.transform.position = new Vector3(edge.x, TryGetBounds(vent, out var vb) ? vb.max.y : 1f, edge.y);
                    }
                }
                else if (Chance(0.4)) GrimeCorner(parent, edge, Vector2.zero);
            }
        }

        // ---------------------------------------------------------------- trash corners (triplanar grime)

        private static readonly string[] TrashPieces =
        {
            "SM_Prop_RubbishPile_01", "SM_Prop_RubbishPile_02", "SM_Prop_RubbishPile_03", "SM_Prop_TrashBag_01", "SM_Prop_TrashBag_02",
            "SM_Prop_TrashBag_03", "SM_Env_Small_Rocks_01", "SM_Env_Small_Rocks_02", "SM_Prop_Box_Empty_01", "SM_Prop_Rubbish_Paper_02"
        };

        /// <summary>
        /// A bin or dumpster with rubbish piled round it, all in Synty's triplanar dirt material (the grime layer from
        /// Demo_TriplanarDirt), on a dirty ground patch. <paramref name="wall"/> points toward a wall to back onto (or zero).
        /// </summary>
        private static void GrimeCorner(Transform parent, Vector2 at, Vector2 wall)
        {
            var corner = new GameObject("Trash Corner");
            corner.transform.SetParent(parent, false);
            corner.transform.position = new Vector3(at.x, 0f, at.y);
            float yaw = wall.sqrMagnitude > 0.1f ? YawOf(-wall) : rng.Next(4) * 90f;
            Place(corner.transform, "SM_Env_Ground_Tile_Half_01", at.x, at.y, rng.Next(4) * 90f, PavingHeight + 0.015f, Range(1f, 1.4f), true, false);
            if (Chance(0.5)) Place(corner.transform, "SM_Prop_Dumpster_01", at.x, at.y, yaw);
            else
            {
                Place(corner.transform, "SM_Prop_TrashCan_01", at.x - 0.6f, at.y, yaw);
                if (Chance(0.6)) Place(corner.transform, "SM_Prop_Rubbish_Bin_01", at.x + 0.6f, at.y, yaw);
            }
            int pieces = rng.Next(2, 5);
            for (int k = 0; k < pieces; k++)
            {
                Vector2 p = at + Random2(Range(1f, 1.9f), true);
                Place(corner.transform, Pick(TrashPieces), p.x, p.y, Range(0f, 360f), 0f, Range(0.8f, 1.2f), true, false);
            }
            var grime = ColorBible.GrimeMaterials();
            if (grime.Count > 0)
            {
                var mat = grime[rng.Next(grime.Count)];
                foreach (var r in corner.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++)
                        if (mats[k] != null && mats[k].name.StartsWith("PolygonScifi_0")) mats[k] = mat;
                    r.sharedMaterials = mats;
                }
            }
        }

        // ---------------------------------------------------------------- special blocks

        private static void BuildSpecialBlocks(Transform parent)
        {
            foreach (var (point, use) in SpecialBlocks)
            {
                Rect block = BlockInterior(point);
                if (block.width < 5f) continue;
                MarkRect(block);
                var g = Group(parent, use.ToString());
                switch (use)
                {
                    case SpecialBlock.Park: BuildPark(g, block); break;
                    case SpecialBlock.Parking: BuildParking(g, block); break;
                    case SpecialBlock.RaisedDeck: BuildDeck(g, block); break;
                    case SpecialBlock.Construction: BuildConstruction(g, block); break;
                }
            }
        }

        /// <summary>The block cells around a point (stopping at sidewalks / streets), as a world rect.</summary>
        private static Rect BlockInterior(Vector2 p)
        {
            int i = CellIndex(p.x), j = CellIndex(p.y);
            if (!InGrid(i, j) || tiles[i, j] != TileKind.Block) return new Rect(p, Vector2.zero);
            int i0 = i, i1 = i, j0 = j, j1 = j;
            while (InGrid(i0 - 1, j) && tiles[i0 - 1, j] == TileKind.Block) i0--;
            while (InGrid(i1 + 1, j) && tiles[i1 + 1, j] == TileKind.Block) i1++;
            while (InGrid(i, j0 - 1) && tiles[i, j0 - 1] == TileKind.Block) j0--;
            while (InGrid(i, j1 + 1) && tiles[i, j1 + 1] == TileKind.Block) j1++;
            return Rect.MinMaxRect(CellMin(i0), CellMin(j0), CellMin(i1) + Tile, CellMin(j1) + Tile);
        }

        private static void BuildPark(Transform g, Rect r)
        {
            for (float x = r.xMin + Tile * 0.5f; x < r.xMax; x += Tile)
            for (float z = r.yMin + Tile * 0.5f; z < r.yMax; z += Tile)
            {
                bool path = Mathf.Abs(x - r.center.x) < Tile * 0.6f || Mathf.Abs(z - r.center.y) < Tile * 0.6f;
                Paving(g, path ? "SM_Env_Sidewalk_03_SF" : "SM_Env_Ground_Tile_06", x, z, 0f);
                if (!path)
                {
                    Place(g, Pick(new[] { "SM_Env_Grass_Patch_01", "SM_Env_Grass_Patch_02", "SM_Env_Grass_Patch_03" }), x + Range(-1f, 1f), z + Range(-1f, 1f), Range(0f, 360f), 0f, 1f, true, false);
                    if (Chance(0.45)) Place(g, Pick(new[] { "SM_Env_Tree_01", "SM_Env_Tree_03", "SM_Gen_Env_Tree_01" }), x + Range(-1f, 1f), z + Range(-1f, 1f), Range(0f, 360f));
                    else if (Chance(0.3)) Place(g, "SM_Env_Hedge_Empty_03", x, z, 0f);
                }
            }
            Place(g, "SM_Prop_Bench_01", r.center.x + 3f, r.center.y + 3.2f, 180f);
            Place(g, "SM_Prop_Bench_01", r.center.x - 3f, r.center.y - 3.2f, 0f);
            Place(g, "SM_Prop_Streetlight_01", r.center.x + 2.8f, r.center.y - 2.8f, 315f, 0f, 1f, alignToBounds: false);
            Place(g, "SM_Prop_Plant_Vase_02", r.center.x - 3f, r.center.y + 3f, 0f);
        }

        private static void BuildParking(Transform g, Rect r)
        {
            for (float x = r.xMin + Tile; x < r.xMax; x += Tile * 2f)
            for (float z = r.yMin + Tile * 0.5f; z < r.yMax; z += Tile)
            {
                Paving(g, "SM_Env_Road_ParkingLines_01_SF", x, z, 0f);
                if (Chance(0.6)) Place(g, Pick(ParkedCars), x + Range(-2.5f, 2.5f), z, Chance(0.5) ? 90f : 270f);
            }
            Place(g, "SM_Prop_ChargingStation_01", r.xMin + 1f, r.center.y, 90f);
            Place(g, "SM_Prop_ChargingStation_01", r.xMin + 1f, r.center.y + 2.5f, 90f);
            Place(g, "SM_Prop_Barrier_Pole_01", r.center.x, r.yMax - 0.5f, 0f);
        }

        private static void BuildDeck(Transform g, Rect r)
        {
            // A raised cafe deck on supports, railed, with food holograms: Synty's floating-platform look.
            const float deckY = 2.6f;
            for (float x = r.xMin + Tile * 0.5f; x < r.xMax - Tile * 0.4f; x += Tile)
            for (float z = r.yMin + Tile * 0.5f; z < r.yMax - Tile * 0.4f; z += Tile)
            {
                Place(g, "SM_Env_Ground_Tile_Supports_02", x, z, 0f, deckY);
                Place(g, "SM_Env_Ground_Tile_Floating_Platform_01", x, z, 0f, deckY);
            }
            for (float x = r.xMin + 2.5f; x < r.xMax; x += 5f)
            {
                Place(g, "SM_Env_Railing_02", x, r.yMin + 0.3f, 0f, deckY + 0.1f);
                Place(g, "SM_Env_Railing_02", x, r.yMax - 0.3f, 0f, deckY + 0.1f);
            }
            Place(g, "SM_Prop_Hologram_Noodles_01", r.center.x, r.center.y, 0f, deckY + 0.1f, 1f, true, false);
            Place(g, "SM_Prop_Table_Small_01", r.center.x + 3f, r.center.y, 0f, deckY + 0.1f);
            Place(g, "SM_Prop_Table_Small_01", r.center.x - 3f, r.center.y + 1.5f, 0f, deckY + 0.1f);
            Place(g, "SM_Prop_MarketLights_01", r.center.x, r.center.y + 3f, 0f, deckY + 0.1f, 1f, true, false);
        }

        private static void BuildConstruction(Transform g, Rect r)
        {
            for (float x = r.xMin + 1f; x < r.xMax - 1f; x += 2.2f)
            {
                Place(g, "SM_Prop_Fence_01", x, r.yMin + 0.5f, 90f);
                Place(g, "SM_Prop_Fence_01", x, r.yMax - 0.5f, 90f);
            }
            // A half-built tower: base and one middle, no top, with beams and crates round it.
            Place(g, "SM_Bld_Large_Part_01", r.center.x, r.center.y, 0f);
            Place(g, "SM_Bld_Large_Part_04", r.center.x, r.center.y, 0f, Footprint("SM_Bld_Large_Part_01").size.y);
            for (int k = 0; k < 5; k++)
            {
                Vector2 p = r.center + Random2(Range(5f, Mathf.Min(r.width, r.height) * 0.45f), true);
                Place(g, Pick(new[] { "SM_Prop_Beams_01", "SM_Prop_Beam_01", "SM_Prop_Crate_Large_01", "SM_Prop_Cylinder_01", "SM_Prop_Barrier_line_01" }), p.x, p.y, Range(0f, 360f));
            }
        }
    }
}
