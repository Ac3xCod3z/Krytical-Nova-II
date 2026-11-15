using System.Collections.Generic;
using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Footprints in the snow (owner, 14 Nov 2026: "foot prints instead of the trail effect ... we know foot placement"). Watches the feet
    /// of the characters nearest the camera (players, NPCs, Hostiles, mounts; how many is the World Effects tier's
    /// <see cref="SeasonSettings.PrintWalkers"/>) and, each time a foot comes down, adds a print at that foot turned to the body's heading
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
        const float StrideMetres = 0.7f;   // the fallback's step length, times the body's size
        const float PlantAbove = 0.035f;   // a foot is down within this of its lowest height (metres, times size)
        const float LiftAbove = 0.09f;     // and lifted past this
        const float StillShare = 0.3f;     // a foot moving slower than this share of the body is planted (it stays put while the body goes on)
        const float SwingShare = 0.8f;     // and one moving faster than this share is in the air

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
        public void Watch(Vector3? centre, SeasonSettings settings)
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
                Step(w, settings);
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
            var w = new Walker { Animator = a, LastStride = a.transform.position, LastBody = a.transform.position };
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

        void Step(Walker w, SeasonSettings settings)
        {
            if (w.Skip) return;
            Transform body = w.Animator.transform;
            float yaw = body.eulerAngles.y;
            Vector3 heading = body.forward;
            heading.y = 0f;
            Vector2 size = (w.Paw ? settings.pawPrint : settings.bootPrint) * w.Size;
            SnowPrintShape shape = w.Paw ? SnowPrintShape.Paw : SnowPrintShape.Boot;

            if (w.Feet == null)
            {
                // nothing to watch: a print each stride, left and right of the path
                Vector3 moved = body.position - w.LastStride;
                moved.y = 0f;
                if (moved.sqrMagnitude < StrideMetres * StrideMetres * w.Size * w.Size) return;
                w.LastStride = body.position;
                w.Side = 1 - w.Side;
                Vector3 side = Vector3.Cross(Vector3.up, heading.normalized) * (w.Side == 0 ? -0.12f : 0.12f) * w.Size;
                SnowPrints.Add(body.position + side, yaw, size, shape);
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 bodyStep = body.position - w.LastBody;
            bodyStep.y = 0f;
            w.LastBody = body.position;
            float bodySpeed = bodyStep.magnitude / dt;
            bool walking = bodySpeed > 0.4f * w.Size;
            for (int i = 0; i < w.Feet.Length; i++)
            {
                Transform foot = w.Feet[i];
                if (foot == null) continue;
                Vector3 footStep = foot.position - w.LastFoot[i];
                footStep.y = 0f;
                w.LastFoot[i] = foot.position;
                float footSpeed = footStep.magnitude / dt;
                float height = foot.position.y - body.position.y;
                // its lowest height lately (the ankle stands above the sole): creeps back up so a slope or a jump never fools it for long
                w.Rest[i] = Mathf.Min(w.Rest[i] == float.MaxValue ? height : w.Rest[i] + dt * 0.05f, height);
                float above = height - w.Rest[i];
                // a foot is in the air when it rises or (walking) swings past the body; it is down when it is low and (walking) stays put
                bool swinging = above > LiftAbove * w.Size || (walking && footSpeed > SwingShare * bodySpeed);
                bool planted = above < LiftAbove * w.Size && (walking ? footSpeed < StillShare * bodySpeed : above < PlantAbove * w.Size);
                if (w.Lifted[i] && planted)
                {
                    w.Lifted[i] = false;
                    // the ankle is over the heel: the print's middle is a little ahead of it
                    Vector3 at = foot.position + heading.normalized * size.x * 0.2f;
                    SnowPrints.Add(at, yaw, size, shape);
                }
                else if (!w.Lifted[i] && swinging) w.Lifted[i] = true;
            }
        }
    }
}
