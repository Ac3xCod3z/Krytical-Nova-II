using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>How the material and particle look of one mount's appear and vanish effect changes from the shared
    /// version (owner, 6 Nov 2026: the timing follows Elden Ring's Torrent, the look is our own, and every mount is
    /// "the same but a bit different"). Every mount uses the one shared <c>MountMaterialise</c> shader graph. This
    /// asset only picks that mount's colours, pattern, direction and particles, so a new mount needs a new asset,
    /// never a new shader. The timings are shared and live in <see cref="MountSettings"/>; a mount can override them
    /// here. <see cref="MountMaterialiser"/> applies the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Mounts/Mount Materialise Settings")]
    public sealed class MountMaterialiseSettings : ScriptableObject
    {
        /// <summary>The pattern laid over the dissolve (the graph's <c>_PatternMode</c>).</summary>
        public enum Pattern { None = 0, Scanlines = 1, Glitch = 2 }

        /// <summary>Which way the body forms (and, reversed, which way it vanishes).</summary>
        public enum Direction { BottomUp = 0, TopDown = 1, FrontToBack = 2, BackToFront = 3 }

        [Header("Material")]
        [Tooltip("A material made from the shared MountMaterialise shader graph (Gameplay/Mounts/Shaders). While the mount " +
                 "appears or vanishes, a copy of it replaces each of the model's materials, keeping that material's own " +
                 "textures and colour. Empty = the mount pops in and out with no dissolve.")]
        public Material material;

        [Header("Edge (the burning line and the rim)")]
        [Tooltip("The colour of the burning edge where the body is forming.")]
        [ColorUsage(false, true)] public Color edgeColorA = new Color(0.2f, 3f, 3.2f);
        [Tooltip("The rim glow while the body is still see-through. It also flickers toward this colour.")]
        [ColorUsage(false, true)] public Color edgeColorB = new Color(3f, 0.25f, 0.2f);
        [Tooltip("How wide the burning edge is, as a share of the dissolve (about 0.08).")]
        [Range(0f, 0.5f)] public float burnWidth = 0.08f;
        [Tooltip("How strong the rim glow is while the body is see-through (multiplies the rim).")]
        [Range(0f, 5f)] public float rimStrength = 1.5f;

        [Header("Pattern")]
        [Tooltip("The noise or pattern the dissolve eats through: circuit traces, leaf veins, cells.")]
        public Texture2D noise;
        [Tooltip("How many times the noise repeats across the model.")]
        public Vector2 noiseTiling = new Vector2(2f, 2f);
        [Tooltip("Scanlines or a glitch over the forming body (for a machine), or none (for a beast).")]
        public Pattern pattern = Pattern.None;
        [Tooltip("The pattern shows only while the body is less formed than this (0 to 1).")]
        [Range(0f, 1f)] public float patternUntil = 0.6f;

        [Header("Direction")]
        public Direction direction = Direction.BottomUp;
        [Tooltip("How much the direction leads the noise: 0 = noise only, 1 = a clean sweep. About 0.85 (owner, 6 Nov 2026): " +
                 "the body clearly forms from the feet up and vanishes from the top down; the noise only breaks up the edge.")]
        [Range(0f, 1f)] public float directionWeight = 0.85f;

        [Header("Swell")]
        [Tooltip("How far the surface puffs out as it forms, in metres (1 to 2 cm).")]
        [Range(0f, 0.1f)] public float inflate = 0.015f;

        [Header("Particles (prefabs in this mount's folder; they destroy themselves after Particle Seconds)")]
        [Tooltip("Started at the hooves just before the body starts forming.")]
        public GameObject appearParticles;
        [Tooltip("Started over the body as it vanishes.")]
        public GameObject vanishParticles;
        [Tooltip("Seconds the particles start ahead of the dissolve on appear.")]
        [Min(0f)] public float particleLead = 0.05f;
        [Tooltip("Seconds a particle prefab lives before it is removed.")]
        [Min(0.1f)] public float particleSeconds = 2f;

        [Header("Timing overrides (0 = the shared timing in Mount Settings)")]
        [Tooltip("This mount forms in this many seconds instead of the shared time. 0 = shared.")]
        [Min(0f)] public float appearSeconds;
        [Tooltip("This mount vanishes in this many seconds instead of the shared time. 0 = shared.")]
        [Min(0f)] public float vanishSeconds;

        public float AppearSeconds(MountSettings shared) =>
            appearSeconds > 0f ? appearSeconds : shared != null ? shared.materialiseSeconds : 0.65f;

        public float VanishSeconds(MountSettings shared) =>
            vanishSeconds > 0f ? vanishSeconds : shared != null ? shared.vanishSeconds : 0.5f;
    }
}
