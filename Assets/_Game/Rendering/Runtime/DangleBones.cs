using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Swings the loose pieces of a Sidekick character: capes, sashes, skirts, long hair, beards. Synty rigs those
    /// pieces with chains of dangle bones inside the part (<c>&lt;slot&gt;_dyn_NN_NN_&lt;side&gt;</c>, for example
    /// <c>tors_dyn_01_01_l</c> under <c>spine_03</c> or <c>abac_dyn_01</c> under <c>backAttach</c>), which nothing
    /// animates. After every merge <see cref="Bind"/> finds the chains the worn mesh uses; each frame they are pulled
    /// by gravity, lag a little behind the body's movement, spring back to the pose the animation gives them, and are
    /// pushed out of capsules on the body bones (spine, pelvis, legs, upper arms, head) so cloth never passes through
    /// the body. A piece that already rests inside a capsule in the bind pose (a cape lying on the back) keeps that
    /// closeness, so nothing is pushed off the body at rest.
    /// <para>
    /// Presentation only: nothing is networked, every machine swings its own. Runs only while the character is seen,
    /// at full rate near the camera, every other frame at the character LOD's second level, and not at all from its
    /// third level on (<see cref="CharacterLod"/>), where the pieces rest in their bind pose. Never on a dedicated
    /// server.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)] // after the animation, IK and the appearance's own LateUpdate merge
    public sealed class DangleBones : MonoBehaviour
    {
        /// <summary>How one kind of dangle piece moves, picked by the bone name's slot prefix.</summary>
        [Serializable]
        public struct Profile
        {
            [Tooltip("Slot prefixes this profile is for, comma separated (abac = back attachment, tors = torso, hair, fchr = facial hair...).")]
            public string prefixes;
            [Tooltip("How hard a bone springs back to its animated pose each 1/60 s (0 = limp, 1 = stiff).")]
            [Range(0f, 1f)] public float stiffness;
            [Tooltip("How much swing is lost each 1/60 s.")]
            [Range(0f, 1f)] public float damping;
            [Tooltip("Gravity, as a share of the world's.")]
            [Range(0f, 2f)] public float gravity;
            [Tooltip("How much of the body's own movement the piece is carried along with (0 = trails fully behind, 1 = none).")]
            [Range(0f, 1f)] public float follow;
            [Tooltip("Thickness of the piece around each bone, metres, kept off the body capsules.")]
            [Range(0f, 0.1f)] public float radius;

            public Profile(string prefixes, float stiffness, float damping, float gravity, float follow, float radius)
            {
                this.prefixes = prefixes; this.stiffness = stiffness; this.damping = damping;
                this.gravity = gravity; this.follow = follow; this.radius = radius;
            }
        }

        /// <summary>A capsule on the body: from bone <c>a</c> to bone <c>b</c>, or past <c>a</c> away from <c>b</c> by <c>extend</c> metres.</summary>
        private struct BodyCapsule
        {
            public string a, b;
            public float radius, extend;
            public BodyCapsule(string a, string b, float radius, float extend = 0f) { this.a = a; this.b = b; this.radius = radius; this.extend = extend; }
        }

        // Sizes at the Sidekick body's own scale (metres); they scale with the character (the height slider).
        private static readonly BodyCapsule[] Capsules =
        {
            new BodyCapsule("pelvis", "spine_02", 0.12f),
            new BodyCapsule("spine_02", "neck_01", 0.12f),
            new BodyCapsule("head", "neck_01", 0.10f, 0.13f),
            new BodyCapsule("thigh_l", "calf_l", 0.08f),
            new BodyCapsule("thigh_r", "calf_r", 0.08f),
            new BodyCapsule("calf_l", "foot_l", 0.06f),
            new BodyCapsule("calf_r", "foot_r", 0.06f),
            new BodyCapsule("upperarm_l", "lowerarm_l", 0.055f),
            new BodyCapsule("upperarm_r", "lowerarm_r", 0.055f),
        };

        private const string DynTag = "_dyn_";
        private const float Step60 = 1f / 60f;
        private const float TeleportMetres = 2.5f;

        [SerializeField] private Profile[] profiles =
        {
            new Profile("abac,ahpb,ahpf,ahpl,ahpr,hips,legl,legr,tors", 0.06f, 0.10f, 1.0f, 0.35f, 0.02f), // cloth
            new Profile("hair", 0.10f, 0.14f, 0.7f, 0.5f, 0.015f),
            new Profile("fchr", 0.25f, 0.2f, 0.4f, 0.7f, 0.01f),
            new Profile("", 0.2f, 0.18f, 0.5f, 0.6f, 0.01f), // anything else (ears, shoulder and head trinkets)
        };

        // Particles: one per dangle bone, plus a tip past every leaf so the leaf has a direction to point along.
        // Ordered parents first. A pinned particle is a chain's first bone: it stays where its body bone puts it.
        private Transform[] bone = Array.Empty<Transform>();   // null for a tip
        private int[] parent = Array.Empty<int>();             // particle index, -1 when pinned
        private int[] leafOf = Array.Empty<int>();             // for a tip: the bone particle it extends
        private Vector3[] tipLocal = Array.Empty<Vector3>();   // for a tip: its point in the leaf's space
        private Vector3[] restPos = Array.Empty<Vector3>();    // the bone's local pose at rest
        private Quaternion[] restRot = Array.Empty<Quaternion>();
        private int[] profileOf = Array.Empty<int>();
        private Vector3[] pos = Array.Empty<Vector3>(), prev = Array.Empty<Vector3>(), target = Array.Empty<Vector3>();
        private int[] firstChild = Array.Empty<int>(), nextSibling = Array.Empty<int>();
        private float[] reach = Array.Empty<float>();          // [particle * capsules + capsule]: share of the radius kept free
        private Transform[] capA = Array.Empty<Transform>(), capB = Array.Empty<Transform>();
        private float[] capRadius = Array.Empty<float>(), capExtend = Array.Empty<float>();
        private Vector3[] segA = Array.Empty<Vector3>(), segB = Array.Empty<Vector3>();
        private SkinnedMeshRenderer[] skins = Array.Empty<SkinnedMeshRenderer>();
        private CharacterLod lod;
        private int count, capsuleCount;
        private bool live, posed;
        private float pending, prevDt = Step60;
        private int frame;
        private Vector3 lastRoot;

        // Rest poses survive a rebind: a bone the last merge already swung is put back before it is read again.
        private readonly Dictionary<Transform, (Vector3 p, Quaternion r)> rest = new Dictionary<Transform, (Vector3, Quaternion)>();
        private readonly Dictionary<string, Transform> byName = new Dictionary<string, Transform>(StringComparer.Ordinal);

        /// <summary>The number of dangle bones swung (0 when the worn parts have none).</summary>
        public int BoneCount { get; private set; }

        /// <summary>
        /// Swings the dangle bones of the mesh <paramref name="renderers"/> now draw. Called by the character's
        /// appearance after every merge; adds the component where it is missing. Does nothing on a machine that draws
        /// nothing (a dedicated server).
        /// </summary>
        public static DangleBones Bind(GameObject character, IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            if (character == null || Application.isBatchMode) return null;
            if (!character.TryGetComponent(out DangleBones dangle))
            {
                if (!HasDangle(renderers)) return null; // no component for characters that never wear a loose piece
                dangle = character.AddComponent<DangleBones>();
            }
            dangle.Rebind(renderers);
            return dangle;
        }

        private static bool HasDangle(IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            if (renderers == null) return false;
            for (int r = 0; r < renderers.Count; r++)
            {
                var smr = renderers[r];
                if (smr == null) continue;
                foreach (var b in smr.bones)
                    if (b != null && b.name.Contains(DynTag)) return true;
            }
            return false;
        }

        private void OnDisable() => RestorePose();

        /// <summary>Finds the dangle chains <paramref name="renderers"/> skin with (use <see cref="Bind"/> in the game).</summary>
        public void Rebind(IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            RestorePose();
            lod = GetComponentInParent<CharacterLod>();
            var list = new List<SkinnedMeshRenderer>();
            if (renderers != null) foreach (var r in renderers) if (r != null) list.Add(r);
            skins = list.ToArray();

            // Every skinned dangle bone and its dangle ancestors, with the bind matrix of each skinned bone.
            var bind = new Dictionary<Transform, Matrix4x4>();
            var dyn = new HashSet<Transform>();
            byName.Clear();
            foreach (var smr in skins)
            {
                var bones = smr.bones;
                var mesh = smr.sharedMesh;
                var poses = mesh != null ? mesh.bindposes : Array.Empty<Matrix4x4>();
                for (int i = 0; i < bones.Length; i++)
                {
                    var b = bones[i];
                    if (b == null) continue;
                    byName.TryAdd(b.name, b);
                    if (i < poses.Length && !bind.ContainsKey(b)) bind[b] = poses[i].inverse;
                    for (var t = b; t != null && t.name.Contains(DynTag); t = t.parent) dyn.Add(t);
                }
            }

            // A bone kept from an earlier piece may now be worn by another one that rests it elsewhere (two pieces that
            // name a chain alike): where the worn mesh's bind poses put it away from the rest kept for it, theirs win.
            foreach (var t in dyn)
            {
                if (t.parent == null || !bind.TryGetValue(t, out var self) || !bind.TryGetValue(t.parent, out var up)) continue;
                var local = up.inverse * self;
                Vector3 p = local.GetColumn(3);
                if (rest.TryGetValue(t, out var kept) && (kept.p - p).sqrMagnitude <= 0.0001f) continue;
                rest[t] = (p, local.rotation);
                t.SetLocalPositionAndRotation(p, local.rotation);
            }

            // Order parents first; collect children among the swung bones.
            var order = new List<Transform>();
            var index = new Dictionary<Transform, int>();
            var roots = new List<Transform>();
            foreach (var t in dyn) if (t.parent == null || !dyn.Contains(t.parent)) roots.Add(t);
            roots.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
            var stack = new Stack<Transform>();
            foreach (var root in roots)
            {
                stack.Push(root);
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    index[t] = order.Count;
                    order.Add(t);
                    for (int c = t.childCount - 1; c >= 0; c--)
                        if (dyn.Contains(t.GetChild(c))) stack.Push(t.GetChild(c));
                }
            }

            BoneCount = order.Count;
            int leaves = 0;
            var tipOf = new Dictionary<Transform, Vector3>();
            foreach (var t in order)
            {
                bool hasChild = false;
                for (int c = 0; c < t.childCount && !hasChild; c++) hasChild = dyn.Contains(t.GetChild(c));
                if (hasChild) continue;
                leaves++;
                if (!rest.ContainsKey(t)) rest[t] = (t.localPosition, BindRotation(t, bind));
                tipOf[t] = TipPoint(t, rest[t].p, rest[t].r);
            }

            count = order.Count + leaves;
            bone = new Transform[count]; parent = new int[count]; leafOf = new int[count]; tipLocal = new Vector3[count];
            restPos = new Vector3[count]; restRot = new Quaternion[count]; profileOf = new int[count];
            pos = new Vector3[count]; prev = new Vector3[count]; target = new Vector3[count];
            firstChild = new int[count]; nextSibling = new int[count];
            for (int i = 0; i < count; i++) { firstChild[i] = -1; nextSibling[i] = -1; leafOf[i] = -1; }

            int k = 0;
            foreach (var t in order)
            {
                bone[k] = t;
                if (!rest.ContainsKey(t)) rest[t] = (t.localPosition, BindRotation(t, bind));
                (restPos[k], restRot[k]) = rest[t];
                parent[k] = t.parent != null && index.TryGetValue(t.parent, out int pi) ? pi : -1;
                profileOf[k] = ProfileFor(t.name);
                k++;
            }
            foreach (var t in order)
            {
                if (!tipOf.TryGetValue(t, out var local)) continue;
                int li = index[t];
                leafOf[k] = li; parent[k] = li; tipLocal[k] = local; profileOf[k] = profileOf[li];
                k++;
            }
            for (int i = count - 1; i >= 0; i--)
            {
                if (parent[i] < 0) continue;
                nextSibling[i] = firstChild[parent[i]];
                firstChild[parent[i]] = i;
            }

            BuildCapsules(bind);
            live = false;
            enabled = count > 0;
        }

        /// <summary>
        /// A bone's rest turn from the bind poses, not from the live bone: a copy of a character made mid-swing (the
        /// equipment preview doll) would otherwise take the swing for its rest. The live bone is kept where its parent
        /// has no bind pose or the two disagree on where the bone sits (a swing turns bones, it never moves them).
        /// </summary>
        private static Quaternion BindRotation(Transform t, Dictionary<Transform, Matrix4x4> bind)
        {
            if (t.parent == null || !bind.TryGetValue(t, out var self) || !bind.TryGetValue(t.parent, out var up)) return t.localRotation;
            var local = up.inverse * self;
            Vector3 p = local.GetColumn(3);
            float tolerance = 0.002f + 0.01f * t.localPosition.magnitude;
            return (p - t.localPosition).sqrMagnitude <= tolerance * tolerance ? local.rotation : t.localRotation;
        }

        /// <summary>Where a leaf's tip sits in its own space: one more step of the same length past it.</summary>
        private static Vector3 TipPoint(Transform leaf, Vector3 restPosition, Quaternion restRotation)
        {
            for (int c = 0; c < leaf.childCount; c++) // an unskinned dangle child of its own already says where
                if (leaf.GetChild(c).name.Contains(DynTag)) return leaf.GetChild(c).localPosition;
            if (restPosition.sqrMagnitude < 1e-8f) return leaf.InverseTransformDirection(Vector3.down) * 0.05f;
            Vector3 s = leaf.localScale;
            Vector3 local = Quaternion.Inverse(restRotation) * restPosition; // the step from the parent, in the leaf's own turn
            return new Vector3(local.x / NonZero(s.x), local.y / NonZero(s.y), local.z / NonZero(s.z));
        }

        private static float NonZero(float v) => Mathf.Abs(v) < 1e-6f ? 1f : v;

        private int ProfileFor(string boneName)
        {
            int cut = boneName.IndexOf(DynTag, StringComparison.Ordinal);
            string prefix = cut > 0 ? boneName.Substring(0, cut) : boneName;
            int fallback = profiles.Length - 1;
            for (int p = 0; p < profiles.Length; p++)
            {
                string list = profiles[p].prefixes;
                if (string.IsNullOrEmpty(list)) { fallback = p; continue; }
                foreach (var entry in list.Split(','))
                    if (string.Equals(entry.Trim(), prefix, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return Mathf.Max(0, fallback);
        }

        private void BuildCapsules(Dictionary<Transform, Matrix4x4> bind)
        {
            var a = new List<Transform>(); var b = new List<Transform>();
            var radius = new List<float>(); var extend = new List<float>();
            var ai = new List<int>();
            for (int c = 0; c < Capsules.Length; c++)
            {
                byName.TryGetValue(Capsules[c].a, out var ta);
                byName.TryGetValue(Capsules[c].b, out var tb);
                if (ta == null || tb == null) continue;
                a.Add(ta); b.Add(tb); radius.Add(Capsules[c].radius); extend.Add(Capsules[c].extend); ai.Add(c);
            }
            capsuleCount = a.Count;
            capA = a.ToArray(); capB = b.ToArray(); capRadius = radius.ToArray(); capExtend = extend.ToArray();
            segA = new Vector3[capsuleCount]; segB = new Vector3[capsuleCount];
            reach = new float[count * capsuleCount];
            for (int i = 0; i < reach.Length; i++) reach[i] = 1f;
            if (capsuleCount == 0) return;

            // Bind space is the merged mesh's: measure its scale against the Sidekick body (pelvis to head).
            float bindScale = 1f;
            if (byName.TryGetValue("pelvis", out var pelvis) && byName.TryGetValue("head", out var head)
                && bind.TryGetValue(pelvis, out var bp) && bind.TryGetValue(head, out var bh))
            {
                float bindLen = Vector3.Distance(bp.GetColumn(3), bh.GetColumn(3));
                float liveLen = Vector3.Distance(pelvis.position, head.position) / Mathf.Max(1e-4f, Scale());
                if (bindLen > 1e-4f && liveLen > 1e-4f) bindScale = bindLen / liveLen;
            }

            // Bind positions of the particles (unskinned ancestors from their parent's bind and their rest pose).
            var bindOf = new Dictionary<Transform, Matrix4x4>();
            Matrix4x4 BindOf(Transform t)
            {
                if (bindOf.TryGetValue(t, out var m)) return m;
                if (bind.TryGetValue(t, out m)) return bindOf[t] = m;
                var up = t.parent != null ? BindOf(t.parent) : Matrix4x4.identity;
                var (lp, lr) = rest.TryGetValue(t, out var r) ? r : (t.localPosition, t.localRotation);
                return bindOf[t] = up * Matrix4x4.TRS(lp, lr, t.localScale);
            }
            var bindPos = new Vector3[count];
            for (int i = 0; i < count; i++)
                bindPos[i] = bone[i] != null ? (Vector3)BindOf(bone[i]).GetColumn(3) : BindOf(bone[leafOf[i]]).MultiplyPoint3x4(tipLocal[i]);

            for (int c = 0; c < capsuleCount; c++)
            {
                Vector3 p0 = BindOf(capA[c]).GetColumn(3), p1 = BindOf(capB[c]).GetColumn(3);
                if (capExtend[c] > 0f) { var dir = (p0 - p1).normalized; p1 = p0 + dir * capExtend[c] * bindScale; }
                for (int i = 0; i < count; i++)
                {
                    if (parent[i] < 0) continue;
                    float r = (capRadius[c] + profiles[profileOf[i]].radius) * bindScale;
                    float d = Vector3.Distance(bindPos[i], ClosestOnSegment(p0, p1, bindPos[i]));
                    reach[i * capsuleCount + c] = Mathf.Clamp01(d / r);
                }
            }
        }

        private float Scale()
        {
            var s = transform.lossyScale;
            return Mathf.Max(1e-4f, Mathf.Abs(s.y));
        }

        private void Update() => BackToRest();

        /// <summary>One frame of <paramref name="dt"/> seconds without the animation: back to rest, then one step of the swing (for the tests).</summary>
        public void Step(float dt)
        {
            BackToRest();
            Simulate(dt);
        }

        private void BackToRest()
        {
            // The animation writes over these bones only if it has keys for them; either way the frame starts from rest.
            if (!posed) return;
            posed = false;
            for (int i = 0; i < count; i++)
            {
                var t = bone[i];
                if (t == null) continue;
                t.localPosition = restPos[i];
                t.localRotation = restRot[i];
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (count == 0 || dt <= 0f) return;
            int level = lod != null ? Mathf.Max(0, lod.Tier) : 0;
            if (level >= 2 || !Seen()) { live = false; return; } // rests in its bind pose (Update put it there)
            pending += dt;
            frame++;
            if (level == 1 && (frame & 1) == 1 && live) { Hold(); return; } // half rate at the second level
            float step = Mathf.Min(pending, 1f / 20f);
            pending = 0f;
            Simulate(step);
        }

        private bool Seen()
        {
            for (int i = 0; i < skins.Length; i++)
                if (skins[i] != null && skins[i].isVisible && !skins[i].forceRenderingOff) return true;
            return false;
        }

        /// <summary>Between two half-rate steps: the pieces keep the last swing, carried along with the body.</summary>
        private void Hold() => Aim(transform.position - lastRoot);

        /// <summary>One step of the swing, <paramref name="dt"/> seconds long.</summary>
        private void Simulate(float dt)
        {
            float scale = Scale();
            ReadTargets();
            Vector3 root = transform.position;
            if (!live || (root - lastRoot).sqrMagnitude > TeleportMetres * TeleportMetres)
            {
                for (int i = 0; i < count; i++) { pos[i] = target[i]; prev[i] = target[i]; }
                live = true;
                lastRoot = root;
                prevDt = dt;
                return; // the bones already stand where the animation put them

            }
            Vector3 moved = root - lastRoot;
            lastRoot = root;

            for (int c = 0; c < capsuleCount; c++)
            {
                Vector3 p0 = capA[c].position, p1 = capB[c].position;
                if (capExtend[c] > 0f) p1 = p0 + (p0 - p1).normalized * capExtend[c] * scale;
                segA[c] = p0; segB[c] = p1;
            }

            float frames = dt / Step60;
            float velScale = Mathf.Clamp(dt / Mathf.Max(1e-4f, prevDt), 0.25f, 4f);
            prevDt = dt;
            Vector3 gravity = Physics.gravity * (dt * dt);
            for (int i = 0; i < count; i++)
            {
                int pi = parent[i];
                if (pi < 0) { pos[i] = target[i]; prev[i] = target[i]; continue; }
                var prof = profiles[profileOf[i]];
                Vector3 carry = moved * prof.follow;
                Vector3 velocity = (pos[i] - prev[i]) * (Mathf.Pow(1f - prof.damping, frames) * velScale);
                prev[i] = pos[i] + carry;
                Vector3 p = pos[i] + velocity + carry + gravity * prof.gravity;

                Vector3 restVec = target[i] - target[pi];
                float length = restVec.magnitude;
                Vector3 goal = pos[pi] + restVec;
                p += (goal - p) * (1f - Mathf.Pow(1f - prof.stiffness, frames));

                p = Keep(pos[pi], p, length);
                float pr = prof.radius * scale;
                for (int c = 0; c < capsuleCount; c++)
                {
                    float r = (capRadius[c] * scale + pr) * reach[i * capsuleCount + c];
                    if (r <= 0f) continue;
                    Vector3 q = ClosestOnSegment(segA[c], segB[c], p);
                    Vector3 d = p - q;
                    float m = d.sqrMagnitude;
                    if (m >= r * r || m < 1e-10f) continue;
                    p = q + d * (r / Mathf.Sqrt(m));
                }
                pos[i] = p;
            }
            Aim(Vector3.zero);
        }

        private static Vector3 Keep(Vector3 anchor, Vector3 p, float length)
        {
            Vector3 d = p - anchor;
            float m = d.magnitude;
            return m > 1e-6f ? anchor + d * (length / m) : p;
        }

        /// <summary>Where the animation (or the bind pose) puts every particle this frame.</summary>
        private void ReadTargets()
        {
            for (int i = 0; i < count; i++)
                target[i] = bone[i] != null ? bone[i].position : bone[leafOf[i]].TransformPoint(tipLocal[i]);
        }

        /// <summary>Turns each bone, parents first, so it points at where its children swung (moved by <paramref name="shift"/>).</summary>
        private void Aim(Vector3 shift)
        {
            posed = true;
            for (int i = 0; i < count; i++)
            {
                var t = bone[i];
                if (t == null || firstChild[i] < 0) continue;
                Vector3 from = Vector3.zero, to = Vector3.zero;
                Vector3 at = t.position;
                for (int c = firstChild[i]; c >= 0; c = nextSibling[c])
                {
                    Vector3 local = bone[c] != null ? restPos[c] : tipLocal[c];
                    from += t.TransformPoint(local) - at;
                    to += pos[c] + shift - at;
                }
                if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) continue;
                t.rotation = Quaternion.FromToRotation(from, to) * t.rotation;
            }
        }

        /// <summary>Puts every swung bone back in its rest pose (done when the component is turned off).</summary>
        public void RestorePose()
        {
            for (int i = 0; i < count; i++)
            {
                var t = bone[i];
                if (t == null) continue;
                t.localPosition = restPos[i];
                t.localRotation = restRot[i];
            }
            live = false;
            posed = false;
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            if (len < 1e-10f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
            return a + ab * t;
        }
    }
}
