using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Dedicated controller component attached to custom ParticleSystem prefabs (such as Particle_URP).
    /// Handles hit impact surface color sampling, brightness boosting, and Linear/Gamma color space compensation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public class QuickDestructionParticleTint : MonoBehaviour
    {
        [Header("Impact Color Tuning")]
        [Range(0.5f, 3.0f), Tooltip("Boost particle brightness so impact particles pop visually and match surface lighting.")]
        public float BrightnessBoost = 1.15f;

        [Range(0f, 1f), Tooltip("Blend weight between prefab default color (0) and sampled impact surface color (1).")]
        public float SampledColorWeight = 1.0f;

        [Tooltip("Automatically compensate for Linear Color Space so impact colors match the bright visible surface.")]
        public bool CorrectLinearColorSpace = true;

        [Range(0.05f, 0.8f), Tooltip("Prevents particles from becoming pitch black when hitting dark materials or shadows.")]
        public float MinimumLuminance = 0.15f;

        ParticleSystem m_system;
        ParticleSystemRenderer m_renderer;
        MaterialPropertyBlock m_block;

        void Awake()
        {
            m_system = GetComponent<ParticleSystem>();
            m_renderer = GetComponent<ParticleSystemRenderer>();
            m_block = new MaterialPropertyBlock();
            EnsurePipelineMaterials();
        }

        /// <summary>
        /// Guarantees every particle renderer in this prefab uses a material valid for the
        /// active render pipeline. The shipped prefab bakes an HDRP/Unlit material, which
        /// renders magenta under URP; this swaps it for the correct pipeline particle
        /// material while carrying its sprite texture over so the look is preserved.
        /// </summary>
        void EnsurePipelineMaterials()
        {
            ParticleSystemRenderer[] renderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                ParticleSystemRenderer r = renderers[i];
                if (r == null) continue;
                Material shared = r.sharedMaterial;
                if (DestructionPresetEffectLibrary.IsMaterialPipelineCompatible(shared))
                    continue;

                Texture mainTexture = ExtractMainTexture(shared);
                Material replacement =
                    DestructionPresetEffectLibrary.CreatePipelineParticleMaterial(mainTexture);
                if (replacement != null) r.sharedMaterial = replacement;
            }
        }

        static Texture ExtractMainTexture(Material material)
        {
            if (material == null) return null;
            if (material.HasProperty("_UnlitColorMap") && material.GetTexture("_UnlitColorMap") != null)
                return material.GetTexture("_UnlitColorMap");
            if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
                return material.GetTexture("_BaseColorMap");
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
                return material.GetTexture("_BaseMap");
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
                return material.GetTexture("_MainTex");
            return material.mainTexture;
        }

        /// <summary>
        /// Applies the sampled impact surface color with brightness boosting and color space correction.
        /// </summary>
        public void ApplyImpactColor(Color surfaceColor, float intensity)
        {
            if (m_system == null) m_system = GetComponent<ParticleSystem>();
            if (m_renderer == null) m_renderer = GetComponent<ParticleSystemRenderer>();
            Color finalColor = surfaceColor;

            // 1. A sampled albedo can legitimately be very dark, but multiplying it by
            // the grayscale Sharp_Piece texture made the complete burst almost black.
            // Lift luminance without changing the sampled hue.
            float luminance = Luminance(finalColor);
            float minimum = Mathf.Clamp(MinimumLuminance, 0.05f, 0.8f);
            if (luminance < minimum)
            {
                if (luminance > 0.0001f)
                    finalColor *= minimum / luminance;
                else
                    finalColor = new Color(minimum, minimum, minimum, finalColor.a);
            }

            // 2. Preserve the sampled hue and saturation. An older guard capped every
            // saturated surface at 0.35, turning the correctly sampled wood color grey.

            // 3. Baked texture samples are working-space colors. In a Linear project the
            // unlit particle material multiplies that value by its own grayscale texture,
            // so feeding the linear value directly makes the visible result much darker
            // than the source surface. This option existed in the component but was never
            // actually applied.
            if (CorrectLinearColorSpace &&
                QualitySettings.activeColorSpace == ColorSpace.Linear)
                finalColor = finalColor.gamma;

            float boost = Mathf.Clamp(BrightnessBoost, 0.5f, 3f);
            finalColor.r = Mathf.Clamp01(finalColor.r * boost);
            finalColor.g = Mathf.Clamp01(finalColor.g * boost);
            finalColor.b = Mathf.Clamp01(finalColor.b * boost);

            // 4. Blend between the prefab default color and the sampled surface color.
            if (SampledColorWeight < 0.999f)
            {
                Color prefabColor = m_system != null ? m_system.main.startColor.color : Color.white;
                finalColor = Color.Lerp(prefabColor, finalColor, Mathf.Clamp01(SampledColorWeight));
            }
            finalColor.a = 1f;

            // 5. Use exactly one RGB multiplier. URP/HDRP Unlit read the material color,
            // while legacy particle shaders may also multiply vertex color by material
            // color. Keeping both tinted squares (or cubes) dark surface colors. A shader
            // with no supported material tint property falls back to vertex color.
            ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem sys = systems[i];
                if (sys == null) continue;
                ParticleSystemRenderer renderer =
                    sys.GetComponent<ParticleSystemRenderer>();
                bool materialTint = renderer != null &&
                                    DestructionPresetEffectLibrary.SupportsMaterialTint(
                                        renderer.sharedMaterial);
                var main = sys.main;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    materialTint ? Color.white : finalColor);

                var colorOverLifetime = sys.colorOverLifetime;
                if (colorOverLifetime.enabled)
                {
                    Gradient g = new Gradient();
                    g.SetKeys(
                        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
                    );
                    colorOverLifetime.color = new ParticleSystem.MinMaxGradient(g);
                }
            }

            // 6. Tint renderers through a property block. The authored/shared material and
            // its Sharp_Piece texture remain untouched; no per-hit material instance is
            // created and no color can bleed between pooled systems.
            ParticleSystemRenderer[] renderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
            m_block ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                ParticleSystemRenderer r = renderers[i];
                if (r == null ||
                    !DestructionPresetEffectLibrary.SupportsMaterialTint(r.sharedMaterial))
                    continue;

                r.GetPropertyBlock(m_block);
                DestructionPresetEffectLibrary.ApplyBlockTint(m_block, finalColor);
                r.SetPropertyBlock(m_block);
            }
        }

        static float Luminance(Color color) =>
            0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
    }
}
