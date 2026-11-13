using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// Makes a mount form out of nothing as its rider hops on and dissolve as they get off (owner, 6 Nov 2026: Torrent's
    /// timing, our own look). For the length of the effect it swaps every material of the model for a copy of the shared
    /// <c>MountMaterialise</c> shader graph material from the mount's <see cref="MountMaterialiseSettings"/>. Each copy
    /// keeps the original material's textures and colour. It then drives <c>_Spawn</c> (0 = gone, 1 = solid) and puts
    /// the originals back when the mount is solid. It also starts the mount's particles. Added on the mount model by
    /// <see cref="MountRider"/> on every machine that draws. Purely visual: nothing here is sent.
    /// </summary>
    /// <remarks>
    /// The shader graph reads these properties (keep the names in step with <c>Shaders/MountMaterialise.md</c>):
    /// <c>_Spawn</c>, <c>_EdgeColorA</c>, <c>_EdgeColorB</c>, <c>_BurnWidth</c>, <c>_RimStrength</c>, <c>_FadeTex</c>,
    /// <c>_FadeTiling</c>, <c>_PatternMode</c>, <c>_PatternUntil</c>, <c>_DirectionWeight</c>, <c>_Inflate</c>,
    /// <c>_SweepOrigin</c> (world, xyz), <c>_SweepAxis</c> (world, xyz, unit) and <c>_SweepLength</c> (metres). The sweep
    /// is in world space and is set every frame, so it fits every growth stage and follows the mount as it moves.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MountMaterialiser : MonoBehaviour
    {
        private static readonly int Spawn = Shader.PropertyToID("_Spawn");
        private static readonly int EdgeColorA = Shader.PropertyToID("_EdgeColorA");
        private static readonly int EdgeColorB = Shader.PropertyToID("_EdgeColorB");
        private static readonly int BurnWidth = Shader.PropertyToID("_BurnWidth");
        private static readonly int RimStrength = Shader.PropertyToID("_RimStrength");
        private static readonly int FadeTex = Shader.PropertyToID("_FadeTex");
        private static readonly int FadeTiling = Shader.PropertyToID("_FadeTiling");
        private static readonly int PatternMode = Shader.PropertyToID("_PatternMode");
        private static readonly int PatternUntil = Shader.PropertyToID("_PatternUntil");
        private static readonly int DirectionWeight = Shader.PropertyToID("_DirectionWeight");
        private static readonly int Inflate = Shader.PropertyToID("_Inflate");
        private static readonly int SweepOrigin = Shader.PropertyToID("_SweepOrigin");
        private static readonly int SweepAxis = Shader.PropertyToID("_SweepAxis");
        private static readonly int SweepLength = Shader.PropertyToID("_SweepLength");

        // The original material's own look, carried onto its dissolve copy when both have the property.
        private static readonly int[] CarriedTextures =
        {
            Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_BumpMap"), Shader.PropertyToID("_EmissionMap"),
            Shader.PropertyToID("_MetallicGlossMap"), Shader.PropertyToID("_OcclusionMap"),
        };
        private static readonly int[] CarriedColors = { Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_EmissionColor") };
        private static readonly int[] CarriedFloats =
        {
            Shader.PropertyToID("_Smoothness"), Shader.PropertyToID("_Metallic"), Shader.PropertyToID("_BumpScale"),
        };

        private struct Swapped
        {
            public Renderer renderer;
            public Material[] originals;
            public Material[] copies;
        }

        private readonly List<Swapped> swapped = new List<Swapped>();
        private Renderer[] renderers;
        private MountMaterialiseSettings look;
        private bool appearing;
        private bool destroyWhenDone;
        private bool particlesStarted;
        private bool gone; // a vanish has finished and the model is hidden
        private float startsAt, endsAt;
        private float bottom, length; // along the sweep axis, in the model's own space

        /// <summary>True while the mount is forming or vanishing.</summary>
        public bool IsBusy => enabled && look != null;

        /// <summary>True once a vanish has finished and the model is hidden.</summary>
        public bool IsGone => gone;

        /// <summary>Form the mount: invisible now, forming from <paramref name="delay"/> seconds for <paramref name="seconds"/>.</summary>
        public void Appear(MountMaterialiseSettings settings, float delay, float seconds) =>
            Begin(settings, true, delay, seconds, false);

        /// <summary>Dissolve the mount from <paramref name="delay"/> seconds for <paramref name="seconds"/>; then hide
        /// it, or destroy its GameObject when <paramref name="destroy"/> is set.</summary>
        public void Vanish(MountMaterialiseSettings settings, float delay, float seconds, bool destroy) =>
            Begin(settings, false, delay, seconds, destroy);

        private void Begin(MountMaterialiseSettings settings, bool appear, float delay, float seconds, bool destroy)
        {
            // Restore any swap still in place before a new one: it is rebuilt from the originals.
            Restore();
            renderers = GetComponentsInChildren<Renderer>(true);
            // Forming again after a finished vanish (the mounted vault, 13 Nov 2026): the vanish hid the renderers and the
            // dissolve only drives _Spawn, so they are shown again here (at _Spawn 0 nothing shows until the forming starts).
            if (appear && gone) SetVisible(true);
            look = settings;
            appearing = appear;
            destroyWhenDone = destroy;
            particlesStarted = false;
            gone = false;
            startsAt = Time.time + Mathf.Max(0f, delay);
            endsAt = startsAt + Mathf.Max(0.01f, seconds);
            Measure();
            if (look != null && look.material != null) SwapIn();
            enabled = true;
            Tick();
        }

        private void Update() => Tick();

        private void Tick()
        {
            if (renderers == null) { enabled = false; return; }
            float now = Time.time;
            if (!particlesStarted && look != null)
            {
                float lead = appearing ? look.particleLead : 0f;
                if (now >= startsAt - lead) StartParticles();
            }
            float t = Mathf.InverseLerp(startsAt, endsAt, now);
            float spawn = appearing ? t : 1f - t;
            if (swapped.Count > 0) Drive(spawn);
            // With no dissolve material the mount simply pops: hidden until it appears, shown until it vanishes.
            else SetVisible(appearing ? now >= startsAt : now < endsAt);
            if (now >= endsAt) Finish();
        }

        private void Finish()
        {
            bool hide = !appearing;
            gone = hide;
            // Hidden before the solid originals go back, so a vanished mount never shows for a frame.
            if (hide) SetVisible(false);
            Restore();
            look = null;
            enabled = false;
            if (hide && destroyWhenDone) Destroy(gameObject);
        }

        /// <summary>The mount is being taken away (its rider is off): let it finish vanishing, or vanish it now, and then
        /// destroy it. Safe to call on a mount that is already vanishing.</summary>
        public void VanishAndDestroy(MountMaterialiseSettings settings, float seconds)
        {
            if (IsBusy && !appearing) { destroyWhenDone = true; return; }
            if (gone) { Destroy(gameObject); return; } // already vanished: nothing left to show
            Vanish(settings, 0f, seconds, true);
        }

        private void OnDestroy() => Restore();

        // ------------------------------------------------------------------ materials

        private void SwapIn()
        {
            foreach (Renderer r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                Material[] originals = r.sharedMaterials;
                Material[] copies = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    Material copy = new Material(look.material) { name = $"{look.material.name} ({(originals[i] != null ? originals[i].name : "none")})" };
                    if (originals[i] != null) Carry(originals[i], copy);
                    Configure(copy);
                    copies[i] = copy;
                }
                r.sharedMaterials = copies;
                swapped.Add(new Swapped { renderer = r, originals = originals, copies = copies });
            }
        }

        private static void Carry(Material from, Material to)
        {
            foreach (int id in CarriedTextures)
                if (from.HasProperty(id) && to.HasProperty(id)) to.SetTexture(id, from.GetTexture(id));
            foreach (int id in CarriedColors)
                if (from.HasProperty(id) && to.HasProperty(id)) to.SetColor(id, from.GetColor(id));
            foreach (int id in CarriedFloats)
                if (from.HasProperty(id) && to.HasProperty(id)) to.SetFloat(id, from.GetFloat(id));
        }

        private void Configure(Material m)
        {
            m.SetColor(EdgeColorA, look.edgeColorA);
            m.SetColor(EdgeColorB, look.edgeColorB);
            m.SetFloat(BurnWidth, look.burnWidth);
            m.SetFloat(RimStrength, look.rimStrength);
            if (look.noise != null) m.SetTexture(FadeTex, look.noise);
            m.SetVector(FadeTiling, look.noiseTiling);
            m.SetFloat(PatternMode, (float)look.pattern);
            m.SetFloat(PatternUntil, look.patternUntil);
            m.SetFloat(DirectionWeight, look.directionWeight);
            m.SetFloat(Inflate, look.inflate);
        }

        private void Drive(float spawn)
        {
            // The sweep in world space, from the model's own measurements: forms from the bottom (or the front) up.
            Vector3 axisLocal = Axis(look.direction, out bool reversed);
            Vector3 axis = transform.TransformDirection(axisLocal).normalized;
            float scale = transform.TransformVector(axisLocal).magnitude;
            float start = reversed ? bottom + length : bottom;
            Vector3 origin = transform.position + axis * (start * scale);
            Vector3 sweepAxis = reversed ? -axis : axis;
            foreach (Swapped s in swapped)
                foreach (Material m in s.copies)
                {
                    if (m == null) continue;
                    m.SetFloat(Spawn, spawn);
                    m.SetVector(SweepOrigin, origin);
                    m.SetVector(SweepAxis, sweepAxis);
                    m.SetFloat(SweepLength, Mathf.Max(0.01f, length * scale));
                }
        }

        private void Restore()
        {
            foreach (Swapped s in swapped)
            {
                if (s.renderer != null) s.renderer.sharedMaterials = s.originals;
                foreach (Material m in s.copies) if (m != null) Destroy(m);
            }
            swapped.Clear();
        }

        private void SetVisible(bool on)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers)
                if (r != null && !(r is ParticleSystemRenderer)) r.enabled = on;
        }

        // ------------------------------------------------------------------ the model's size and the particles

        private static Vector3 Axis(MountMaterialiseSettings.Direction direction, out bool reversed)
        {
            reversed = direction == MountMaterialiseSettings.Direction.TopDown || direction == MountMaterialiseSettings.Direction.BackToFront;
            return direction == MountMaterialiseSettings.Direction.BottomUp || direction == MountMaterialiseSettings.Direction.TopDown
                ? Vector3.up
                : Vector3.forward;
        }

        // Where the model begins and how long it is along its sweep axis, in its own space (works for every stage).
        private void Measure()
        {
            Vector3 axis = look != null ? Axis(look.direction, out _) : Vector3.up;
            float min = float.MaxValue, max = float.MinValue;
            foreach (Renderer r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float along = Vector3.Dot(transform.InverseTransformPoint(corner), axis);
                    min = Mathf.Min(min, along);
                    max = Mathf.Max(max, along);
                }
            }
            if (min > max) { min = 0f; max = 1f; }
            bottom = min;
            length = Mathf.Max(0.01f, max - min);
        }

        private void StartParticles()
        {
            particlesStarted = true;
            GameObject prefab = appearing ? look.appearParticles : look.vanishParticles;
            if (prefab == null) return;
            // Appearing, the particles ride with the mount; vanishing, they stay where it was.
            GameObject fx = appearing
                ? Instantiate(prefab, transform.position, transform.rotation, transform)
                : Instantiate(prefab, transform.position, transform.rotation);
            Destroy(fx, look.particleSeconds);
        }
    }
}
