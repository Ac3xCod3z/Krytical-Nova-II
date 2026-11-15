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
        // Per renderer, what its material said the last time it was looked at (perf clean-ups round 2, chunk 15 part 2: HasProperty,
        // IsKeywordEnabled and GetColor were asked every frame of a pulse): looked at again only when the material reference changes,
        // as when the appear effect swaps its own copies in and out.
        private Material[] seenMaterial;
        private Color[] seenEmission;
        private byte[] seenKind;   // 0 no emission colour, 1 an emission colour, 2 and its emission switched on

        /// <summary>What <paramref name="m"/> says for the glow: 0 no emission colour, 1 one, 2 one and switched on; its colour.</summary>
        public static byte KindOf(Material m, out Color emission)
        {
            emission = default;
            if (m == null || !m.HasProperty(EmissionColor)) return 0;
            emission = m.GetColor(EmissionColor);
            return m.IsKeywordEnabled("_EMISSION") ? (byte)2 : (byte)1;
        }

        private byte Look(int i, Material m, out Color emission)
        {
            if (seenMaterial == null || seenMaterial.Length != renderers.Length)
            {
                seenMaterial = new Material[renderers.Length];
                seenEmission = new Color[renderers.Length];
                seenKind = new byte[renderers.Length];
            }
            if (m == null) { emission = default; return 0; }
            if (!ReferenceEquals(seenMaterial[i], m)) { seenMaterial[i] = m; seenKind[i] = KindOf(m, out seenEmission[i]); }
            emission = seenEmission[i];
            return seenKind[i];
        }
        private float glow, shake, seconds, startedAt;
        private Color colour;
        private Vector3 shakeApplied;
        private Light pop;
        private readonly System.Collections.Generic.List<MountPulseShed> sheds = new System.Collections.Generic.List<MountPulseShed>(2);
        /// <summary>How long a shed of particles stays (the old destroy time).</summary>
        public const float ShedSeconds = 1.6f;

        public static void Play(Transform mount, float glow, float shake, float seconds, Color colour, GameObject particles, float particleScale)
        {
            if (mount == null) return;
            if (!mount.TryGetComponent(out MountPulse pulse)) pulse = mount.gameObject.AddComponent<MountPulse>();
            pulse.Begin(glow, shake, seconds > 0f ? seconds : DefaultSeconds, colour);
            if (particles != null) pulse.Shed(particles, particleScale, Time.time);
        }

        /// <summary>
        /// Shed off the body: from its middle, riding with it, gone after the pulse plus their own fall (<see cref="ShedSeconds"/>).
        /// A copy whose shed is over is reused (perf night chunk 5: was a new copy made and destroyed per landing); a new one is made
        /// only while every copy is still falling. Returns the copy (tests).
        /// </summary>
        public MountPulseShed Shed(GameObject particles, float particleScale, float now)
        {
            Transform mount = transform;
            Vector3 middle = mount.position + Vector3.up * 0.6f;
            MountPulseShed shed = null;
            for (int i = sheds.Count - 1; i >= 0; i--)
            {
                if (sheds[i] == null) { sheds.RemoveAt(i); continue; }   // destroyed from outside
                if (shed == null && sheds[i].Prefab == particles && sheds[i].IsFree(now)) shed = sheds[i];
            }
            if (shed == null) { shed = MountPulseShed.Make(particles, middle, mount.rotation, mount); sheds.Add(shed); }
            else shed.Restart(middle, mount.rotation);
            shed.transform.localScale = Vector3.one * Mathf.Max(0.05f, particleScale);
            shed.Live(now, ShedSeconds);
            return shed;
        }

        private void Begin(float glowTimes, float shakeMetres, float length, Color glowColour)
        {
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            if (block == null) block = new MaterialPropertyBlock();
            if (seenMaterial != null) System.Array.Clear(seenMaterial, 0, seenMaterial.Length);   // a new pulse looks again
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
                    if (Look(i, m, out Color emission) != 2) continue;
                    r.GetPropertyBlock(block); // keep what else is set on it
                    block.SetColor(EmissionColor, emission * (1f + glow * k));
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
                    if (Look(i, m, out Color emission) == 0) continue;
                    r.GetPropertyBlock(block);
                    block.SetColor(EmissionColor, emission);
                    r.SetPropertyBlock(block);
                }
            }
            if (pop != null) { pop.intensity = 0f; pop.enabled = false; }
            enabled = false;
        }
    }
}
