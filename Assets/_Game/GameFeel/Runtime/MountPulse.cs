using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// A mount's pulse as the rider lands in the saddle (owner, 7 Nov 2026 VFX pick d; retest 4: it must be clearly seen):
    /// its lights flare (the emission of every material that glows, brightened for a moment) and its body shudders. The
    /// numbers are the stage's own data (MountStageLook.pulseGlow / pulseShake / pulseSeconds), so the Circuit Strider's
    /// eyes and strips flare and the Brambleback - no lights - shakes off dust, with no code for either. One component per
    /// mount model, its renderers found once, switched off between pulses; it only adds to the model's position, so the
    /// ride's own spring and the vanish keep working under it.
    /// </summary>
    public sealed class MountPulse : MonoBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private const float DefaultSeconds = 0.4f;

        private Renderer[] glowing;
        private Color[] baseEmission;
        private MaterialPropertyBlock block;
        private float glow, shake, seconds, startedAt;
        private Vector3 shakeApplied;

        public static void Play(Transform mount, float glow, float shake, float seconds)
        {
            if (mount == null) return;
            if (!mount.TryGetComponent(out MountPulse pulse)) pulse = mount.gameObject.AddComponent<MountPulse>();
            pulse.Begin(glow, shake, seconds > 0f ? seconds : DefaultSeconds);
        }

        private void Begin(float glowTimes, float shakeMetres, float length)
        {
            if (glowing == null) FindGlowing();
            glow = glowTimes;
            shake = shakeMetres;
            seconds = length;
            startedAt = Time.time;
            enabled = true;
        }

        // The renderers whose first material glows (emission on), with its colour as authored.
        private void FindGlowing()
        {
            block = new MaterialPropertyBlock();
            var found = new System.Collections.Generic.List<Renderer>();
            var colours = new System.Collections.Generic.List<Color>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null || !m.HasProperty(EmissionColor) || !m.IsKeywordEnabled("_EMISSION")) continue;
                found.Add(r);
                colours.Add(m.GetColor(EmissionColor));
            }
            glowing = found.ToArray();
            baseEmission = colours.ToArray();
        }

        /// <summary>0..1..0: up fast (a fifth of the time), down slower.</summary>
        public static float Envelope(float t01)
        {
            if (t01 <= 0f || t01 >= 1f) return 0f;
            return t01 < 0.2f ? t01 / 0.2f : 1f - (t01 - 0.2f) / 0.8f * ((t01 - 0.2f) / 0.8f);
        }

        private void LateUpdate()
        {
            float t = seconds > 0f ? (Time.time - startedAt) / seconds : 1f;
            float k = Envelope(t);
            if (glow > 0f && glowing != null)
            {
                for (int i = 0; i < glowing.Length; i++)
                {
                    if (glowing[i] == null) continue;
                    glowing[i].GetPropertyBlock(block); // keep what else is set on it (the appear/vanish effect)
                    block.SetColor(EmissionColor, baseEmission[i] * (1f + glow * k));
                    glowing[i].SetPropertyBlock(block);
                }
            }
            if (shake > 0f)
            {
                // A shudder side to side and a little up, fading out.
                float s = Time.time * 38f;
                Vector3 offset = (transform.right * Mathf.Sin(s) + transform.up * 0.4f * Mathf.Sin(s * 1.7f)) * (shake * k);
                transform.position += offset - shakeApplied;
                shakeApplied = offset;
            }
            if (t >= 1f) Finish();
        }

        private void Finish()
        {
            transform.position -= shakeApplied;
            shakeApplied = Vector3.zero;
            if (glowing != null)
            {
                for (int i = 0; i < glowing.Length; i++)
                {
                    if (glowing[i] == null) continue;
                    glowing[i].GetPropertyBlock(block);
                    block.SetColor(EmissionColor, baseEmission[i]);
                    glowing[i].SetPropertyBlock(block);
                }
            }
            enabled = false;
        }
    }
}
