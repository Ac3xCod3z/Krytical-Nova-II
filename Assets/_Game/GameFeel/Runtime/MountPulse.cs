using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// A mount's pulse as the rider lands in the saddle (owner, 7 Nov 2026 VFX pick d; retest 5: one weighty jolt that
    /// settles fast, and a flare that reads on screen). The body takes the weight in one compression and a quick shudder
    /// - no rocking sway; a mount with lights flares them (every glowing material's emission, brightened hard for a
    /// moment) with a short light pop in its glow colour; and it sheds its own particles (the Brambleback's leaves). All
    /// the numbers are the stage's own data (MountStageLook.pulse*), so no mount has code of its own. One component per
    /// mount model, switched off between pulses; it only adds to the model's position, so the ride's spring and the
    /// vanish keep working under it.
    /// </summary>
    public sealed class MountPulse : MonoBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private const float DefaultSeconds = 0.4f;
        /// <summary>Seconds to the bottom of the jolt, then it springs back and settles.</summary>
        public const float JoltDownSeconds = 0.06f;
        /// <summary>Peak brightness of the light pop, and its reach in metres at a mount of 1 m.</summary>
        private const float LightPeak = 6f, LightRange = 4.5f;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float glow, shake, seconds, startedAt;
        private Color colour;
        private Vector3 shakeApplied;
        private Light pop;

        public static void Play(Transform mount, float glow, float shake, float seconds, Color colour, GameObject particles, float particleScale)
        {
            if (mount == null) return;
            if (!mount.TryGetComponent(out MountPulse pulse)) pulse = mount.gameObject.AddComponent<MountPulse>();
            pulse.Begin(glow, shake, seconds > 0f ? seconds : DefaultSeconds, colour);
            if (particles != null)
            {
                // Shed off the body: from its middle, riding with it, gone after the pulse plus their own fall.
                Vector3 middle = mount.position + Vector3.up * 0.6f;
                GameObject shed = Instantiate(particles, middle, mount.rotation, mount);
                shed.transform.localScale = Vector3.one * Mathf.Max(0.05f, particleScale);
                Destroy(shed, 1.6f);
            }
        }

        private void Begin(float glowTimes, float shakeMetres, float length, Color glowColour)
        {
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            if (block == null) block = new MaterialPropertyBlock();
            glow = glowTimes;
            shake = shakeMetres;
            seconds = length;
            colour = glowColour;
            startedAt = Time.time;
            if (glow > 0f && pop == null)
            {
                pop = new GameObject("Pulse Light").AddComponent<Light>();
                pop.transform.SetParent(transform, false);
                pop.transform.localPosition = Vector3.up * 0.8f;
                pop.type = LightType.Point;
                pop.shadows = LightShadows.None;
            }
            if (pop != null)
            {
                pop.color = colour.maxColorComponent > 0f ? colour : Color.white;
                pop.range = LightRange;
                pop.intensity = 0f;
                pop.enabled = glow > 0f;
            }
            enabled = true;
        }

        /// <summary>0..1..0: up fast (a fifth of the time), down slower.</summary>
        public static float Envelope(float t01)
        {
            if (t01 <= 0f || t01 >= 1f) return 0f;
            return t01 < 0.2f ? t01 / 0.2f : 1f - (t01 - 0.2f) / 0.8f * ((t01 - 0.2f) / 0.8f);
        }

        /// <summary>
        /// The jolt, in units of the pulse's shake, at <paramref name="t"/> seconds: down to 1 at
        /// <see cref="JoltDownSeconds"/>, then a stiff damped spring back through a small overshoot, settled (under 5 %)
        /// by about 0.35 s.
        /// </summary>
        public static float Jolt(float t)
        {
            if (t <= 0f) return 0f;
            if (t < JoltDownSeconds) return t / JoltDownSeconds;
            float u = t - JoltDownSeconds;
            return Mathf.Exp(-11f * u) * Mathf.Cos(2f * Mathf.PI * 4.2f * u);
        }

        private void LateUpdate()
        {
            float since = Time.time - startedAt;
            float t = seconds > 0f ? since / seconds : 1f;
            float k = Envelope(t);
            if (glow > 0f && renderers != null)
            {
                // The materials are looked at every frame: the appear effect swaps its own copies in and out around now.
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    Material m = r != null ? r.sharedMaterial : null;
                    if (m == null || !m.HasProperty(EmissionColor) || !m.IsKeywordEnabled("_EMISSION")) continue;
                    r.GetPropertyBlock(block); // keep what else is set on it
                    block.SetColor(EmissionColor, m.GetColor(EmissionColor) * (1f + glow * k));
                    r.SetPropertyBlock(block);
                }
                if (pop != null) pop.intensity = LightPeak * k;
            }
            if (shake > 0f)
            {
                // One compression and a quick shudder that dies within a few hundredths of a second: weight, not sway.
                float shudder = Mathf.Exp(-22f * since) * Mathf.Sin(2f * Mathf.PI * 19f * since);
                Vector3 offset = -transform.up * (shake * Jolt(since)) + transform.right * (shake * 0.25f * shudder);
                transform.position += offset - shakeApplied;
                shakeApplied = offset;
            }
            if (t >= 1f) Finish();
        }

        private void Finish()
        {
            transform.position -= shakeApplied;
            shakeApplied = Vector3.zero;
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    Material m = r != null ? r.sharedMaterial : null;
                    if (m == null || !m.HasProperty(EmissionColor)) continue;
                    r.GetPropertyBlock(block);
                    block.SetColor(EmissionColor, m.GetColor(EmissionColor));
                    r.SetPropertyBlock(block);
                }
            }
            if (pop != null) { pop.intensity = 0f; pop.enabled = false; }
            enabled = false;
        }
    }
}
