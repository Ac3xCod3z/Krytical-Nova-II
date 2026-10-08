using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Progression;
using KryticalNova.Rendering;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Reads every weapon model's mesh and writes two things onto its prefab.
    ///
    /// Which part of it glows when it is tempered (<see cref="WeaponGlowZone"/>). Edged and hafted weapons glow on the
    /// cutting part only: the blade past the guard, the head of an axe or a scythe and not the shaft beside it. Guns
    /// glow everywhere but the grip. Bows glow everywhere but the string. A model the rules read wrongly is put
    /// right on its prefab and ticked "keep as set", and is then left alone.
    ///
    /// Its outermost points (<see cref="WeaponReach"/>), which keep a long weapon out of the ground.
    ///
    /// Both come from the meshes themselves, so a new weapon model needs nothing but a re-run. Re-runnable.
    /// </summary>
    public static class WeaponGlowZoneBaker
    {
        // Every weapon model is laid out along its own z: the grip at the pivot, the tip at +z.
        private const float Slice = 0.02f;          // the weapon is read in slices this thick
        private const float EdgeStep = 0.01f;       // and its faces sampled this finely...
        private const int MostSteps = 200;          // ...in no more steps than this a face
        private const float GripFrom = -0.04f, GripTo = 0.06f;   // where the hand is, along z
        private const float Margin = 0.02f;
        private const float ZoneAtLeast = 0.04f;    // a glowing part shorter than this: the whole weapon glows instead

        // Blades
        private const float WiderBy = 1.7f;         // "wider than the grip": this many times its width...
        private const float WiderAtLeast = 0.03f;   // ...and at least this much more
        private const float GuardWithin = 0.30f;    // a guard sits within this of the hand; further out it is a head
        private const float GuardLength = 0.12f;    // and is no longer than this
        private const float GuardDrop = 0.6f;       // past a guard the weapon narrows to under this share of it
        private const float BladeAtLeast = 0.15f;   // a blade past a guard is at least this long
        private const float NarrowTo = 0.8f;        // a blade no wider than its hilt starts where the weapon last narrows to this share of the two slices behind
        private const float NoGuardFrom = 0.25f;    // nothing to go by at all: the outer three quarters glow
        private const float HeldInTheMiddle = 0.7f; // as much of it behind the hand as ahead (this share or more)...
        private const float SmallReach = 0.30f;     // ...and none of it reaching further than this: it has no handle end, all of it glows

        // Hafted weapons
        private const float ShaftGrow = 1.3f;       // a slice is still shaft while it is no more than this many times the grip's size...
        private const float ShaftSlack = 0.01f;     // ...plus this
        private const float ShaftBefore = 0.16f;    // where the shaft runs is read from this much of it before the head...
        private const float ShaftSkip = 0.04f;      // ...leaving out what touches the head
        private const float HeadJoins = 0.03f;      // the head has joined the shaft once the shaft's own outline reaches this far past where it ran
        private const float Cell = 0.01f;           // the side view is read in cells this size

        // Reach
        private const int ReachDirections = 96;     // a model's outermost point is found in this many directions
        private const float ReachMerge = 0.01f;     // points nearer each other than this count as one

        // Guns and bows
        private const float GripBack = 0.15f, GripAhead = 0.02f, GripAbove = 0.035f;   // a gun's grip, from its trigger
        private const float MuzzleLength = 0.05f;   // the last stretch of a gun, where only the barrel is: it says how high the barrel runs
        private static readonly string[] BowExclusions = { "String" };

        private enum Kind { Bladed, Hafted, Gun, Bow }

        private static Kind KindOf(WeaponClassId weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClassId.Carbine:
                case WeaponClassId.ArcCaster:
                    return Kind.Gun;
                case WeaponClassId.Warbow:
                    return Kind.Bow;
                case WeaponClassId.Glaive:
                case WeaponClassId.Harvester:
                case WeaponClassId.Longaxe:
                    return Kind.Hafted;
                default:
                    return Kind.Bladed;
            }
        }

        [MenuItem("Krytical Nova/Items/8. Bake Weapon Models (glow zones, reach)")]
        public static void Bake()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            string[] dictionaries = AssetDatabase.FindAssets("t:GearDictionary");
            var gear = dictionaries.Length > 0 ? AssetDatabase.LoadAssetAtPath<GearDictionary>(AssetDatabase.GUIDToAssetPath(dictionaries[0])) : null;
            if (catalog == null || gear == null)
            {
                Debug.LogError("[GlowZones] No item catalog or gear dictionary: run Krytical Nova/Items/1 and 6 first.");
                return;
            }

            var models = new Dictionary<GameObject, WeaponClassId>();
            foreach (var entry in gear.Entries)
            {
                if (entry.slot != EquipSlot.Weapon) continue;
                ItemDefinition item = catalog.Get(entry.itemId);
                if (item != null && item.weaponPrefab != null && !models.ContainsKey(item.weaponPrefab)) models[item.weaponPrefab] = entry.weaponClass;
            }

            int written = 0, kept = 0;
            var report = new System.Text.StringBuilder();
            foreach (var pair in models)
            {
                string path = AssetDatabase.GetAssetPath(pair.Key);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/_Game/"))
                {
                    report.AppendLine($"  {pair.Key.name}: not one of our model prefabs ({path}), left alone");
                    continue;
                }
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!root.TryGetComponent(out WeaponGlowZone part)) part = root.AddComponent<WeaponGlowZone>();
                    if (!root.TryGetComponent(out WeaponReach reach)) reach = root.AddComponent<WeaponReach>();
                    reach.points = ReachPoints(root);
                    string note;
                    if (part.keepAsSet)
                    {
                        kept++;
                        note = "glow zone kept as set by hand";
                    }
                    else
                    {
                        Kind kind = KindOf(pair.Value);
                        switch (kind)
                        {
                            case Kind.Gun: part.zone = GunZone(root, out note); break;
                            case Kind.Bow: part.zone = BowZone(out note); break;
                            case Kind.Bladed: part.zone = BladedZone(Outline.Of(root), out note); break;
                            default: part.zone = HaftedZone(Outline.Of(root), out note); break;
                        }
                        part.exclude = kind == Kind.Bow ? BowExclusions : new string[0];
                        written++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.AppendLine($"  {root.name} ({pair.Value}): {note}; {reach.points.Length} reach points");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GlowZones] {written} weapon models given their glow zone, {kept} kept as set by hand; all given their reach.\n{report}");
        }

        // ---------------------------------------------------------------- reach

        /// <summary>
        /// The model's outermost points, in its own space: for each of many directions spread evenly round it, the
        /// point of it furthest that way. Whichever way the weapon is turned, its lowest point is (near enough) one
        /// of them. Skinned meshes give the corners of their box: their vertices are not where the mesh shows them.
        /// </summary>
        private static Vector3[] ReachPoints(GameObject root)
        {
            var vertices = new List<Vector3>(4096);
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                Matrix4x4 toModel = toRoot * renderer.transform.localToWorldMatrix;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    Bounds box = skinned.localBounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                        vertices.Add(toModel.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents, sign)));
                    }
                }
                else if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    foreach (Vector3 vertex in filter.sharedMesh.vertices) vertices.Add(toModel.MultiplyPoint3x4(vertex));
                }
            }

            var found = new List<Vector3>(ReachDirections);
            if (vertices.Count == 0) return found.ToArray();
            for (int d = 0; d < ReachDirections; d++)
            {
                // Directions spread evenly over a sphere: up the axis in equal steps, turning by the golden angle.
                float y = 1f - 2f * (d + 0.5f) / ReachDirections;
                float ring = Mathf.Sqrt(1f - y * y);
                float turn = d * 2.39996323f;
                var direction = new Vector3(Mathf.Cos(turn) * ring, y, Mathf.Sin(turn) * ring);
                Vector3 furthest = vertices[0];
                float most = float.MinValue;
                foreach (Vector3 vertex in vertices)
                {
                    float along = Vector3.Dot(vertex, direction);
                    if (along <= most) continue;
                    most = along;
                    furthest = vertex;
                }
                bool known = false;
                foreach (Vector3 point in found)
                    if ((point - furthest).sqrMagnitude < ReachMerge * ReachMerge) { known = true; break; }
                if (!known) found.Add(furthest);
            }
            return found.ToArray();
        }

        // ---------------------------------------------------------------- blades

        /// <summary>
        /// From the hand outward the weapon stays as thin as its grip until something wider begins. Close to the hand
        /// and short, with the weapon narrowing again after it, that is a guard and the blade starts past it.
        /// Otherwise it is the blade itself. A blade no wider than its hilt (a katana, a rapier) starts where the
        /// weapon last narrows near the hand: the far side of its guard.
        /// </summary>
        private static GlowZone BladedZone(Outline o, out string note)
        {
            if (o == null)
            {
                note = "no mesh: glows all over";
                return default;
            }
            if (o.zMax < SmallReach && -o.zMin >= o.zMax * HeldInTheMiddle)
            {
                note = "held at its middle, with no handle end: glows all over";
                return default;
            }

            // The thin end of what the hand is on: a fist weapon's cross-bar is in the hand too, and is no measure of its grip.
            float gripWidth = o.GripMeasure(o.Width, 0.25f);
            float wide = Mathf.Max(gripWidth * WiderBy, gripWidth + WiderAtLeast);
            int widens = -1;
            for (int i = o.IndexOf(GripTo); i < o.count; i++)
                if (o.Width(i) > wide) { widens = i; break; }

            float start;
            if (widens >= 0)
            {
                start = o.At(widens);
                note = $"blade from {start:F2} m";
                if (start <= GuardWithin)
                {
                    // A guard: a short wide stretch, then the weapon narrows and runs on.
                    float peak = 0f;
                    int last = Mathf.Min(o.count - 1, widens + Mathf.CeilToInt(GuardLength / Slice));
                    for (int i = widens; i <= last; i++) peak = Mathf.Max(peak, o.Width(i));
                    for (int i = widens + 1; i <= last; i++)
                    {
                        if (o.Width(i) > peak * GuardDrop) continue;
                        if (o.zMax - o.At(i) >= BladeAtLeast)
                        {
                            start = o.At(i);
                            note = $"blade past the guard, from {start:F2} m";
                        }
                        break;
                    }
                }
            }
            else
            {
                start = o.NothingWider(out note);
            }
            return o.From(start, ref note);
        }

        // ---------------------------------------------------------------- hafted weapons

        /// <summary>
        /// The head is the widest part of the weapon: from there back towards the hand until the weapon is its shaft
        /// again. Where part of the head reaches back alongside the shaft without touching it (the beard of an axe,
        /// the horns of a double one, the point of a scythe), the shaft there stays dark until the head joins it.
        /// </summary>
        private static GlowZone HaftedZone(Outline o, out string note)
        {
            if (o == null)
            {
                note = "no mesh: glows all over";
                return default;
            }
            float gripTall = o.GripMedian(o.Tall), gripThick = o.GripMedian(o.Thick);
            bool ShaftLike(int i) => o.Tall(i) <= gripTall * ShaftGrow + ShaftSlack && o.Thick(i) <= gripThick * ShaftGrow + ShaftSlack;

            int first = o.IndexOf(GripTo);
            int peak = first;
            for (int i = first; i < o.count; i++)
                if (o.Width(i) > o.Width(peak)) peak = i;
            if (ShaftLike(peak))
            {
                float plain = o.NothingWider(out note);
                return o.From(plain, ref note);
            }

            int head = peak;
            while (head - 1 >= first && !ShaftLike(head - 1)) head--;
            note = $"head from {o.At(head):F2} m";

            GlowZone zone = o.From(o.At(head), ref note);
            if (zone.mode != GlowZoneMode.Only) return zone;

            // Where the shaft runs, from the stretch of it before the head.
            var lows = new List<float>();
            var highs = new List<float>();
            for (int i = Mathf.Max(0, o.IndexOf(o.At(head) - ShaftBefore)); i < head && o.At(i) < o.At(head) - ShaftSkip; i++)
            {
                if (!o.filled[i]) continue;
                lows.Add(o.low[i].y);
                highs.Add(o.high[i].y);
            }
            if (lows.Count == 0) return zone;
            float yFrom = Median(lows), yTo = Median(highs);

            // Follow it into the head's stretch: it is still bare shaft while its own outline stays where it ran.
            int joins = head;
            while (joins < o.count && o.Run(joins, yFrom, yTo, out float runFrom, out float runTo)
                   && runFrom >= yFrom - HeadJoins && runTo <= yTo + HeadJoins) joins++;
            if (joins == head) return zone;

            zone.dark = Enclosing(
                new Vector3(o.bounds.min.x - Margin, yFrom - Cell, o.zMin - Margin),
                new Vector3(o.bounds.max.x + Margin, yTo + Cell, o.At(joins)));
            note += $", shaft dark beside it to {o.At(joins):F2} m";
            return zone;
        }

        // ---------------------------------------------------------------- guns and bows

        /// <summary>
        /// A gun glows everywhere but its grip: what hangs behind the trigger on the trigger's side of the body. Found
        /// from the model's own trigger mesh; a gun without one loses the part round its pivot, where the hand is.
        /// </summary>
        private static GlowZone GunZone(GameObject root, out string note)
        {
            List<Vector3> body = SamplePoints(root, null);
            if (body.Count == 0)
            {
                note = "no mesh: glows all over";
                return default;
            }
            Bounds bounds = BoundsOf(body);
            List<Vector3> trigger = SamplePoints(root, "Trigger");
            Vector3 at = trigger.Count > 0 ? BoundsOf(trigger).center : Vector3.zero;

            // Which way is the muzzle, and which side of the barrel does the trigger hang on? The grip is on that side.
            bool muzzleAhead = bounds.max.z - at.z >= at.z - bounds.min.z;
            float low = float.MaxValue, high = float.MinValue;
            foreach (Vector3 p in body)
            {
                bool atMuzzle = muzzleAhead ? p.z >= bounds.max.z - MuzzleLength : p.z <= bounds.min.z + MuzzleLength;
                if (!atMuzzle) continue;
                low = Mathf.Min(low, p.y);
                high = Mathf.Max(high, p.y);
            }
            float barrel = low <= high ? (low + high) * 0.5f : bounds.center.y;
            bool gripBelow = at.y <= barrel;
            float zFrom = muzzleAhead ? at.z - GripBack : at.z - GripAhead;
            float zTo = muzzleAhead ? at.z + GripAhead : at.z + GripBack;
            float yFrom = gripBelow ? bounds.min.y - Margin : at.y - GripAbove;
            float yTo = gripBelow ? at.y + GripAbove : bounds.max.y + Margin;
            note = (trigger.Count > 0 ? "grip found from the trigger" : "no trigger mesh, grip taken round the pivot") +
                   $": dark from z {zFrom:F2} to {zTo:F2}, {(gripBelow ? "below" : "above")} y {(gripBelow ? yTo : yFrom):F2}";
            return new GlowZone
            {
                mode = GlowZoneMode.Whole,
                dark = Enclosing(new Vector3(bounds.min.x - Margin, yFrom, zFrom), new Vector3(bounds.max.x + Margin, yTo, zTo)),
            };
        }

        private static GlowZone BowZone(out string note)
        {
            note = "glows all over but for its string";
            return default;
        }

        // ---------------------------------------------------------------- the mesh, in the model's own space

        /// <summary>
        /// A model read along its length: the box its surface fills in each slice of z, and which cells of its side
        /// view (z across, y up) hold any of it.
        /// </summary>
        private sealed class Outline
        {
            public Bounds bounds;
            public float zMin, zMax;
            public int count;
            public Vector2[] low, high;     // each slice's corners across the weapon: x = thickness, y = height
            public bool[] filled;
            private int first;              // slices sit on multiples of the slice size, so a re-run reads the same
            private bool[,] cells;          // [slice, row of y]
            private int rows;

            public static Outline Of(GameObject root)
            {
                List<Vector3> points = SamplePoints(root, null);
                if (points.Count == 0) return null;
                var o = new Outline { bounds = BoundsOf(points) };
                o.zMin = o.bounds.min.z;
                o.zMax = o.bounds.max.z;
                o.first = Mathf.FloorToInt(o.zMin / Slice);
                o.count = Mathf.FloorToInt(o.zMax / Slice) - o.first + 1;
                o.low = new Vector2[o.count];
                o.high = new Vector2[o.count];
                o.filled = new bool[o.count];
                o.rows = Mathf.FloorToInt(o.bounds.size.y / Cell) + 1;
                o.cells = new bool[o.count, o.rows];
                foreach (Vector3 p in points)
                {
                    int i = o.IndexOf(p.z);
                    var at = new Vector2(p.x, p.y);
                    if (!o.filled[i]) { o.low[i] = o.high[i] = at; o.filled[i] = true; }
                    else { o.low[i] = Vector2.Min(o.low[i], at); o.high[i] = Vector2.Max(o.high[i], at); }
                    o.cells[i, o.RowOf(p.y)] = true;
                }
                return o;
            }

            public float At(int i) => (first + i) * Slice;
            public int IndexOf(float z) => Mathf.Clamp(Mathf.FloorToInt(z / Slice + 0.001f) - first, 0, count - 1);
            public float Tall(int i) => filled[i] ? high[i].y - low[i].y : 0f;
            public float Thick(int i) => filled[i] ? high[i].x - low[i].x : 0f;
            public float Width(int i) => Mathf.Max(Tall(i), Thick(i));
            private int RowOf(float y) => Mathf.Clamp(Mathf.FloorToInt((y - bounds.min.y) / Cell), 0, rows - 1);

            /// <summary>
            /// The unbroken stretch of the side view, in one slice, that holds the heights from <paramref name="yFrom"/>
            /// to <paramref name="yTo"/>: how far it reaches up and down. A hole of one cell does not break it. False
            /// when nothing of the model is at those heights there.
            /// </summary>
            public bool Run(int i, float yFrom, float yTo, out float runFrom, out float runTo)
            {
                runFrom = runTo = 0f;
                int lowest = -1, highest = -1;
                for (int r = RowOf(yFrom); r <= RowOf(yTo); r++)
                {
                    if (!cells[i, r]) continue;
                    if (lowest < 0) lowest = r;
                    highest = r;
                }
                if (lowest < 0) return false;
                while (lowest > 0 && (cells[i, lowest - 1] || (lowest > 1 && cells[i, lowest - 2]))) lowest--;
                while (highest < rows - 1 && (cells[i, highest + 1] || (highest < rows - 2 && cells[i, highest + 2]))) highest++;
                runFrom = bounds.min.y + lowest * Cell;
                runTo = bounds.min.y + (highest + 1) * Cell;
                return true;
            }

            /// <summary>The middle value of a measure over the slices the hand is on.</summary>
            public float GripMedian(System.Func<int, float> measure) => GripMeasure(measure, 0.5f);

            /// <summary>
            /// A measure over the slices the hand is on, sorted: <paramref name="share"/> 0.5 is the middle value,
            /// 0.25 the one a quarter of the way up from the smallest.
            /// </summary>
            public float GripMeasure(System.Func<int, float> measure, float share)
            {
                var values = new List<float>();
                for (int i = IndexOf(GripFrom); i <= IndexOf(GripTo); i++)
                    if (filled[i]) values.Add(measure(i));
                if (values.Count == 0) values.Add(measure(IndexOf(0f)));
                values.Sort();
                return values[Mathf.Min(values.Count - 1, Mathf.FloorToInt(values.Count * share))];
            }

            /// <summary>
            /// Where the glow starts on a weapon with nothing wider than its grip: past the last place near the hand
            /// where it suddenly narrows (the far side of a guard), or failing that a quarter of the way out.
            /// </summary>
            public float NothingWider(out string note)
            {
                for (int i = IndexOf(GuardWithin); i >= 2; i--)
                {
                    float behind = Mathf.Max(Width(i - 1), Width(i - 2));
                    if (behind <= 0f || Width(i) > behind * NarrowTo || zMax - At(i) < BladeAtLeast) continue;
                    note = $"blade no wider than the hilt: past the guard, from {At(i):F2} m";
                    return At(i);
                }
                float start = Mathf.Max(0f, zMax * NoGuardFrom);
                note = $"no wider part: glows from {start:F2} m ahead of the grip";
                return start;
            }

            /// <summary>The zone from a point along the weapon to its tip; all of it when that is next to nothing.</summary>
            public GlowZone From(float start, ref string note)
            {
                if (zMax - start < ZoneAtLeast)
                {
                    note = "too small to split: glows all over";
                    return default;
                }
                return new GlowZone
                {
                    mode = GlowZoneMode.Only,
                    box = Enclosing(new Vector3(bounds.min.x - Margin, bounds.min.y - Margin, start),
                                    new Vector3(bounds.max.x + Margin, bounds.max.y + Margin, zMax + Margin)),
                };
            }
        }

        /// <summary>
        /// Points all over the model's surface: every face sampled finely, edge to edge. Enough to read the model's
        /// outline anywhere along it, which bare vertices are not (a blade is two long faces). Only the meshes that
        /// are switched on count: a prefab can carry several models and show one.
        /// </summary>
        private static List<Vector3> SamplePoints(GameObject root, string onlyNamed)
        {
            var points = new List<Vector3>(4096);
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                else if (renderer.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (onlyNamed != null && !renderer.name.Contains(onlyNamed)) continue;
                Matrix4x4 toModel = toRoot * renderer.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toModel.MultiplyPoint3x4(vertices[i]);
                points.AddRange(vertices);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                    float longest = Mathf.Max(Vector3.Distance(a, b), Mathf.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                    int steps = Mathf.Min(MostSteps, Mathf.CeilToInt(longest / EdgeStep));
                    for (int i = 0; i <= steps; i++)
                        for (int j = 0; j <= steps - i; j++)
                            if (steps > 1) points.Add(a + (b - a) * (i / (float)steps) + (c - a) * (j / (float)steps));
                }
            }
            return points;
        }

        private static float Median(List<float> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }

        private static Bounds BoundsOf(List<Vector3> points)
        {
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++) bounds.Encapsulate(points[i]);
            return bounds;
        }

        private static Bounds Enclosing(Vector3 min, Vector3 max)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }
    }
}
