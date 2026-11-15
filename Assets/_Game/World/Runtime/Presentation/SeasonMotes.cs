using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Summer's small life in the air (owner's DAY list item 21, part 3): dust and pollen motes drifting in the light by day in Summer
    /// and late Spring, and fireflies blinking in the grass from sunset toward midnight on warm evenings. Two particle systems made at
    /// runtime beside the <see cref="SeasonPresenter"/> (no scene object), following the camera; the presenter says how much of each
    /// it wants every frame (<see cref="Step"/>). Counts come from <see cref="SeasonSettings"/> per World Effects tier (none on Low).
    /// Client presentation only; no garbage a frame.
    /// </summary>
    public sealed class SeasonMotes : MonoBehaviour
    {
        ParticleSystem m_Motes, m_Flies;
        ParticleSystemRenderer m_MotesDraw, m_FliesDraw;
        int m_MotesMax = -1, m_FliesMax = -1;
        Material m_Material;

        /// <summary>The motes and fireflies alive now (GM window, tests).</summary>
        public int MotesAlive => m_Motes != null ? m_Motes.particleCount : 0;
        public int FirefliesAlive => m_Flies != null ? m_Flies.particleCount : 0;

        /// <summary>
        /// One frame: <paramref name="motes"/> and <paramref name="flies"/> are 0 none .. 1 full; <paramref name="camera"/> null stops both
        /// (the particles already out fade on their own).
        /// </summary>
        public void Step(SeasonSettings settings, Vector3? camera, float motes, float flies)
        {
            if (settings == null || settings.motesMaterial == null || camera == null)
            {
                Emit(m_Motes, m_MotesDraw, 0f, 1f);
                Emit(m_Flies, m_FliesDraw, 0f, 1f);
                return;
            }
            WorldEffects tier = WorldEffectsRuntime.Quality;
            int motesMax = settings.Motes(tier), fliesMax = settings.Fireflies(tier);
            if (m_Material != settings.motesMaterial) { m_Material = settings.motesMaterial; m_MotesMax = m_FliesMax = -1; }
            if (motesMax != m_MotesMax) { m_MotesMax = motesMax; Configure(ref m_Motes, ref m_MotesDraw, "Motes", settings, motesMax, false); }
            if (fliesMax != m_FliesMax) { m_FliesMax = fliesMax; Configure(ref m_Flies, ref m_FliesDraw, "Fireflies", settings, fliesMax, true); }

            Vector3 at = camera.Value;
            if (m_Motes != null) m_Motes.transform.position = at;
            if (m_Flies != null) m_Flies.transform.position = at + new Vector3(0f, settings.fireflyHeight, 0f);
            Emit(m_Motes, m_MotesDraw, motesMax * Mathf.Clamp01(motes), settings.motesLifetime);
            Emit(m_Flies, m_FliesDraw, fliesMax * Mathf.Clamp01(flies), settings.fireflyLifetime);
        }

        static void Emit(ParticleSystem ps, ParticleSystemRenderer r, float count, float lifetime)
        {
            if (ps == null || r == null) return;
            ParticleSystem.EmissionModule emission = ps.emission;
            // a steady crowd of about count alive: count new ones every lifetime
            float rate = count > 0.5f ? count / Mathf.Max(0.5f, lifetime) : 0f;
            emission.rateOverTime = rate;
            if (rate > 0f && !ps.isPlaying) ps.Play(false);
            bool show = rate > 0f || ps.particleCount > 0;
            if (r.enabled != show) r.enabled = show;
            if (!show && ps.isPlaying) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        void Configure(ref ParticleSystem ps, ref ParticleSystemRenderer r, string name, SeasonSettings settings, int max, bool flies)
        {
            if (max <= 0)
            {
                if (ps != null) { Destroy(ps.gameObject); ps = null; r = null; }
                return;
            }
            if (ps == null)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            float life = flies ? settings.fireflyLifetime : settings.motesLifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life * 1.3f);
            main.startSpeed = flies ? 0.25f : 0.08f;
            float size = flies ? settings.fireflySize : settings.motesSize;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f);
            Color c = flies ? settings.fireflyColor : settings.motesColor;
            main.startColor = c;
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = flies ? settings.fireflyBox : settings.motesBox;

            // a slow wander (motes ride the air; fireflies weave)
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = flies ? 0.6f : 0.25f;
            noise.frequency = flies ? 0.5f : 0.2f;
            noise.scrollSpeed = 0.15f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            // fade in and out; fireflies blink twice in a life
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            var colours = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
            GradientAlphaKey[] alphas = flies
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.05f, 0.35f),
                          new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 0.85f) }
                : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) };
            gradient.SetKeys(colours, alphas);
            col.color = gradient;

            r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = settings.motesMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
        }

        void OnDestroy()
        {
            // only our own objects (the sky's quit-crash rule: nothing global is put back here)
            if (m_Motes != null) Destroy(m_Motes.gameObject);
            if (m_Flies != null) Destroy(m_Flies.gameObject);
        }
    }
}
