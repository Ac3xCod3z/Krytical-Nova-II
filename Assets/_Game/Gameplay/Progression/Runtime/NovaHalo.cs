using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// The Nova a character wears: a disc of light floating above the head, drawn by the Nova shader on one quad.
    /// Created by <see cref="PlayerNova"/> on every machine that draws the character; the tier decides how it looks
    /// (<see cref="NovaLookSettings"/>). It follows the head's place but not its turning, so it floats steadily.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NovaHalo : MonoBehaviour
    {
        private const string HeadBone = "head";
        private const float HeadTop = 0.2f;        // from the head joint to the crown, at scale 1
        private const float FallbackHeight = 1.75f; // a body with no head joint
        private const float GlowMargin = 0.3f;      // room round the Nova for its light to fall away, in ring radii

        private static readonly int InnerId = Shader.PropertyToID("_Inner");
        private static readonly int OuterId = Shader.PropertyToID("_Outer");
        private static readonly int RingId = Shader.PropertyToID("_Ring");
        private static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int InnerRingsId = Shader.PropertyToID("_InnerRings");
        private static readonly int SpokesId = Shader.PropertyToID("_Spokes");
        private static readonly int GlyphsId = Shader.PropertyToID("_Glyphs");
        private static readonly int RaysId = Shader.PropertyToID("_Rays");
        private static readonly int RayLengthId = Shader.PropertyToID("_RayLength");
        private static readonly int PrismId = Shader.PropertyToID("_Prism");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int SpinId = Shader.PropertyToID("_Spin");

        private static Mesh quad;

        private Transform owner;
        private Transform head;
        private PlayerResources resources;
        private MeshRenderer body;
        private Material material;
        private NovaLookSettings look;
        private NovaTierLook tierLook;
        private Camera viewer;

        /// <summary>The tier being shown; 0 while there is no Nova.</summary>
        public int Tier { get; private set; }

        /// <summary>How far above the crown of the head the Nova reaches (metres): what a namecard has to clear. 0 with no Nova.</summary>
        public float TopAboveHead
        {
            get
            {
                if (Tier <= 0 || look == null) return 0f;
                float radius = tierLook.ringDiameter * 0.5f;
                float upright = Mathf.Cos(look.tilt * Mathf.Deg2Rad);
                return Mathf.Max(0f, look.gapAboveHead) + radius * upright * (2f + tierLook.rayLength) + radius * 0.1f;
            }
        }

        /// <summary>The crown of the character's head in the world: where a Nova and a namecard are measured from.</summary>
        public Vector3 Crown
        {
            get
            {
                float scale = owner != null ? owner.lossyScale.y : 1f;
                if (head != null) return head.position + Vector3.up * (HeadTop * scale);
                return owner != null ? owner.position + Vector3.up * (FallbackHeight * scale) : transform.position;
            }
        }

        public static NovaHalo Create(Transform owner)
        {
            var go = new GameObject("Nova") { layer = owner.gameObject.layer };
            go.transform.SetParent(owner, false);
            var halo = go.AddComponent<NovaHalo>();
            halo.owner = owner;
            halo.resources = owner.GetComponent<PlayerResources>();
            halo.head = FindDeep(owner, HeadBone);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            halo.body = go.AddComponent<MeshRenderer>();
            halo.body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            halo.body.receiveShadows = false;
            halo.body.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            halo.body.enabled = false;
            return halo;
        }

        /// <summary>Shows the Nova of a tier, or none for tier 0.</summary>
        public void Show(NovaLookSettings settings, int tier)
        {
            look = settings;
            bool any = settings != null && settings.material != null && settings.tiers != null && settings.tiers.Length > 0 && tier > 0;
            Tier = any ? tier : 0;
            if (!any)
            {
                body.enabled = false;
                return;
            }
            tierLook = settings.tiers[Mathf.Min(tier, settings.tiers.Length) - 1];
            if (material == null || material.shader != settings.material.shader)
            {
                if (material != null) Destroy(material);
                material = new Material(settings.material) { name = "Nova (instance)" };
                body.sharedMaterial = material;
            }

            float radius = Mathf.Max(0.05f, tierLook.ringDiameter * 0.5f);
            float reach = 1f + tierLook.rayLength + GlowMargin; // the quad's half-size, in ring radii
            transform.localScale = Vector3.one * (radius * reach * 2f / Mathf.Max(0.0001f, owner.lossyScale.y));
            material.SetColor(InnerId, tierLook.inner);
            material.SetColor(OuterId, tierLook.outer);
            material.SetFloat(RingId, 1f / reach);
            material.SetFloat(RingWidthId, tierLook.ringWidth);
            material.SetFloat(FillId, tierLook.fill);
            material.SetFloat(InnerRingsId, tierLook.innerRings);
            material.SetFloat(SpokesId, tierLook.spokes);
            material.SetFloat(GlyphsId, tierLook.glyphs);
            material.SetFloat(RaysId, tierLook.rays);
            material.SetFloat(RayLengthId, tierLook.rayLength);
            material.SetFloat(PrismId, tierLook.prism);
            material.SetFloat(BrightnessId, tierLook.brightness);
            material.SetFloat(SpinId, settings.spinPerMinute / 60f);
            Place();
        }

        private void LateUpdate()
        {
            if (Tier <= 0) return;
            bool alive = resources == null || resources.IsAlive; // a body on the ground wears no Nova
            if (body.enabled != alive) body.enabled = alive;
            if (alive) Place();
        }

        private void Place()
        {
            if (owner == null || look == null) return;
            if (head == null) head = FindDeep(owner, HeadBone);
            float radius = tierLook.ringDiameter * 0.5f;
            float upright = Mathf.Cos(look.tilt * Mathf.Deg2Rad);
            Vector3 forward = owner.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            Vector3 centre = Crown + Vector3.up * (look.gapAboveHead + radius * upright) - forward * look.behindHead;
            Vector3 facing = forward;
            if (look.faceCamera)
            {
                if (viewer == null) viewer = Camera.main;
                if (viewer != null)
                {
                    Vector3 toViewer = viewer.transform.position - centre;
                    toViewer.y = 0f;
                    if (toViewer.sqrMagnitude > 0.0001f) facing = toViewer.normalized;
                }
            }
            Quaternion rotation = Quaternion.LookRotation(facing, Vector3.up) * Quaternion.AngleAxis(-look.tilt, Vector3.right);
            transform.SetPositionAndRotation(centre, rotation);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        /// <summary>A unit quad in the XY plane with its UVs running 0 to 1, shared by every Nova.</summary>
        private static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Nova Quad", hideFlags = HideFlags.HideAndDontSave };
            quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quad.RecalculateBounds();
            return quad;
        }
    }
}
