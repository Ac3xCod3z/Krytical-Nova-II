using System.Collections.Generic;
using KryticalNova.Combat;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Sits on the object PlayerAnimancerMotor moves between the sheath and hand sockets (the old katana blade, which
    /// also carries the WeaponHitbox). Swaps the visible weapon model under it and resizes the hitbox; the draw/sheathe
    /// reparenting keeps working untouched because it moves this mount, not the model.
    /// A tempered weapon also wears a glow: a second, additive material laid over the model's own (see
    /// <see cref="SetGlow"/>), so it works on any weapon whatever shader that uses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponVisualMount : MonoBehaviour
    {
        [Tooltip("Renderer baked into the mount itself (the original katana). Hidden once item visuals take over.")]
        [SerializeField] private Renderer builtInRenderer;
        [SerializeField] private WeaponHitbox hitbox;

        private GameObject spawned;
        private ItemDefinition current;

        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int SparkleId = Shader.PropertyToID("_Sparkle");
        private Material glow;          // this mount's own copy: every weapon glows in its own colour
        private bool glowOn;
        private readonly List<Material> materialScratch = new List<Material>(4);

        private void Awake()
        {
            if (builtInRenderer == null) builtInRenderer = GetComponent<Renderer>();
            if (hitbox == null) hitbox = GetComponent<WeaponHitbox>();
        }

        public void SetWeapon(ItemDefinition item)
        {
            if (current == item && (item == null || spawned != null)) return;
            current = item;
            if (spawned != null) Destroy(spawned);
            spawned = null;

            if (builtInRenderer != null) builtInRenderer.enabled = false;
            if (item == null || item.weaponPrefab == null) return; // unarmed: mount stays, nothing visible

            spawned = Instantiate(item.weaponPrefab, transform, false);
            spawned.name = item.weaponPrefab.name;
            spawned.transform.localPosition = item.weaponLocalPosition;
            spawned.transform.localRotation = Quaternion.Euler(item.weaponLocalEuler);
            spawned.transform.localScale = item.weaponLocalScale;
            VisualUtility.PrepareCosmetic(spawned, gameObject.layer);
            if (hitbox != null) hitbox.Configure(item.hitboxCenter, item.hitboxSize);
            if (glowOn) AttachGlow(); // a new model for the same glow (the look changed, not the Temper)
        }

        /// <summary>
        /// Turns the glow on in a Temper band's colour at a rarity's strength, or off when <paramref name="template"/>
        /// is null or <paramref name="on"/> is false. The template is the shared glow material; each mount works on a copy.
        /// </summary>
        public void SetGlow(Material template, bool on, Color color, TemperGlowLook look)
        {
            if (!on || template == null)
            {
                if (glowOn) DetachGlow();
                glowOn = false;
                return;
            }
            if (glow == null) glow = new Material(template) { name = template.name + " (weapon)" };
            glow.SetColor(GlowColorId, color);
            glow.SetFloat(IntensityId, look.intensity);
            glow.SetFloat(RimPowerId, look.rimPower);
            glow.SetFloat(PulseId, look.pulse);
            glow.SetFloat(PulseSpeedId, look.pulseSpeed);
            glow.SetFloat(SparkleId, look.sparkles ? 1.5f : 0f);
            if (!glowOn) AttachGlow();
            glowOn = true;
        }

        private const string GlowShellName = "TemperGlow";

        /// <summary>
        /// The glow is a shell: a copy of each mesh of the model, drawn once more with the glow material on every one
        /// of its submeshes. (A material added to the model's own renderer would only cover its last submesh.)
        /// </summary>
        private void AttachGlow()
        {
            if (spawned == null || glow == null) return;
            DetachGlow();
            foreach (var filter in spawned.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                var source = filter.GetComponent<MeshRenderer>();
                // A shell being removed this frame is still in the hierarchy: never build a shell of a shell.
                if (mesh == null || source == null || filter.name == GlowShellName) continue;
                var shell = new GameObject(GlowShellName) { layer = filter.gameObject.layer };
                shell.transform.SetParent(filter.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = shell.AddComponent<MeshRenderer>();
                materialScratch.Clear();
                for (int i = 0; i < mesh.subMeshCount; i++) materialScratch.Add(glow);
                renderer.SetSharedMaterials(materialScratch);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
        }

        private void DetachGlow()
        {
            if (spawned == null) return;
            foreach (var filter in spawned.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == GlowShellName) Destroy(filter.gameObject);
        }

        private void OnDestroy()
        {
            if (glow != null) Destroy(glow);
        }
    }

    public static class VisualUtility
    {
        /// <summary>Cosmetic copies must never collide, receive raycasts, or run gameplay physics.</summary>
        public static void PrepareCosmetic(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
