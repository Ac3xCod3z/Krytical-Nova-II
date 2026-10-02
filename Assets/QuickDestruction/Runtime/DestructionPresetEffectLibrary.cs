using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Lightweight ready-to-use VFX and SFX authored entirely at runtime. This keeps the
    /// presets render-pipeline independent and avoids shipping scene-specific references.
    /// Custom ParticleSystem and AudioClip fields on DestructionBreakEffects still override
    /// these defaults.
    /// </summary>
    public static class DestructionPresetEffectLibrary
    {
        enum ParticleMeshKind
        {
            None,
            ConcreteChunk,
            WoodSplinter,
            BrickChip,
            StoneSlab,
            PillarChunk,
            ImpactChip,
            GlassShard
        }

#pragma warning disable 0649 // Optional mesh/texture fields are intentionally unused by some presets.
        struct VisualSpec
        {
            public Color Color;
            public int Count;
            public float Lifetime;
            public float Speed;
            public float Size;
            public float Gravity;
            public float ConeAngle;
            public bool Stretched;
            public ParticleMeshKind MeshKind;
            public float SurfaceColorWeight;
            public string TexturePath;
            public int TextureTilesX;
        }
#pragma warning restore 0649

        static readonly Dictionary<FracturePattern, AudioClip> s_audio =
            new Dictionary<FracturePattern, AudioClip>();
        sealed class ParticleMaterialState
        {
            public Material ParticleMaterial;
            public Material TexturedParticleMaterial;
            public Texture2D TexturedParticleTexture;
        }

        static readonly ParticleMaterialState s_particleState = new ParticleMaterialState();
        static readonly Dictionary<ParticleMeshKind, Mesh> s_particleMeshes =
            new Dictionary<ParticleMeshKind, Mesh>();
        static readonly Dictionary<string, Texture2D> s_particleTextures =
            new Dictionary<string, Texture2D>();
        static readonly HashSet<string> s_missingTextureWarnings = new HashSet<string>();
        const string DefaultParticleTexturePath = "Textures/Sharp_Piece";
        static readonly int[] s_particleTextureProperties =
        {
            Shader.PropertyToID("_UnlitColorMap"),
            Shader.PropertyToID("_BaseColorMap"),
            Shader.PropertyToID("_BaseMap"),
            Shader.PropertyToID("_MainTex")
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeCache()
        {
            s_particleState.ParticleMaterial = null;
            s_particleState.TexturedParticleMaterial = null;
            s_particleState.TexturedParticleTexture = null;
            s_particleMeshes.Clear();
            s_particleTextures.Clear();
            s_missingTextureWarnings.Clear();
        }

        public static ParticleSystem CreateParticleSystem(Transform parent)
        {
            GameObject defaultPrefab = LoadPipelineParticlePrefab("Particle");
            if (defaultPrefab != null)
            {
                var instantiated = UnityEngine.Object.Instantiate(defaultPrefab, parent, false);
                instantiated.name = "QD Preset Particles";
                instantiated.hideFlags = HideFlags.DontSave;
                var sys = instantiated.GetComponent<ParticleSystem>();
                if (sys != null)
                {
                    if (instantiated.GetComponent<QuickDestructionParticleTint>() == null)
                        instantiated.AddComponent<QuickDestructionParticleTint>();
                    instantiated.SetActive(false);
                    return sys;
                }
            }

            var go = new GameObject("QD Preset Particles");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = CreateParticleMaterial();
            go.SetActive(false);
            return system;
        }

        public static void ConfigureParticleSystem(ParticleSystem system,
            FracturePattern pattern, float intensity, float scale,
            Color surfaceColor, bool hasSurfaceColor)
        {
            if (system == null) return;

            VisualSpec spec = GetVisualSpec(pattern);
            Color particleColor = hasSurfaceColor ? surfaceColor : spec.Color;
            particleColor.a = spec.Color.a > 0.01f ? spec.Color.a : 1f;
            float strength = Mathf.Clamp(intensity, 0.3f, 1.5f);

            var main = system.main;
            main.loop = false;
            main.duration = 0.45f;
            main.startDelay = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                spec.Lifetime * 0.65f, spec.Lifetime * 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                spec.Speed * 0.55f * strength, spec.Speed * 1.25f * strength);
            main.startSize = new ParticleSystem.MinMaxCurve(
                spec.Size * 0.55f * scale, spec.Size * 1.2f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = particleColor;
            main.gravityModifier = spec.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = pattern == FracturePattern.RadialGlass ? 32 : 96;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            short count = (short)Mathf.Clamp(Mathf.RoundToInt(spec.Count * strength), 2, 64);
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spec.ConeAngle;
            shape.radius = 0.025f * scale;
            shape.length = 0.05f * scale;

            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    // startColor already owns the preset tint. A second copy here is
                    // multiplied by legacy particle shaders and makes dark colors darker.
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 0.18f),
                    new GradientColorKey(new Color(0.65f, 0.65f, 0.65f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.85f),
                    new GradientAlphaKey(1f, 1f)
                });
            color.color = gradient;

            string particleTexturePath = !string.IsNullOrEmpty(spec.TexturePath)
                ? spec.TexturePath
                : spec.MeshKind != ParticleMeshKind.None
                    ? DefaultParticleTexturePath
                    : null;
            Texture2D particleTexture = GetParticleTexture(particleTexturePath);
            // Never return to the old mesh silhouette while a sprite is waiting
            // for Unity's importer. A warning below identifies a missing import.
            bool usesAlphaTexture = spec.MeshKind == ParticleMeshKind.None &&
                                    particleTexture != null;

            // 3D chunks tumble in flight; transparent sprites rotate around the
            // camera-facing axis so the supplied fragment silhouettes remain visible.
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = !usesAlphaTexture;
            if (usesAlphaTexture)
                rotation.z = new ParticleSystem.MinMaxCurve(-Mathf.PI * 2.25f, Mathf.PI * 2.25f);
            else
            {
                rotation.x = new ParticleSystem.MinMaxCurve(-Mathf.PI * 4f, Mathf.PI * 4f);
                rotation.y = new ParticleSystem.MinMaxCurve(-Mathf.PI * 4f, Mathf.PI * 4f);
                rotation.z = new ParticleSystem.MinMaxCurve(-Mathf.PI * 4f, Mathf.PI * 4f);
            }

            // Size Over Lifetime curve: natural burst expansion and fade
            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.75f),
                new Keyframe(0.2f, 1.15f),
                new Keyframe(0.75f, 0.9f),
                new Keyframe(1f, 0.0f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // World Collision Bounce: Debris chunks & splinters bounce off floors and walls (disabled for glass to prevent CPU hitching)
            var collision = system.collision;
            collision.enabled = pattern != FracturePattern.RadialGlass;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.bounce = 0.35f;
            collision.dampen = 0.45f;
            collision.lifetimeLoss = 0.12f;
            collision.minKillSpeed = 0f;
            collision.maxKillSpeed = 1000f;
            collision.radiusScale = 0.85f;
            collision.quality = ParticleSystemCollisionQuality.Low;

            var noise = system.noise;
            noise.enabled = pattern != FracturePattern.RadialGlass;
            noise.strength = pattern == FracturePattern.UniformConcrete
                ? 0.42f * strength : 0.18f * strength;
            noise.frequency = pattern == FracturePattern.UniformConcrete ? 1.5f : 0.6f;
            noise.scrollSpeed = pattern == FracturePattern.UniformConcrete ? 0.6f : 0.25f;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (spec.MeshKind != ParticleMeshKind.None)
            {
                // The bundled pipeline prefab carries the Sharp_Piece texture. Procedural
                // particle meshes must keep it and provide valid UV0 coordinates; otherwise
                // an alpha-clipped material samples transparent (0,0) and the complete burst
                // disappears. Keep a compatible prefab material intact. Only a missing or
                // cross-pipeline material receives the cached runtime fallback, preserving
                // the same texture without allocating a material on every hit.
                Texture assignedTexture = GetAssignedParticleTexture(renderer.sharedMaterial);
                Texture2D meshTexture = particleTexture != null
                    ? particleTexture
                    : assignedTexture as Texture2D;
                if (renderer.sharedMaterial == null ||
                    !IsMaterialPipelineCompatible(renderer.sharedMaterial) ||
                    assignedTexture != meshTexture)
                    renderer.sharedMaterial = GetTexturedParticleMaterial(meshTexture);
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = GetParticleMesh(spec.MeshKind);
                renderer.velocityScale = 0f;
                renderer.lengthScale = 1f;
            }
            else
            {
                if (renderer.sharedMaterial == null ||
                    !IsMaterialPipelineCompatible(renderer.sharedMaterial))
                    renderer.sharedMaterial = CreateParticleMaterial();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.mesh = null;
                renderer.velocityScale = 0.04f;
                renderer.lengthScale = 1.6f;
            }

            var textureSheet = system.textureSheetAnimation;
            textureSheet.enabled = false;
        }

        static Texture GetAssignedParticleTexture(Material material)
        {
            if (material == null) return null;
            for (int i = 0; i < s_particleTextureProperties.Length; i++)
            {
                int property = s_particleTextureProperties[i];
                if (!material.HasProperty(property)) continue;
                Texture texture = material.GetTexture(property);
                if (texture != null) return texture;
            }
            return null;
        }

        public static AudioClip GetAudioClip(FracturePattern pattern)
        {
            if (s_audio.TryGetValue(pattern, out AudioClip clip) && clip != null)
                return clip;

            const int rate = 22050;
            float duration;
            float baseFrequency;
            float resonance;
            float noiseMix;
            float decay;
            switch (pattern)
            {
                case FracturePattern.RadialGlass:
                    duration = 0.18f; baseFrequency = 520f; resonance = 1.46f;
                    noiseMix = 0.1f; decay = 25f; break;
                case FracturePattern.UniformConcrete:
                    duration = 0.35f; baseFrequency = 80f; resonance = 1.5f;
                    noiseMix = 0.88f; decay = 18f; break;
                case FracturePattern.DirectionalWood:
                    duration = 0.28f; baseFrequency = 165f; resonance = 2.7f;
                    noiseMix = 0.72f; decay = 15f; break;
                case FracturePattern.BrickCells:
                    duration = 0.24f; baseFrequency = 245f; resonance = 1.48f;
                    noiseMix = 0.62f; decay = 13f; break;
                case FracturePattern.LayeredSlabs:
                    duration = 0.31f; baseFrequency = 92f; resonance = 1.83f;
                    noiseMix = 0.76f; decay = 10f; break;
                case FracturePattern.ColumnarPillars:
                    duration = 0.36f; baseFrequency = 74f; resonance = 2.1f;
                    noiseMix = 0.7f; decay = 9f; break;
                case FracturePattern.ImpactShatter:
                    duration = 0.23f; baseFrequency = 310f; resonance = 2.35f;
                    noiseMix = 0.67f; decay = 16f; break;
                default:
                    duration = 0.3f; baseFrequency = 118f; resonance = 1.65f;
                    noiseMix = 0.8f; decay = 12f; break;
            }

            int sampleCount = Mathf.CeilToInt(duration * rate);
            var samples = new float[sampleCount];
            uint random = 0x9E3779B9u + (uint)pattern * 0x85EBCA6Bu;
            float smoothedNoise = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)rate;
                float normalized = i / (float)Mathf.Max(1, sampleCount - 1);
                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                float white = ((random & 0x00FFFFFFu) / 8388607.5f) - 1f;
                smoothedNoise = Mathf.Lerp(smoothedNoise, white, 0.38f);
                float attack = Mathf.Clamp01(t * 520f);
                float envelope = attack * Mathf.Exp(-decay * t) *
                                 Mathf.SmoothStep(1f, 0f, normalized);
                float crack = i < rate / 180 ? white * (1f - i / (rate / 180f)) : 0f;
                float body = Mathf.Sin(Mathf.PI * 2f * baseFrequency * t) * 0.62f +
                             Mathf.Sin(Mathf.PI * 2f * baseFrequency * resonance * t) * 0.28f;
                float materialSignal;
                switch (pattern)
                {
                    case FracturePattern.DirectionalWood:
                        // Wood: Sharp hollow timber crack, high-frequency splintering snap (480Hz/920Hz), dry fibrous creak, ZERO heavy sub-bass
                        float woodSnapA = Mathf.Sin(Mathf.PI * 2f * 480f * t) * Mathf.Exp(-45f * t);
                        float woodSnapB = Mathf.Sin(Mathf.PI * 2f * 920f * t) * Mathf.Exp(-65f * Mathf.Max(0f, t - 0.015f));
                        float woodResonance = Mathf.Sin(Mathf.PI * 2f * 260f * t) * Mathf.Exp(-22f * t);
                        float woodRattle = smoothedNoise * (1f + 0.8f * Mathf.Sin(Mathf.PI * 2f * 320f * t)) * Mathf.Exp(-14f * t);
                        materialSignal = woodSnapA * 0.85f + woodSnapB * 0.55f + woodResonance * 0.45f + woodRattle * 0.35f + crack * 0.85f;
                        break;

                    case FracturePattern.UniformConcrete:
                        // Concrete: Heavy sub-bass impact thud (42Hz), dense masonry wall collapse, coarse gravel crunch
                        float concThump = Mathf.Sin(Mathf.PI * 2f * 42f * t) * Mathf.Exp(-12f * t);
                        float concPunch = Mathf.Sin(Mathf.PI * 2f * 115f * t) * Mathf.Exp(-25f * t);
                        float concRubble = (white * 0.7f + smoothedNoise * 0.3f) * (1f + 0.6f * Mathf.Sin(Mathf.PI * 2f * 85f * t)) * Mathf.Exp(-8f * t);
                        materialSignal = concThump * 1.1f + concPunch * 0.65f + concRubble * 0.85f + crack * 0.5f;
                        break;

                    case FracturePattern.ColumnarPillars:
                    case FracturePattern.LayeredSlabs:
                        // Stone: Dense solid granite rock impact (210Hz/380Hz), sharp stone slab clack, heavy rock rumble
                        float stoneClack = Mathf.Sin(Mathf.PI * 2f * 210f * t) * Mathf.Exp(-28f * t);
                        float stoneRing = Mathf.Sin(Mathf.PI * 2f * 380f * t) * Mathf.Exp(-42f * t);
                        float stoneRumble = Mathf.Sin(Mathf.PI * 2f * 65f * t) * Mathf.Exp(-10f * t);
                        float stoneGravel = smoothedNoise * Mathf.Exp(-12f * t);
                        materialSignal = stoneClack * 0.9f + stoneRing * 0.45f + stoneRumble * 0.65f + stoneGravel * 0.55f + crack * 0.65f;
                        break;

                    case FracturePattern.BrickCells:
                        float brickClack = Mathf.Sin(Mathf.PI * 2f * 265f * t) * Mathf.Exp(-22f * t);
                        materialSignal = brickClack * 0.7f + smoothedNoise * 0.5f + crack * 0.45f;
                        break;

                    case FracturePattern.RadialGlass:
                        float tinkle = Mathf.Sin(Mathf.PI * 2f * 520f * t) * 0.3f +
                            Mathf.Sin(Mathf.PI * 2f * 760f * t) * 0.07f;
                        materialSignal = tinkle * Mathf.Exp(-25f * t) + crack * 0.025f;
                        break;

                    default:
                        materialSignal = Mathf.Lerp(body, smoothedNoise, noiseMix) + crack * 0.55f;
                        break;
                }
                samples[i] = Mathf.Clamp(materialSignal * envelope * 0.82f, -1f, 1f);
            }

            clip = AudioClip.Create("QD Preset " + pattern, sampleCount, 1, rate, false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            s_audio[pattern] = clip;
            return clip;
        }

        static VisualSpec GetVisualSpec(FracturePattern pattern)
        {
            switch (pattern)
            {
                // Every preset names the debris shape it throws. Only wood used to, so
                // stone, brick and concrete all fell back to the stretched sprite path and
                // came out as camera-facing slivers of the Sharp_Piece sprite — splinters,
                // whatever the material was. The chunk meshes were already built here and
                // simply had nothing selecting them.
                case FracturePattern.RadialGlass:
                    return new VisualSpec { Color = new Color(0.72f, 0.93f, 1f, 1f), Count = 8,
                        Lifetime = 0.32f, Speed = 1.8f, Size = 0.08f, Gravity = 0.85f,
                        ConeAngle = 45f, Stretched = false,
                        MeshKind = ParticleMeshKind.GlassShard,
                        SurfaceColorWeight = 1f };
                case FracturePattern.UniformConcrete:
                    return new VisualSpec { Color = new Color(0.47f, 0.46f, 0.43f, 1f), Count = 28,
                        Lifetime = 0.58f, Speed = 2.45f, Size = 0.14f, Gravity = 1.35f,
                        ConeAngle = 70f, Stretched = false,
                        MeshKind = ParticleMeshKind.ConcreteChunk,
                        SurfaceColorWeight = 1f };
                case FracturePattern.DirectionalWood:
                    return new VisualSpec { Color = new Color(0.48f, 0.24f, 0.08f, 1f), Count = 14,
                        Lifetime = 0.48f, Speed = 1.75f, Size = 0.12f, Gravity = 0.9f,
                        ConeAngle = 48f, Stretched = false,
                        MeshKind = ParticleMeshKind.WoodSplinter,
                        SurfaceColorWeight = 1f };
                case FracturePattern.BrickCells:
                    return new VisualSpec { Color = new Color(0.58f, 0.23f, 0.12f, 1f), Count = 13,
                        Lifetime = 0.58f, Speed = 1.65f, Size = 0.075f, Gravity = 1f,
                        ConeAngle = 58f, Stretched = false,
                        MeshKind = ParticleMeshKind.BrickChip,
                        SurfaceColorWeight = 1f };
                case FracturePattern.LayeredSlabs:
                    return new VisualSpec { Color = new Color(0.53f, 0.54f, 0.52f, 1f), Count = 17,
                        Lifetime = 0.64f, Speed = 1.45f, Size = 0.14f, Gravity = 0.62f,
                        ConeAngle = 62f, Stretched = false,
                        MeshKind = ParticleMeshKind.StoneSlab,
                        SurfaceColorWeight = 1f };
                case FracturePattern.ColumnarPillars:
                    return new VisualSpec { Color = new Color(0.34f, 0.33f, 0.31f, 1f), Count = 20,
                        Lifetime = 0.7f, Speed = 1.55f, Size = 0.14f, Gravity = 0.75f,
                        ConeAngle = 65f, Stretched = false,
                        MeshKind = ParticleMeshKind.PillarChunk,
                        SurfaceColorWeight = 1f };
                case FracturePattern.ImpactShatter:
                    return new VisualSpec { Color = new Color(0.43f, 0.39f, 0.34f, 1f), Count = 19,
                        Lifetime = 0.48f, Speed = 2.55f, Size = 0.12f, Gravity = 0.95f,
                        ConeAngle = 52f, Stretched = false,
                        MeshKind = ParticleMeshKind.ImpactChip,
                        SurfaceColorWeight = 1f };
                default:
                    return new VisualSpec { Color = new Color(0.55f, 0.54f, 0.5f, 1f), Count = 18,
                        Lifetime = 0.62f, Speed = 1.7f, Size = 0.09f, Gravity = 0.72f,
                        ConeAngle = 62f, Stretched = false,
                        MeshKind = ParticleMeshKind.ConcreteChunk,
                        SurfaceColorWeight = 1f };
            }
        }

        static Material CreateParticleMaterial()
        {
            Material template = GetParticleMaterial();
            if (template == null) return null;
            var material = new Material(template)
            {
                name = "QD Preset Particle Material Instance",
                hideFlags = HideFlags.DontSave
            };
            return material;
        }

        /// <summary>
        /// Tints every color slot a particle shader might read across HDRP, URP and the
        /// built-in renderer. HDRP/Unlit only reads <c>_UnlitColor</c>, URP reads
        /// <c>_BaseColor</c>, and legacy particle shaders read <c>_Color</c>/<c>_TintColor</c>;
        /// the old tint path set every one of those EXCEPT <c>_UnlitColor</c>, which is why
        /// HDRP particles stayed white regardless of the sampled impact color.
        /// </summary>
        public static void ApplyMaterialTint(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", color);
        }

        /// <summary>Adds every tint property to a MaterialPropertyBlock so per-spawn
        /// coloring works without instancing a new material for each break.</summary>
        public static void ApplyBlockTint(MaterialPropertyBlock block, Color color)
        {
            if (block == null) return;
            block.SetColor("_UnlitColor", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            block.SetColor("_TintColor", color);
        }

        internal static bool SupportsMaterialTint(Material material)
        {
            return material != null &&
                   (material.HasProperty("_UnlitColor") ||
                    material.HasProperty("_BaseColor") ||
                    material.HasProperty("_Color") ||
                    material.HasProperty("_TintColor"));
        }

        static void SetMaterialColor(Material material, Color color)
        {
            ApplyMaterialTint(material, color);
        }

        /// <summary>
        /// True when the material renders correctly under the active render pipeline. A
        /// null/error shader, or an HDRP shader running under URP (and vice versa), shows
        /// as magenta. A prefab that bakes a pipeline-specific particle material must be
        /// swapped for a compatible one before it is shown in the other pipeline.
        /// </summary>
        public static bool IsMaterialPipelineCompatible(Material material)
        {
            if (material == null || material.shader == null) return false;
            string shaderName = material.shader.name;
            if (shaderName == "Hidden/InternalErrorShader") return false;

            bool shaderIsHdrp = shaderName.StartsWith("HDRP/");
            bool shaderIsUrp = shaderName.StartsWith("Universal Render Pipeline/") ||
                               shaderName.StartsWith("URP/");

            GetActivePipeline(out bool isHdrp, out bool isUrp);
            if (isHdrp) return shaderIsHdrp;
            if (isUrp) return shaderIsUrp;
            // Built-in pipeline renders neither HDRP nor URP SRP shaders.
            return !shaderIsHdrp && !shaderIsUrp;
        }

        /// <summary>
        /// Builds a transparent, unlit particle material valid for the active render
        /// pipeline, optionally carrying a main texture over so a swapped material keeps
        /// its sprite silhouette. Used to replace a baked pipeline-specific material.
        /// </summary>
        public static Material CreatePipelineParticleMaterial(Texture mainTexture)
        {
            Shader shader = FindParticleShader();
            if (shader == null) return null;
            var material = new Material(shader)
            {
                name = "QD Runtime Particle Material",
                hideFlags = HideFlags.DontSave
            };
            ConfigureTransparent(material);
            if (mainTexture != null)
                SetParticleTexture(material, mainTexture as Texture2D);
            return material;
        }

        public static void GetActivePipeline(out bool isHdrp, out bool isUrp)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                pipeline = GraphicsSettings.defaultRenderPipeline;
            string pipelineName = pipeline != null
                ? pipeline.GetType().FullName ?? pipeline.GetType().Name
                : string.Empty;
            isHdrp = pipelineName.Contains("HighDefinition") ||
                     pipelineName.Contains("HDRenderPipeline");
            isUrp = pipelineName.Contains("Universal");
        }

        /// <summary>
        /// Swaps one of this package's own pipeline prefabs for the variant that matches the
        /// active render pipeline, and leaves anything the user supplied untouched. A stored
        /// reference records only the pipeline its profile was authored under, so the same
        /// profile opened elsewhere drew its burst with a magenta material. A name that
        /// resolves to no bundled variant falls through to the original reference, which is
        /// what keeps a user prefab that merely starts with "Particle_" working.
        /// </summary>
        public static ParticleSystem ResolveForActivePipeline(ParticleSystem authored)
        {
            if (authored == null || authored.gameObject == null) return authored;
            string name = authored.gameObject.name;
            if (!name.StartsWith("Particle_", System.StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Grey Volume Fog",
                    System.StringComparison.OrdinalIgnoreCase))
                return authored;

            GameObject match = LoadPipelineParticlePrefab(name);
            ParticleSystem resolved = match != null
                ? match.GetComponent<ParticleSystem>()
                : null;
            return resolved != null ? resolved : authored;
        }

        public static GameObject LoadPipelineParticlePrefab(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return null;
            GetActivePipeline(out bool isHdrp, out bool isUrp);

            string[] resourcePaths = GetPipelineParticleResourcePaths(
                baseName, isHdrp, isUrp);
            for (int i = 0; i < resourcePaths.Length; i++)
            {
                GameObject prefab = Resources.Load<GameObject>(resourcePaths[i]);
                if (prefab != null) return prefab;
            }
            return null;
        }

        static string[] GetPipelineParticleResourcePaths(
            string baseName, bool isHdrp, bool isUrp)
        {
            if (baseName.Equals("Particle_Universal", System.StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals("Particle_URP", System.StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals("Particle_HDRP", System.StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals("Particle_Built-In", System.StringComparison.OrdinalIgnoreCase))
            {
                baseName = "Particle";
            }
            else if (baseName.StartsWith("Grey Volume Fog", System.StringComparison.OrdinalIgnoreCase))
            {
                baseName = "Grey Volume Fog";
            }

            if (isHdrp)
            {
                return new[]
                {
                    $"Particles/{baseName}_HDRP",
                    $"Particles/{baseName}",
                    $"Particles/{baseName}_URP",
                    $"Particles/{baseName}_Built-In"
                };
            }
            if (isUrp)
            {
                return new[]
                {
                    $"Particles/{baseName}_URP",
                    $"Particles/{baseName}",
                    $"Particles/{baseName}_HDRP",
                    $"Particles/{baseName}_Built-In"
                };
            }

            return new[]
            {
                $"Particles/{baseName}_Built-In",
                $"Particles/{baseName}",
                $"Particles/{baseName}_URP",
                $"Particles/{baseName}_HDRP"
            };
        }

        static Shader FindParticleShader()
        {
            GetActivePipeline(out bool isHdrp, out bool isUrp);
            Shader shader = null;
            if (isHdrp)
                shader = Shader.Find("HDRP/Unlit");
            else if (isUrp)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Universal Render Pipeline/Unlit");
            else
                shader = Shader.Find("Particles/Standard Unlit") ??
                         Shader.Find("Legacy Shaders/Particles/Alpha Blended");

            return shader ??
                   Shader.Find("Sprites/Default") ??
                   Shader.Find("Unlit/Transparent");
        }

        static void ConfigureTransparent(Material material)
        {
            if (material == null) return;
            // HDRP surface options.
            if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 1f);
            if (material.HasProperty("_BlendMode")) material.SetFloat("_BlendMode", 0f);
            // URP surface options.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            // Built-in Standard/Particle surface options.
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 2f);
            if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
        }

        static Texture2D GetParticleTexture(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (s_particleTextures.TryGetValue(resourcePath, out Texture2D texture))
                return texture;
            texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return null;
            s_particleTextures[resourcePath] = texture;
            return texture;
        }

        static void SetParticleTexture(Material material, Texture2D texture)
        {
            if (material == null) return;
            if (material.HasProperty("_UnlitColorMap"))
                material.SetTexture("_UnlitColorMap", texture);
            if (material.HasProperty("_BaseColorMap"))
                material.SetTexture("_BaseColorMap", texture);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (texture == null) return;

            // The same alpha textures work in HDRP, URP and the built-in renderer.
            if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 1f);
            if (material.HasProperty("_BlendMode")) material.SetFloat("_BlendMode", 0f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 2f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
        }

        static Mesh GetParticleMesh(ParticleMeshKind kind)
        {
            if (s_particleMeshes.TryGetValue(kind, out Mesh mesh) && mesh != null)
                return mesh;

            Vector3[] vertices;
            switch (kind)
            {
                case ParticleMeshKind.WoodSplinter:
                    vertices = new[]
                    {
                        new Vector3(-0.75f, 0f, 0f), new Vector3(0.75f, 0.02f, 0f),
                        new Vector3(0.08f, 0.08f, 0.12f), new Vector3(-0.05f, -0.06f, 0.1f),
                        new Vector3(0.03f, 0.03f, -0.12f)
                    };
                    break;
                case ParticleMeshKind.BrickChip:
                    vertices = new[]
                    {
                        new Vector3(-0.48f, -0.24f, -0.18f), new Vector3(0.48f, -0.24f, -0.18f),
                        new Vector3(0.48f, 0.24f, -0.18f), new Vector3(-0.48f, 0.24f, -0.18f),
                        new Vector3(-0.36f, -0.18f, 0.22f), new Vector3(0.36f, -0.18f, 0.22f),
                        new Vector3(0.36f, 0.18f, 0.22f), new Vector3(-0.36f, 0.18f, 0.22f)
                    };
                    break;
                case ParticleMeshKind.StoneSlab:
                    vertices = new[]
                    {
                        new Vector3(-0.62f, -0.08f, -0.46f), new Vector3(0.62f, -0.08f, -0.42f),
                        new Vector3(0.48f, 0.1f, 0.5f), new Vector3(-0.5f, 0.08f, 0.52f),
                        new Vector3(0.05f, 0.28f, 0.02f)
                    };
                    break;
                case ParticleMeshKind.PillarChunk:
                    vertices = new[]
                    {
                        new Vector3(-0.45f, -0.5f, -0.32f), new Vector3(0.45f, -0.5f, -0.32f),
                        new Vector3(0.38f, 0.5f, -0.22f), new Vector3(-0.38f, 0.5f, -0.22f),
                        new Vector3(0f, 0.1f, 0.46f)
                    };
                    break;
                case ParticleMeshKind.GlassShard:
                    vertices = new[]
                    {
                        new Vector3(-0.48f, -0.28f, 0.02f), new Vector3(0.55f, -0.15f, -0.04f),
                        new Vector3(0.18f, 0.62f, 0.05f), new Vector3(-0.32f, 0.45f, -0.04f),
                        new Vector3(0.04f, 0.08f, 0.16f), new Vector3(-0.06f, -0.04f, -0.16f)
                    };
                    break;
                case ParticleMeshKind.ImpactChip:
                    vertices = new[]
                    {
                        new Vector3(-0.58f, 0f, 0f), new Vector3(0.55f, 0.02f, 0f),
                        new Vector3(0.08f, 0.16f, 0.28f), new Vector3(-0.04f, -0.13f, 0.22f),
                        new Vector3(0.03f, 0.07f, -0.24f)
                    };
                    break;
                default:
                    vertices = new[]
                    {
                        new Vector3(-0.6f, 0f, 0f),
                        new Vector3(0.6f, 0.05f, 0f), new Vector3(0.12f, 0.45f, 0.32f),
                        new Vector3(-0.32f, -0.38f, 0.26f), new Vector3(0.22f, 0.12f, -0.42f)
                    };
                    break;
            }

            int[] triangles = vertices.Length == 8
                ? new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                    0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
                    2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 }
                : new[] { 0, 2, 3, 1, 3, 2, 0, 4, 2, 1, 2, 4,
                    0, 3, 4, 1, 4, 3 };
            mesh = new Mesh
            {
                name = "QD Preset " + kind + " Particle",
                hideFlags = HideFlags.DontSave,
                vertices = vertices,
                triangles = triangles,
                uv = BuildParticleUvs(vertices)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            s_particleMeshes[kind] = mesh;
            return mesh;
        }

        static Vector2[] BuildParticleUvs(Vector3[] vertices)
        {
            var uvs = new Vector2[vertices.Length];
            if (vertices.Length == 0) return uvs;

            Vector3 min = vertices[0];
            Vector3 max = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }

            Vector3 size = max - min;
            int smallestAxis = size.x <= size.y && size.x <= size.z
                ? 0
                : size.y <= size.z ? 1 : 2;
            int uAxis = smallestAxis == 0 ? 1 : 0;
            int vAxis = smallestAxis == 2 ? 1 : 2;
            float uMin = Axis(min, uAxis);
            float vMin = Axis(min, vAxis);
            float uSize = Mathf.Max(Axis(size, uAxis), 0.00001f);
            float vSize = Mathf.Max(Axis(size, vAxis), 0.00001f);

            // This window is fully opaque in the bundled Sharp_Piece alpha mask and has a
            // mid-grey average instead of the near-black centre used previously. It keeps
            // visible rocky detail without clipping triangles or making a random subset of
            // chunks markedly darker. The mesh remains cached, so this is a one-time cost.
            for (int i = 0; i < vertices.Length; i++)
            {
                float u = Mathf.Clamp01((Axis(vertices[i], uAxis) - uMin) / uSize);
                float v = Mathf.Clamp01((Axis(vertices[i], vAxis) - vMin) / vSize);
                uvs[i] = new Vector2(Mathf.Lerp(0.48f, 0.62f, u),
                    Mathf.Lerp(0.62f, 0.78f, v));
            }
            return uvs;
        }

        static float Axis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        static Material GetParticleMaterial()
        {
            if (s_particleState.ParticleMaterial != null) return s_particleState.ParticleMaterial;

            // Never fall back to the built-in "Default-Particle.mat": its legacy shader
            // renders magenta under HDRP and URP. Always build the transparent unlit
            // material for whichever pipeline is actually active.
            s_particleState.ParticleMaterial = CreatePipelineParticleMaterial(null);
            if (s_particleState.ParticleMaterial != null)
                s_particleState.ParticleMaterial.name = "QD Preset Particle Material";
            return s_particleState.ParticleMaterial;
        }

        static Material GetTexturedParticleMaterial(Texture2D texture)
        {
            if (texture == null) return GetParticleMaterial();
            if (s_particleState.TexturedParticleMaterial != null &&
                s_particleState.TexturedParticleTexture == texture &&
                IsMaterialPipelineCompatible(s_particleState.TexturedParticleMaterial))
                return s_particleState.TexturedParticleMaterial;

            s_particleState.TexturedParticleTexture = texture;
            s_particleState.TexturedParticleMaterial = CreatePipelineParticleMaterial(texture);
            if (s_particleState.TexturedParticleMaterial != null)
                s_particleState.TexturedParticleMaterial.name = "QD Preset Textured Particle Material";
            return s_particleState.TexturedParticleMaterial;
        }
    }
}
