using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Instant, self-expiring Scene View hint: when a preset chip is clicked or a fracture
    /// parameter changes, the planned cut layout is flashed directly on the source model
    /// for a few seconds as an x-ray line grid. No solver run, no scene objects, no assets;
    /// the overlay fades out and unhooks itself automatically.
    /// </summary>
    internal static class PatternHintOverlay
    {
        const float Duration = 4f;
        const float FadeSeconds = 1.1f;

        sealed class OverlayState
        {
            public MeshFilter s_filter;
            public DestructionProfile s_profile;
            public MeshFilter s_trackedFilter;
            public DestructionProfile s_trackedProfile;
            public FracturePattern s_pattern;
            public int s_seed;
            public double s_expiresAt;
            public bool s_hooked;
            public double s_nextRepaintAt;
            public System.Action ImpactPointMoved;
        }

        static readonly OverlayState s_state = new OverlayState();


        /// <summary>Flashes the cut layout of <paramref name="pattern"/> on the source
        /// model. The profile is optional: without one, preset defaults are sketched.</summary>
        public static void Show(GameObject source, DestructionProfile profile,
            FracturePattern pattern)
        {
            MeshFilter filter = source != null
                ? source.GetComponentInChildren<MeshFilter>()
                : null;
            if (filter == null || filter.sharedMesh == null) return;

            s_state.s_filter = filter;
            s_state.s_profile = profile;
            s_state.s_pattern = pattern;
            s_state.s_seed = profile != null ? profile.Seed : 12345;
            s_state.s_expiresAt = EditorApplication.timeSinceStartup + Duration;
            s_state.s_nextRepaintAt = 0d; // Repaint immediately on first show
            if (!s_state.s_hooked)
            {
                s_state.s_hooked = true;
                SceneView.duringSceneGui += OnSceneGUI;
                EditorApplication.update += Tick;
            }
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Keeps the impact handle alive in the Scene view for as long as the Impact
        /// pattern is being edited, independently of the few-second hint flash. The
        /// window calls this while it is open; the handle only draws when the profile
        /// actually uses a placed hit.
        /// </summary>
        public static void Track(GameObject source, DestructionProfile profile)
        {
            MeshFilter filter = source != null
                ? source.GetComponentInChildren<MeshFilter>()
                : null;
            if (filter == null || filter.sharedMesh == null)
            {
                s_state.s_trackedFilter = null;
                s_state.s_trackedProfile = null;
                return;
            }
            s_state.s_trackedFilter = filter;
            s_state.s_trackedProfile = profile;
            if (!s_state.s_hooked)
            {
                s_state.s_hooked = true;
                SceneView.duringSceneGui += OnSceneGUI;
                EditorApplication.update += Tick;
            }
        }

        public static void Hide()
        {
            if (!s_state.s_hooked) return;
            s_state.s_hooked = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= Tick;
            s_state.s_filter = null;
            s_state.s_profile = null;
            SceneView.RepaintAll();
        }

        static void Tick()
        {
            bool tracking = s_state.s_trackedFilter != null && s_state.s_trackedProfile != null &&
                            s_state.s_trackedProfile.Pattern == FracturePattern.ImpactShatter;
            if (EditorApplication.timeSinceStartup > s_state.s_expiresAt || s_state.s_filter == null)
            {
                // The flash is over, but the impact handle must stay grabbable.
                if (tracking)
                {
                    s_state.s_filter = null;
                    s_state.s_profile = null;
                    return;
                }
                Hide();
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if (now >= s_state.s_nextRepaintAt)
            {
                s_state.s_nextRepaintAt = now + 0.033d; // limit to 30 FPS
                SceneView.RepaintAll();
            }
        }

        static float CurrentAlpha()
        {
            float remaining = (float)(s_state.s_expiresAt - EditorApplication.timeSinceStartup);
            return Mathf.Clamp01(remaining / FadeSeconds);
        }

        static void OnSceneGUI(SceneView sceneView)
        {
            DrawImpactHandle();

            if (s_state.s_filter == null || s_state.s_filter.sharedMesh == null ||
                Event.current.type != EventType.Repaint)
                return;

            float alpha = CurrentAlpha();
            if (alpha <= 0f) return;

            Bounds bounds = s_state.s_filter.sharedMesh.bounds;
            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            var previousZTest = Handles.zTest;
            Handles.matrix = s_state.s_filter.transform.localToWorldMatrix;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            var rng = new System.Random(s_state.s_seed);
            Color accent = QDUI.Accent;
            Handles.color = new Color(accent.r, accent.g, accent.b, 0.85f * alpha);

            switch (s_state.s_pattern)
            {
                case FracturePattern.RadialGlass: DrawGlass(bounds, rng); break;
                case FracturePattern.DirectionalWood: DrawWood(bounds, rng); break;
                case FracturePattern.BrickCells: DrawBrick(bounds, rng); break;
                case FracturePattern.LayeredSlabs: DrawSlabs(bounds, rng); break;
                case FracturePattern.ColumnarPillars: DrawColumns(bounds, rng); break;
                case FracturePattern.ImpactShatter: DrawImpact(bounds, rng); break;
                default: DrawConcrete(bounds, rng); break;
            }

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        /// <summary>
        /// Draggable hit position for Impact Shatter. The handle lives in the source's own
        /// space and is stored as a bounds fraction, so moving or rescaling the prop keeps
        /// the hit where it was placed. Dragging marks the profile dirty and refreshes the
        /// hint, which is what makes the placement visible immediately.
        /// </summary>
        static void DrawImpactHandle()
        {
            if (s_state.s_trackedFilter == null || s_state.s_trackedFilter.sharedMesh == null ||
                s_state.s_trackedProfile == null ||
                s_state.s_trackedProfile.Pattern != FracturePattern.ImpactShatter ||
                !s_state.s_trackedProfile.ImpactPointOverride)
                return;

            Bounds bounds = s_state.s_trackedFilter.sharedMesh.bounds;
            Transform owner = s_state.s_trackedFilter.transform;
            Vector3 local = FractureEngine.ImpactPointFromNormalized(
                bounds, s_state.s_trackedProfile.ImpactPointLocal);
            Vector3 world = owner.TransformPoint(local);

            float size = HandleUtility.GetHandleSize(world);
            Color previous = Handles.color;
            Handles.color = new Color(1f, 0.45f, 0.1f, 0.95f);
            Handles.DrawWireDisc(world, sceneNormal(world), size * 0.28f);
            Handles.DrawWireDisc(world, sceneNormal(world), size * 0.14f);
            Handles.color = previous;

            EditorGUI.BeginChangeCheck();
            Vector3 movedWorld = Handles.PositionHandle(world, owner.rotation);
            if (!EditorGUI.EndChangeCheck()) return;

            Vector3 movedLocal = owner.InverseTransformPoint(movedWorld);
            var normalized = new Vector3(
                Mathf.InverseLerp(bounds.min.x, bounds.max.x, movedLocal.x),
                Mathf.InverseLerp(bounds.min.y, bounds.max.y, movedLocal.y),
                Mathf.InverseLerp(bounds.min.z, bounds.max.z, movedLocal.z));
            Undo.RecordObject(s_state.s_trackedProfile, "Move Impact Point");
            s_state.s_trackedProfile.ImpactPointLocal = normalized;
            EditorUtility.SetDirty(s_state.s_trackedProfile);
            s_state.ImpactPointMoved?.Invoke();

            Vector3 sceneNormal(Vector3 point) =>
                SceneView.currentDrawingSceneView != null
                    ? -SceneView.currentDrawingSceneView.camera.transform.forward
                    : Vector3.up;
        }

        /// <summary>Raised after the Scene handle moved the hit, so the window can rebuild
        /// its preview without polling.</summary>
        internal static void AddImpactPointMovedListener(System.Action listener)
        {
            s_state.ImpactPointMoved += listener;
        }

        internal static void RemoveImpactPointMovedListener(System.Action listener)
        {
            s_state.ImpactPointMoved -= listener;
        }

        // ------------------------------------------------------------------ face helpers

        static Vector3 SmallestAxis(Vector3 size)
        {
            if (size.x <= size.y && size.x <= size.z) return Vector3.right;
            if (size.y <= size.z) return Vector3.up;
            return Vector3.forward;
        }

        static Vector3 LargestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return Vector3.right;
            if (size.y >= size.z) return Vector3.up;
            return Vector3.forward;
        }

        static void FaceBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            u = Vector3.Cross(normal,
                Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            v = Vector3.Cross(normal, u);
        }

        static void Line(Vector3 a, Vector3 b, float width = 2.5f)
        {
            Handles.DrawAAPolyLine(width, a, b);
        }

        /// <summary>Rectangle outline of the bounds cross-section at coordinate
        /// <paramref name="t"/> (0..1) along <paramref name="axis"/>.</summary>
        static void SliceRing(Bounds bounds, Vector3 axis, float t, float width = 2.5f)
        {
            FaceBasis(axis, out Vector3 u, out Vector3 v);
            float extentA = Mathf.Abs(Vector3.Dot(axis, bounds.extents));
            float extentU = Mathf.Abs(Vector3.Dot(u, bounds.extents));
            float extentV = Mathf.Abs(Vector3.Dot(v, bounds.extents));
            Vector3 center = bounds.center + axis * Mathf.Lerp(-extentA, extentA, t);
            Vector3 c00 = center - u * extentU - v * extentV;
            Vector3 c10 = center + u * extentU - v * extentV;
            Vector3 c11 = center + u * extentU + v * extentV;
            Vector3 c01 = center - u * extentU + v * extentV;
            Line(c00, c10, width);
            Line(c10, c11, width);
            Line(c11, c01, width);
            Line(c01, c00, width);
        }

        static float Jittered(System.Random rng, int index, int cells, float jitter)
        {
            return Mathf.Clamp01((float)index / cells +
                ((float)rng.NextDouble() - 0.5f) * Mathf.Clamp01(jitter) * 0.25f / cells);
        }

        // ------------------------------------------------------------------ patterns

        static void DrawConcrete(Bounds bounds, System.Random rng)
        {
            int fragments = s_state.s_profile != null ? s_state.s_profile.FragmentCount : 24;
            float strength = s_state.s_profile != null
                ? Mathf.Clamp01(s_state.s_profile.DirectionalCutStrength)
                : 0f;
            Vector3 preferred = s_state.s_profile != null &&
                                s_state.s_profile.PreferredCutAxis != AxisMode.Auto
                ? FractureEngine.ResolveAxis(s_state.s_profile.PreferredCutAxis, bounds.size)
                : LargestAxis(bounds.size);

            DrawSubdividedCuts(bounds, rng, fragments, null,
                (box, r) => SteeredNormal(r, preferred, strength, box));
        }

        /// <summary>
        /// Mirrors what the solver does, on boxes instead of meshes: repeatedly take the
        /// largest remaining piece and cut it. Drawing one ring per cut inside the piece it
        /// belongs to is why the hint now thins out where the fracture will actually be
        /// dense, instead of showing full-height rings that no later cut ever produces.
        /// Everything is driven by the profile seed, so one seed always draws one layout.
        /// </summary>
        static void DrawSubdividedCuts(Bounds bounds, System.Random rng, int fragments,
            System.Func<Bounds, float> priority,
            System.Func<Bounds, System.Random, Vector3> normalPicker)
        {
            int cuts = Mathf.Clamp(fragments - 1, 1, 42);
            var boxes = new List<Bounds> { bounds };
            for (int i = 0; i < cuts; i++)
            {
                int best = 0;
                float bestScore = float.NegativeInfinity;
                for (int b = 0; b < boxes.Count; b++)
                {
                    Vector3 s = boxes[b].size;
                    float score = s.x * s.y * s.z;
                    if (priority != null) score *= priority(boxes[b]);
                    if (score > bestScore) { bestScore = score; best = b; }
                }

                Bounds box = boxes[best];
                Vector3 normal = normalPicker(box, rng);
                float t = 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.5f;
                SliceRing(box, normal, Mathf.Clamp01(t), 2f);

                // Bookkeeping stays axis aligned: the ring shows the true cut angle, the
                // split only has to decide which halves the next cuts will land in.
                int axis = Mathf.Abs(normal.x) >= Mathf.Abs(normal.y) &&
                           Mathf.Abs(normal.x) >= Mathf.Abs(normal.z) ? 0
                    : Mathf.Abs(normal.y) >= Mathf.Abs(normal.z) ? 1 : 2;
                Vector3 min = box.min;
                Vector3 max = box.max;
                float split = Mathf.Lerp(AxisValue(min, axis), AxisValue(max, axis),
                    Mathf.Clamp(t, 0.15f, 0.85f));
                boxes[best] = BoundsFromTo(min, WithAxis(max, axis, split));
                boxes.Add(BoundsFromTo(WithAxis(min, axis, split), max));
            }
        }

        static Vector3 SteeredNormal(System.Random rng, Vector3 preferred,
            float strength, Bounds box)
        {
            Vector3 normal = new Vector3(
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 2f - 1f);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            normal = normal.normalized;
            // Proportion control: an elongated piece is cut across its long side, the
            // same correction the solver applies before the style bias.
            Vector3 size = box.size;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float shortest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (shortest > 1e-6f && longest / shortest > 2.5f)
            {
                Vector3 longAxis = LargestAxis(size);
                if (Vector3.Dot(normal, longAxis) < 0f) longAxis = -longAxis;
                normal = Vector3.Slerp(normal, longAxis, 0.6f).normalized;
            }
            if (Vector3.Dot(normal, preferred) < 0f) preferred = -preferred;
            return Vector3.Slerp(normal, preferred, strength).normalized;
        }

        static float AxisValue(Vector3 value, int axis) =>
            axis == 0 ? value.x : axis == 1 ? value.y : value.z;

        static Vector3 WithAxis(Vector3 value, int axis, float component)
        {
            if (axis == 0) value.x = component;
            else if (axis == 1) value.y = component;
            else value.z = component;
            return value;
        }

        static Bounds BoundsFromTo(Vector3 min, Vector3 max)
        {
            var result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }

        static void DrawCrackWeb(Bounds bounds, Vector3 normal, System.Random rng,
            int cracks, float roughness)
        {
            FaceBasis(normal, out Vector3 u, out Vector3 v);
            float extentN = Mathf.Abs(Vector3.Dot(normal, bounds.extents));
            float extentU = Mathf.Abs(Vector3.Dot(u, bounds.extents));
            float extentV = Mathf.Abs(Vector3.Dot(v, bounds.extents));
            Vector3 faceCenter = bounds.center + normal.normalized * extentN;

            for (int i = 0; i < cracks; i++)
            {
                // Enter on one edge, exit on the opposite edge, wobble in between.
                bool horizontal = rng.NextDouble() < 0.5;
                Vector3 walkAxis = horizontal ? u : v;
                Vector3 sideAxis = horizontal ? v : u;
                float walkExtent = horizontal ? extentU : extentV;
                float sideExtent = horizontal ? extentV : extentU;
                float side = ((float)rng.NextDouble() * 2f - 1f) * sideExtent * 0.8f;

                const int segments = 5;
                Vector3 previous = faceCenter - walkAxis * walkExtent + sideAxis * side;
                for (int s = 1; s <= segments; s++)
                {
                    float walk = Mathf.Lerp(-walkExtent, walkExtent, (float)s / segments);
                    side += ((float)rng.NextDouble() - 0.5f) * sideExtent * roughness;
                    side = Mathf.Clamp(side, -sideExtent, sideExtent);
                    Vector3 next = faceCenter + walkAxis * walk + sideAxis * side;
                    Line(previous, next, 2f);
                    previous = next;
                }
            }
        }

        static void DrawGlass(Bounds bounds, System.Random rng)
        {
            Vector3 normal = FractureEngine.ResolveAxis(
                s_state.s_profile != null ? s_state.s_profile.GlassNormalAxis : AxisMode.Auto,
                bounds.size, false);
            FaceBasis(normal, out Vector3 u, out Vector3 v);
            int spokes = s_state.s_profile != null ? s_state.s_profile.RadialSpokes : 10;
            int rings = s_state.s_profile != null ? s_state.s_profile.RadialRings : 3;
            float jitter = s_state.s_profile != null ? s_state.s_profile.RadialJitter : 0.35f;
            float extentN = Mathf.Abs(Vector3.Dot(normal, bounds.extents));
            float extentU = Mathf.Abs(Vector3.Dot(u, bounds.extents));
            float extentV = Mathf.Abs(Vector3.Dot(v, bounds.extents));
            float maxRadius = Mathf.Sqrt(extentU * extentU + extentV * extentV);

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 center = bounds.center + normal * extentN * side;
                for (int i = 0; i < spokes; i++)
                {
                    float angle = (i + ((float)rng.NextDouble() - 0.5f) * jitter) *
                                  Mathf.PI * 2f / spokes;
                    Vector3 direction = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                    Line(center, center + direction * maxRadius, 2f);
                }
                for (int j = 1; j <= rings; j++)
                {
                    float radius = maxRadius * j / (rings + 1f);
                    Handles.DrawWireDisc(center, normal, radius);
                }
            }
        }

        static void DrawWood(Bounds bounds, System.Random rng)
        {
            Vector3 fiber = FractureEngine.ResolveAxis(
                s_state.s_profile != null ? s_state.s_profile.WoodAxis : AxisMode.Auto,
                bounds.size);
            // Both families are drawn as full section rings, the same shape the solver's
            // planes actually leave behind, and their counts follow Fragment Count and
            // Lengthwise Split Bias instead of a fixed eight lines per face.
            float lengthwiseBias = s_state.s_profile != null
                ? Mathf.Clamp01(s_state.s_profile.FiberCutShare)
                : 0.8f;
            int fragments = s_state.s_profile != null ? s_state.s_profile.FragmentCount : 24;
            int cuts = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(fragments) * 1.5f), 3, 14);
            int crosswise = Mathf.Clamp(
                Mathf.RoundToInt(cuts * (1f - lengthwiseBias)), 0, cuts);
            int lengthwise = Mathf.Max(1, cuts - crosswise);

            Vector3 basisU = Vector3.Cross(fiber,
                Mathf.Abs(fiber.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 basisV = Vector3.Cross(fiber, basisU);

            // Splits along the grain: the plane contains the fibre axis, so its normal
            // rotates freely around it and the piece stays long.
            for (int i = 0; i < lengthwise; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                Vector3 normal = (basisU * Mathf.Cos(angle) +
                                  basisV * Mathf.Sin(angle)).normalized;
                SliceRing(bounds, normal,
                    Jittered(rng, i + 1, lengthwise + 1, 0.8f), 2.5f);
            }
            // Cross cuts: normal along the grain, chopping the plank into shorter pieces.
            for (int i = 0; i < crosswise; i++)
            {
                SliceRing(bounds, fiber,
                    Jittered(rng, i + 1, crosswise + 1, 0.8f), 1.5f);
            }
        }

        static void DrawBrick(Bounds bounds, System.Random rng)
        {
            Vector3Int grid = s_state.s_profile != null
                ? FractureEngine.GetBrickGrid(bounds, s_state.s_profile)
                : new Vector3Int(3, 4, 3);
            float jitter = s_state.s_profile != null ? s_state.s_profile.BrickJitter : 0.35f;
            for (int i = 1; i < grid.x; i++)
                SliceRing(bounds, Vector3.right, Jittered(rng, i, grid.x, jitter));
            for (int i = 1; i < grid.y; i++)
                SliceRing(bounds, Vector3.up, Jittered(rng, i, grid.y, jitter));
            for (int i = 1; i < grid.z; i++)
                SliceRing(bounds, Vector3.forward, Jittered(rng, i, grid.z, jitter));
        }

        static void DrawSlabs(Bounds bounds, System.Random rng)
        {
            Vector3 axis = FractureEngine.ResolveAxis(
                s_state.s_profile != null ? s_state.s_profile.SlabNormalAxis : AxisMode.Auto,
                bounds.size, false);
            int layers = s_state.s_profile != null ? s_state.s_profile.SlabLayers : 6;
            float jitter = s_state.s_profile != null ? s_state.s_profile.SlabJitter : 0.25f;
            for (int i = 1; i < layers; i++)
                SliceRing(bounds, axis, Jittered(rng, i, layers, jitter), 3f);
            DrawPlateCuts(bounds, axis, rng, layers);
        }

        /// <summary>
        /// Cross cuts that break each sheet into plates. They follow the same direction
        /// rule as the solver (Plate Length Axis, Lengthwise Split Bias and Directional
        /// Influence), so dragging those sliders visibly changes the hint instead of
        /// re-rolling the same three random cracks.
        /// </summary>
        static void DrawPlateCuts(Bounds bounds, Vector3 axis, System.Random rng, int layers)
        {
            FaceBasis(axis, out Vector3 u, out Vector3 v);
            Vector3 plateLength = s_state.s_profile != null &&
                                  s_state.s_profile.PreferredCutAxis != AxisMode.Auto
                ? Vector3.ProjectOnPlane(
                    FractureEngine.ResolveAxis(s_state.s_profile.PreferredCutAxis, bounds.size),
                    axis).normalized
                : u;
            if (plateLength.sqrMagnitude < 1e-8f) plateLength = u;
            Vector3 plateWidth = Vector3.Cross(axis, plateLength).normalized;

            int fragments = s_state.s_profile != null ? s_state.s_profile.FragmentCount : 24;
            int cuts = Mathf.Clamp(
                Mathf.RoundToInt((float)fragments / Mathf.Max(1, layers)) - 1, 1, 6);
            float bias = s_state.s_profile != null ? s_state.s_profile.LengthwiseSplitBias : 0.5f;
            float strength = s_state.s_profile != null
                ? Mathf.Clamp01(s_state.s_profile.DirectionalCutStrength)
                : 0f;

            float extentAxis = Mathf.Abs(Vector3.Dot(axis, bounds.extents));
            float extentU = Mathf.Abs(Vector3.Dot(u, bounds.extents));
            float extentV = Mathf.Abs(Vector3.Dot(v, bounds.extents));
            float span = Mathf.Sqrt(extentU * extentU + extentV * extentV);

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 faceCenter = bounds.center + axis * extentAxis * side;
                for (int i = 0; i < cuts; i++)
                {
                    float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    Vector3 normal = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                    Vector3 preferred = rng.NextDouble() < bias ? plateWidth : plateLength;
                    if (Vector3.Dot(normal, preferred) < 0f) preferred = -preferred;
                    normal = Vector3.Slerp(normal, preferred, strength).normalized;
                    Vector3 direction = Vector3.Cross(axis, normal).normalized;
                    Vector3 offset = normal *
                        (((float)rng.NextDouble() - 0.5f) * 1.4f * span * 0.5f);
                    Vector3 point = faceCenter + offset;
                    Line(point - direction * span, point + direction * span, 2f);
                }
            }
        }

        static void DrawColumns(Bounds bounds, System.Random rng)
        {
            Vector3 axis = FractureEngine.ResolveAxis(
                s_state.s_profile != null ? s_state.s_profile.PillarAxis : AxisMode.Auto,
                bounds.size);
            FaceBasis(axis, out Vector3 u, out Vector3 v);
            int nu = 4;
            int nv = 4;
            if (s_state.s_profile != null)
                FractureEngine.GetColumnGrid(bounds, s_state.s_profile, u, v, out nu, out nv);
            float jitter = s_state.s_profile != null ? s_state.s_profile.ColumnJitter : 0.35f;
            for (int i = 1; i < nu; i++)
                SliceRing(bounds, u, Jittered(rng, i, nu, jitter));
            for (int i = 1; i < nv; i++)
                SliceRing(bounds, v, Jittered(rng, i, nv, jitter));
        }

        static void DrawImpact(Bounds bounds, System.Random rng)
        {
            // The solver does not draw rays from the hit: it subdivides, weighting the
            // choice by proximity to the impact point, so cuts crowd near the hit and
            // thin out with distance. The hint now does exactly that, with the same
            // impact origin the solver derives from this seed.
            Vector3 impact = s_state.s_profile != null && s_state.s_profile.ImpactPointOverride
                ? FractureEngine.ImpactPointFromNormalized(
                    bounds, s_state.s_profile.ImpactPointLocal)
                : FractureEngine.GetImpactPoint(bounds, rng);
            int fragments = s_state.s_profile != null ? s_state.s_profile.FragmentCount : 24;
            float focus = s_state.s_profile != null ? Mathf.Clamp01(s_state.s_profile.ImpactFocus) : 0.5f;
            float maxDimension = Mathf.Max(bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            Vector3 plateNormal = SmallestAxis(bounds.size);
            float thickness = Mathf.Abs(Vector3.Dot(plateNormal, bounds.size));
            Vector3 inPlaneSize = bounds.size - new Vector3(
                Mathf.Abs(plateNormal.x) * bounds.size.x,
                Mathf.Abs(plateNormal.y) * bounds.size.y,
                Mathf.Abs(plateNormal.z) * bounds.size.z);
            float inPlaneMinimum = Mathf.Max(1e-6f,
                Mathf.Min(inPlaneSize.x > 0f ? inPlaneSize.x : float.PositiveInfinity,
                    Mathf.Min(inPlaneSize.y > 0f ? inPlaneSize.y : float.PositiveInfinity,
                        inPlaneSize.z > 0f ? inPlaneSize.z : float.PositiveInfinity)));
            bool planarImpact = thickness <= inPlaneMinimum * 0.18f;

            DrawSubdividedCuts(bounds, rng, fragments,
                box =>
                {
                    float distance = Vector3.Distance(box.center, impact);
                    float proximity = Mathf.Pow(
                        1f / (0.15f + distance / Mathf.Max(maxDimension, 1e-4f)),
                        1f + 2f * focus);
                    return Mathf.Lerp(1f, proximity, focus);
                },
                (box, r) =>
                {
                    Vector3 normal = new Vector3(
                        (float)r.NextDouble() * 2f - 1f,
                        (float)r.NextDouble() * 2f - 1f,
                        (float)r.NextDouble() * 2f - 1f);
                    if (planarImpact)
                        normal = Vector3.ProjectOnPlane(normal, plateNormal);
                    return normal.sqrMagnitude < 1e-6f
                        ? Vector3.Cross(plateNormal,
                            Mathf.Abs(plateNormal.y) < 0.9f
                                ? Vector3.up : Vector3.right).normalized
                        : normal.normalized;
                });

            Handles.DrawWireDisc(impact, SmallestAxis(bounds.size),
                maxDimension * 0.04f);
        }
    }
}
