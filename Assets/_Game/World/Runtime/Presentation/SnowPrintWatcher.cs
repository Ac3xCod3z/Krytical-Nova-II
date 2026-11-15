using System.Collections.Generic;
using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Footprints in the snow (owner, 14 Nov 2026: "foot prints instead of the trail effect ... we know foot placement"). Watches the feet
    /// of the characters nearest the camera (players, NPCs, Hostiles, mounts; how many is the World Effects tier's
    /// <see cref="SeasonSettings.PrintWalkers"/>) and, every stride, adds a print at that side's foot turned to the body's heading (in deep snow a ploughed path instead)
    /// (<see cref="SnowPrints"/>; the track map stamps it). Humanoids are watched by their foot bones (where the foot IK put them), beasts and
    /// mounts by their foot, hoof or paw bones (a paw print), anything with neither by its stride (left, right, left).
    /// <para>
    /// One component on the season presenter, no script on any character; it looks for characters once a second, only while there is snow
    /// to print into. Presentation only, on every machine alike; it never switches anything off (build 12 black-sky rule).
    /// </para>
    /// </summary>
    public sealed class SnowPrintWatcher : MonoBehaviour
    {
        const float ScanSeconds = 1f;

        sealed class Walker
        {
            public Animator Animator;
            public Transform[] Feet;
            public bool[] Lifted;
            public float[] Rest;
            public bool Paw;
            public float Size = 1f;
            public Vector3 LastStride;
            public int Side;
            public bool Seen;
            public bool Skip;
            public Vector3[] LastFoot;
            public Vector3 LastBody;
            public Vector3 LastPath;
        }

        readonly List<Walker> m_Walkers = new List<Walker>(32);
        readonly Dictionary<Animator, Walker> m_Known = new Dictionary<Animator, Walker>(64);
        readonly List<(float distance, Animator animator)> m_Near = new List<(float, Animator)>(64);
        readonly List<Animator> m_Gone = new List<Animator>(16);
        readonly List<Transform> m_Bones = new List<Transform>(8);
        float m_NextScan;

        /// <summary>Characters watched now (tests, GM status).</summary>
        public int Watching => m_Walkers.Count;

        /// <summary>
        /// Once a frame from the season presenter: watch feet round <paramref name="centre"/>, or stop (null: no snow).
        /// </summary>
        public void Watch(Vector3? centre, SeasonSettings settings, float depthMetres = 0f)
        {
            if (centre == null || settings == null)
            {
                if (m_Walkers.Count > 0) { m_Walkers.Clear(); m_Known.Clear(); }
                m_NextScan = 0f;
                return;
            }
            if (Time.unscaledTime >= m_NextScan)
            {
                m_NextScan = Time.unscaledTime + ScanSeconds;
                Scan(centre.Value, settings.PrintWalkers(WorldEffectsRuntime.Quality), settings.trackMetres * 0.5f);
            }
            for (int i = 0; i < m_Walkers.Count; i++)
            {
                Walker w = m_Walkers[i];
                if (w.Animator == null || !w.Animator.isActiveAndEnabled) continue;
                Step(w, settings, depthMetres);
            }
        }

        void Scan(Vector3 centre, int most, float reach)
        {
            m_Near.Clear();
            float reachSq = reach * reach;
            foreach (Animator a in FindObjectsByType<Animator>())
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                float d = (a.transform.position - centre).sqrMagnitude;
                if (d <= reachSq) m_Near.Add((d, a));
            }
            m_Near.Sort((x, y) => x.distance.CompareTo(y.distance));

            foreach (Walker w in m_Known.Values) w.Seen = false;
            m_Walkers.Clear();
            for (int i = 0; i < m_Near.Count && m_Walkers.Count < most; i++)
            {
                Animator a = m_Near[i].animator;
                if (!m_Known.TryGetValue(a, out Walker w)) { w = Make(a); m_Known[a] = w; }
                w.Seen = true;
                m_Walkers.Add(w);
            }
            // forget characters gone or far away (they start fresh when they come back)
            m_Gone.Clear();
            foreach (KeyValuePair<Animator, Walker> kv in m_Known) if (kv.Key == null || !kv.Value.Seen) m_Gone.Add(kv.Key);
            foreach (Animator a in m_Gone) m_Known.Remove(a);
        }

        Walker Make(Animator a)
        {
            var w = new Walker { Animator = a, LastStride = a.transform.position, LastBody = a.transform.position, LastPath = a.transform.position };
            // a generic animator over or under a humanoid one (a body's "Visuals" holder) is the same feet: watch them once, as boots
            if (!a.isHuman)
            {
                foreach (Animator other in a.GetComponentsInChildren<Animator>())
                    if (other != a && other.isHuman) { w.Skip = true; return w; }
                Animator parent = a.transform.parent != null ? a.transform.parent.GetComponentInParent<Animator>() : null;
                if (parent != null && parent.isHuman) { w.Skip = true; return w; }
            }
            w.Size = Mathf.Clamp(a.transform.lossyScale.y, 0.3f, 6f);
            if (a.isHuman && a.avatar != null)
            {
                Transform l = a.GetBoneTransform(HumanBodyBones.LeftFoot), r = a.GetBoneTransform(HumanBodyBones.RightFoot);
                if (l != null && r != null) w.Feet = new[] { l, r };
            }
            if (w.Feet == null)
            {
                // a beast or a mount: its foot, hoof or paw bones (the lowest of each chain), paw prints sized by the body
                m_Bones.Clear();
                foreach (Transform t in a.GetComponentsInChildren<Transform>())
                {
                    string n = t.name.ToLowerInvariant();
                    if ((n.Contains("foot") || n.Contains("hoof") || n.Contains("paw")) && !n.Contains("end") && !n.Contains("ik") && !HasFootChild(t))
                        m_Bones.Add(t);
                    if (m_Bones.Count == 4) break;
                }
                if (m_Bones.Count >= 2) { w.Feet = m_Bones.ToArray(); w.Paw = true; }
                Renderer body = a.GetComponentInChildren<Renderer>();
                if (body != null) w.Size = Mathf.Clamp(body.bounds.size.y / 1.8f, 0.3f, 6f);
            }
            if (w.Feet != null)
            {
                w.Lifted = new bool[w.Feet.Length];
                w.LastFoot = new Vector3[w.Feet.Length];
                for (int i = 0; i < w.Feet.Length; i++) w.LastFoot[i] = w.Feet[i] != null ? w.Feet[i].position : a.transform.position;
                w.Rest = new float[w.Feet.Length];
                for (int i = 0; i < w.Rest.Length; i++) w.Rest[i] = float.MaxValue;
            }
            return w;
        }

        static bool HasFootChild(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                string n = t.GetChild(i).name.ToLowerInvariant();
                if ((n.Contains("foot") || n.Contains("hoof") || n.Contains("paw")) && !n.Contains("end")) return true;
            }
            return false;
        }

        void Step(Walker w, SeasonSettings settings, float depthMetres)
        {
            if (w.Skip) return;
            Transform body = w.Animator.transform;
            float yaw = body.eulerAngles.y;
            Vector3 heading = body.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f) heading = Vector3.forward;
            heading.Normalize();
            Vector2 size = (w.Paw ? settings.pawPrint : settings.bootPrint) * w.Size;
            SnowPrintShape shape = w.Paw ? SnowPrintShape.Paw : SnowPrintShape.Boot;

            // owner 14 Nov: deeper than a print's depth the body ploughs a path; prints and path blend near it
            float printMax = Mathf.Max(0.01f, settings.printMaxDepth);
            float path = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(printMax * 0.8f, printMax * 1.25f, depthMetres));
            Vector3 pathMoved = body.position - w.LastPath;
            pathMoved.y = 0f;
            if (pathMoved.sqrMagnitude > 25f) w.LastPath = body.position;   // a teleport: no path across the map
            else if (path > 0.01f && pathMoved.sqrMagnitude >= 0.25f * 0.25f)
            {
                SnowPrints.AddPath(w.LastPath, body.position, settings.pathHalfWidth * w.Size * (w.Paw ? 1.6f : 1f), path);
                w.LastPath = body.position;
            }
            else if (path <= 0.01f) w.LastPath = body.position;

            // a print every stride, left and right in turn (owner 14 Nov: every step leaves a print; the walk clip plants a foot only
            // every ~4 m at run speed, so the stride decides, and a watched foot bone only says where that side's foot is)
            Vector3 moved = body.position - w.LastStride;
            moved.y = 0f;
            float stride = Mathf.Max(0.2f, settings.strideMetres) * w.Size;
            if (moved.sqrMagnitude > 25f) { w.LastStride = body.position; return; }   // a teleport
            if (moved.sqrMagnitude < stride * stride) return;
            w.LastStride = body.position;
            w.Side = 1 - w.Side;
            if (path >= 0.99f) return;   // deep snow: the path only
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            Vector3 at = body.position + right * ((w.Side == 0 ? -0.12f : 0.12f) * w.Size);
            if (w.Feet != null && w.Feet.Length >= 2)
            {
                // the foot on this side, where the animation and foot IK put it, if it is near the body's path
                Transform foot = null;
                float best = float.MinValue;
                for (int i = 0; i < w.Feet.Length; i++)
                {
                    if (w.Feet[i] == null) continue;
                    float side = Vector3.Dot(w.Feet[i].position - body.position, right) * (w.Side == 0 ? -1f : 1f);
                    if (side > best) { best = side; foot = w.Feet[i]; }
                }
                if (foot != null)
                {
                    Vector3 f = foot.position;
                    f.y = body.position.y;
                    if ((f - body.position).sqrMagnitude < 0.6f * 0.6f * w.Size * w.Size)
                        at = body.position + right * Vector3.Dot(f - body.position, right) + heading * Mathf.Clamp(Vector3.Dot(f - body.position, heading), -0.2f, 0.35f) * w.Size;
                }
            }
            SnowPrints.Add(at + heading * (size.x * 0.2f), yaw, size, shape, 1f - path * 0.7f);
        }
    }
}
