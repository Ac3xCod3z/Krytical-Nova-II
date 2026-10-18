using System.Collections.Generic;
using KryticalNova.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Progression
{
    /// <summary>
    /// The Nova a character wears: a small star floating above the head with rings of light turning round it on
    /// their own tilted axes, a glow, and sparks in orbit. Created by <see cref="PlayerNova"/> on every machine that
    /// draws the character. The tier decides how much of it there is (<see cref="NovaLookSettings"/>): every ring
    /// whose tier has been reached is shown, so a higher Nova is the lower one with more turning in it. It follows
    /// the head's place but never its turning, so it floats steadily; and it answers what happens at the Herald
    /// (<see cref="React"/>): a flare on a level gained, a fold and an unfolding on a new tier, a flicker on a slip.
    /// Characters, pets and effects behind it are not seen through it: a disc behind it hides them, and only them
    /// (the stages of KryticalNova/NovaMask), and it reports itself to <see cref="NovaCull"/> for the glow halo.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NovaHalo : MonoBehaviour
    {
        private const string HeadBone = "head";
        private const float HeadTop = 0.2f;         // from the head joint to the crown, at scale 1
        private const float FallbackHeight = 0.72f; // above the root (itself a metre up), for a body with no head joint
        private const int RingSegments = 96;
        private const int MoteCount = 40;
        private const float MoteSize = 0.011f;      // metres, for a spark of size 1
        private const int DiscSegments = 40;
        private const float HeadRadius = 0.16f;     // metres: the owner's head, for telling when it is seen through the Nova

        // How it moves when something happens.
        private const float FlashFade = 2.6f;        // of the flash lost each second
        private const float BoostFade = 1.8f;
        private const float UnfoldStagger = 0.07f, UnfoldSeconds = 0.45f;
        private const float PunchSeconds = 0.45f, PunchSwell = 0.28f;
        private const float FlickerSeconds = 0.7f, HoldSeconds = 0.35f, DudSeconds = 0.3f;
        private const float FlickerRate = 18f;       // steps of the sputter a second
        // How bright each step of a slip's sputter is: out, back, out twice, back... a light that nearly fails.
        private static readonly float[] Flicker = { 0.1f, 1f, 0.15f, 0.1f, 1f, 0.2f, 1f, 0.1f, 0.6f, 0.15f, 1f, 0.4f, 1f };

        private static readonly int InnerId = Shader.PropertyToID("_Inner");
        private static readonly int OuterId = Shader.PropertyToID("_Outer");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int PrismId = Shader.PropertyToID("_Prism");
        private static readonly int PatternId = Shader.PropertyToID("_Pattern");
        private static readonly int MarksId = Shader.PropertyToID("_Marks");
        private static readonly int CometId = Shader.PropertyToID("_Comet");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int CoreId = Shader.PropertyToID("_Core");
        private static readonly int ReachId = Shader.PropertyToID("_Reach");
        private static readonly int CoronaId = Shader.PropertyToID("_Corona");
        private static readonly int RaysId = Shader.PropertyToID("_Rays");
        private static readonly int RayLengthId = Shader.PropertyToID("_RayLength");
        private static readonly int BackdropId = Shader.PropertyToID("_Backdrop");
        private static readonly int FeatherId = Shader.PropertyToID("_Feather");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int CountId = Shader.PropertyToID("_Count");
        private static readonly int SizeId = Shader.PropertyToID("_Size");

        private static readonly Dictionary<int, Mesh> RingMeshes = new Dictionary<int, Mesh>();
        private static Mesh sphereMesh, quadMesh, moteMesh, discMesh;

        // The stages of the disc that hides what is behind a Nova, in the order they are drawn (see the shader).
        // Stencil bits 0x40 and 0x20 are this shader's own.
        private const int SkyBit = 0x40, OpenBit = 0x20, BothBits = SkyBit | OpenBit;
        private const int FirstBlendedQueue = 2501;
        private struct MaskStage
        {
            public string Name;
            public int Order, Queue;
            public bool Far, WritesDepth;
            public CompareFunction Depth, Stencil;
            public StencilOp Pass;
            public int Ref;
        }
        private static readonly MaskStage[] MaskStages =
        {
            new MaskStage { Name = "Mask Sky", Order = ActorDrawOrder.Mask, Queue = 2000, Far = true, Depth = CompareFunction.LessEqual,
                Stencil = CompareFunction.Always, Pass = StencilOp.Replace, Ref = SkyBit },
            new MaskStage { Name = "Mask", Order = ActorDrawOrder.Mask, Queue = 2001, WritesDepth = true, Depth = CompareFunction.LessEqual,
                Stencil = CompareFunction.Always, Pass = StencilOp.Keep },
            new MaskStage { Name = "Mask Open", Order = ActorDrawOrder.Unmask, Queue = 2000, Depth = CompareFunction.Equal,
                Stencil = CompareFunction.Equal, Pass = StencilOp.Invert, Ref = SkyBit },
            new MaskStage { Name = "Mask Clear", Order = ActorDrawOrder.Unmask, Queue = 2001, Far = true, WritesDepth = true, Depth = CompareFunction.Always,
                Stencil = CompareFunction.Equal, Pass = StencilOp.Zero, Ref = OpenBit },
            new MaskStage { Name = "Mask Tidy", Order = ActorDrawOrder.Unmask, Queue = 2002, Far = true, Depth = CompareFunction.Always,
                Stencil = CompareFunction.Always, Pass = StencilOp.Zero },
            new MaskStage { Name = "Mask Again", Order = -1, Queue = FirstBlendedQueue, WritesDepth = true, Depth = CompareFunction.LessEqual,
                Stencil = CompareFunction.Always, Pass = StencilOp.Keep },
        };
        private static Material[] maskMaterials;
        private static Shader maskMaterialsShader;

        private sealed class Ring
        {
            public NovaRing Look;
            public Transform Pivot;
            public MeshRenderer Body;
            public float Spin, Tumble;
        }

        private Transform owner;
        private Transform head;
        private PlayerResources resources;
        private NovaLookSettings look;
        private NovaTierLook tierLook;
        private Camera viewer;
        private MaterialPropertyBlock block;
        private Transform core, corona;
        private MeshRenderer coreBody, coronaBody, moteBody;
        private readonly List<Ring> rings = new List<Ring>(12);
        private Transform mask;
        private MeshRenderer[] maskBodies;
        private ActorDrawOrder ownerOrder;
        private float quadReach = 1f;   // the corona quad's half-size, in Nova radii
        private bool shown;

        // Reactions.
        private float flash, boost;
        private float unfoldStarted = -100f, punchStarted = -100f, flickerUntil, holdUntil, dudUntil;
        private bool settled;           // nothing is animating: the materials are not touched

        /// <summary>The tier being shown; 0 while there is no Nova.</summary>
        public int Tier { get; private set; }

        /// <summary>How far above the crown of the head the Nova reaches (metres): what a namecard has to clear. 0 with no Nova.</summary>
        public float TopAboveHead => Tier <= 0 || look == null ? 0f : Mathf.Max(0f, look.gapAboveHead) + 2f * Reach + 0.02f;

        /// <summary>The crown of the character's head in the world: where the Nova is measured from.</summary>
        public Vector3 Crown => owner != null ? CrownOf(owner, ref head) : transform.position;

        /// <summary>From the star to the furthest any ring reaches, in metres.</summary>
        private float Reach => look != null ? tierLook.radius * look.Reach(Tier) : 0f;

        /// <summary>
        /// The crown of a character's head in the world. <paramref name="head"/> is the caller's own cache of the
        /// head joint: it is looked up once and handed back.
        /// </summary>
        public static Vector3 CrownOf(Transform owner, ref Transform head)
        {
            if (head == null) head = FindDeep(owner, HeadBone);
            float scale = owner.lossyScale.y;
            // Along the body's own up: on a wall (walls as a second ground) the crown is out from the face, not overhead.
            Vector3 up = owner.up;
            return head != null ? head.position + up * (HeadTop * scale) : owner.position + up * (FallbackHeight * scale);
        }

        public static NovaHalo Create(Transform owner)
        {
            var go = new GameObject("Nova") { layer = owner.gameObject.layer };
            go.transform.SetParent(owner, false);
            var halo = go.AddComponent<NovaHalo>();
            halo.owner = owner;
            halo.resources = owner.GetComponent<PlayerResources>();
            halo.head = FindDeep(owner, HeadBone);
            halo.block = new MaterialPropertyBlock();
            halo.ownerOrder = ActorDrawOrder.On(owner.gameObject);
            return halo;
        }

        // ------------------------------------------------------------------ what is shown

        /// <summary>Shows the Nova of a tier, or none for tier 0.</summary>
        public void Show(NovaLookSettings settings, int tier)
        {
            bool any = settings != null && settings.coreMaterial != null && settings.ringMaterial != null && settings.coronaMaterial != null
                && settings.tiers != null && settings.tiers.Length > 0 && tier > 0;
            int previous = Tier;
            look = settings;
            Tier = any ? tier : 0;
            if (!any)
            {
                SetShown(false);
                return;
            }
            tierLook = settings.tiers[Mathf.Min(tier, settings.tiers.Length) - 1];
            BuildParts();
            if (previous != tier || rings.Count == 0)
            {
                BuildRings();
                unfoldStarted = Time.time; // a new set of rings opens out one after another
            }
            // The corona's quad has to hold the furthest ring and the longest ray, with room for the light to fall away.
            quadReach = Mathf.Max(look.Reach(tier), tierLook.core + tierLook.rayLength) * 1.2f + 0.35f;
            settled = false;
            SetShown(true);
            Place();
            Turn();
            Paint();
        }

        private void SetShown(bool show)
        {
            shown = show;
            if (coreBody != null) coreBody.enabled = show;
            if (coronaBody != null) coronaBody.enabled = show;
            if (moteBody != null) moteBody.enabled = show && tierLook.motes > 0;
            foreach (Ring ring in rings) ring.Body.enabled = show;
            if (maskBodies != null)
                foreach (MeshRenderer body in maskBodies) body.enabled = show;
            if (!show && ownerOrder != null) ownerOrder.DrawWithScenery = false;
        }

        private void BuildParts()
        {
            if (core != null) return;
            core = Part("Star", SphereMesh(), look.coreMaterial, out coreBody);
            corona = Part("Corona", QuadMesh(), look.coronaMaterial, out coronaBody);
            if (look.motesMaterial != null) Part("Motes", MoteMesh(), look.motesMaterial, out moteBody);
            BuildMask();
        }

        /// <summary>The disc that hides what is behind the Nova: one transform, a renderer for each stage.</summary>
        private void BuildMask()
        {
            Material[] materials = MaskMaterials(look.maskShader);
            if (materials == null) return;
            foreach (Material stage in materials) stage.SetFloat(FeatherId, Mathf.Clamp(look.maskFeather, 0f, 0.9f));
            var go = new GameObject("Mask") { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            mask = go.transform;
            maskBodies = new MeshRenderer[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                Transform stage = Part(MaskStages[i].Name, DiscMesh(), materials[i], out maskBodies[i]);
                stage.SetParent(mask, false);
                maskBodies[i].sortingOrder = MaskStages[i].Order;
            }
        }

        private static Material[] MaskMaterials(Shader shader)
        {
            if (shader == null) return null;
            if (maskMaterials != null && maskMaterialsShader == shader && maskMaterials[0] != null) return maskMaterials;
            maskMaterialsShader = shader;
            maskMaterials = new Material[MaskStages.Length];
            for (int i = 0; i < MaskStages.Length; i++)
            {
                MaskStage stage = MaskStages[i];
                var material = new Material(shader) { name = "M_Nova" + stage.Name.Replace(" ", string.Empty), hideFlags = HideFlags.HideAndDontSave };
                material.renderQueue = stage.Queue;
                material.SetFloat("_ZTest", (float)stage.Depth);
                material.SetFloat("_ZWrite", stage.WritesDepth ? 1f : 0f);
                material.SetFloat("_Far", stage.Far ? 1f : 0f);
                material.SetFloat("_StencilRef", stage.Ref);
                bool marks = stage.Pass != StencilOp.Keep;
                material.SetFloat("_StencilRead", stage.Stencil == CompareFunction.Always ? 0f : BothBits);
                material.SetFloat("_StencilWrite", marks ? BothBits : 0f);
                material.SetFloat("_StencilComp", (float)stage.Stencil);
                material.SetFloat("_StencilPass", (float)stage.Pass);
                maskMaterials[i] = material;
            }
            return maskMaterials;
        }

        private Transform Part(string partName, Mesh mesh, Material material, out MeshRenderer body)
        {
            var go = new GameObject(partName) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            body = go.AddComponent<MeshRenderer>();
            body.sharedMaterial = material;
            body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.receiveShadows = false;
            body.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            body.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        private void BuildRings()
        {
            foreach (Ring old in rings)
                if (old.Pivot != null) Destroy(old.Pivot.gameObject);
            rings.Clear();
            if (look.rings == null) return;
            foreach (NovaRing ringLook in look.rings)
            {
                if (ringLook.fromTier > Tier || ringLook.radius <= 0f) continue;
                var ring = new Ring { Look = ringLook };
                ring.Pivot = Part("Ring", RingMesh(ringLook.width / ringLook.radius, ringLook.upright), look.ringMaterial, out ring.Body);
                // Each starts somewhere of its own, so the rings of a Nova are never caught lined up.
                ring.Spin = (rings.Count * 67f) % 360f;
                ring.Tumble = (rings.Count * 131f) % 360f;
                rings.Add(ring);
            }
        }

        // ------------------------------------------------------------------ reactions

        /// <summary>
        /// Something happened to this character's Ascension: the Nova shows it. A new tier needs nothing more here:
        /// the rings unfold when <see cref="Show"/> is given the new tier.
        /// </summary>
        public void React(AscensionOutcome outcome, bool newTier)
        {
            if (Tier <= 0) return;
            settled = false;
            float now = Time.time;
            switch (outcome)
            {
                case AscensionOutcome.Rise:
                    flash = Mathf.Max(flash, newTier ? 3f : 1.4f);
                    boost = Mathf.Max(boost, newTier ? 9f : 5f);
                    punchStarted = now;
                    break;
                case AscensionOutcome.Slip:
                    flickerUntil = now + FlickerSeconds;
                    boost = -0.85f; // it all but stops, then picks up again
                    break;
                case AscensionOutcome.SlipHeld:
                    flash = Mathf.Max(flash, 1.8f);
                    holdUntil = now + HoldSeconds;
                    break;
                default:
                    dudUntil = now + DudSeconds;
                    break;
            }
        }

        // ------------------------------------------------------------------ every frame

        private void LateUpdate()
        {
            if (Tier <= 0) return;
            bool alive = resources == null || resources.IsAlive; // a body on the ground wears no Nova
            if (shown != alive) SetShown(alive);
            if (!alive) return;
            Place();
            Turn();
            if (!settled) Paint();
        }

        /// <summary>
        /// Above the head along the body's up, so on a wall it stays over the head (owner, 18 Oct 2026); on the ground
        /// that is upright in the world whichever way the character turns.
        /// </summary>
        private void Place()
        {
            if (owner == null || look == null) return;
            Vector3 up = owner.up;
            Vector3 centre = Crown + up * look.CentreAboveHead(Tier);
            transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.up, up));
            float ownerScale = Mathf.Max(0.0001f, owner.lossyScale.y);
            transform.localScale = Vector3.one / ownerScale; // children are sized in metres

            float radius = tierLook.radius;
            float sincePunch = Time.time - punchStarted;
            float punch = sincePunch < PunchSeconds
                ? 1f + PunchSwell * Mathf.Sin(sincePunch / PunchSeconds * Mathf.PI) * (1f - sincePunch / PunchSeconds) * 2f : 1f;
            core.localScale = Vector3.one * (radius * tierLook.core * 2f * punch);

            if (viewer == null) viewer = Camera.main;
            if (viewer != null) corona.rotation = viewer.transform.rotation;
            corona.localScale = Vector3.one * (radius * quadReach * 2f);
            PlaceMask(centre);
        }

        /// <summary>
        /// The hiding disc: behind every ring, square to the line of sight, and as wide as the Nova looks from the
        /// camera. The owner seen through their own Nova (the camera looking down on it) is not cut out by it.
        /// </summary>
        private void PlaceMask(Vector3 centre)
        {
            if (mask == null || viewer == null) return;
            float reach = Reach;
            Vector3 eye = viewer.transform.position;
            Vector3 away = centre - eye;
            float distance = away.magnitude;
            if (reach <= 0f || distance < 0.01f) return;
            away /= distance;
            // The disc stands behind every ring, and is as wide from the camera as the setting says the Nova is.
            float size = reach * Mathf.Clamp(look.maskSize, 0.3f, 1.2f);
            mask.SetPositionAndRotation(centre + away * reach, Quaternion.LookRotation(away, viewer.transform.up));
            mask.localScale = Vector3.one * (size * (distance + reach) / distance);
            NovaCull.Report(centre, size, look.maskFeather, ownerOrder);

            if (ownerOrder == null) return;
            Vector3 toCrown = Crown - eye;
            float along = Vector3.Dot(toCrown, away);
            bool through = along > distance && (toCrown - away * along).magnitude < (size + HeadRadius) * along / distance;
            ownerOrder.DrawWithScenery = through;
        }

        private void Turn()
        {
            float now = Time.time;
            float speed = look.spinScale * (1f + boost) * (now < holdUntil ? 0f : 1f);
            float delta = Time.deltaTime * speed;
            float sinceUnfold = now - unfoldStarted;
            for (int i = 0; i < rings.Count; i++)
            {
                Ring ring = rings[i];
                ring.Spin = Mathf.Repeat(ring.Spin + ring.Look.spin * delta, 360f);
                ring.Tumble = Mathf.Repeat(ring.Tumble + ring.Look.tumble * delta, 360f);
                ring.Pivot.localRotation = Quaternion.AngleAxis(ring.Tumble, Vector3.up)
                    * Quaternion.Euler(ring.Look.tilt.x, 0f, ring.Look.tilt.y)
                    * Quaternion.AngleAxis(ring.Spin, Vector3.up);
                ring.Pivot.localScale = Vector3.one * (tierLook.radius * ring.Look.radius * Unfold(sinceUnfold, i));
            }
        }

        /// <summary>How open ring <paramref name="index"/> is, a time after the set began to unfold: 0, a little past 1, then 1.</summary>
        private static float Unfold(float since, int index)
        {
            float t = Mathf.Clamp01((since - index * UnfoldStagger) / UnfoldSeconds);
            if (t >= 1f) return 1f;
            const float overshoot = 1.70158f;
            float u = t - 1f;
            return Mathf.Max(0f, 1f + (overshoot + 1f) * u * u * u + overshoot * u * u);
        }

        /// <summary>Hands every part its colours and its share of whatever is happening. Skipped once nothing is.</summary>
        private void Paint()
        {
            float now = Time.time;
            flash = Mathf.Max(0f, flash - FlashFade * Time.deltaTime * Mathf.Max(0.35f, flash));
            boost = Mathf.MoveTowards(boost, 0f, BoostFade * Time.deltaTime * Mathf.Max(1f, Mathf.Abs(boost)));
            float sinceUnfold = now - unfoldStarted;
            bool unfolding = sinceUnfold < UnfoldSeconds + rings.Count * UnfoldStagger + 0.05f;
            bool flickering = now < flickerUntil;
            float dim = now < dudUntil ? 0.55f : 1f;
            if (flickering)
            {
                int beat = Mathf.FloorToInt((FlickerSeconds - (flickerUntil - now)) * FlickerRate);
                dim *= Flicker[Mathf.Clamp(beat, 0, Flicker.Length - 1)];
            }
            float brightness = tierLook.brightness * dim;
            float flare = flash * tierLook.brightness;

            block.Clear();
            block.SetColor(InnerId, tierLook.inner);
            block.SetColor(OuterId, tierLook.outer);
            block.SetFloat(BrightnessId, brightness);
            block.SetFloat(FlashId, flare);
            block.SetFloat(PrismId, tierLook.prism);
            coreBody.SetPropertyBlock(block);

            block.SetFloat(CoreId, tierLook.core / quadReach);
            block.SetFloat(ReachId, 1f / quadReach);
            block.SetFloat(CoronaId, tierLook.corona);
            block.SetFloat(RaysId, tierLook.rays);
            block.SetFloat(RayLengthId, tierLook.rayLength);
            block.SetFloat(BackdropId, look.backdrop);
            coronaBody.SetPropertyBlock(block);

            for (int i = 0; i < rings.Count; i++)
            {
                Ring ring = rings[i];
                block.Clear();
                block.SetColor(ColorId, tierLook.outer);
                block.SetFloat(BrightnessId, brightness * ring.Look.brightness);
                block.SetFloat(FlashId, flare * ring.Look.brightness);
                block.SetFloat(PatternId, (float)ring.Look.pattern);
                block.SetFloat(MarksId, Mathf.Max(1, ring.Look.marks));
                block.SetFloat(CometId, ring.Look.comet);
                block.SetFloat(PrismId, tierLook.prism);
                block.SetFloat(FadeId, Mathf.Clamp01(Unfold(sinceUnfold, i)));
                ring.Body.SetPropertyBlock(block);
            }

            if (moteBody != null)
            {
                block.Clear();
                block.SetColor(ColorId, Color.Lerp(tierLook.inner, tierLook.outer, 0.5f));
                block.SetFloat(BrightnessId, brightness * 1.3f);
                block.SetFloat(FlashId, flare);
                block.SetFloat(RadiusId, tierLook.radius);
                block.SetFloat(CountId, tierLook.motes);
                block.SetFloat(SizeId, MoteSize);
                moteBody.SetPropertyBlock(block);
            }

            settled = flash <= 0f && Mathf.Approximately(boost, 0f) && !unfolding && !flickering && now >= dudUntil && now >= holdUntil
                && now - punchStarted >= PunchSeconds;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        // ------------------------------------------------------------------ meshes (made once, shared by every Nova)

        /// <summary>A disc of radius 1 in its own xy plane.</summary>
        private static Mesh DiscMesh()
        {
            if (discMesh != null) return discMesh;
            var vertices = new Vector3[DiscSegments + 1];
            var triangles = new int[DiscSegments * 3];
            for (int i = 0; i < DiscSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / DiscSegments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % DiscSegments + 1;
            }
            discMesh = new Mesh { name = "Nova Disc", hideFlags = HideFlags.HideAndDontSave };
            discMesh.vertices = vertices;
            discMesh.triangles = triangles;
            // Two of its stages are drawn at the far plane: it must never be culled by where its vertices say it is.
            discMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return discMesh;
        }

        /// <summary>
        /// A ring of radius 1 whose band is <paramref name="width"/> wide: lying in its own plane, or standing on
        /// its edge like a hoop. u runs once round it, v across the band; uv1 is each vertex's offset from the
        /// middle line of the band, which the shader uses to keep a far ring from thinning away to nothing.
        /// </summary>
        private static Mesh RingMesh(float width, bool upright)
        {
            width = Mathf.Clamp(width, 0.005f, 0.9f);
            int key = Mathf.RoundToInt(width * 1000f) * 2 + (upright ? 1 : 0);
            if (RingMeshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var vertices = new Vector3[(RingSegments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var across = new Vector3[vertices.Length];
            var triangles = new int[RingSegments * 6];
            for (int i = 0; i <= RingSegments; i++)
            {
                float around = i / (float)RingSegments;
                float angle = around * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 half = (upright ? Vector3.up : direction) * (width * 0.5f);
                vertices[i * 2] = direction - half;
                vertices[i * 2 + 1] = direction + half;
                across[i * 2] = -half;
                across[i * 2 + 1] = half;
                uvs[i * 2] = new Vector2(around, 0f);
                uvs[i * 2 + 1] = new Vector2(around, 1f);
            }
            for (int i = 0; i < RingSegments; i++)
            {
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            var mesh = new Mesh { name = upright ? "Nova Hoop" : "Nova Ring", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, across);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.4f);
            RingMeshes[key] = mesh;
            return mesh;
        }

        private static Mesh SphereMesh()
        {
            if (sphereMesh != null) return sphereMesh;
            const int rows = 14, columns = 22;
            var vertices = new List<Vector3>((rows + 1) * (columns + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var triangles = new List<int>(rows * columns * 6);
            for (int row = 0; row <= rows; row++)
            {
                float polar = Mathf.PI * row / rows;
                for (int column = 0; column <= columns; column++)
                {
                    float around = Mathf.PI * 2f * column / columns;
                    var normal = new Vector3(Mathf.Sin(polar) * Mathf.Cos(around), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(around));
                    normals.Add(normal);
                    vertices.Add(normal * 0.5f);
                }
            }
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int a = row * (columns + 1) + column, b = a + columns + 1;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
            }
            sphereMesh = new Mesh { name = "Nova Star", hideFlags = HideFlags.HideAndDontSave };
            sphereMesh.SetVertices(vertices);
            sphereMesh.SetNormals(normals);
            sphereMesh.SetTriangles(triangles, 0);
            sphereMesh.RecalculateBounds();
            return sphereMesh;
        }

        /// <summary>A unit quad in the XY plane with its UVs running 0 to 1.</summary>
        private static Mesh QuadMesh()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "Nova Quad", hideFlags = HideFlags.HideAndDontSave };
            quadMesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            quadMesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            quadMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quadMesh.RecalculateBounds();
            return quadMesh;
        }

        /// <summary>
        /// Every spark as a quad with its orbit written into it (see the NovaMotes shader, which does the moving).
        /// The same sparks for every Nova, always in the same order, so a tier shows the first few of them.
        /// </summary>
        private static Mesh MoteMesh()
        {
            if (moteMesh != null) return moteMesh;
            var random = new System.Random(20261007);
            float Next(float from, float to) => from + (float)random.NextDouble() * (to - from);

            var vertices = new Vector3[MoteCount * 4];
            var corners = new Vector2[MoteCount * 4];
            var orbits = new Vector4[MoteCount * 4];
            var extras = new Vector4[MoteCount * 4];
            var triangles = new int[MoteCount * 6];
            Vector2[] quad = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            for (int i = 0; i < MoteCount; i++)
            {
                float turns = Next(0.06f, 0.3f) * (random.Next(2) == 0 ? -1f : 1f);
                var orbit = new Vector4(Next(0f, 1f), Next(0.45f, 1.3f), turns, Next(0.6f, 1.5f));
                var extra = new Vector4(Next(-1.3f, 1.3f), Next(-1.3f, 1.3f), i, Next(0f, 1f));
                for (int corner = 0; corner < 4; corner++)
                {
                    int v = i * 4 + corner;
                    corners[v] = quad[corner];
                    orbits[v] = orbit;
                    extras[v] = extra;
                }
                int t = i * 6, first = i * 4;
                triangles[t] = first; triangles[t + 1] = first + 2; triangles[t + 2] = first + 1;
                triangles[t + 3] = first; triangles[t + 4] = first + 3; triangles[t + 5] = first + 2;
            }
            moteMesh = new Mesh { name = "Nova Motes", hideFlags = HideFlags.HideAndDontSave };
            moteMesh.SetVertices(vertices);
            moteMesh.SetUVs(0, corners);
            moteMesh.SetUVs(1, orbits);
            moteMesh.SetUVs(2, extras);
            moteMesh.SetTriangles(triangles, 0);
            moteMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.6f); // every vertex is at the star: the shader moves them
            return moteMesh;
        }
    }
}
