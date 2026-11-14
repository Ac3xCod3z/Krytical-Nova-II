using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items.Visuals;
using KryticalNova.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// The look a townsperson wears (plan 3.8), the same on every client: its cast and seed pick one of the cast's looks, and each look is
    /// a Sidekick recipe made from the look's own number (faction by weight, a lineage of it, body type, then the character creator's own
    /// randomiser). Townsfolk of one look share one merged mesh (owner pick 3a: bake once per outfit, colours on top).
    /// </summary>
    public static class TownsfolkLooks
    {
        public static int LookIndex(uint seed, int looksPerCast) => (int)(seed % (uint)math.max(1, looksPerCast));

        static readonly List<LineageDefinition> s_Lineages = new();

        /// <summary>The recipe of look <paramref name="look"/> of <paramref name="cast"/>; false when the cast or database gives none.</summary>
        public static bool Recipe(TownsfolkCast cast, CharacterCreationDatabase database, int look, out CharacterRecipe recipe, out BodyType body,
                                  out FactionId faction)
        {
            recipe = default;
            body = BodyType.Masculine;
            faction = FactionId.None;
            if (cast == null || database == null || cast.factions.Length == 0) return false;
            var rng = new System.Random((int)math.hash(new uint2(cast.id, (uint)look + 1u)));
            float sum = 0f;
            foreach (TownsfolkCast.FactionWeight f in cast.factions) sum += Mathf.Max(0f, f.weight);
            if (sum <= 0f) return false;
            double roll = rng.NextDouble() * sum;
            TownsfolkCast.FactionWeight chosen = cast.factions[cast.factions.Length - 1];
            foreach (TownsfolkCast.FactionWeight f in cast.factions)
            {
                roll -= Mathf.Max(0f, f.weight);
                if (roll < 0) { chosen = f; break; }
            }
            faction = chosen.faction;
            database.GetLineages(chosen.faction, s_Lineages);
            if (chosen.lineages != null && chosen.lineages.Length > 0) s_Lineages.RemoveAll(l => System.Array.IndexOf(chosen.lineages, l.id) < 0);
            if (s_Lineages.Count == 0) return false;
            s_Lineages.Sort((a, b) => a.id.CompareTo(b.id));   // the database's order never decides a look
            LineageDefinition lineage = s_Lineages[rng.Next(s_Lineages.Count)];
            body = rng.NextDouble() < cast.feminineShare ? BodyType.Feminine : BodyType.Masculine;
            recipe = RecipeEdit.Randomize(lineage, body, database, rng);
            return true;
        }
    }

    /// <summary>
    /// One pooled townsperson body on a client: the players' Sidekick model wearing a shared look, the shared animator, the character
    /// LOD. It plays what its ghost says: walk and jog by speed, and the activity's idle at a spot (plan 3.8; head-look and barks are
    /// step 6). Never on a dedicated server: the server has no bodies at all.
    /// </summary>
    public sealed class TownsfolkBody : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int ActivityId = Animator.StringToHash("Activity");
        public static readonly int OffsetId = Animator.StringToHash("Offset");

        public Animator Animator;
        public CharacterAppearance Appearance;
        public CharacterLook Look;
        public long LookKey = -1;
        public byte ShownActivity = 255;
        float m_Speed;

        public void Show(in TownsfolkGhost ghost, float damp, float dt)
        {
            if (Animator == null) return;
            float target = ghost.SpeedSteps * 0.05f;
            m_Speed = damp > 0f ? Mathf.Lerp(m_Speed, target, 1f - Mathf.Exp(-dt / damp)) : target;
            Animator.SetFloat(SpeedId, m_Speed);
            if (ghost.Activity != ShownActivity)
            {
                ShownActivity = ghost.Activity;
                Animator.SetInteger(ActivityId, ghost.Activity);
            }
        }
    }

    /// <summary>
    /// Client (plan 3.8): the townsfolk this player sees. The nearest <see cref="TownsfolkSettings.viewBudget"/> townsperson ghosts within
    /// <see cref="TownsfolkSettings.viewRadius"/> of the camera each get a pooled body dressed in their look (a hidden template per look is
    /// baked once; bodies share its mesh), placed on the ghost's interpolated transform every frame. Bodies keep their look in the pool,
    /// so a townsperson walking in and out of range is not baked again. Managed bridge; no allocation per frame once the pool is warm.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TownsfolkViewClientSystem : SystemBase
    {
        sealed class Template
        {
            public CharacterAppearance Appearance;
            public SkinnedMeshRenderer Renderer;
            public Vector3 Scale;
            public BodyType Body;
        }

        TownsfolkViewAssets m_Assets;
        bool m_Looked;
        Transform m_PoolRoot, m_Hidden;
        readonly Dictionary<Entity, TownsfolkBody> m_Shown = new();
        readonly Dictionary<long, Template> m_Templates = new();
        readonly List<TownsfolkBody> m_Free = new();
        readonly List<(float distSq, Entity ghost)> m_Near = new();
        readonly HashSet<Entity> m_Wanted = new();
        readonly List<Entity> m_Drop = new();
        EntityQuery m_Ghosts;
        int m_Made;

        public int Shown => m_Shown.Count;
        public int Templates => m_Templates.Count;

        /// <summary>gm.town.debug: labels over every shown townsperson (plan 3.9).</summary>
        public static bool DebugLabels;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => DebugLabels = false;

        /// <summary>What the labels show: each shown body and its ghost's state this frame (filled only while <see cref="DebugLabels"/>).</summary>
        internal readonly List<(TownsfolkBody body, TownsfolkGhost ghost, float distance)> Labels = new();

        protected override void OnCreate() =>
            m_Ghosts = GetEntityQuery(ComponentType.ReadOnly<TownsfolkGhost>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<GhostInstance>());

        protected override void OnDestroy()
        {
            if (m_PoolRoot != null) Object.Destroy(m_PoolRoot.gameObject);
            m_Shown.Clear();
            m_Free.Clear();
            m_Templates.Clear();
        }

        protected override void OnUpdate()
        {
            if (m_Ghosts.IsEmpty && m_Shown.Count == 0) return;
            if (!m_Looked)
            {
                m_Looked = true;
                m_Assets = Resources.Load<TownsfolkViewAssets>(TownsfolkViewAssets.ResourcePath);
                if (m_Assets == null || m_Assets.body == null || m_Assets.settings == null || m_Assets.database == null)
                    Debug.LogWarning("[Townsfolk] Resources/" + TownsfolkViewAssets.ResourcePath + " is missing or empty: townsfolk are not drawn (run Krytical Nova ▸ Townsfolk ▸ Build Assets).");
            }
            if (m_Assets == null || m_Assets.body == null || m_Assets.settings == null || m_Assets.database == null) return;
            Camera eye = Camera.main;
            if (eye == null) return;
            TownsfolkSettings s = m_Assets.settings;
            float3 from = eye.transform.position;
            EntityManager em = EntityManager;

            m_Near.Clear();
            using (NativeArray<Entity> ghosts = m_Ghosts.ToEntityArray(Allocator.Temp))
                foreach (Entity g in ghosts)
                {
                    float d = math.distancesq(em.GetComponentData<LocalTransform>(g).Position, from);
                    if (d <= s.viewRadius * s.viewRadius) m_Near.Add((d, g));
                }
            m_Near.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            m_Wanted.Clear();
            for (int i = 0; i < m_Near.Count && i < s.viewBudget; i++) m_Wanted.Add(m_Near[i].ghost);

            m_Drop.Clear();
            foreach (KeyValuePair<Entity, TownsfolkBody> pair in m_Shown)
                if (!m_Wanted.Contains(pair.Key) || !em.Exists(pair.Key) || pair.Value == null) m_Drop.Add(pair.Key);
            foreach (Entity g in m_Drop) { Release(m_Shown[g]); m_Shown.Remove(g); }

            float dt = SystemAPI.Time.DeltaTime;
            Labels.Clear();
            foreach (Entity g in m_Wanted)
            {
                TownsfolkGhost ghost = em.GetComponentData<TownsfolkGhost>(g);
                LocalTransform place = em.GetComponentData<LocalTransform>(g);
                if (!m_Shown.TryGetValue(g, out TownsfolkBody body))
                {
                    long key = ((long)ghost.Cast << 32) | (uint)TownsfolkLooks.LookIndex(ghost.Seed, s.looksPerCast);
                    body = Take(key, ghost, place);
                    if (body == null) continue;
                    m_Shown[g] = body;
                }
                body.transform.SetPositionAndRotation(place.Position, place.Rotation);
                body.Show(ghost, s.speedDamp, dt);
                if (DebugLabels) Labels.Add((body, ghost, math.distance(place.Position, from)));
            }
            if (DebugLabels && m_PoolRoot != null && m_PoolRoot.GetComponent<TownsfolkDebugLabels>() == null)
                m_PoolRoot.gameObject.AddComponent<TownsfolkDebugLabels>().Bind(this, m_Assets);
        }

        TownsfolkBody Take(long key, in TownsfolkGhost ghost, in LocalTransform place)
        {
            Template template = TemplateOf(key, ghost.Cast);
            if (template == null) return null;
            TownsfolkBody body = null;
            for (int i = 0; i < m_Free.Count; i++)
                if (m_Free[i] != null && m_Free[i].LookKey == key) { body = m_Free[i]; m_Free.RemoveAt(i); break; }
            if (body == null && m_Free.Count > 0) { body = m_Free[m_Free.Count - 1]; m_Free.RemoveAt(m_Free.Count - 1); }
            if (body == null) body = Make();
            if (body == null) return null;
            body.transform.SetPositionAndRotation(place.Position, place.Rotation);
            if (body.LookKey != key) Dress(body, template, key);
            body.gameObject.SetActive(true);
            body.ShownActivity = 255;
            if (body.Animator != null)
            {
                // variety from the seed: each one's clips start at its own point (plan 3.6)
                body.Animator.SetFloat(TownsfolkBody.OffsetId, (ghost.Seed >> 8 & 0xFF) / 255f);
            }
            return body;
        }

        void Dress(TownsfolkBody body, Template template, long key)
        {
            body.Appearance.ShareFrom(template.Appearance);
            UseAnimator(body, m_Assets, template.Body);
            if (body.Appearance.Renderer != null && template.Renderer != null) body.Appearance.Renderer.sharedMaterials = template.Renderer.sharedMaterials;
            body.transform.localScale = template.Scale;
            body.LookKey = key;
        }

        void Release(TownsfolkBody body)
        {
            if (body == null) return;
            body.gameObject.SetActive(false);
            m_Free.Add(body);
        }

        Transform PoolRoot()
        {
            if (m_PoolRoot != null) return m_PoolRoot;
            m_PoolRoot = new GameObject("Townsfolk (bodies)").transform;
            Object.DontDestroyOnLoad(m_PoolRoot.gameObject);
            m_Hidden = new GameObject("Unpacking").transform;
            m_Hidden.SetParent(m_PoolRoot, false);
            m_Hidden.gameObject.SetActive(false);
            return m_PoolRoot;
        }

        /// <summary>A body from the Sidekick model: only the look, blink and LOD parts kept (no weapon, IK or collider), the shared animator.</summary>
        TownsfolkBody Instantiate(string name)
        {
            Transform root = PoolRoot();
            // made under an inactive parent so the player model's own parts never wake up before they are taken off
            GameObject go = Object.Instantiate(m_Assets.body, m_Hidden);
            go.name = name;
            foreach (MonoBehaviour b in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(b is CharacterAppearance) && !(b is CharacterLook) && !(b is FaceBlinker) && !(b is CharacterLod)) Object.DestroyImmediate(b);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (go.GetComponent<CharacterLod>() == null) go.AddComponent<CharacterLod>();
            var body = go.AddComponent<TownsfolkBody>();
            body.Animator = go.GetComponentInChildren<Animator>(true);
            body.Appearance = go.GetComponent<CharacterAppearance>();
            body.Look = go.GetComponent<CharacterLook>();
            if (body.Animator != null)
            {
                body.Animator.applyRootMotion = false;
                body.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            go.transform.SetParent(root, false);
            return body;
        }

        TownsfolkBody Make()
        {
            if (m_Made >= Mathf.Max(1, m_Assets.settings.viewBudget) + 4) return null;
            m_Made++;
            TownsfolkBody body = Instantiate("Townsperson " + m_Made);
            if (body.Appearance == null) { Object.Destroy(body.gameObject); return null; }
            return body;
        }

        /// <summary>The hidden template of a look: dressed and baked once, its mesh shared by every body wearing it.</summary>
        Template TemplateOf(long key, byte castId)
        {
            if (m_Templates.TryGetValue(key, out Template t)) return t;
            TownsfolkCast cast = m_Assets.CastOf(castId);
            int look = (int)(key & 0xFFFFFFFF);
            if (!TownsfolkLooks.Recipe(cast, m_Assets.database, look, out CharacterRecipe recipe, out BodyType bodyType, out _))
            {
                m_Templates[key] = null;
                Debug.LogWarning($"[Townsfolk] Cast {castId} gives no look {look} (no cast asset, no faction weights or no lineage).");
                return null;
            }
            TownsfolkBody body = Instantiate($"Look {castId}.{look}");
            if (body.Look == null || body.Appearance == null) { Object.Destroy(body.gameObject); m_Templates[key] = null; return null; }
            body.Look.Dressed = cast.dressed;
            if (!body.Look.Apply(recipe, m_Assets.database)) { Object.Destroy(body.gameObject); m_Templates[key] = null; return null; }
            body.Appearance.RebuildNow();
            if (body.Animator != null) body.Animator.enabled = false;
            if (body.Appearance.Renderer != null) body.Appearance.Renderer.enabled = false;
            body.transform.position = new Vector3(0f, -2000f, 0f);
            t = new Template { Appearance = body.Appearance, Renderer = body.Appearance.Renderer, Scale = body.transform.localScale, Body = bodyType };
            m_Templates[key] = t;
            return t;
        }

        /// <summary>The animator a body of this look plays (set when it is dressed).</summary>
        public static void UseAnimator(TownsfolkBody body, TownsfolkViewAssets assets, BodyType bodyType)
        {
            if (body.Animator == null || assets.masculine == null) return;
            body.Animator.runtimeAnimatorController = bodyType == BodyType.Feminine && assets.feminine != null ? assets.feminine : assets.masculine;
        }
    }
}

namespace KryticalNova.Townsfolk
{
    /// <summary>gm.town.debug's labels (client): personality, activity, chat circle, look and distance over each shown townsperson.</summary>
    public sealed class TownsfolkDebugLabels : MonoBehaviour
    {
        TownsfolkViewClientSystem m_View;
        TownsfolkViewAssets m_Assets;
        GUIStyle m_Style;

        public void Bind(TownsfolkViewClientSystem view, TownsfolkViewAssets assets) { m_View = view; m_Assets = assets; }

        void OnGUI()
        {
            if (!TownsfolkViewClientSystem.DebugLabels || m_View == null) return;
            Camera eye = Camera.main;
            if (eye == null) return;
            m_Style ??= new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.LowerCenter, normal = { textColor = Color.white } };
            foreach ((TownsfolkBody body, TownsfolkGhost ghost, float distance) in m_View.Labels)
            {
                if (body == null) continue;
                Vector3 p = eye.WorldToScreenPoint(body.transform.position + Vector3.up * 2.1f);
                if (p.z <= 0f) continue;
                TownsfolkPersonality who = m_Assets != null ? m_Assets.PersonalityOf(ghost.Personality) : null;
                string name = who != null ? who.displayName : "#" + ghost.Personality;
                string circle = ghost.GroupId != 0 ? " circle " + ghost.GroupId : "";
                string text = name + "\n" + (TownActivity)ghost.Activity + circle + "\nlook " + (body.LookKey & 0xFFFF) + " " + distance.ToString("F0") + " m";
                GUI.Label(new Rect(p.x - 90f, Screen.height - p.y - 44f, 180f, 44f), text, m_Style);
            }
        }
    }
}
