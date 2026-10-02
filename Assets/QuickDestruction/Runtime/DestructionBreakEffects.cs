using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Ready-to-use, no-code particle and audio response for a DestructibleRoot. The
    /// destruction profile supplies material-aware defaults; optional asset references
    /// override them. Effects play on every gameplay hit and successful direct break.
    /// Runtime instances are pooled globally and returned when playback finishes.
    /// </summary>
    [AddComponentMenu("Quick Destruction/Break Effects")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DestructibleRoot))]
    public sealed class DestructionBreakEffects : MonoBehaviour
    {
        [Header("Ready-To-Use Preset Effects")]
        [Tooltip("When no custom Particle Prefab is assigned, use particles styled for the destruction profile pattern.")]
        public bool UsePresetParticles = true;

        [Tooltip("When no custom Break Sound is assigned, use a synthesized sound styled for the destruction profile pattern.")]
        public bool UsePresetSound = true;

        [Tooltip("Play the particle and sound for every ApplyDamage hit, including later hits after the object has fractured.")]
        public bool PlayOnEveryHit = true;

        [Header("Optional Custom Overrides")]
        [Tooltip("Optional custom ParticleSystem. When assigned, it replaces the preset particle effect.")]
        public ParticleSystem ParticlePrefab;

        [Tooltip("Optional custom AudioClip. When assigned, it replaces the preset sound.")]
        public AudioClip BreakSound;

        [Tooltip("Extra sound variants. Every break picks one at random from the clip above plus this list.")]
        public List<AudioClip> SoundVariants = new List<AudioClip>();

        [Tooltip("Rotates the particle prefab so its local forward axis follows the break normal.")]
        public bool AlignParticlesToBreakNormal = true;

        [Min(0.01f), Tooltip("Additional scale multiplier applied to the particle prefab.")]
        public float ParticleScale = 1f;

        [Range(0f, 1f)] public float SoundVolume = 0.9f;
        [Range(0f, 0.5f), Tooltip("Random pitch range around 1. A value of 0.1 produces 0.9-1.1.")]
        public float PitchVariation = 0.08f;
        [Range(0f, 1f), Tooltip("0 is 2D audio; 1 is fully positional 3D audio.")]
        public float SpatialBlend = 1f;
        [Min(0.01f)] public float MinDistance = 1f;
        [Min(0.01f)] public float MaxDistance = 30f;

        DestructibleRoot m_root;
        int m_lastHitFrame = -1;
        int m_lastBrokenFrame = -1;
        int m_lastHitSourceId;
        Vector3 m_lastHitPoint;

        DestructionProfile EffectProfile => m_root != null ? m_root.Profile : null;
        /// <summary>
        /// The highest-priority authored override: this component's prefab, then the
        /// profile's, then the preset resource. Particles carry no variant list — the burst
        /// is procedural and already takes its colour from the surface it came off, so a
        /// second prefab only added a field nobody had a reason to fill.
        /// </summary>
        internal ParticleSystem ResolvedParticlePrefab
        {
            get
            {
                ParticleSystem picked = ResolveParticle(ParticlePrefab, true);
                if (picked != null) return picked;

                DestructionProfile profile = EffectProfile;
                if (profile != null)
                {
                    picked = ResolveParticle(profile.HitParticlePrefab, true);
                    if (picked != null) return picked;
                }

                // A pattern-specific resource is an authored override. The generic
                // Particle_URP/HDRP/Built-In asset is only a renderer template and must be
                // treated as an empty field here, so the pool runs ConfigureParticleSystem
                // for Wood/Glass/etc. The schema 38 migration cleared one where it could
                // reach it, but a profile saved after that still stores whichever template
                // matched the pipeline it was authored under — and honouring that reference
                // in a different pipeline is exactly what renders the burst magenta.
                GameObject go = Resources.Load<GameObject>($"Particles/QD_Particle_{PresetPattern}");
                return go != null ? go.GetComponent<ParticleSystem>() : null;
            }
        }

        internal AudioClip ResolvedBreakSound
        {
            get
            {
                AudioClip picked = PickClip(BreakSound, SoundVariants);
                if (picked != null) return picked;

                DestructionProfile profile = EffectProfile;
                if (profile != null)
                {
                    picked = PickClip(profile.HitSound, profile.HitSoundVariants);
                    if (picked != null) return picked;
                }

                // The bundled per-pattern clip IS the preset sound, so it has to answer to
                // the preset toggle. It did not: a clip exists for every pattern, so this
                // fallback always returned one, the caller's UseDefaultHitSound branch was
                // unreachable, and turning the preset sound off changed nothing.
                if (!ResolvedUsePresetSound) return null;
                return Resources.Load<AudioClip>($"Sounds/QD_Sound_{PresetPattern}");
            }
        }

        // rejectPipelineTemplate separates the two particle sources. For the hit burst the
        // generic Particle_URP/HDRP/Built-In asset is only a renderer stand-in and must not
        // count as an authored entry; for landing dust the bundled fog prefab IS the
        // intended asset, and the caller's fallback deliberately loads exactly that.
        static ParticleSystem ResolveParticle(ParticleSystem single,
            bool rejectPipelineTemplate)
        {
            return IsUsableParticle(single, rejectPipelineTemplate) ? single : null;
        }

        // Sampling with replacement replays the previous clip as often as one time in N,
        // which is exactly what makes a populated variant list still sound repetitive. With
        // two or more choices the one played last steps aside, so the draw is uniform over
        // what is left and never repeats immediately. A pool holding a single usable clip
        // keeps returning it: there is nothing else to play, and letting the exclusion empty
        // a pool would hand the choice to the next override level instead.
        static AudioClip PickClip(AudioClip single, List<AudioClip> variants,
            AudioClip avoid = null)
        {
            int count = single != null ? 1 : 0;
            if (variants != null)
                for (int i = 0; i < variants.Count; i++)
                    if (variants[i] != null) count++;
            if (count == 0) return null;

            int skipped = 0;
            if (count > 1 && avoid != null)
            {
                if (single != null && single == avoid) skipped++;
                if (variants != null)
                    for (int i = 0; i < variants.Count; i++)
                        if (variants[i] != null && variants[i] == avoid) skipped++;
            }
            int pickable = count - skipped;
            bool avoiding = skipped > 0 && pickable > 0;
            if (!avoiding) pickable = count;

            int index = pickable == 1 ? 0 : Random.Range(0, pickable);
            if (single != null && (!avoiding || single != avoid))
            {
                if (index == 0) return single;
                index--;
            }
            if (variants != null)
                for (int i = 0; i < variants.Count; i++)
                {
                    if (variants[i] == null) continue;
                    if (avoiding && variants[i] == avoid) continue;
                    if (index == 0) return variants[i];
                    index--;
                }
            return null;
        }

        AudioClip m_lastDrawnSound;

        /// <summary>
        /// The real draw for one break, as opposed to <see cref="ResolvedBreakSound"/>,
        /// which also serves null tests such as <see cref="HasAnyEffect"/>. Only a draw
        /// records what played, so a null test can never consume the anti-repeat memory.
        /// </summary>
        internal AudioClip DrawBreakSound()
        {
            AudioClip picked = PickClip(BreakSound, SoundVariants, m_lastDrawnSound);
            if (picked == null)
            {
                DestructionProfile profile = EffectProfile;
                if (profile != null)
                    picked = PickClip(profile.HitSound, profile.HitSoundVariants,
                        m_lastDrawnSound);
            }
            if (picked == null)
            {
                if (!ResolvedUsePresetSound) return null;
                picked = Resources.Load<AudioClip>($"Sounds/QD_Sound_{PresetPattern}");
            }
            if (picked != null) m_lastDrawnSound = picked;
            return picked;
        }

        static bool IsUsableParticle(ParticleSystem particle, bool rejectPipelineTemplate) =>
            particle != null &&
            (!rejectPipelineTemplate ||
             !DestructionProfile.IsPipelineParticleTemplate(particle));

        internal bool ResolvedUsePresetParticles => EffectProfile != null
            ? EffectProfile.UseDefaultHitParticles
            : UsePresetParticles;
        internal bool ResolvedUsePresetSound => EffectProfile != null
            ? EffectProfile.UseDefaultHitSound
            : UsePresetSound;
        internal bool ResolvedPlayOnEveryHit => EffectProfile != null
            ? EffectProfile.PlayHitEffectsOnEveryHit
            : PlayOnEveryHit;

        public bool HasAnyEffect => ResolvedParticlePrefab != null ||
                                    ResolvedBreakSound != null ||
                                    ResolvedUsePresetParticles ||
                                    ResolvedUsePresetSound;

        internal FracturePattern PresetPattern => m_root != null && m_root.Profile != null
            ? m_root.Profile.Pattern
            : FracturePattern.UniformConcrete;

        void Awake()
        {
            ResolveRoot();
            AutoPopulateResources();
        }

        void OnEnable()
        {
            ResolveRoot();
            AutoPopulateResources();
            if (m_root == null) return;
            m_root.HitReceived += OnHitReceived;
            m_root.Broken += OnBroken;
            m_root.WasReset += OnReset;
            m_root.FragmentImpact += OnFragmentImpact;
        }

        void OnDisable()
        {
            if (m_root != null)
            {
                m_root.HitReceived -= OnHitReceived;
                m_root.Broken -= OnBroken;
                m_root.WasReset -= OnReset;
                m_root.FragmentImpact -= OnFragmentImpact;
            }
            DestructionEffectPool.StopOwner(GetHashCode());
        }

        void OnValidate()
        {
            ParticleScale = Mathf.Max(0.01f, ParticleScale);
            MinDistance = Mathf.Max(0.01f, MinDistance);
            MaxDistance = Mathf.Max(MinDistance, MaxDistance);
            AutoPopulateResources();
        }

        void ResolveRoot()
        {
            if (m_root == null) m_root = GetComponent<DestructibleRoot>();
        }

        void AutoPopulateResources()
        {
            // Local component fields remain empty by default so they dynamically inherit
            // the exact profile settings (Wood -> QD_Sound_DirectionalWood, Concrete -> QD_Sound_UniformConcrete).
        }

        void OnHitReceived(DamageContext context)
        {
            if (!ResolvedPlayOnEveryHit) return;
            if (m_lastHitFrame == Time.frameCount &&
                m_lastHitSourceId == context.SourceId &&
                (m_lastHitPoint - context.Point).sqrMagnitude < 0.000001f)
                return;

            m_lastHitFrame = Time.frameCount;
            m_lastHitSourceId = context.SourceId;
            m_lastHitPoint = context.Point;
            float intensity = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, context.Amount) / 35f),
                0.38f, 1.15f);
            PlayAt(context.Point, context.Normal, intensity);
        }

        void OnBroken(BreakContext context)
        {
            if (m_lastBrokenFrame == Time.frameCount) return;
            m_lastBrokenFrame = Time.frameCount;

            // ApplyDamage dispatches HitReceived before it can dispatch Broken. Suppress
            // that same physical hit here so one shot never produces two effects.
            bool alreadyPlayedForThisHit = ResolvedPlayOnEveryHit &&
                m_lastHitFrame == Time.frameCount &&
                m_lastHitSourceId == context.SourceId &&
                (m_lastHitPoint - context.Point).sqrMagnitude < 0.000001f;
            if (!alreadyPlayedForThisHit)
                PlayAt(context.Point, context.Normal,
                    Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, context.Energy) / 35f), 0.5f, 1.35f));
        }

        [Header("One-Time Ground Landing Dust")]
        [Tooltip("Optional ground dust particle override. When empty, uses the profile ground-dust asset. It is never used for normal hit particles.")]
        public ParticleSystem GroundDustParticlePrefab;
        [Tooltip("Optional landing-sound override. When empty, uses the profile setting or the bundled QD_stone falls clip.")]
        public AudioClip GroundDustSound;
        [Tooltip("Extra landing-sound variants. Every landing picks one at random from the clip above plus this list.")]
        public List<AudioClip> GroundDustSoundVariants = new List<AudioClip>();

        internal ParticleSystem ResolvedGroundDustParticlePrefab
        {
            get
            {
                // Unlike the hit burst, the bundled fog prefab IS the intended asset here —
                // the fallback below loads exactly that. So an authored reference to one is
                // kept and merely re-pointed at the variant of the pipeline actually running.
                ParticleSystem authoredDust =
                    ResolveParticle(GroundDustParticlePrefab, false);
                if (authoredDust == null && EffectProfile != null)
                    authoredDust = ResolveParticle(
                        EffectProfile.GroundDustParticlePrefab, false);
                if (authoredDust != null)
                    return DestructionPresetEffectLibrary.ResolveForActivePipeline(
                        authoredDust);
                GameObject presetDust = DestructionPresetEffectLibrary.LoadPipelineParticlePrefab("Grey Volume Fog");
                return presetDust != null ? presetDust.GetComponent<ParticleSystem>() : null;
            }
        }

        internal AudioClip ResolvedGroundDustSound
        {
            get
            {
                AudioClip authored = PickClip(GroundDustSound, GroundDustSoundVariants);
                if (authored != null) return authored;
                if (EffectProfile != null)
                {
                    authored = PickClip(EffectProfile.GroundDustSound,
                        EffectProfile.GroundDustSoundVariants);
                    if (authored != null) return authored;
                }

                // Ground contact is deliberately distinct from a break hit. This clip
                // remains available for older profiles whose new field serializes empty.
                AudioClip landing = Resources.Load<AudioClip>("Sounds/QD_stone falls");
                if (landing != null) return landing;
                AudioClip breakClip = ResolvedBreakSound;
                return breakClip != null
                    ? breakClip
                    : ResolvedUsePresetSound
                        ? DestructionPresetEffectLibrary.GetAudioClip(PresetPattern)
                        : null;
            }
        }

        AudioClip m_lastDrawnGroundDustSound;

        /// <summary>
        /// The real draw for one landing. Debris hits the floor far more often than a break
        /// happens, so a repeated clip is more noticeable here than anywhere else. The
        /// memory is separate from the break sound's: the two are different categories and
        /// must not suppress each other's variants. The dust particle keeps no such memory —
        /// one puff looks like the next, and only the sound carries the repetition.
        /// </summary>
        internal AudioClip DrawGroundDustSound()
        {
            AudioClip picked = PickClip(GroundDustSound, GroundDustSoundVariants,
                m_lastDrawnGroundDustSound);
            if (picked == null)
            {
                DestructionProfile profile = EffectProfile;
                if (profile != null)
                    picked = PickClip(profile.GroundDustSound,
                        profile.GroundDustSoundVariants, m_lastDrawnGroundDustSound);
            }
            if (picked == null) picked = ResolvedGroundDustSound;
            if (picked != null) m_lastDrawnGroundDustSound = picked;
            return picked;
        }

        void OnFragmentImpact(Vector3 point, float energy, int index, float size)
        {
            if (!isActiveAndEnabled || !Application.isPlaying) return;
            DestructionProfile profile = EffectProfile;
            if (profile != null && profile.Pattern == FracturePattern.RadialGlass) return; // Glass shards do not create ground dust
            if (profile != null && !profile.FragmentImpactEffects) return;
            ParticleSystem dustPrefab = ResolvedGroundDustParticlePrefab;
            if (dustPrefab == null) return;
            float minSpeed = profile != null ? profile.FragmentImpactMinSpeed : 1.5f;
            if (energy < minSpeed) return;

            float strength = profile != null ? Mathf.Clamp01(profile.FragmentImpactStrength) : 0.6f;
            // Scale dust effect size & intensity by the physical volume/size of the large primary fragment!
            float sizeMultiplier = Mathf.Clamp(size, 0.3f, 4.0f);
            float intensity = Mathf.Clamp(
                strength * Mathf.Sqrt(Mathf.Max(1f, energy) / 12f) * sizeMultiplier, 0.15f, 3.5f);

            // Landing dust intentionally retains the authored prefab/preset colour. It
            // must not inherit stone, roof or painted-surface tints from the fragment.
            DestructionEffectPool.PlayCustom(this, dustPrefab, point, Vector3.up, intensity,
                Color.white, false, DrawGroundDustSound());
        }

        void OnReset(ResetMode mode)
        {
            m_lastHitFrame = -1;
            DestructionEffectPool.StopOwner(GetHashCode());
        }

        /// <summary>Public manual preview/gameplay hook using the same pooled playback path.</summary>
        public void PlayAt(Vector3 point, Vector3 normal)
            => PlayAt(point, normal, 1f);

        internal void PlayAt(Vector3 point, Vector3 normal, float intensity)
        {
            if (!isActiveAndEnabled || !Application.isPlaying || !HasAnyEffect) return;
            Color surfaceColor = Color.white;
            bool hasSurfaceColor = false;
            DestructibleRoot targetRoot = m_root != null ? m_root : GetComponentInParent<DestructibleRoot>();
            if (targetRoot != null)
            {
                hasSurfaceColor = targetRoot.TryGetFragmentEffectColor(point, out surfaceColor);
            }
            DestructionEffectPool.Play(this, point, normal, intensity,
                surfaceColor, hasSurfaceColor);
        }
    }

    /// <summary>Small shared pool: no per-break helper GameObjects are destroyed.</summary>
    internal sealed class DestructionEffectPool : MonoBehaviour
    {
        struct ParticleLease
        {
            public ParticleSystem System;
            public int PrefabId;
            public int OwnerId;
        }

        struct AudioLease
        {
            public AudioSource Source;
            public int OwnerId;
        }

        sealed class PoolState
        {
            public DestructionEffectPool Instance;
        }

        static readonly PoolState s_state = new PoolState();
        readonly Dictionary<int, Stack<ParticleSystem>> m_particlePools =
            new Dictionary<int, Stack<ParticleSystem>>();
        readonly Stack<AudioSource> m_audioPool = new Stack<AudioSource>();
        readonly List<ParticleLease> m_activeParticles = new List<ParticleLease>();
        readonly List<AudioLease> m_activeAudio = new List<AudioLease>();
        MaterialPropertyBlock m_particleTintBlock;

        internal static int ActiveParticleCount => s_state.Instance != null
            ? s_state.Instance.m_activeParticles.Count : 0;
        internal static int ActiveAudioCount => s_state.Instance != null
            ? s_state.Instance.m_activeAudio.Count : 0;

        void Awake()
        {
            EnsureParticleTintBlock();
        }

        MaterialPropertyBlock EnsureParticleTintBlock()
        {
            // MaterialPropertyBlock owns a native Unity object. Creating it in a
            // MonoBehaviour field initializer runs from the component constructor,
            // which Unity explicitly forbids. Awake is the normal path; the lazy
            // guard also covers editor/domain-reload edge cases safely.
            if (m_particleTintBlock == null)
                m_particleTintBlock = new MaterialPropertyBlock();
            return m_particleTintBlock;
        }

        internal static void Play(DestructionBreakEffects settings,
            Vector3 point, Vector3 normal, float intensity,
            Color surfaceColor, bool hasSurfaceColor)
        {
            if (settings == null || !Application.isPlaying) return;
            DestructionEffectPool pool = GetOrCreate();
            // Each override pool is drawn from exactly once per break. Reading the resolved
            // properties twice would roll a second, different variant and throw the first
            // one away — the null test and the effect that plays must be the same asset.
            ParticleSystem particlePrefab = settings.ResolvedParticlePrefab;
            if (particlePrefab != null || settings.ResolvedUsePresetParticles)
                pool.PlayParticleWithPrefab(settings, particlePrefab, point, normal,
                    intensity, surfaceColor, hasSurfaceColor);
            AudioClip resolvedClip = settings.DrawBreakSound();
            AudioClip clip = resolvedClip != null
                ? resolvedClip
                : settings.ResolvedUsePresetSound
                    ? DestructionPresetEffectLibrary.GetAudioClip(settings.PresetPattern)
                    : null;
            if (clip != null)
                pool.PlayAudio(settings, clip, point, intensity);
        }

        internal static void PlayCustom(DestructionBreakEffects settings,
            ParticleSystem particlePrefab, Vector3 point, Vector3 normal, float intensity,
            Color surfaceColor, bool hasSurfaceColor, AudioClip soundClip)
        {
            if (settings == null || !Application.isPlaying) return;
            DestructionEffectPool pool = GetOrCreate();
            if (particlePrefab != null)
                pool.PlayParticleWithPrefab(settings, particlePrefab, point, normal, intensity,
                    surfaceColor, hasSurfaceColor);

            // Ground contact has its own clip and a minimum useful loudness, so it
            // remains audible beside the visual dust without becoming a break-sound spam.
            AudioClip clip = soundClip;
            if (clip != null)
                pool.PlayAudio(settings, clip, point,
                    Mathf.Clamp(intensity * 0.8f, 0.55f, 1f));
        }

        internal static void StopOwner(int ownerId)
        {
            if (s_state.Instance != null) s_state.Instance.ReleaseOwner(ownerId);
        }

        static HideFlags PooledObjectHideFlags
        {
            get
            {
#if UNITY_EDITOR
                // Play-created objects already disappear when Play Mode ends. Applying
                // DontSaveInEditor to any child can opt that hierarchy out of teardown.
                return HideFlags.HideInHierarchy;
#else
                return HideFlags.DontSave;
#endif
            }
        }

        static DestructionEffectPool GetOrCreate()
        {
            if (s_state.Instance != null) return s_state.Instance;
            var go = new GameObject("Quick Destruction FX Pool");
            go.hideFlags = PooledObjectHideFlags;
            DontDestroyOnLoad(go);
            s_state.Instance = go.AddComponent<DestructionEffectPool>();
            return s_state.Instance;
        }

        // Two was low enough that an ordinary collapse ate its own feedback: a hit, the
        // break it caused and the first fragments landing all resolve in one frame, so the
        // effect the player actually shot for was silently dropped. The concurrency cap
        // below is the real budget; the per-frame number only smooths a single spike.
        const int MaxParticlePerFrame = 6;
        const int MaxActiveParticleSystems = 24;
        int m_lastParticleFrame = -1;
        int m_frameParticleCount;

        void PlayParticleWithPrefab(DestructionBreakEffects settings,
            ParticleSystem prefab, Vector3 point, Vector3 normal, float intensity,
            Color surfaceColor, bool hasSurfaceColor)
        {
            if (Time.frameCount != m_lastParticleFrame)
            {
                m_lastParticleFrame = Time.frameCount;
                m_frameParticleCount = 0;
            }

            if (m_frameParticleCount >= MaxParticlePerFrame ||
                m_activeParticles.Count >= MaxActiveParticleSystems)
                return;

            m_frameParticleCount++;

            bool custom = prefab != null;
            int prefabId = custom
                ? prefab.GetHashCode()
                : -1000 - (int)settings.PresetPattern;
            if (!m_particlePools.TryGetValue(prefabId, out Stack<ParticleSystem> available))
            {
                available = new Stack<ParticleSystem>();
                m_particlePools.Add(prefabId, available);
            }

            ParticleSystem system = null;
            while (available.Count > 0 && system == null) system = available.Pop();
            if (system == null)
            {
                if (custom)
                {
                    system = Instantiate(prefab, transform);
                    system.name = prefab.name + " (QD Pooled)";
                    system.gameObject.hideFlags = PooledObjectHideFlags;
                    var main = system.main;
                    main.stopAction = ParticleSystemStopAction.None;
                }
                else
                {
                    system = DestructionPresetEffectLibrary.CreateParticleSystem(transform);
                    system.name = "QD " + settings.PresetPattern + " Particles (Pooled)";
                    system.gameObject.hideFlags = PooledObjectHideFlags;
                }
            }

            Transform t = system.transform;
            t.SetParent(transform, false);
            t.position = point;
            t.rotation = settings.AlignParticlesToBreakNormal && normal.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(normal.normalized)
                : custom ? prefab.transform.rotation : Quaternion.identity;
            if (custom)
            {
                // A pooled custom particle can have been tinted by an earlier hit if a
                // user assigns the same prefab to more than one effect. Restore the
                // authored visual state before deciding whether this playback is tinted.
                // Ground landing dust passes hasSurfaceColor=false, so it stays exactly
                // at the prefab's own colour every time.
                RestoreCustomParticleAppearance(system, prefab);
                float hitScale = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0.2f, intensity)), 0.65f, 1.2f);
                t.localScale = prefab.transform.localScale * settings.ParticleScale * hitScale;
            }
            else
            {
                t.localScale = Vector3.one;
                DestructionPresetEffectLibrary.ConfigureParticleSystem(system,
                    settings.PresetPattern, intensity, settings.ParticleScale,
                    surfaceColor, hasSurfaceColor);
            }

            if (hasSurfaceColor)
            {
                QuickDestructionParticleTint tint = system.GetComponent<QuickDestructionParticleTint>();
                if (tint != null)
                {
                    tint.ApplyImpactColor(surfaceColor, intensity);
                }
                else
                {
                    var main = system.main;
                    Color orig = main.startColor.color;
                    Color tinted = new Color(
                        Mathf.Clamp01(surfaceColor.r * 1.15f),
                        Mathf.Clamp01(surfaceColor.g * 1.15f),
                        Mathf.Clamp01(surfaceColor.b * 1.15f),
                        orig.a > 0.01f ? orig.a : 1f);
                    var r = system.GetComponent<ParticleSystemRenderer>();
                    bool materialTint = r != null &&
                        DestructionPresetEffectLibrary.SupportsMaterialTint(r.sharedMaterial);
                    main.startColor = materialTint ? Color.white : tinted;

                    // Do not mutate or instantiate the user's material. A property block
                    // supplies the sole RGB multiplier; vertex color is the fallback only
                    // for custom shaders without a standard tint property.
                    if (materialTint)
                    {
                        MaterialPropertyBlock tintBlock = EnsureParticleTintBlock();
                        r.GetPropertyBlock(tintBlock);
                        DestructionPresetEffectLibrary.ApplyBlockTint(
                            tintBlock, tinted);
                        r.SetPropertyBlock(tintBlock);
                    }
                }
            }
            system.gameObject.SetActive(true);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(true);
            m_activeParticles.Add(new ParticleLease
            {
                System = system,
                PrefabId = prefabId,
                OwnerId = settings.GetHashCode()
            });
        }

        static void RestoreCustomParticleAppearance(ParticleSystem target,
            ParticleSystem source)
        {
            if (target == null || source == null) return;
            ParticleSystem[] targets = target.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem[] sources = source.GetComponentsInChildren<ParticleSystem>(true);
            int systemCount = Mathf.Min(targets.Length, sources.Length);
            for (int i = 0; i < systemCount; i++)
            {
                var targetMain = targets[i].main;
                var sourceMain = sources[i].main;
                targetMain.startColor = sourceMain.startColor;

                var targetColor = targets[i].colorOverLifetime;
                var sourceColor = sources[i].colorOverLifetime;
                targetColor.enabled = sourceColor.enabled;
                if (sourceColor.enabled) targetColor.color = sourceColor.color;
            }

            ParticleSystemRenderer[] targetRenderers =
                target.GetComponentsInChildren<ParticleSystemRenderer>(true);
            ParticleSystemRenderer[] sourceRenderers =
                source.GetComponentsInChildren<ParticleSystemRenderer>(true);
            int rendererCount = Mathf.Min(targetRenderers.Length, sourceRenderers.Length);
            for (int i = 0; i < rendererCount; i++)
            {
                targetRenderers[i].sharedMaterial = sourceRenderers[i].sharedMaterial;
                targetRenderers[i].SetPropertyBlock(null);
            }
        }

        const int MaxActiveAudioSources = 16;
        const int MaxAudioPerFrame = 4;
        int m_lastAudioFrame = -1;
        int m_frameAudioCount;

        void PlayAudio(DestructionBreakEffects settings, AudioClip clip,
            Vector3 point, float intensity)
        {
            if (clip == null) return;
            if (Time.frameCount != m_lastAudioFrame)
            {
                m_lastAudioFrame = Time.frameCount;
                m_frameAudioCount = 0;
            }

            if (m_frameAudioCount >= MaxAudioPerFrame || m_activeAudio.Count >= MaxActiveAudioSources)
                return;

            m_frameAudioCount++;

            AudioSource source = null;
            while (m_audioPool.Count > 0 && source == null) source = m_audioPool.Pop();
            if (source == null)
            {
                var go = new GameObject("QD Pooled Audio");
                go.hideFlags = PooledObjectHideFlags;
                go.transform.SetParent(transform, false);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
            }

            source.transform.position = point;
            source.clip = clip;
            source.loop = false;
            source.volume = settings.SoundVolume * Mathf.Clamp(intensity, 0.4f, 1.15f);
            source.pitch = 1f + Random.Range(-settings.PitchVariation,
                settings.PitchVariation);
            source.spatialBlend = settings.SpatialBlend;
            source.minDistance = settings.MinDistance;
            source.maxDistance = settings.MaxDistance;
            source.gameObject.SetActive(true);
            source.Play();
            m_activeAudio.Add(new AudioLease
            {
                Source = source,
                OwnerId = settings.GetHashCode()
            });
        }

        void Update()
        {
            for (int i = m_activeParticles.Count - 1; i >= 0; i--)
            {
                ParticleSystem system = m_activeParticles[i].System;
                if (system != null && system.IsAlive(true)) continue;
                ReleaseParticleAt(i);
            }

            for (int i = m_activeAudio.Count - 1; i >= 0; i--)
            {
                AudioSource source = m_activeAudio[i].Source;
                if (source != null && source.isPlaying) continue;
                ReleaseAudioAt(i);
            }
        }

        void ReleaseOwner(int ownerId)
        {
            for (int i = m_activeParticles.Count - 1; i >= 0; i--)
                if (m_activeParticles[i].OwnerId == ownerId) ReleaseParticleAt(i);
            for (int i = m_activeAudio.Count - 1; i >= 0; i--)
                if (m_activeAudio[i].OwnerId == ownerId) ReleaseAudioAt(i);
        }

        void ReleaseParticleAt(int index)
        {
            ParticleLease lease = m_activeParticles[index];
            m_activeParticles.RemoveAt(index);
            if (lease.System == null) return;
            lease.System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            lease.System.gameObject.SetActive(false);
            if (!m_particlePools.TryGetValue(lease.PrefabId,
                    out Stack<ParticleSystem> available))
            {
                available = new Stack<ParticleSystem>();
                m_particlePools.Add(lease.PrefabId, available);
            }
            available.Push(lease.System);
        }

        void ReleaseAudioAt(int index)
        {
            AudioSource source = m_activeAudio[index].Source;
            m_activeAudio.RemoveAt(index);
            if (source == null) return;
            source.Stop();
            source.clip = null;
            source.gameObject.SetActive(false);
            m_audioPool.Push(source);
        }

        void OnDestroy()
        {
            if (s_state.Instance == this) s_state.Instance = null;
        }
    }
}
