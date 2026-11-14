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
using UnityEngine.Rendering;

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
    /// LOD (driven by the view, by the player's Crowd Detail). It plays what its ghost says: walk and jog by speed, and the activity's idle
    /// at a spot (plan 3.8; head-look and barks are step 6). Never on a dedicated server: the server has no bodies at all.
    /// </summary>
    public sealed class TownsfolkBody : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int ActivityId = Animator.StringToHash("Activity");
        public static readonly int OffsetId = Animator.StringToHash("Offset");

        public Animator Animator;
        public CharacterAppearance Appearance;
        public CharacterLook Look;
        public CharacterLod Lod;
        public long LookKey = -1;
        public byte ShownActivity = 255;
        float m_Speed;
        // a far body animates every few frames, with the time saved up since its last update
        float m_Pending;
        bool m_Manual;

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

        /// <summary>Near: the animator runs every frame. Far: it is updated by hand every <paramref name="every"/> frames, staggered by the seed.</summary>
        public void Animate(bool everyFrame, int every, int frame, uint seed, float dt)
        {
            if (Animator == null) return;
            if (everyFrame || every <= 1)
            {
                if (m_Manual) { m_Manual = false; Animator.enabled = true; m_Pending = 0f; }
                return;
            }
            if (!m_Manual) { m_Manual = true; Animator.enabled = false; m_Pending = 0f; }
            m_Pending += dt;
            if ((frame + (int)(seed & 0xFF)) % every != 0) return;
            Animator.Update(m_Pending);
            m_Pending = 0f;
        }

        /// <summary>Back to a normal animator (a body taken from the pool).</summary>
        public void ResetAnimation()
        {
            if (m_Manual && Animator != null) Animator.enabled = true;
            m_Manual = false;
            m_Pending = 0f;
        }
    }

    /// <summary>
    /// Client (plan 3.8; owner 13 Nov 2026: performance first, by the existing Graphics settings): the townsfolk this player sees, by the
    /// Crowd Detail tier's numbers (<see cref="TownsfolkSettings"/> rows, the tier from <see cref="CrowdDetailRuntime"/>). The nearest
    /// townsfolk within the tier's full-body distance and count each get a pooled body dressed in their look (a hidden template per look
    /// is baked once; bodies share its mesh and materials), placed on the ghost's interpolated transform every frame: only the nearest cast
    /// shadows and skin with every bone, far bodies animate every few frames. Farther ones, out to the view radius, are cards: a snapshot of
    /// their look drawn as one instanced quad per look (<see cref="TownsfolkCards"/>). Bodies keep their look in the pool, so a townsperson
    /// walking in and out of range is not baked again. Managed bridge; no allocation per frame once the pool is warm.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TownsfolkViewClientSystem : SystemBase
    {
        sealed class Template
        {
            public TownsfolkBody Source;
            public CharacterAppearance Appearance;
            public SkinnedMeshRenderer Renderer;
            public Vector3 Scale;
            public BodyType Body;
        }

        sealed class ByDistance : IComparer<(float distSq, Entity ghost)>
        {
            public static readonly ByDistance Instance = new();
            public int Compare((float distSq, Entity ghost) a, (float distSq, Entity ghost) b) => a.distSq.CompareTo(b.distSq);
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
        TownsfolkCards m_Cards;
        EntityQuery m_Ghosts;
        int m_Made, m_Frame;

        public int Shown => m_Shown.Count;
        public int Templates => m_Templates.Count;
        /// <summary>Far cards drawn this frame.</summary>
        public int Cards => m_Cards != null ? m_Cards.Drawn : 0;

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
            m_Cards?.Dispose();
            m_Cards = null;
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

            // the player's Crowd Detail picks the tier's numbers (only what this client draws; the server's crowd is the same)
            int tier = CrowdDetailRuntime.Tier;
            float radius = TownsfolkSettings.ByTier(s.viewRadiusByTier, tier, 100f);
            float fullSq = Sq(TownsfolkSettings.ByTier(s.fullBodyDistanceByTier, tier, 45f));
            int fullCount = TownsfolkSettings.ByTier(s.fullBodiesByTier, tier, 48);
            int cardCount = TownsfolkSettings.ByTier(s.cardsByTier, tier, 64);
            float everyFrameSq = Sq(TownsfolkSettings.ByTier(s.everyFrameWithinByTier, tier, 20f));
            int every = Mathf.Max(1, TownsfolkSettings.ByTier(s.farAnimateEveryByTier, tier, 2));
            float shadowSq = Sq(TownsfolkSettings.ByTier(s.shadowsWithinByTier, tier, 18f));
            float fullSkinSq = Sq(TownsfolkSettings.ByTier(s.fullSkinWithinByTier, tier, 20f));

            m_Near.Clear();
            using (NativeArray<Entity> ghosts = m_Ghosts.ToEntityArray(Allocator.Temp))
                foreach (Entity g in ghosts)
                {
                    float d = math.distancesq(em.GetComponentData<LocalTransform>(g).Position, from);
                    if (d <= radius * radius) m_Near.Add((d, g));
                }
            m_Near.Sort(ByDistance.Instance);
            // nearest first: a full body while within the tier's distance and count, a card after that
            int full = 0;
            m_Wanted.Clear();
            while (full < m_Near.Count && full < fullCount && m_Near[full].distSq <= fullSq) m_Wanted.Add(m_Near[full++].ghost);

            m_Drop.Clear();
            foreach (KeyValuePair<Entity, TownsfolkBody> pair in m_Shown)
                if (!m_Wanted.Contains(pair.Key) || !em.Exists(pair.Key) || pair.Value == null) m_Drop.Add(pair.Key);
            foreach (Entity g in m_Drop) { Release(m_Shown[g]); m_Shown.Remove(g); }

            float dt = SystemAPI.Time.DeltaTime;
            m_Frame++;
            Labels.Clear();
            foreach (Entity g in m_Wanted)
            {
                TownsfolkGhost ghost = em.GetComponentData<TownsfolkGhost>(g);
                LocalTransform place = em.GetComponentData<LocalTransform>(g);
                if (!m_Shown.TryGetValue(g, out TownsfolkBody body))
                {
                    body = Take(KeyOf(ghost, s), ghost, place);
                    if (body == null) continue;
                    m_Shown[g] = body;
                }
                body.transform.SetPositionAndRotation(place.Position, place.Rotation);
                float dSq = math.distancesq(place.Position, from);
                if (body.Lod != null) body.Lod.Drive(dSq <= fullSkinSq ? 0 : 1, dSq > shadowSq);
                body.Show(ghost, s.speedDamp, dt);
                body.Animate(dSq <= everyFrameSq, every, m_Frame, ghost.Seed, dt);
                if (DebugLabels) Labels.Add((body, ghost, math.sqrt(dSq)));
            }

            // far ones: cards, one instanced draw per look
            if (full < m_Near.Count && cardCount > 0 && m_Assets.cardMaterial != null) m_Cards ??= new TownsfolkCards(m_Assets, PoolRoot());
            if (m_Cards != null)
            {
                m_Cards.Begin();
                for (int i = full; i < m_Near.Count && i - full < cardCount; i++)
                {
                    Entity g = m_Near[i].ghost;
                    TownsfolkGhost ghost = em.GetComponentData<TownsfolkGhost>(g);
                    long key = KeyOf(ghost, s);
                    Template t = TemplateOf(key, ghost.Cast);
                    if (t == null) continue;
                    m_Cards.Add(key, t.Source, t.Renderer, ControllerOf(m_Assets, t.Body), em.GetComponentData<LocalTransform>(g).Position, from);
                }
                m_Cards.Draw();
            }
            if (DebugLabels && m_PoolRoot != null && m_PoolRoot.GetComponent<TownsfolkDebugLabels>() == null)
                m_PoolRoot.gameObject.AddComponent<TownsfolkDebugLabels>().Bind(this, m_Assets);
        }

        static float Sq(float v) => v * v;

        static long KeyOf(in TownsfolkGhost ghost, TownsfolkSettings s) => ((long)ghost.Cast << 32) | (uint)TownsfolkLooks.LookIndex(ghost.Seed, s.looksPerCast);

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
            body.ResetAnimation();
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
            var body = go.AddComponent<TownsfolkBody>();
            body.Lod = CharacterLod.Ensure(go);
            body.Lod.DrivenExternally = true;   // the view sets its level by Crowd Detail
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
            TownsfolkSettings s = m_Assets.settings;
            if (m_Made >= Mathf.Max(1, TownsfolkSettings.ByTier(s.fullBodiesByTier, CrowdDetailRuntime.Tier, 48)) + 4) return null;
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
            if (body.Lod != null) body.Lod.Drive(0, true);
            body.transform.position = new Vector3(0f, -2000f, 0f);
            t = new Template { Source = body, Appearance = body.Appearance, Renderer = body.Appearance.Renderer, Scale = body.transform.localScale, Body = bodyType };
            m_Templates[key] = t;
            return t;
        }

        static RuntimeAnimatorController ControllerOf(TownsfolkViewAssets assets, BodyType bodyType) =>
            bodyType == BodyType.Feminine && assets.feminine != null ? assets.feminine : assets.masculine;

        /// <summary>The animator a body of this look plays (set when it is dressed).</summary>
        public static void UseAnimator(TownsfolkBody body, TownsfolkViewAssets assets, BodyType bodyType)
        {
            if (body.Animator == null || assets.masculine == null) return;
            body.Animator.runtimeAnimatorController = ControllerOf(assets, bodyType);
        }
    }

    /// <summary>
    /// Far townsfolk as cards (owner 13 Nov 2026: impostors, slim what cannot be turned down): each look is photographed once by a hidden
    /// camera (its template standing idle, on a layer nothing else uses) into a small texture, and every far townsperson of that look is a
    /// quad facing the camera, all of one look in one instanced draw, no shadow. The card follows the light of the hour: it is tinted by the
    /// sun and sky now against when it was taken, and taken again when the light has halved or doubled. If the pipeline cannot take a
    /// picture, cards switch themselves off (with one warning) and far townsfolk are simply not drawn, as before.
    /// </summary>
    sealed class TownsfolkCards : System.IDisposable
    {
        sealed class Card
        {
            public RenderTexture Texture;
            public Material Material;
            public Matrix4x4[] Matrices;
            public int Count;
            public float Width, Height, Lift, CapturedLight;
            public TownsfolkBody Source;
            public SkinnedMeshRenderer Renderer;
            public RuntimeAnimatorController Controller;
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Bounds Everywhere = new(Vector3.zero, Vector3.one * 100000f);

        readonly TownsfolkViewAssets m_Assets;
        readonly Transform m_Root;
        readonly Dictionary<long, Card> m_ByLook = new();
        readonly List<Card> m_All = new();
        readonly int m_MostCards;
        Mesh m_Quad;
        Camera m_Camera;
        bool m_Failed;

        public int Drawn { get; private set; }

        public TownsfolkCards(TownsfolkViewAssets assets, Transform root)
        {
            m_Assets = assets;
            m_Root = root;
            int most = 1;
            foreach (int n in assets.settings.cardsByTier ?? System.Array.Empty<int>()) most = Mathf.Max(most, n);
            m_MostCards = most;
        }

        public void Begin()
        {
            for (int i = 0; i < m_All.Count; i++) m_All[i].Count = 0;
            Drawn = 0;
        }

        public void Add(long key, TownsfolkBody source, SkinnedMeshRenderer renderer, RuntimeAnimatorController controller, Vector3 at, Vector3 eye)
        {
            if (m_Failed) return;
            if (!m_ByLook.TryGetValue(key, out Card card))
            {
                card = Capture(new Card { Source = source, Renderer = renderer, Controller = controller });
                m_ByLook[key] = card;
                if (card != null) m_All.Add(card);
            }
            if (card == null || card.Count >= card.Matrices.Length) return;
            Vector3 toEye = eye - at;
            toEye.y = 0f;
            Quaternion face = toEye.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(-toEye) : Quaternion.identity;
            card.Matrices[card.Count++] = Matrix4x4.TRS(at + Vector3.up * card.Lift, face, new Vector3(card.Width, card.Height, 1f));
            Drawn++;
        }

        public void Draw()
        {
            if (m_Failed) return;
            float light = LightNow();
            bool retook = false;
            for (int i = 0; i < m_All.Count; i++)
            {
                Card card = m_All[i];
                if (card.Count == 0) continue;
                float ratio = light / Mathf.Max(0.05f, card.CapturedLight);
                // the hour's light changed a lot since the picture: take it again (one look a frame)
                if (!retook && (ratio < 0.5f || ratio > 2f) && Capture(card) != null) { retook = true; ratio = 1f; }
                if (m_Failed) return;
                float k = Mathf.Clamp(ratio, 0.15f, 1.5f);
                card.Material.SetColor(BaseColorId, new Color(k, k, k, 1f));
                var rp = new RenderParams(card.Material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = Everywhere };
                Graphics.RenderMeshInstanced(rp, m_Quad, 0, card.Matrices, card.Count);
            }
        }

        /// <summary>The sun's and the sky's light now, as one number (only its ratio to the picture's is used).</summary>
        static float LightNow()
        {
            Light sun = RenderSettings.sun;
            float sunPart = sun != null && sun.isActiveAndEnabled ? sun.intensity * sun.color.grayscale * Mathf.Max(0f, -sun.transform.forward.y) : 0f;
            return sunPart + RenderSettings.ambientLight.grayscale * Mathf.Max(0.01f, RenderSettings.ambientIntensity);
        }

        Card Capture(Card card)
        {
            if (card.Source == null || card.Renderer == null || m_Assets.cardMaterial == null) return null;
            TownsfolkSettings s = m_Assets.settings;
            Renderer r = card.Renderer;
            int oldLayer = r.gameObject.layer;
            bool wasOn = r.enabled;
            try
            {
                m_Quad ??= MakeQuad();
                int layer = Mathf.Clamp(s.cardCaptureLayer, 0, 31);
                int px = Mathf.Clamp(s.cardPixels, 32, 512);
                if (card.Texture == null)
                {
                    card.Texture = new RenderTexture(px, px * 2, 16, RenderTextureFormat.ARGB32) { name = "Townsfolk card", antiAliasing = 1 };
                    card.Matrices = new Matrix4x4[m_MostCards];
                    card.Material = new Material(m_Assets.cardMaterial) { name = "Townsfolk card", enableInstancing = true };
                    card.Material.SetTexture(BaseMapId, card.Texture);
                }
                // stand the template idle for its picture
                Animator animator = card.Source.Animator;
                if (animator != null && card.Controller != null)
                {
                    animator.runtimeAnimatorController = card.Controller;
                    animator.enabled = true;
                    animator.SetFloat(TownsfolkBody.SpeedId, 0f);
                    animator.SetInteger(TownsfolkBody.ActivityId, (int)TownActivity.Stand);
                    animator.Update(0.6f);
                    animator.enabled = false;
                }
                r.gameObject.layer = layer;
                r.enabled = true;
                Transform root = card.Source.transform;
                Bounds b = r.bounds;
                float h = Mathf.Max(0.5f, b.size.y * 1.04f);
                Vector3 middle = new Vector3(root.position.x, b.min.y + h * 0.5f, root.position.z);
                Camera cam = CameraFor(layer);
                cam.orthographicSize = h * 0.5f;
                cam.aspect = 0.5f;
                cam.transform.SetPositionAndRotation(middle + root.forward * 5f, Quaternion.LookRotation(-root.forward));
                cam.targetTexture = card.Texture;
                var request = new RenderPipeline.StandardRequest { destination = card.Texture };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();
                cam.targetTexture = null;
                card.Height = h;
                card.Width = h * 0.5f;
                card.Lift = b.min.y - root.position.y;
                card.CapturedLight = LightNow();
                return card;
            }
            catch (System.Exception e)
            {
                m_Failed = true;
                Debug.LogWarning("[Townsfolk] Far cards are off (the picture could not be taken): " + e.Message);
                return null;
            }
            finally
            {
                r.gameObject.layer = oldLayer;
                r.enabled = wasOn;
            }
        }

        Camera CameraFor(int layer)
        {
            if (m_Camera != null) return m_Camera;
            var go = new GameObject("Townsfolk card camera");
            go.transform.SetParent(m_Root, false);
            m_Camera = go.AddComponent<Camera>();
            m_Camera.enabled = false;
            m_Camera.orthographic = true;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.clear;
            m_Camera.cullingMask = 1 << layer;
            m_Camera.nearClipPlane = 0.1f;
            m_Camera.farClipPlane = 20f;
            m_Camera.allowHDR = false;
            m_Camera.allowMSAA = false;
            return m_Camera;
        }

        static Mesh MakeQuad()
        {
            var mesh = new Mesh { name = "Townsfolk card" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            foreach (Card c in m_All)
            {
                if (c.Texture != null) { c.Texture.Release(); Object.Destroy(c.Texture); }
                if (c.Material != null) Object.Destroy(c.Material);
            }
            m_All.Clear();
            m_ByLook.Clear();
            if (m_Quad != null) Object.Destroy(m_Quad);
            if (m_Camera != null) Object.Destroy(m_Camera.gameObject);
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
