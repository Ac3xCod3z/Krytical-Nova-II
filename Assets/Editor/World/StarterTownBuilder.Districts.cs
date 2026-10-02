using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Themed districts. Every block frontage is packed with buildings facing the street: modular shopfronts
    /// (Synty Section modules, see .Facades), whole Sci-Fi City prefabs, and stacked towers. A variety rule keeps the
    /// skyline from repeating: no recipe twice within 20 m, touching buildings differ in height by at least 25%, and
    /// outer towers get a vertical stretch. Inner blocks stay low and shop-like; outer blocks rise toward the wall.
    /// </summary>
    public static partial class StarterTownBuilder
    {
        private struct District
        {
            public string Name;
            public string[] Inner;      // whole prefabs for band A (plaza .. mid ring)
            public string[] Outer;      // whole prefabs for band B (mid ring .. wall)
            public float ModularInner;  // share of frontages that try a modular shopfront first
            public float ModularOuter;
            public float StackOuter;    // share of outer frontages that try a stacked tower first
        }

        // Quadrants: 0 NE, 1 NW, 2 SW, 3 SE. Repeated names weight the pick.
        private static readonly District[] Districts =
        {
            new District
            {
                Name = "NE Market Quarter", ModularInner = 0.9f, ModularOuter = 0.6f, StackOuter = 0.25f,
                Inner = new[] { "SM_Bld_Shop_Open_01", "SM_Bld_FoodHole_01", "SM_Bld_Raised_01", "SM_Bld_Raised_02", "SM_Bld_Roof_Pagoda_01", "SM_Bld_Background_Small_01" },
                Outer = new[] { "SM_Bld_Large_01", "SM_Bld_Large_02", "SM_Bld_Large_03", "SM_Bld_Large_06", "SM_Bld_Background_Med_01", "SM_Bld_Background_Med_05", "SM_Bld_Background_Med_07", "SM_Bld_Background_Lrg_03" },
            },
            new District
            {
                Name = "NW Barracks", ModularInner = 0.8f, ModularOuter = 0.55f, StackOuter = 0.2f,
                Inner = new[] { "SM_Bld_Security_WatchStation_01", "SM_Bld_Raised_01", "SM_Bld_Raised_02", "SM_Bld_Power_01", "SM_Bld_Shop_Open_01" },
                Outer = new[] { "SM_Bld_Large_04", "SM_Bld_Large_05", "SM_Bld_Warehouse_01", "SM_Bld_Background_Med_02", "SM_Bld_Background_Med_06", "SM_Bld_Background_Med_08", "SM_Bld_Large_03" },
            },
            new District
            {
                Name = "SW Industrial Yards", ModularInner = 0.75f, ModularOuter = 0.5f, StackOuter = 0.15f,
                Inner = new[] { "SM_Bld_Chopshop_01", "SM_Bld_Power_01", "SM_Bld_Power_02", "SM_Bld_Power_03", "SM_Bld_Raised_02", "SM_Bld_Industrial_01" },
                Outer = new[] { "SM_Bld_Warehouse_01", "SM_Bld_Large_05", "SM_Bld_Background_Lrg_01", "SM_Bld_Power_02", "SM_Bld_Power_03", "SM_Bld_Background_Med_09", "SM_Bld_Large_04" },
            },
            new District
            {
                Name = "SE Trade Quarter", ModularInner = 0.85f, ModularOuter = 0.55f, StackOuter = 0.3f,
                Inner = new[] { "SM_Bld_Shop_Open_01", "SM_Bld_FoodHole_01", "SM_Bld_Raised_01", "SM_Bld_Roof_Pagoda_02", "SM_Bld_Chopshop_01" },
                Outer = new[] { "SM_Bld_Background_Lrg_02", "SM_Bld_Background_Lrg_03", "SM_Bld_Large_01", "SM_Bld_Large_02", "SM_Bld_Large_06", "SM_Bld_Background_Med_04", "SM_Bld_Background_Med_09" },
            },
        };

        private static readonly string[] YardClutter =
        {
            "SM_Prop_Crate_Large_01", "SM_Prop_Crate_03", "SM_Prop_AirConditioningUnit_02", "SM_Prop_Box_Supplies_01",
            "SM_Env_Tree_03", "SM_Env_Tree_02", "SM_Env_Hedge_Empty_03", "SM_Prop_SatDish_02", "SM_Prop_Cylinder_02"
        };

        private const float Setback = 0.4f;   // gap between the sidewalk and a building front
        private const float BuildingGap = 0.6f;
        private const float RepeatRadius = 20f;
        private const float TouchRadius = 8.5f;  // side-by-side neighbours on the same street row
        private const float MinHeightContrast = 0.25f;

        private static int Quadrant(float x, float z) => x >= 0f ? (z >= 0f ? 0 : 3) : (z >= 0f ? 1 : 2);

        private struct Frontage
        {
            public int I, J;
            public Vector2 Center;   // sidewalk cell centre
            public Vector2 Dir;      // toward the street
        }

        /// <summary>Every building the city builder placed, for the variety rule, palette, signage and cables.</summary>
        private struct Building
        {
            public GameObject Go;
            public Rect Rect;
            public Vector2 Center;
            public Vector2 Front;    // facade normal (toward the street)
            public float Height;
            public string Key;       // prefab name or recipe id
            public bool Modular;     // composed from Section modules (facade plane known exactly)
            public int Bays, Floors;
        }

        private static readonly List<Building> buildings = new List<Building>();
        private static bool[,] buildingOcc;

        /// <summary>A building option for one frontage: what it is, its footprint (width along the street, depth) and height.</summary>
        private struct Candidate
        {
            public string Key;
            public string Prefab;    // whole prefab, or null for a recipe
            public int Bays, Depth, Floors;
            public bool Stack;
            public int StackSeed;
            public float Width, DepthM, Height, YScale;
        }

        private static void BuildDistricts(Transform parent)
        {
            var groups = new Transform[Districts.Length];
            for (int q = 0; q < Districts.Length; q++) groups[q] = Group(parent, Districts[q].Name);

            // Every straight sidewalk cell with block behind it is a frontage. Shuffled so rows vary run to run.
            var frontages = new List<Frontage>();
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                if (tiles[i, j] != TileKind.Sidewalk) continue;
                var dirs = StreetDirections(i, j);
                if (dirs.Count != 1) continue;
                int bi = i - dirs[0].x, bj = j - dirs[0].y;
                if (!InGrid(bi, bj) || tiles[bi, bj] != TileKind.Block) continue;
                frontages.Add(new Frontage { I = i, J = j, Center = new Vector2(CellCenter(i), CellCenter(j)), Dir = dirs[0] });
            }
            Shuffle(frontages);

            foreach (var f in frontages) TryFrontage(groups, f, strict: true);
            Shuffle(frontages);
            foreach (var f in frontages) TryFrontage(groups, f, strict: false); // fill what the strict pass left

            // Courtyard infill so blocks read dense from the street.
            var cells = new List<Vector2Int>();
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
                if (tiles[i, j] == TileKind.Block) cells.Add(new Vector2Int(i, j));
            Shuffle(cells);
            foreach (var c in cells)
            {
                Vector2 center = new Vector2(CellCenter(c.x), CellCenter(c.y));
                int q = Quadrant(center.x, center.y);
                bool outer = Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(center.y)) >= MidOut;
                var options = new List<Candidate>();
                foreach (string p in outer ? Districts[q].Outer : Districts[q].Inner) options.Add(PrefabCandidate(p, outer));
                if (outer) { options.Add(StackCandidate()); options.Add(StackCandidate()); }
                for (int k = 0; k < 3; k++)
                {
                    var m = ModularCandidate(outer);
                    m.Depth = 2; m.DepthM = 2 * Tile;
                    options.Add(m);
                }
                Shuffle(options);
                Vector2 facing = NearestStreetDir(c.x, c.y);
                foreach (var cand in options)
                    if (TryPlaceInfill(groups[q], cand, center, facing)) break;
            }

            BuildYardClutter(Group(parent, "Yard Clutter"));
        }

        private static void TryFrontage(Transform[] groups, Frontage f, bool strict)
        {
            int q = Quadrant(f.Center.x, f.Center.y);
            var d = Districts[q];
            bool outer = Mathf.Max(Mathf.Abs(f.Center.x), Mathf.Abs(f.Center.y)) >= MidOut;
            var options = new List<Candidate>();

            // Order the options by the district's mix, then shuffle within each kind.
            var modular = new List<Candidate>();
            for (int k = 0; k < 6; k++) modular.Add(ModularCandidate(outer));
            var prefabs = new List<Candidate>();
            foreach (string p in outer ? d.Outer : d.Inner) prefabs.Add(PrefabCandidate(p, outer));
            Shuffle(prefabs);
            var stacks = new List<Candidate>();
            if (outer) for (int k = 0; k < 2; k++) stacks.Add(StackCandidate());

            if (outer && Chance(d.StackOuter)) options.AddRange(stacks);
            if (Chance(outer ? d.ModularOuter : d.ModularInner)) { options.AddRange(modular); options.AddRange(prefabs); }
            else { options.AddRange(prefabs); options.AddRange(modular); }
            options.AddRange(stacks);
            if (!strict)
            {
                // A single-bay shop always fits a free 5 m frontage; then a small shack as the last resort.
                var one = ModularCandidate(outer);
                one.Bays = 1; one.Width = Tile; one.Depth = 1; one.DepthM = Tile; one.Key = $"MOD1x{one.Floors}";
                options.Add(one);
                options.Add(SmallFiller());
            }

            foreach (var c in options)
                if (TryPlaceCandidate(groups[q], c, f, strict)) return;
        }

        private static Candidate ModularCandidate(bool outer)
        {
            int bays = rng.Next(1, 4);
            int floors = outer ? rng.Next(2, 7) : rng.Next(1, 5);
            int depth = rng.Next(1, 3);
            return new Candidate
            {
                Key = $"MOD{bays}x{floors}", Bays = bays, Depth = depth, Floors = floors,
                Width = bays * Tile, DepthM = depth * Tile, Height = floors * SectionHeight, YScale = 1f,
            };
        }

        private static Candidate PrefabCandidate(string prefab, bool outer)
        {
            Bounds b = Footprint(prefab);
            float y = outer && b.size.y > 15f ? Range(0.85f, 1.25f) : 1f;
            return new Candidate { Key = prefab, Prefab = prefab, Width = b.size.x, DepthM = b.size.z, Height = b.size.y * y, YScale = y };
        }

        private static Candidate StackCandidate()
        {
            int seed = rng.Next(1 << 20);
            float h = TowerStackHeight(seed);
            return new Candidate { Key = "STACK" + (seed % 7), Stack = true, StackSeed = seed, Width = 8.7f, DepthM = 8.7f, Height = h, YScale = 1f };
        }

        private static Candidate SmallFiller()
        {
            string p = Pick(new[] { "SM_Bld_Background_Small_02", "SM_Bld_FoodHole_01", "SM_Building_Shack_01", "SM_Building_Shack_02" });
            return PrefabCandidate(p, false);
        }

        /// <summary>No repeat of the same recipe nearby, and side-by-side neighbours differ clearly in height.</summary>
        private static bool VarietyOk(Candidate c, Vector2 center, Vector2 front)
        {
            foreach (var b in buildings)
            {
                float dist = Vector2.Distance(b.Center, center);
                bool sameRow = Vector2.Dot(b.Front, front) > 0.9f;
                // Modular shops already vary piece by piece and in colour, so only whole prefabs and stacks are repeat-checked.
                bool modular = c.Prefab == null && !c.Stack;
                if (!modular && dist < RepeatRadius && b.Key == c.Key) return false;
                if (sameRow && dist < TouchRadius && Mathf.Abs(b.Height - c.Height) < MinHeightContrast * Mathf.Max(b.Height, c.Height)) return false;
            }
            return true;
        }

        private static bool TryPlaceCandidate(Transform parent, Candidate c, Frontage f, bool strict)
        {
            if (c.DepthM > 16f || c.Width > 16f) return false;
            Vector2 dir = f.Dir;
            float yaw = YawOf(dir);
            Vector2 worldSize = Mathf.Abs(dir.x) > 0.5f ? new Vector2(c.DepthM, c.Width) : new Vector2(c.Width, c.DepthM);
            Vector2 along = new Vector2(dir.y, -dir.x);
            // Modular shops snap to the 5 m grid and stand shoulder to shoulder like a street row; prefabs may slide.
            bool modular = c.Prefab == null && !c.Stack;
            float[] slides = modular ? (c.Bays % 2 == 1 ? new[] { 0f } : new[] { 2.5f, -2.5f }) : new[] { 0f, 1.5f, -1.5f, 2.5f, -2.5f };
            float gap = modular ? 0.1f : BuildingGap;
            foreach (float slide in slides)
            {
                Vector2 center = f.Center - dir * (Tile * 0.5f + Setback + c.DepthM * 0.5f) + along * slide;
                Rect rect = CenteredRect(center, worldSize + Vector2.one * gap);
                if (!IsFree(rect)) continue;
                if (strict && !VarietyOk(c, center, dir)) return false;
                MarkRect(rect);
                MarkBuilding(CenteredRect(center, worldSize));

                GameObject go;
                if (c.Prefab != null)
                {
                    go = Place(parent, c.Prefab, center.x, center.y, yaw);
                    if (go != null && !Mathf.Approximately(c.YScale, 1f)) go.transform.localScale = new Vector3(1f, c.YScale, 1f);
                }
                else if (c.Stack) go = BuildTowerStack(parent, center, yaw, c.StackSeed);
                else go = BuildModularShop(parent, center, yaw, c.Bays, c.Depth, c.Floors);
                if (go == null) return false;

                ApplyPalette(go, center);
                buildings.Add(new Building
                {
                    Go = go, Rect = CenteredRect(center, worldSize), Center = center, Front = dir, Height = c.Height,
                    Key = c.Key, Modular = c.Prefab == null && !c.Stack, Bays = c.Bays, Floors = c.Floors,
                });
                return true;
            }
            return false;
        }

        /// <summary>Courtyard building inside a big lot, facing its nearest street, tried at a few grid offsets.</summary>
        private static bool TryPlaceInfill(Transform parent, Candidate c, Vector2 center, Vector2 facing)
        {
            if (c.Width > 14f || c.DepthM > 14f) return false;
            float yaw = YawOf(facing);
            Vector2 size = Mathf.Abs(facing.x) > 0.5f ? new Vector2(c.DepthM, c.Width) : new Vector2(c.Width, c.DepthM);
            Vector2[] offsets = { Vector2.zero, new Vector2(2.5f, 0f), new Vector2(-2.5f, 0f), new Vector2(0f, 2.5f), new Vector2(0f, -2.5f) };
            foreach (var off in offsets)
            {
                Vector2 p = center + off;
                Rect rect = CenteredRect(p, size + Vector2.one * BuildingGap);
                if (!IsFree(rect) || !VarietyOk(c, p, facing)) continue;
                MarkRect(rect);
                MarkBuilding(CenteredRect(p, size));
                GameObject go;
                if (c.Prefab != null)
                {
                    go = Place(parent, c.Prefab, p.x, p.y, yaw);
                    if (go != null && !Mathf.Approximately(c.YScale, 1f)) go.transform.localScale = new Vector3(1f, c.YScale, 1f);
                }
                else if (c.Stack) go = BuildTowerStack(parent, p, yaw, c.StackSeed);
                else go = BuildModularShop(parent, p, yaw, c.Bays, c.Depth, c.Floors);
                if (go == null) return false;
                ApplyPalette(go, p);
                buildings.Add(new Building
                {
                    Go = go, Rect = CenteredRect(p, size), Center = p, Front = facing, Height = c.Height, Key = c.Key,
                    Modular = c.Prefab == null && !c.Stack, Bays = c.Bays, Floors = c.Floors,
                });
                return true;
            }
            return false;
        }

        /// <summary>Grid direction from a block cell toward the closest street (or alley) cell.</summary>
        private static Vector2 NearestStreetDir(int i, int j)
        {
            for (int r = 1; r < Cells; r++)
                foreach (var d in Dirs)
                    if (IsStreet(i + d.x * r, j + d.y * r)) return d;
            return Vector2.up;
        }

        private static void MarkBuilding(Rect r)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin + WallHalf)), x1 = Mathf.Min(OccSize, Mathf.CeilToInt(r.xMax + WallHalf));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin + WallHalf)), z1 = Mathf.Min(OccSize, Mathf.CeilToInt(r.yMax + WallHalf));
            for (int x = x0; x < x1; x++)
            for (int z = z0; z < z1; z++)
                buildingOcc[x, z] = true;
        }

        private static bool IsBuildingAt(Vector2 p)
        {
            int x = Mathf.FloorToInt(p.x + WallHalf), z = Mathf.FloorToInt(p.y + WallHalf);
            return x >= 0 && z >= 0 && x < OccSize && z < OccSize && buildingOcc[x, z];
        }

        /// <summary>Any 5 m block cell still fully free gets back-lot clutter or a grimy trash corner.</summary>
        private static void BuildYardClutter(Transform parent)
        {
            for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                if (tiles[i, j] != TileKind.Block) continue;
                var cell = new Rect(CellMin(i), CellMin(j), Tile, Tile);
                if (!IsFree(cell)) continue;
                MarkRect(cell);
                if (Chance(0.35)) { GrimeCorner(parent, cell.center, Vector2.zero); continue; }
                int count = rng.Next(1, 3);
                for (int k = 0; k < count; k++)
                    Place(parent, Pick(YardClutter), cell.center.x + Range(-1.3f, 1.3f), cell.center.y + Range(-1.3f, 1.3f), rng.Next(4) * 90f);
            }
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int k = rng.Next(i + 1);
                (list[i], list[k]) = (list[k], list[i]);
            }
        }
    }
}
