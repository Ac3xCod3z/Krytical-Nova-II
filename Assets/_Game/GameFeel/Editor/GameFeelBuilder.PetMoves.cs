using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Effects that belong to the animations of the Kitsune Spirit (its first form and its 25 % form) and to the
    /// three animations the grown Tidecallers gained on 12 Oct 2026 (ShellSpin, Undertow, Maelstrom). PetFollower
    /// plays each at the moments or over the stretch its PetEffect names.
    /// <para>The fox's are spirit fire: ribbons drawn with the Tidecaller's own Pet Water Flow shader in the fox's
    /// orange and gold, so the two pets' ribbons are cut the same way, with rings and a ward in the energy shader
    /// (All In 1 Shader Nodes) and small solid stars and sparks like the other pets' comic pieces.</para>
    /// <para>The Tidecaller's are made of what its water was already made of: the same ribbons in the same
    /// material (M_FX_PetWaterFlow), the same water shards, and the wave of its tide call. Lightning, arcs and
    /// bubbles are POLYGON Particle FX made over for URP.</para>
    /// Sizes are in footprints: PetFollower sizes an effect so that 1 is as far as the pet is wide.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string SpiritTrailPath = VfxFolder + "/PFX_Pet_SpiritTrail.prefab";
        public const string SpiritMotesPath = VfxFolder + "/PFX_Pet_SpiritMotes.prefab";
        public const string SpiritGlintPath = VfxFolder + "/PFX_Pet_SpiritGlint.prefab";
        public const string SpiritSprayPath = VfxFolder + "/PFX_Pet_SpiritSpray.prefab";
        public const string SpiritWardPath = VfxFolder + "/PFX_Pet_SpiritWard.prefab";
        public const string SpiritArcsPath = VfxFolder + "/PFX_Pet_SpiritArcs.prefab";

        public const string TideTornadoPath = VfxFolder + "/PFX_Pet_TideTornado.prefab";
        public const string TideFlingPath = VfxFolder + "/PFX_Pet_TideFling.prefab";
        public const string TideFunnelPath = VfxFolder + "/PFX_Pet_TideFunnel.prefab";
        public const string TideBubblesPath = VfxFolder + "/PFX_Pet_TideBubbles.prefab";
        public const string TideGeyserPath = VfxFolder + "/PFX_Pet_TideGeyser.prefab";
        public const string TideSprayPath = VfxFolder + "/PFX_Pet_TideSpray.prefab";
        public const string TideGatherPath = VfxFolder + "/PFX_Pet_TideGather.prefab";
        public const string TideShockPath = VfxFolder + "/PFX_Pet_TideShock.prefab";
        public const string TideLightningPath = VfxFolder + "/PFX_Pet_TideLightning.prefab";
        public const string TideArcsPath = VfxFolder + "/PFX_Pet_TideArcs.prefab";

        // Comic pieces from the Cartoon FX pack, made by the same means as the hit marks and words (GameFeelBuilder.Comic).
        public const string PetPounceMarkPath = ComicFolder + "/PFX_Mark_PetPounce.prefab";
        public const string PetPounceWordPath = ComicFolder + "/PFX_Word_PetPounce.prefab";
        public const string PetClapMarkPath = ComicFolder + "/PFX_Mark_PetClap.prefab";
        public const string PetClapWordPath = ComicFolder + "/PFX_Word_PetClap.prefab";
        public const string PetWhooshWordPath = ComicFolder + "/PFX_Word_PetWhoosh.prefab";

        private const string SpiritFlowMaterialPath = MaterialFolder + "/M_FX_PetSpiritFlow.mat";
        private const string SpiritStarMeshPath = PetMeshFolder + "/SM_FX_SpiritStar.asset";

        // The fox's fire: the orange of its glow, the gold of its last stage, and the pale heart of a flame.
        private static readonly Color SpiritOrange = new Color(1f, 0.54f, 0.24f);
        private static readonly Color SpiritDeep = new Color(0.93f, 0.33f, 0.14f);
        private static readonly Color SpiritGold = new Color(1f, 0.82f, 0.25f);
        private static readonly Color SpiritPale = new Color(1f, 0.96f, 0.8f);

        /// <summary>Builds only the Kitsune Spirit's effects and the grown Tidecallers' three new ones.</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Kitsune and Tidecaller Move Effects")]
        public static void BuildPetMoveEffects()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            WeatherVfxBuilder.EnsureFolder(PetMeshFolder);
            var energy = AssetDatabase.LoadAssetAtPath<Shader>(DroneEnergyShaderPath);
            Material water = WaterFlowMaterial();
            if (energy == null || water == null)
            {
                Debug.LogError("[GameFeel] The energy shader or the water material is missing: build the Drone Core and Tidecaller effects first.");
                return;
            }
            Mesh disc = SaveUvMesh(DroneDiscMeshPath, DiscMesh("SM_FX_DroneDisc"));
            Mesh shard = SaveMesh(ShardMeshPath, ShardMesh());
            Mesh star = SaveMesh(SpiritStarMeshPath, StarMesh("SM_FX_SpiritStar", 4, 0.5f, 0.17f, Color.white, 0f));
            Mesh ball = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            Material flat = WeatherVfxBuilder.OpaqueParticleMaterial("M_ComicPuff", Color.white);

            BuildSpiritEffects(energy, disc, shard, star, ball, flat);
            BuildTideMoveEffects(energy, water, disc, shard, flat);

            // In tandem with those, comic pieces from the Cartoon FX pack. Sizes are for a pet a metre across:
            // PetFollower multiplies them by the pet's footprint.
            WeatherVfxBuilder.EnsureFolder(ComicFolder);
            BuildMark(new Mark("PFX_Mark_PetPounce", "Impact 028", 0.9f));       // where the fox's front paws strike
            BuildWord(new Word("PFX_Word_PetPounce", "Texts/Text 022", "Bangers", 0.2f, 1.1f, "POUNCE!"));
            BuildMark(new Mark("PFX_Mark_PetClap", "Explosion 006", 3.2f));      // a ring of puffs round the Tidecaller's clap
            BuildWord(new Word("PFX_Word_PetClap", "Texts/Text 003", "Frijole", 0.3f, 2f, "KRAKOOM!"));
            BuildWord(new Word("PFX_Word_PetWhoosh", "Texts/Text 016", "Titan One", 0.22f, 1.3f, "WHOOSH!"));
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ the Kitsune Spirit

        private static void BuildSpiritEffects(Shader energy, Mesh disc, Mesh shard, Mesh star, Mesh ball, Material flat)
        {
            // The same cut-out ribbon as the Tidecaller's water, in fire.
            var flowShader = AssetDatabase.LoadAssetAtPath<Shader>(WaterFlowShaderPath);
            var fire = AssetDatabase.LoadAssetAtPath<Material>(SpiritFlowMaterialPath);
            if (fire == null)
            {
                fire = new Material(flowShader) { name = "M_FX_PetSpiritFlow" };
                fire.SetColor("_WaterColor", SpiritOrange);
                fire.SetColor("_DeepColor", SpiritDeep);
                fire.SetColor("_FoamColor", SpiritPale);
                fire.SetFloat("_FlowSpeed", 2.4f);
                fire.SetFloat("_Bands", 3f);
                fire.SetFloat("_Waver", 0.3f);
                AssetDatabase.CreateAsset(fire, SpiritFlowMaterialPath);
            }
            Material ring = Energy(energy, "M_FX_SpiritRing", "RING", m =>
            {
                m.SetColor("_ColorA", new Color(SpiritOrange.r, SpiritOrange.g, SpiritOrange.b, 0.8f));
                m.SetColor("_ColorB", SpiritGold);
                m.SetColor("_GlowColor", SpiritOrange);
                m.SetFloat("_Glow", 0.5f);
                m.SetFloat("_RingWidth", 0.1f);
            });
            Material ward = Energy(energy, "M_FX_SpiritWard", "HOLO", m =>
            {
                m.SetColor("_ColorA", new Color(SpiritOrange.r, SpiritOrange.g, SpiritOrange.b, 0.4f));
                m.SetColor("_ColorB", SpiritGold);
                m.SetColor("_GlowColor", SpiritOrange);
                m.SetFloat("_Glow", 0.35f);
                m.SetFloat("_HoloFrequency", 9f);
                m.SetFloat("_HoloSpeed", 0.6f);
                m.SetFloat("_HoloAccentFrequency", 3f);
                m.SetFloat("_HoloBase", 0.06f);
                m.SetFloat("_EndFade", 0f);
            });

            // A ribbon left behind by whatever it is worn on (a tail tip), drawn in the pet's own space (owner, 22 Oct
            // 2026: no streak down the road behind a running fox, the way Tidecaller's water never trails): the ribbon
            // follows the tip's swish around the body, and the pet carries it along as it runs. The pet is set as its
            // space when it is worn (PetFollower); with none it would fall back to the world. For as long as it plays.
            var trailRoot = new GameObject("PFX_Pet_SpiritTrail");
            try
            {
                var system = trailRoot.AddComponent<ParticleSystem>();
                var main = system.main;
                main.playOnAwake = true;
                main.loop = true;
                main.duration = 1f;
                main.startLifetime = 0.32f;
                main.startSpeed = 0f;
                main.startSize = 1f;
                main.startColor = Color.white;
                main.gravityModifier = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = 40;
                var emission = system.emission;
                emission.rateOverTime = 48f;
                var shape = system.shape;
                shape.enabled = false;
                var trails = system.trails;
                trails.enabled = true;
                trails.mode = ParticleSystemTrailMode.Ribbon;
                trails.ribbonCount = 1;
                trails.attachRibbonsToTransform = false;
                trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
                trails.sizeAffectsWidth = true;
                trails.inheritParticleColor = true;
                trails.widthOverTrail = 0.11f;
                trails.generateLightingData = false;
                var renderer = trailRoot.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.None;
                renderer.trailMaterial = fire;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                SavePrefab(trailRoot, SpiritTrailPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(trailRoot); }

            // Motes off a tail tip while it rests: a small star now and then, which drifts up and goes out.
            var motesRoot = new GameObject("PFX_Pet_SpiritMotes");
            try
            {
                ParticleSystem motes = FlatSystem(motesRoot, "Motes", star, flat, motesRoot.transform);
                var main = motes.main;
                main.loop = true;
                main.duration = 2f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.11f);
                main.startColor = new ParticleSystem.MinMaxGradient(SpiritGold, SpiritOrange);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 8;
                var emission = motes.emission;
                emission.rateOverTime = 2.2f;
                var shape = motes.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.05f;
                var velocity = motes.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
                velocity.y = new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
                velocity.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
                var size = motes.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
                SavePrefab(motesRoot, SpiritMotesPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(motesRoot); }

            // A glint: a ring of light that opens and a few stars knocked out of it. Once each time it is played.
            var glintRoot = new GameObject("PFX_Pet_SpiritGlint");
            try
            {
                Pop(glintRoot, "Ring", disc, ring, 0.4f, 0.36f, billboard: true);
                ParticleSystem stars = FlatSystem(glintRoot, "Stars", star, flat, glintRoot.transform);
                var main = stars.main;
                main.loop = false;
                main.duration = 0.4f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
                main.startColor = new ParticleSystem.MinMaxGradient(SpiritGold, SpiritPale);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles = 8;
                var emission = stars.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });
                var shape = stars.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.06f;
                Slowed(stars, 0.1f, 0.15f);
                var size = stars.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
                SavePrefab(glintRoot, SpiritGlintPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(glintRoot); }

            // What flies off a fox that shakes itself: sparks thrown out sideways from where it is worn (neck, back),
            // which fall a little and go out. For as long as it plays.
            var sprayRoot = new GameObject("PFX_Pet_SpiritSpray");
            try
            {
                ParticleSystem sparks = FlatSystem(sprayRoot, "Sparks", shard, flat, sprayRoot.transform);
                var main = sparks.main;
                main.loop = true;
                main.duration = 1f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
                main.startColor = new ParticleSystem.MinMaxGradient(SpiritGold, SpiritOrange);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = 0.5f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 40;
                var emission = sparks.emission;
                emission.rateOverTime = 40f;
                var shape = sparks.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.08f;
                shape.scale = new Vector3(1f, 0.5f, 0.4f); // out to both sides, as the shake goes
                var size = sparks.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));
                SavePrefab(sprayRoot, SpiritSprayPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(sprayRoot); }

            // The ward of its guard: a dome of light lines that rises round it as its tails curl (frames 5 to 16 of
            // 72), holds, and drops as it stands down (54 to 68), with a ring on the ground under it. Once.
            var wardRoot = new GameObject("PFX_Pet_SpiritWard");
            try
            {
                const float seconds = 63f / 24f;
                var rise = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.17f, 1f), new Keyframe(0.78f, 1f), new Keyframe(1f, 0.4f));
                var hold = new Gradient();
                hold.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.78f), new GradientAlphaKey(0f, 1f) });
                ParticleSystem dome = Pop(wardRoot, "Dome", ball, ward, seconds, 0.62f, billboard: false);
                ParticleSystem ground = Pop(wardRoot, "Ground", disc, ring, seconds, 0.8f, billboard: false);
                ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ground.transform.localPosition = new Vector3(0f, -0.28f, 0f);
                foreach (ParticleSystem part in new[] { dome, ground })
                {
                    var size = part.sizeOverLifetime;
                    size.size = new ParticleSystem.MinMaxCurve(1f, rise);
                    var colour = part.colorOverLifetime;
                    colour.color = hold;
                }
                SavePrefab(wardRoot, SpiritWardPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(wardRoot); }

            // Small arcs that jump about a tail tip while the guard is held.
            Make("FX_Electricity_01", "PFX_Pet_SpiritArcs", 1f, SpiritGold, loop: true);
        }

        // ------------------------------------------------------------------ the grown Tidecallers' new animations

        private static void BuildTideMoveEffects(Shader energy, Material water, Mesh disc, Mesh shard, Material flat)
        {
            Material shock = Energy(energy, "M_FX_TideShock", "RING", m =>
            {
                m.SetColor("_ColorA", new Color(WaterColour.r, WaterColour.g, WaterColour.b, 0.85f));
                m.SetColor("_ColorB", FoamColour);
                m.SetColor("_GlowColor", WaterColour);
                m.SetFloat("_Glow", 0.5f);
                m.SetFloat("_RingWidth", 0.1f);
            });

            // ShellSpin: a water tornado. Ribbons start on a ring on the ground under the pet and wind up round it,
            // widening as they climb. For as long as it plays.
            var tornado = new GameObject("PFX_Pet_TideTornado");
            try
            {
                ParticleSystem twist = Winding(tornado, "Twist", water, 0.13f, 16f, new Vector2(0.75f, 1f));
                Ring(twist, 0.32f, 0f);
                Wind(twist, new Vector2(1.3f, 1.8f), 7.5f, 0.38f);
                SavePrefab(tornado, TideTornadoPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(tornado); }

            // ... flung outwards when the fins snap wide: a ring of ribbons and shards thrown out level. Once.
            var fling = new GameObject("PFX_Pet_TideFling");
            try
            {
                ParticleSystem ribbons = Winding(fling, "Ribbons", water, 0.12f, 0f, new Vector2(0.35f, 0.5f));
                Once(ribbons, 0.5f, 12);
                Ring(ribbons, 0.45f, 0f);
                var main = ribbons.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 3.4f);
                Slowed(ribbons, 0.1f, 0.4f);
                ParticleSystem drops = Shards(fling, "Drops", shard, flat, 18, new Vector2(2.2f, 3.4f), 0.5f);
                Ring(drops, 0.45f, 0f);
                SavePrefab(fling, TideFlingPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(fling); }

            // Undertow: a funnel drawn down round it. Ribbons start on a wide ring above the pet and wind down and
            // inwards to a point below it. For as long as it plays.
            var funnel = new GameObject("PFX_Pet_TideFunnel");
            try
            {
                ParticleSystem twist = Winding(funnel, "Twist", water, 0.12f, 16f, new Vector2(0.7f, 0.9f));
                Ring(twist, 0.8f, 0.7f);
                Wind(twist, new Vector2(-1.8f, -1.4f), 6.5f, -0.75f);
                SavePrefab(funnel, TideFunnelPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(funnel); }

            // ... a held breath of bubbles while it hangs gathered at the bottom.
            Make("FX_Bubbles_Float_01", "PFX_Pet_TideBubbles", 1f, loop: true);

            // ... the geyser of its one great stroke: a column of ribbons thrown straight up from under it, with
            // shards at its head. Once.
            var geyser = new GameObject("PFX_Pet_TideGeyser");
            try
            {
                ParticleSystem column = Winding(geyser, "Column", water, 0.16f, 0f, new Vector2(0.55f, 0.75f));
                Once(column, 0.6f, 10, 8, 6);
                Ring(column, 0.22f, 0f);
                Wind(column, new Vector2(3.2f, 4.4f), 2f, 0.1f);
                Slowed(column, 0.06f, 0.6f);
                ParticleSystem drops = Shards(geyser, "Drops", shard, flat, 14, new Vector2(3f, 4.6f), 1.1f);
                var cone = drops.shape;
                cone.enabled = true;
                cone.shapeType = ParticleSystemShapeType.Cone;
                cone.angle = 14f;
                cone.radius = 0.18f;
                cone.rotation = new Vector3(-90f, 0f, 0f);
                SavePrefab(geyser, TideGeyserPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(geyser); }

            // A burst of spray: shards thrown out all round from where it is worn, which fall. Once.
            var spray = new GameObject("PFX_Pet_TideSpray");
            try
            {
                ParticleSystem drops = Shards(spray, "Drops", shard, flat, 20, new Vector2(1.6f, 3f), 1f);
                var ball = drops.shape;
                ball.enabled = true;
                ball.shapeType = ParticleSystemShapeType.Sphere;
                ball.radius = 0.3f;
                SavePrefab(spray, TideSprayPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(spray); }

            // Maelstrom: the water it gathers behind it while it leans back and trembles: a curtain of ribbons rising
            // from the ground behind the pet. The wave itself is its tide call's wave, played larger.
            var gather = new GameObject("PFX_Pet_TideGather");
            try
            {
                ParticleSystem curtain = Winding(gather, "Curtain", water, 0.14f, 14f, new Vector2(0.8f, 1.1f));
                var shape = curtain.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.position = new Vector3(0f, 0f, -0.95f);
                shape.scale = new Vector3(1.9f, 0.05f, 0.25f);
                Wind(curtain, new Vector2(1.2f, 2.1f), 0f, 0f);
                var noise = curtain.noise;
                noise.enabled = true;
                noise.separateAxes = true;
                noise.strengthX = 0.2f;
                noise.strengthY = 0f;
                noise.strengthZ = 0.08f;
                noise.frequency = 1.4f;
                noise.scrollSpeed = 0.6f;
                noise.octaveCount = 1;
                SavePrefab(gather, TideGatherPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(gather); }

            // ... the shock of the clap: a ring that opens flat along the ground and one that opens ahead of the fins. Once.
            var shockRoot = new GameObject("PFX_Pet_TideShock");
            try
            {
                ParticleSystem ground = Pop(shockRoot, "Ground", disc, shock, 0.55f, 3.4f, billboard: false);
                ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ground.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                Pop(shockRoot, "Ahead", disc, shock, 0.4f, 1.8f, billboard: false);
                SavePrefab(shockRoot, TideShockPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(shockRoot); }

            // ... lightning on the clap, and small arcs that crawl over the shell while it gathers.
            Make("FX_LightningStrike_01", "PFX_Pet_TideLightning", 1f, FoamColour);
            Make("FX_Electricity_01", "PFX_Pet_TideArcs", 1f, new Color(0.6f, 0.93f, 1f), loop: true);
        }

        /// <summary>
        /// Ribbons of the faux water (or of fire) that wind about the effect's upright axis: a ribbon system as the
        /// Tidecaller's flow and swirl are made of, let go <paramref name="aSecond"/> times a second.
        /// </summary>
        private static ParticleSystem Winding(GameObject root, string name, Material material, float width, float aSecond, Vector2 life)
        {
            ParticleSystem system = RibbonSystem(root, name, material, width, 0.5f);
            var main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.maxParticles = Mathf.CeilToInt(Mathf.Max(aSecond, 12f) * life.y) + 30;
            var emission = system.emission;
            emission.rateOverTime = aSecond;
            return system;
        }

        /// <summary>Lets a system go in bursts instead: this many at the start, then at each tenth of a second after it. Once each time it is played.</summary>
        private static void Once(ParticleSystem system, float seconds, params int[] counts)
        {
            var main = system.main;
            main.loop = false;
            main.duration = seconds;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            var bursts = new ParticleSystem.Burst[counts.Length];
            for (int i = 0; i < counts.Length; i++) bursts[i] = new ParticleSystem.Burst(i * 0.1f, (short)counts[i]);
            emission.SetBursts(bursts);
        }

        /// <summary>Starts a system on a level ring about the effect's middle, this wide and this far up.</summary>
        private static void Ring(ParticleSystem system, float radius, float up)
        {
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.15f;
            shape.arc = 360f;
            shape.position = new Vector3(0f, up, 0f);
            shape.rotation = new Vector3(90f, 0f, 0f); // the circle lies across z: laid flat
            shape.scale = Vector3.one;
        }

        /// <summary>Carries a system's particles up (or down) at this speed while they turn about the upright axis and move out from it (or in).</summary>
        private static void Wind(ParticleSystem system, Vector2 rise, float turn, float outward)
        {
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise.x, rise.y);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(turn);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            velocity.radial = new ParticleSystem.MinMaxCurve(outward);
        }

        /// <summary>Water shards as the tide wave shatters into: one burst of solid flakes in the water's colours, which tumble, fall and shrink away.</summary>
        private static ParticleSystem Shards(GameObject root, string name, Mesh shard, Material flat, int count, Vector2 speed, float gravity)
        {
            ParticleSystem drops = FlatSystem(root, name, shard, flat, root.transform);
            var main = drops.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(WaterColour, FoamColour);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.maxParticles = count + 4;
            var emission = drops.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var size = drops.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.15f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
            return drops;
        }
    }
}
