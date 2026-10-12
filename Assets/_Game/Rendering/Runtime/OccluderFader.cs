using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Dither-fades world props that stand between the local camera and the player (client-only, local camera only).
    /// Every <see cref="CheckInterval"/> it sphere-casts camera → player (chest and head). Hit props swap to cached
    /// "KryticalNova/Occluder Dither" clones of their materials and ease their per-renderer _Fade up; once clear they
    /// ease back and get their original materials. Only occluders pay anything; no per-tick allocations.
    /// Ground/terrain are never faded (only <see cref="occluderLayers"/> are tested).
    /// A player going fast is looked ahead for: at the top Run rank the camera covers three metres between two
    /// checks and nine in the time a fade takes, so a tree or a truck filled the screen before it began to fade.
    /// Above <see cref="FastSpeed"/> the check runs every frame and also along the line the camera will be looking
    /// down a moment from now, so what is about to stand in the way is already thin when it does.
    /// <para><b>What a fade may cost is bounded</b> (11 Oct 2026, after one wall of the North Gate took the frame from
    /// 5 ms to 19 ms of the graphics card). A prop of a few pieces fades whole; of a prop of many pieces (a building
    /// under one collider) only the pieces the line of sight passes through fade. No more than
    /// <see cref="MostFadedRenderers"/> pieces are faded at once, whatever stands in the way: the rest stay solid.
    /// And a piece is listed in <see cref="Faded"/> with the number of parts that are its own
    /// (<see cref="OwnSubmeshes"/>): a piece of the world's combined static mesh answers for the whole of that mesh
    /// when its mesh is asked, 208 parts for one wall, and whoever drew "the faded props whole" drew a town.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OccluderFader : MonoBehaviour
    {
        private const float CheckInterval = 0.1f;
        private const float CastRadius = 0.35f;
        private const float FadedAmount = 0.82f;
        private const float FadeSeconds = 0.2f;
        private const float SeeThroughAmount = 0.3f;
        private const float FastSpeed = 8f;            // metres a second: above walking, the look-ahead is on
        private const float LookAheadSeconds = 0.3f;   // a fade and a half
        private const float SpeedEase = 12f;
        /// <summary>The most pieces faded at one time. More than this in the way, and the rest stay solid.</summary>
        public const int MostFadedRenderers = 48;
        // A prop of up to this many pieces fades whole; a larger one only where the line of sight goes through it.
        private const int WholeUpTo = 8;
        private const float SightPadding = 0.75f;      // metres round the line of sight that count as in the way
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField] private LayerMask occluderLayers = 1; // Default
        [SerializeField] private Shader ditherShader;

        private sealed class Occluder
        {
            public Renderer[] renderers;
            public Material[][] originals;
            public Material[][] dithered;
            // Piece by piece: how far it is faded, when the line of sight last went through it, whether it wears its dither.
            public float[] fades;
            public int[] seenTicks;
            public bool[] swapped;
            public float fade;      // the most faded of its pieces: what "can be seen through" is asked of
            public int seenTick;
        }

        /// <summary>
        /// Every renderer drawn faded right now, with its number of submeshes. The depth the camera keeps has the
        /// fade's holes in it: what must not show through a fading prop as if it were not there (a glow's light)
        /// draws these whole (<see cref="GlowHaloFeature"/>).
        /// </summary>
        public static IReadOnlyList<(Renderer renderer, int submeshes)> Faded => FadedList;
        private static readonly List<(Renderer renderer, int submeshes)> FadedList = new List<(Renderer, int)>(16);

        // Enter Play Mode runs without a domain reload, so the list must be emptied by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => FadedList.Clear();

        private Transform target;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Dictionary<Collider, Occluder> byCollider = new Dictionary<Collider, Occluder>();
        private readonly List<Occluder> active = new List<Occluder>();
        private readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
        private MaterialPropertyBlock block;
        private float nextCheck;
        private int tick;
        private int fadedCount;
        // The pieces faded enough to see through, and what each faded piece wore before its dither.
        private readonly HashSet<Renderer> seeThrough = new HashSet<Renderer>();
        private readonly Dictionary<Renderer, Material[]> worn = new Dictionary<Renderer, Material[]>();
        private Vector3 lastTargetPosition, targetVelocity;
        private bool hasLastTargetPosition;

        public void Configure(Transform player, Shader shader)
        {
            if (target != player) hasLastTargetPosition = false;
            target = player;
            if (shader != null) ditherShader = shader;
            if (ditherShader == null) ditherShader = Shader.Find("KryticalNova/Occluder Dither");
        }

        private void Awake() => block = new MaterialPropertyBlock();

        /// <summary>
        /// True while what this collider stands for is drawn faded enough to see through. What can be seen through
        /// can be clicked through: pointer picks skip it, so the ground behind a faded building stays selectable.
        /// It is asked of what is drawn, not of the collider the line of sight happened to meet: a prop with two
        /// colliders is see-through for a click on either.
        /// </summary>
        public bool IsFaded(Collider col) => IsFaded(col, false, default);

        /// <summary>
        /// The same for a pointer's hit. Of a prop of many pieces only the faded ones are see-through, so the piece
        /// under the point clicked decides.
        /// </summary>
        public bool IsFaded(in RaycastHit hit) => IsFaded(hit.collider, true, hit.point);

        private bool IsFaded(Collider col, bool atPoint, Vector3 point)
        {
            if (col == null || seeThrough.Count == 0) return false; // nothing is faded: nothing is looked up
            if (!byCollider.TryGetValue(col, out var o))
            {
                o = Build(col);
                byCollider[col] = o;
            }
            if (o == null) return false;
            bool whole = !atPoint || o.renderers.Length <= WholeUpTo;
            for (int r = 0; r < o.renderers.Length; r++)
            {
                Renderer piece = o.renderers[r];
                if (piece == null || !seeThrough.Contains(piece)) continue;
                if (whole) return true;
                Bounds bounds = piece.bounds;
                bounds.Expand(SightPadding);
                if (bounds.Contains(point)) return true;
            }
            return false;
        }

        private void OnDisable()
        {
            for (int i = active.Count - 1; i >= 0; i--) Restore(active[i]);
            active.Clear();
            fadedCount = 0;
            seeThrough.Clear();
            worn.Clear();
        }

        /// <summary>
        /// The parts of a renderer that are its own to draw: as many as it has materials, and no more than its mesh
        /// has. A piece of a static batch shows the batch's combined mesh as its own, with every part of every piece
        /// in it; its materials are still only its own.
        /// </summary>
        public static int OwnSubmeshes(Renderer renderer)
        {
            if (renderer == null) return 0;
            int materials = renderer.sharedMaterials.Length;
            if (renderer.isPartOfStaticBatch) return materials;
            return renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null
                ? Mathf.Min(materials, filter.sharedMesh.subMeshCount) : 0;
        }

        private void OnDestroy()
        {
            foreach (var m in clones.Values) if (m != null) Destroy(m);
            clones.Clear();
        }

        private void LateUpdate()
        {
            if (target == null || ditherShader == null || !target.gameObject.activeInHierarchy) return;

            Vector3 ahead = TravelAhead();
            bool fast = ahead.sqrMagnitude > 0f;
            if (fast || Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckInterval;
                tick++;
                Vector3 eye = transform.position;
                Vector3 chest = target.position + Vector3.up * 1.2f;
                Cast(eye, chest);
                Cast(eye, target.position + Vector3.up * 1.8f);
                if (fast)
                {
                    // The camera travels with the player: where both will be half a look-ahead on, and a whole one.
                    Cast(eye + ahead * 0.5f, chest + ahead * 0.5f);
                    Cast(eye + ahead, chest + ahead);
                }
            }

            float step = Time.deltaTime / FadeSeconds * FadedAmount;
            // A check every frame would let go of a prop two frames after the line cleared it; it is held as long
            // as two ordinary checks, as when standing.
            int held = fast ? Mathf.Max(1, Mathf.CeilToInt(CheckInterval / Mathf.Max(Time.unscaledDeltaTime, 0.001f))) : 1;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var o = active[i];
                float most = 0f;
                bool any = false;
                for (int r = 0; r < o.renderers.Length; r++)
                {
                    bool occluding = tick - o.seenTicks[r] <= held; // still in the way on this or the previous check
                    if (!occluding && !o.swapped[r]) continue;
                    if (!o.swapped[r] && !Swap(o, r)) continue;      // over the limit: it stays solid
                    float goal = occluding ? FadedAmount : 0f;
                    if (!Mathf.Approximately(o.fades[r], goal))
                    {
                        o.fades[r] = Mathf.MoveTowards(o.fades[r], goal, step);
                        if (o.fades[r] <= 0f && !occluding)
                        {
                            Restore(o, r);
                            continue;
                        }
                        ApplyFade(o, r);
                    }
                    if (o.fades[r] >= SeeThroughAmount) seeThrough.Add(o.renderers[r]); else seeThrough.Remove(o.renderers[r]);
                    any = true;
                    if (o.fades[r] > most) most = o.fades[r];
                }
                o.fade = most;
                if (!any) active.RemoveAt(i);
            }
        }

        /// <summary>
        /// How far the player travels in <see cref="LookAheadSeconds"/> at its present speed, or zero below
        /// <see cref="FastSpeed"/>. The speed is eased, so one long frame or a placement does not read as a sprint.
        /// </summary>
        private Vector3 TravelAhead()
        {
            Vector3 now = target.position;
            float delta = Time.deltaTime;
            if (!hasLastTargetPosition || delta <= 0f)
            {
                hasLastTargetPosition = true;
                lastTargetPosition = now;
                targetVelocity = Vector3.zero;
                return Vector3.zero;
            }
            Vector3 velocity = (now - lastTargetPosition) / delta;
            lastTargetPosition = now;
            velocity.y = 0f;
            targetVelocity = Vector3.Lerp(targetVelocity, velocity, 1f - Mathf.Exp(-delta * SpeedEase));
            return targetVelocity.sqrMagnitude >= FastSpeed * FastSpeed ? targetVelocity * LookAheadSeconds : Vector3.zero;
        }

        private void Cast(Vector3 from, Vector3 aim)
        {
            Vector3 dir = aim - from;
            float dist = dir.magnitude - CastRadius; // stop short of the player
            if (dist <= 0.1f) return;
            Vector3 along = dir / (dist + CastRadius);
            int count = Physics.SphereCastNonAlloc(from, CastRadius, along, hits, dist,
                occluderLayers, QueryTriggerInteraction.Ignore);
            var sight = new Ray(from, along);
            for (int i = 0; i < count; i++)
            {
                var col = hits[i].collider;
                if (col == null || col.transform.IsChildOf(target)) continue;
                if (!byCollider.TryGetValue(col, out var o))
                {
                    o = Build(col);
                    byCollider[col] = o; // null is cached too: colliders with nothing to fade
                }
                if (o == null) continue;
                if (o.seenTick != tick && !active.Contains(o)) active.Add(o);
                o.seenTick = tick;
                // A prop of a few pieces is in the way as one thing. Of a prop of many, only the pieces the line of
                // sight goes through are: a building under one collider is not made see-through from end to end.
                bool whole = o.renderers.Length <= WholeUpTo;
                for (int r = 0; r < o.renderers.Length; r++)
                {
                    if (!whole)
                    {
                        Renderer piece = o.renderers[r];
                        if (piece == null) continue;
                        Bounds bounds = piece.bounds;
                        bounds.Expand(SightPadding * 2f);
                        if (!bounds.Contains(from) && (!bounds.IntersectRay(sight, out float entered) || entered > dist)) continue;
                    }
                    o.seenTicks[r] = tick;
                }
            }
        }

        private Occluder Build(Collider col)
        {
            Renderer[] renderers;
            var lod = col.GetComponentInParent<LODGroup>();
            if (lod != null) renderers = lod.GetComponentsInChildren<Renderer>();
            else
            {
                renderers = col.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    var parent = col.GetComponentInParent<Renderer>();
                    renderers = parent != null ? new[] { parent } : renderers;
                }
            }

            var kept = new List<Renderer>(renderers.Length);
            foreach (var r in renderers)
                if (r is MeshRenderer && r.enabled && IsOpaque(worn.TryGetValue(r, out Material[] real) ? real : r.sharedMaterials)) kept.Add(r);
            if (kept.Count == 0) return null;

            var o = new Occluder
            {
                renderers = kept.ToArray(),
                originals = new Material[kept.Count][],
                dithered = new Material[kept.Count][],
                fades = new float[kept.Count],
                seenTicks = new int[kept.Count],
                swapped = new bool[kept.Count],
            };
            for (int i = 0; i < kept.Count; i++)
            {
                // A piece another collider's prop has faded right now wears its dither: what it wore before is its own.
                o.originals[i] = worn.TryGetValue(kept[i], out Material[] real) ? real : kept[i].sharedMaterials;
                o.seenTicks[i] = int.MinValue / 2;
            }
            return o;
        }

        private static bool IsOpaque(Material[] mats)
        {
            foreach (var m in mats) if (m == null || m.renderQueue >= 2500) return false;
            return mats.Length > 0;
        }

        // One piece into its dither. False when as many are faded as may be: it stays as it is.
        private bool Swap(Occluder o, int index)
        {
            Renderer r = o.renderers[index];
            // Over the limit, or already faded as a piece of another collider's prop: that one answers for it.
            if (r == null || fadedCount >= MostFadedRenderers || worn.ContainsKey(r)) return false;
            // The dither copies of its materials are made the first time it fades, not when it is first met.
            if (o.dithered[index] == null)
            {
                Material[] mats = o.originals[index];
                o.dithered[index] = new Material[mats.Length];
                for (int m = 0; m < mats.Length; m++) o.dithered[index][m] = CloneFor(mats[m]);
            }
            worn[r] = o.originals[index];
            r.sharedMaterials = o.dithered[index];
            int submeshes = OwnSubmeshes(r);
            if (submeshes > 0) FadedList.Add((r, submeshes));
            o.swapped[index] = true;
            fadedCount++;
            return true;
        }

        private void Restore(Occluder o)
        {
            for (int i = 0; i < o.renderers.Length; i++)
                if (o.swapped[i]) Restore(o, i);
            o.fade = 0f;
        }

        private void Restore(Occluder o, int index)
        {
            Renderer r = o.renderers[index];
            if (r != null)
            {
                r.sharedMaterials = o.originals[index];
                r.SetPropertyBlock(null);
            }
            // A piece that was destroyed while faded is still let go of.
            if (!ReferenceEquals(r, null))
            {
                seeThrough.Remove(r);
                worn.Remove(r);
            }
            for (int i = FadedList.Count - 1; i >= 0; i--)
                if (FadedList[i].renderer == null || FadedList[i].renderer == r) FadedList.RemoveAt(i);
            o.swapped[index] = false;
            o.fades[index] = 0f;
            fadedCount = Mathf.Max(0, fadedCount - 1);
        }

        private void ApplyFade(Occluder o, int index)
        {
            Renderer r = o.renderers[index];
            if (r == null) return;
            r.GetPropertyBlock(block);
            block.SetFloat(FadeId, o.fades[index]);
            r.SetPropertyBlock(block);
        }

        /// <summary>One dither clone per source material, copying the Synty Generic_Basic (or URP Lit) inputs.</summary>
        private Material CloneFor(Material src)
        {
            if (clones.TryGetValue(src, out var clone)) return clone;
            clone = new Material(ditherShader) { name = src.name + " (Occluder Dither)" };
            CopyTexture(src, clone, "_Albedo_Map", "_Albedo_Map", "_BaseMap", "_MainTex");
            CopyTexture(src, clone, "_Normal_Map", "_Normal_Map", "_BumpMap");
            bool emissive = !src.HasProperty("_Enable_Emission") || src.GetFloat("_Enable_Emission") > 0.5f;
            if (emissive)
            {
                CopyTexture(src, clone, "_Emission_Map", "_Emission_Map", "_EmissionMap");
                CopyColor(src, clone, "_Emission_Color", "_Emission_Color", "_EmissionColor");
            }
            CopyColor(src, clone, "_BaseColor", "_BaseColor", "_Color");
            CopyFloat(src, clone, "_Normal_Amount", "_Normal_Amount", "_BumpScale");
            CopyFloat(src, clone, "_Smoothness", "_Smoothness", "_Glossiness");
            CopyFloat(src, clone, "_Metallic", "_Metallic");
            CopyFloat(src, clone, "_Cutoff", "_Alpha_Clip_Threshold", "_Cutoff");
            clones[src] = clone;
            return clone;
        }

        private static void CopyTexture(Material src, Material dst, string to, params string[] from)
        {
            foreach (var name in from)
            {
                if (!src.HasProperty(name) || src.GetTexture(name) == null) continue;
                dst.SetTexture(to, src.GetTexture(name));
                dst.SetTextureScale(to, src.GetTextureScale(name));
                dst.SetTextureOffset(to, src.GetTextureOffset(name));
                return;
            }
        }

        private static void CopyColor(Material src, Material dst, string to, params string[] from)
        {
            foreach (var name in from)
                if (src.HasProperty(name)) { dst.SetColor(to, src.GetColor(name)); return; }
        }

        private static void CopyFloat(Material src, Material dst, string to, params string[] from)
        {
            foreach (var name in from)
                if (src.HasProperty(name)) { dst.SetFloat(to, src.GetFloat(name)); return; }
        }
    }
}
