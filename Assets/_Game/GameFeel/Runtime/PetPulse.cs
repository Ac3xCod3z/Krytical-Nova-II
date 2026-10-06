using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The pet's own part of an evolution: it swells and snaps back while its materials flash in the colour of the
    /// stage it has reached. Works on whatever model stands in for the pet, and leaves the shared materials alone
    /// (the flash is a per-renderer property block). Whatever scale the pet has when the pulse ends is kept, so a
    /// size step applied in the same frame is not undone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetPulse : MonoBehaviour
    {
        private const float Seconds = 0.7f;
        private const float Swell = 0.55f;      // extra size at the peak, as a share of the pet's own size
        private const float FlashPeak = 2.2f;   // HDR multiplier at the first frame of the flash
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private Color color;
        private float elapsed;
        private float lastFactor = 1f;

        public static void Play(Transform pet, Color glow)
        {
            if (pet == null) return;
            if (!pet.TryGetComponent(out PetPulse pulse)) pulse = pet.gameObject.AddComponent<PetPulse>();
            pulse.Begin(glow);
        }

        private void Begin(Color glow)
        {
            Restore();
            renderers = GetComponentsInChildren<Renderer>(false);
            block ??= new MaterialPropertyBlock();
            color = glow.maxColorComponent > 0.01f ? glow : Color.white;
            color.a = 1f;
            elapsed = 0f;
            enabled = true;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Seconds);

            // A quick swell with one overshoot: out fast, back with a small bounce.
            float swell = Mathf.Sin(t * Mathf.PI) * Mathf.Exp(-3f * t) * 2.2f;
            float factor = 1f + Swell * swell;
            transform.localScale *= factor / lastFactor;
            lastFactor = factor;

            float flash = (1f - t) * (1f - t);
            ApplyFlash(flash);
            if (t >= 1f) Restore();
        }

        private void ApplyFlash(float amount)
        {
            if (renderers == null) return;
            Color emission = color * (FlashPeak * amount);
            foreach (Renderer target in renderers)
            {
                if (target == null || target is ParticleSystemRenderer || target is TrailRenderer) continue;
                target.GetPropertyBlock(block);
                block.SetColor(EmissionId, emission);
                // Materials without emission still flash: their own colour is pushed toward the glow.
                Material material = target.sharedMaterial;
                if (material != null)
                {
                    int id = material.HasProperty(BaseColorId) ? BaseColorId : material.HasProperty(ColorId) ? ColorId : 0;
                    if (id != 0) block.SetColor(id, Color.Lerp(material.GetColor(id), color * 1.6f, amount * 0.85f));
                }
                target.SetPropertyBlock(block);
            }
        }

        private void Restore()
        {
            if (lastFactor != 1f) transform.localScale /= lastFactor;
            lastFactor = 1f;
            if (renderers != null)
                foreach (Renderer target in renderers)
                    if (target != null && !(target is ParticleSystemRenderer) && !(target is TrailRenderer)) target.SetPropertyBlock(null);
            renderers = null;
            enabled = false;
        }

        private void OnDisable()
        {
            if (renderers != null) Restore();
        }
    }
}
