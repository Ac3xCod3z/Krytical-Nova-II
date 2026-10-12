using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Foxfire: the effects of the Kitsune Spirit's later forms (50, 75 and 100 %) and of the animations they gained
    /// (FoxfireFlick, EmberWaltz, Kindle, NinefoldBlaze, SpiritStep), and the fire the two largest forms stand on
    /// instead of clouds. Made from the same three sources as the other pets' effects: the game's ribbon shader in
    /// the fox's fire colours and the energy shader built on All In 1 Shader Nodes, POLYGON Particle FX (flame,
    /// embers, the puffs under its paws), and a Cartoon FX word. PetFollower plays each at the moments or over the
    /// stretch its PetEffect names. Sizes are in footprints (1 = as far as the pet is wide).
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string FoxfirePawsPath = VfxFolder + "/PFX_Pet_FoxfirePaws.prefab";
        public const string FoxfireWispPath = VfxFolder + "/PFX_Pet_FoxfireWisp.prefab";
        public const string FoxfireBurstPath = VfxFolder + "/PFX_Pet_FoxfireBurst.prefab";
        public const string FoxfireFlamePath = VfxFolder + "/PFX_Pet_FoxfireFlame.prefab";
        public const string FoxfireTuftPath = VfxFolder + "/PFX_Pet_FoxfireTuft.prefab";
        public const string FoxfirePuffPath = VfxFolder + "/PFX_Pet_FoxfirePuff.prefab";
        public const string FoxfirePillarPath = VfxFolder + "/PFX_Pet_FoxfirePillar.prefab";
        public const string FoxfireShockPath = VfxFolder + "/PFX_Pet_FoxfireShock.prefab";
        public const string FoxfireEmbersPath = VfxFolder + "/PFX_Pet_FoxfireEmbers.prefab";
        public const string PetBlazeWordPath = ComicFolder + "/PFX_Word_PetBlaze.prefab";

        /// <summary>Builds only the foxfire effects. The spirit effects they share materials with must be there (Rebuild Kitsune and Tidecaller Move Effects).</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Kitsune Foxfire Effects")]
        public static void BuildFoxfireEffects()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            WeatherVfxBuilder.EnsureFolder(PetMeshFolder);
            var energy = AssetDatabase.LoadAssetAtPath<Shader>(DroneEnergyShaderPath);
            var fire = AssetDatabase.LoadAssetAtPath<Material>(SpiritFlowMaterialPath);
            if (energy == null || fire == null)
            {
                Debug.LogError("[GameFeel] The energy shader or the spirit fire material is missing: run Rebuild Kitsune and Tidecaller Move Effects first.");
                return;
            }
            Mesh disc = SaveUvMesh(DroneDiscMeshPath, DiscMesh("SM_FX_DroneDisc"));
            Mesh shard = SaveMesh(ShardMeshPath, ShardMesh());
            Mesh star = SaveMesh(SpiritStarMeshPath, StarMesh("SM_FX_SpiritStar", 4, 0.5f, 0.17f, Color.white, 0f));
            Material flat = WeatherVfxBuilder.OpaqueParticleMaterial("M_ComicPuff", Color.white);
            Material ring = Energy(energy, "M_FX_SpiritRing", "RING", null); // the fox's own ring, as it is

            // The fire is the Cartoon FX Remaster pack's own (CFXR Fire: flame shapes with a hot heart, and embers),
            // with its smoke, its light and its script taken off, made over for each place the fox wears it. Sizes
            // are in footprints (1 = as far as the pet is wide: 0.95 m on the 75 % fox, 1.2 m on the 100 %), and every
            // pet effect that wears one wears it at size 1:
            //   on a body tuft   a flame about 0.2 tall that stands where the tuft leaves the body and is drawn back
            //                    behind the fox as it rises, with a few embers
            //   under a paw      a low fire about 0.1 tall, short-lived so it stays under the paw: the fire the paw
            //                    stands in, no taller than the paw
            //   on a tail tip    a candle of it, 0.12 tall, no embers
            //   thrown           the same fire let go along the way of something thrown: a flame that flies
            const string packFire = CartoonRemaster + "Fire/CFXR Fire.prefab";

            // The fire of one of the 100 % form's six tufts, worn all the time where the tuft leaves the body.
            WeatherVfxBuilder.Build(packFire, FoxfireTuftPath, root => PackFire(root, 0.14f, embers: 3f, back: 0.9f, life: 1f, prewarm: true));

            // Under the paws of the two largest forms, in place of the cloud and worn the same way (one under each paw).
            WeatherVfxBuilder.Build(packFire, FoxfirePawsPath, root => PackFire(root, 0.085f, embers: 1.5f, back: 0.5f, life: 0.75f, prewarm: true));

            // The flame it kindles on a tail tip, for as long as it plays.
            WeatherVfxBuilder.Build(packFire, FoxfireFlamePath, root => PackFire(root, 0.075f, embers: 0f, back: 0f, life: 1f, prewarm: false));

            // A flame thrown ahead from a tail tip: something that flies ahead and lets the pack's fire go all along
            // its way, so the fire itself flies and its flames are left behind it. Once each time it is played.
            var wispRoot = new GameObject("PFX_Pet_FoxfireWisp");
            try
            {
                var carrier = wispRoot.AddComponent<ParticleSystem>();
                var main = carrier.main;
                main.playOnAwake = true;
                main.loop = false;
                main.duration = 0.6f;
                main.startLifetime = 0.5f;
                main.startSpeed = 2.2f;
                main.startSize = 0.01f;
                main.gravityModifier = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = 2;
                var emission = carrier.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var shape = carrier.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 0f;
                shape.radius = 0.001f;
                wispRoot.GetComponent<ParticleSystemRenderer>().enabled = false; // what flies is not seen: the fire is

                var thrownFire = (GameObject)UnityEngine.Object.Instantiate(Load(packFire), wispRoot.transform);
                thrownFire.name = "Fire";
                PackFire(thrownFire, 0.07f, embers: 0f, back: 0f, life: 0.8f, prewarm: false);
                var flames = thrownFire.GetComponent<ParticleSystem>();
                var flamesMain = flames.main;
                flamesMain.loop = false;
                flamesMain.playOnAwake = false;
                flamesMain.simulationSpace = ParticleSystemSimulationSpace.World;
                var flamesEmission = flames.emission;
                flamesEmission.rateOverTime = 34f;
                var let = carrier.subEmitters;
                let.enabled = true;
                let.AddSubEmitter(flames, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
                SavePrefab(wispRoot, FoxfireWispPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(wispRoot); }

            // A ring of fire let go: ribbons of fire and sparks thrown outwards, level, from round the pet. Once.
            var burstRoot = new GameObject("PFX_Pet_FoxfireBurst");
            try
            {
                ParticleSystem ribbons = Winding(burstRoot, "Ribbons", fire, 0.13f, 0f, new Vector2(0.4f, 0.55f));
                Once(ribbons, 0.5f, 14);
                Ring(ribbons, 0.5f, 0f);
                var main = ribbons.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 3.2f);
                Slowed(ribbons, 0.1f, 0.4f);
                ParticleSystem sparks = Shards(burstRoot, "Sparks", shard, flat, 18, new Vector2(2f, 3.2f), 0.35f);
                Embers(sparks);
                Ring(sparks, 0.5f, 0f);
                SavePrefab(burstRoot, FoxfireBurstPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(burstRoot); }

            // A puff of breath: a few sparks blown ahead of where it is worn (the muzzle). Once.
            var puffRoot = new GameObject("PFX_Pet_FoxfirePuff");
            try
            {
                ParticleSystem sparks = Shards(puffRoot, "Sparks", shard, flat, 7, new Vector2(0.8f, 1.5f), 0.15f);
                Embers(sparks);
                var main = sparks.main;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                var cone = sparks.shape;
                cone.enabled = true;
                cone.shapeType = ParticleSystemShapeType.Cone;
                cone.angle = 16f;
                cone.radius = 0.02f;
                SavePrefab(puffRoot, FoxfirePuffPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(puffRoot); }

            // The pillar of the ninefold blaze: ribbons of fire that wind straight up from the ground round the pet,
            // with sparks thrown up through them. For as long as it plays.
            var pillarRoot = new GameObject("PFX_Pet_FoxfirePillar");
            try
            {
                ParticleSystem column = Winding(pillarRoot, "Column", fire, 0.2f, 26f, new Vector2(0.55f, 0.8f));
                Ring(column, 0.34f, 0f);
                Wind(column, new Vector2(3.4f, 4.8f), 3.5f, 0.12f);
                ParticleSystem sparks = Shards(pillarRoot, "Sparks", shard, flat, 0, new Vector2(2.5f, 4.5f), 0.4f);
                Embers(sparks);
                var main = sparks.main;
                main.loop = true;
                main.duration = 1f;
                main.maxParticles = 40;
                var emission = sparks.emission;
                emission.SetBursts(new ParticleSystem.Burst[0]);
                emission.rateOverTime = 22f;
                var cone = sparks.shape;
                cone.enabled = true;
                cone.shapeType = ParticleSystemShapeType.Cone;
                cone.angle = 9f;
                cone.radius = 0.3f;
                cone.rotation = new Vector3(-90f, 0f, 0f);
                SavePrefab(pillarRoot, FoxfirePillarPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(pillarRoot); }

            // The ring that leaves along the ground under the pillar. Once.
            var shockRoot = new GameObject("PFX_Pet_FoxfireShock");
            try
            {
                ParticleSystem ground = Pop(shockRoot, "Ground", disc, ring, 0.6f, 3.6f, billboard: false);
                ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ground.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                SavePrefab(shockRoot, FoxfireShockPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(shockRoot); }

            // Embers that drift about and down, for as long as it plays. POLYGON's embers.
            Make("FX_Embers_01", "PFX_Pet_FoxfireEmbers", 1f, loop: true);

            // The word of the ninefold blaze, from the Cartoon FX pack like the game's hit words.
            WeatherVfxBuilder.EnsureFolder(ComicFolder);
            BuildWord(new Word("PFX_Word_PetBlaze", "Texts/Text 022", "Bangers", 0.26f, 1.5f, "BLAZE!"));
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Makes the pack's fire over for a pet: its smoke, light and script are taken off; it is brought to this
        /// size (footprints, against the pack's own size); its embers are let go this many a second (0 = none);
        /// its flames are drawn back behind the pet at this speed as they rise (the pack's own units a second); and
        /// they live this share of the time the pack gave them (shorter = a lower fire).
        /// </summary>
        private static void PackFire(GameObject root, float size, float embers, float back, float life, bool prewarm)
        {
            StripCartoonRemaster(root);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == root.transform) continue;
                bool keep = child.name == "Ember" && embers > 0f;
                if (!keep) UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            root.transform.localScale = Vector3.one * size;
            root.transform.localRotation = Quaternion.identity;
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.loop = true;
                main.playOnAwake = true;
                main.prewarm = prewarm;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.stopAction = ParticleSystemStopAction.None;
                main.startLifetimeMultiplier *= life;
                bool isEmber = system.gameObject != root;
                if (isEmber)
                {
                    main.startDelay = 0f;
                    main.startSpeedMultiplier *= 0.4f;
                    var few = system.emission;
                    few.rateOverTime = embers;
                }
                if (back <= 0f) continue;
                // Drawn back behind the pet as they rise (the effect faces the way the pet faces).
                var velocity = system.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.z = new ParticleSystem.MinMaxCurve(-back, -back * 0.6f);
            }
        }

        /// <summary>Turns water shards into sparks: the fox's gold and orange, and smaller.</summary>
        private static void Embers(ParticleSystem sparks)
        {
            var main = sparks.main;
            main.startColor = new ParticleSystem.MinMaxGradient(SpiritGold, SpiritOrange);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
        }
    }
}
