using System;
using System.Collections.Generic;
using System.Linq;
using KryticalNova.GameFeel;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the GameFeel system (see GameFeelPresenter): first-party copies of the effects it spawns (URP copies of
    /// POLYGON Particle FX with its cartoon puffs made solid, stripped copies of Cartoon FX impacts and comic text,
    /// the blade trail on an All In 1 Shader Nodes slash material, the area-attack ground ring), the PFX_GameFeel
    /// prefab with one FEEL MMF_Player per moment plus the per-skill catalog, and the scene wiring those players need
    /// — a Cinemachine impulse listener + FOV shaker on the orbit camera, the Beautify vignette pulse + URP
    /// chromatic-aberration shaker on the global volume.
    /// Also swaps the player's hit VFX for the URP copy (the Synty original uses a legacy particle shader).
    /// Re-runnable: assets are rebuilt from the tables below, scene components are added only when missing. Vendor
    /// folders are never modified. Sound slots take their clips, volume and pitch range from the table in
    /// GameFeelBuilder.Sounds.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string PrefabFolder = "Assets/_Game/GameFeel/Prefabs";
        public const string PrefabPath = PrefabFolder + "/PFX_GameFeel.prefab";
        public const string VfxFolder = "Assets/_Game/VFX/GameFeel";
        private const string GameplayVfxFolder = "Assets/_Game/VFX/Gameplay";
        private const string MaterialFolder = "Assets/_Game/VFX/Materials";
        private const string ParticleFx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        private const string SwipeTexture = "Assets/Synty/PolygonParticleFX/Textures/PolygonParticles_Swipe_01.png";
        private const string RingTexture = "Assets/Synty/PolygonParticleFX/Textures/PolygonParticles_Ring_01.png";
        private const string DiscTexture = "Assets/Synty/PolygonParticleFX/Textures/PolygonParticles_Circle_01.png";
        /// <summary>How far out the ring and disc art reach on their quads (the rest is transparent margin).</summary>
        private const float GroundArtRadius = 0.95f;
        private const string AllIn1SlashMaterial = "Assets/Plugins/AllIn1ShaderNodes/Demo/Materials/DemoMaterials/MAT_Demo_VfxSlash.mat";
        private const string CartoonFx = "Assets/_ThirdParty/Cartoon FX Pack v2/Assets/Prefabs/";
        /// <summary>The smear package's URP shader. Linked from the smear settings, never edited.</summary>
        private const string SmearShaderPath = "Assets/_ThirdParty/smear effect/Demo_urp/shader/smear_urp.shadergraph";
        public const string SmearSettingsPath = "Assets/_Game/GameFeel/Data/Assets/SmearSettings.asset";
        private const string RunStartPlayer = "Run Start", DeathPlayer = "Death", CritDeathPlayer = "Critical Death";
        public const string PetChatterSettingsPath = "Assets/_Game/Gameplay/Pets/Data/Assets/PetChatterSettings.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string VolumeProfilePath = "Assets/_Game/Rendering/Settings/SampleSceneProfile.asset";
        private const string OrbitCameraName = "Player Orbit Camera";
        private const string SystemsHeader = "--- Systems ---";
        private const string BladeTrailName = "PFX_Blade_Trail";
        private const string AreaRingName = "PFX_Area_Ring";
        /// <summary>Matches GameFeelPresenter's Area Ring Grow Time: the ring is full size this far through its one second.</summary>
        private const float AreaRingGrowTime = 0.6f;

        // Colour Bible (Rendering-Bible.md §6b), shared with the skill icons.
        private static readonly Color Cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
        private static readonly Color Yellow = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        private static readonly Color Red = new Color32(0xF0, 0x3A, 0x2E, 0xFF); // rage
        // The hurt tint: a soft red wash from the corners with Ben-Day dots over it; the scene stays readable.
        private const float HurtVignetteSeconds = 0.6f;
        private const float HurtVignetteReach = 0.3f;
        private static readonly Color HurtVignetteColor = new Color(0.75f, 0.05f, 0.08f, 0.22f);
        private const float HurtVignetteDots = 0.5f;
        private static readonly Color Coral = new Color32(0xFF, 0x9E, 0x66, 0xFF);
        private static readonly Color Violet = new Color(0.62f, 0.4f, 1f);
        private static readonly Color Teal = new Color(0.35f, 0.95f, 0.78f);
        private static readonly Color HealGreen = new Color(0.55f, 1f, 0.6f);

        private static readonly Vector3 Chest = new Vector3(0f, 1.1f, 0f);
        private static readonly Vector3 Feet = new Vector3(0f, 0.05f, 0f);
        private static readonly Vector3 SlashOrigin = new Vector3(0f, 1f, 0.6f);

        private static string Vfx(string name) => $"{VfxFolder}/{name}.prefab";

        [MenuItem("Krytical Nova/GameFeel/Build Game Feel")]
        public static void BuildAll()
        {
            WeatherVfxBuilder.EnsureFolder(PrefabFolder);
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            BuildVfx();
            BuildMarks();
            foreach (string word in ComicWordNames()) BuildComicWord(word);
            BuildHitLookSettings();
            BuildPrefab();
            WireScene();
            UseUrpVfxOnPlayer();
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Built {PrefabPath}, VFX in {VfxFolder}, and wired the open scene. Assign sounds on the prefab's MMF_Sound slots.");
        }

        // ---------------------------------------------------------------- VFX

        private static void BuildVfx()
        {
            // Hits, clicks and selection.
            Make("FX_Sparks_01", "PFX_Hit_Sparks", 0.6f);
            Make("FX_Impact_Large_01", "PFX_Hit_Heavy", 0.45f);
            Make("FX_SwordStab_01", "PFX_Hit_SwordStab", 1f);
            Make("FX_Cartoony_Rings_01", "PFX_Click_Ring", 0.35f, new Color(0.55f, 0.95f, 1f));
            Make("FX_Cartoony_Rings_01", "PFX_Select_Hostile", 0.55f, new Color(1f, 0.35f, 0.3f));
            Make("FX_Sparkles_Small_01", "PFX_Select_Friendly", 0.6f, new Color(1f, 0.85f, 0.45f));
            BuildComicVfx();

            // Locomotion: POLYGON's cartoon puffs (its "dust" effects are ambient motes, far too faint for a burst).
            Make("FX_Cartoony_Jump_01", "PFX_Dust_Run", 0.6f);        // kick-off ring of puffs
            Make("FX_Cartoony_Jump_01", "PFX_Dust_Jump", 0.85f);
            Make("FX_Cartoony_Jump_01", "PFX_Dust_Land", 1.2f);
            Make("FX_Cartoony_Jump_Up_01", "PFX_Jump_Burst", 1.15f);
            Make("FX_Cartoony_Rings_01", "PFX_Land_Ring", 1.1f, then: GroundRings);
            Make("FX_Cartoony_Sprint_01", "PFX_Run_Loop", 0.85f, loop: true);
            Make("FX_Trail_Dust_01", "PFX_Air_Loop", 0.6f, loop: true);

            // Skill casts.
            Make("FX_SwordSlash_01", "PFX_Cast_Slash", 1f);
            Make("FX_MagicBlast_02", "PFX_Cast_Buff", 0.8f, Yellow);
            Make("FX_LevelUp_01", "PFX_Cast_PowerUp", 0.7f);
            Make("FX_LightningStrike_01", "PFX_Cast_Thunder", 0.8f);
            Make("FX_GroundCrack_Blast_01", "PFX_Cast_GroundCrack", 0.7f);
            Make("FX_Fire_Explosion_01", "PFX_Cast_Fire", 0.5f);
            Make("FX_Sparkle_Orbit_01", "PFX_Buff_End", 0.6f);
            Make("FX_Heal_01", "PFX_Heal", 0.8f, HealGreen);
            Make("FX_Heal_01", "PFX_Chi", 0.8f, Cyan);

            foreach (AuraRecipe aura in Auras) Make(aura);

            // Held charges: energy drawn inward at the chest, a fast swirl at the feet.
            Make("FX_PowerDraw_01", "PFX_Charge_Default", 0.55f, Yellow, loop: true);
            BuildRageCharge();

            BuildDeathVfx();
            BuildPetVfx();
            BuildForgeVfx();
            BuildNovaVfx();
            BuildAreaRing();
            BuildBladeTrail();
            UseUrpMaterials($"{GameplayVfxFolder}/FX_ComicSlash.prefab");
        }

        /// <summary>
        /// What the comic layer needs that is not a mark or a word, and the end of the seven comic effects every
        /// context used to share (a hit, a skill, a death and the forge all showed the same POW and WHAM): each
        /// moment has its own now, in <see cref="ComicFolder"/>.
        /// </summary>
        private static void BuildComicVfx()
        {
            Make("FX_ExperienceGain_01", "PFX_LevelUp", 0.7f); // a level gained: light rising through the character
            foreach (string old in new[] { "PFX_Comic_Hit", "PFX_Comic_HitHeavy", "PFX_Comic_Slash", "PFX_Comic_Pow", "PFX_Comic_Wham",
                         "PFX_Comic_Zap", "PFX_Comic_SlashText" })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Vfx(old)) != null) AssetDatabase.DeleteAsset(Vfx(old));
        }

        // ---------------------------------------------------------------- death & respawn

        private const string DeathTrailName = "PFX_CritDeath_Trail";
        /// <summary>The longest chromatic punch of the death players: the kill confirm's own wave starts as it ends.</summary>
        private const float KillAftershockDelay = 0.4f;
        // A pale gold wash from the corners: the opposite of the red hurt tint.
        private static readonly Color KillFlashColor = new Color(1f, 0.86f, 0.45f, 0.18f);
        private static readonly string[] DeathCameraPlayers = { "Death", "Critical Death", "Kill Confirm", "Body Land", "Respawn" };
        private static readonly string[] DeathWorldPlayers = { "Death", "Critical Death", "Body Land", "Critical Body Land", "Respawn" };

        /// <summary>Effects of a death of their own: the blow that kills, the thrown body's trail and crater, the return.</summary>
        private static void BuildDeathVfx()
        {
            Make("FX_Explosion_Body_01", "PFX_Death_Burst", 0.5f);                 // the life knocked out of the body
            Make("FX_StarStunned_01", "PFX_Death_Stars", 0.8f);                    // stars over the body once it is down
            Make("FX_Explosion_Body_01", "PFX_CritDeath_Blast", 0.9f, Red);        // a critical kill: bigger, in rage red
            Make("FX_Smoke_Trail_Small_01", DeathTrailName, 0.8f, loop: true);     // rides on the thrown body
            Make("FX_GroundCrack_Blast_01", "PFX_CritDeath_Crater", 0.5f);         // where the thrown body comes down
            Make("FX_Cartoony_Rings_01", "PFX_CritKill_Shockwave", 1.5f, Yellow, then: GroundRings); // the killing blow, on the ground
            Make("FX_LightRayRound_01", "PFX_Respawn", 0.8f, Cyan);                // back on a spawn point
        }

        /// <summary>
        /// Rebuilds the death and respawn effects and their players on the game feel prefab without regenerating the
        /// rest of it (a full build rewrites every effect and the whole prefab, tuning included).
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Death Feel")]
        public static void RebuildDeathFeel()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            BuildDeathVfx();
            AssetDatabase.SaveAssets();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                GameObject root = scope.prefabContentsRoot;
                var presenter = root.GetComponent<GameFeelPresenter>();
                Transform world = root.transform.Find("World");
                if (presenter == null || world == null)
                {
                    Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first.");
                    return;
                }
                Remove(root.transform, DeathCameraPlayers);
                Remove(world, DeathWorldPlayers);

                var so = new SerializedObject(presenter);
                AddDeathFeel(root, world.gameObject, so);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            WireScene(); // the kill confirm's lens punch needs its shaker on the global volume
            Debug.Log($"[GameFeel] Death and respawn feel rebuilt on {PrefabPath}. Assign sounds on the new MMF_Sound slots.");
        }

        private static void Remove(Transform parent, string[] childNames)
        {
            foreach (string name in childNames)
            {
                Transform child = parent.Find(name);
                if (child != null) Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void AddDeathFeel(GameObject root, GameObject world, SerializedObject so)
        {
            // ---- camera / screen / sound: the local player died, or killed
            so.FindProperty("deathFeel").objectReferenceValue = Player(root, "Death", p =>
            {
                Sound(p, "Death");
                Impulse(p, new Vector3(0.4f, 0.28f, 0f), 0.32f);
                Chromatic(p, "Chromatic Punch", 0.25f, 0.6f);
            });
            so.FindProperty("critDeathFeel").objectReferenceValue = Player(root, "Critical Death", p =>
            {
                Sound(p, "Critical Death");
                Impulse(p, new Vector3(0.9f, 0.55f, 0f), 0.45f);
                Chromatic(p, "Chromatic Slam", 0.4f, 1f);
                // The view punches out as the body is launched and settles while it flies.
                var fov = p.AddFeedback(typeof(MMF_CameraFieldOfView)) as MMF_CameraFieldOfView;
                fov.Label = "FOV Punch";
                fov.Duration = 0.55f;
                fov.RelativeFieldOfView = true;
                fov.RemapFieldOfViewZero = 0f;
                fov.RemapFieldOfViewOne = 7f;
                fov.ShakeFieldOfView = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f));
            });
            // Only for the local player's own kills, on top of the two above: full strength on a critical kill.
            so.FindProperty("killConfirmFeel").objectReferenceValue = Player(root, "Kill Confirm", p =>
            {
                Sound(p, "Kill Confirm");
                // The presenter points this along the killing blow.
                Impulse(p, Vector3.zero, 0.3f);
                // Takes over as the death's own chromatic punch ends: a second, longer wave with an echo.
                var aftershock = p.AddFeedback(typeof(MMF_ChromaticAberration_URP)) as MMF_ChromaticAberration_URP;
                aftershock.Label = "Chromatic Aftershock";
                aftershock.Timing.InitialDelay = KillAftershockDelay;
                aftershock.Duration = 0.7f;
                aftershock.RemapIntensityOne = 0.85f;
                aftershock.Intensity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(0.45f, 0.25f),
                    new Keyframe(0.6f, 0.7f), new Keyframe(1f, 0f));
                var lens = p.AddFeedback(typeof(MMF_LensDistortion_URP)) as MMF_LensDistortion_URP;
                lens.Label = "Lens Punch";
                lens.Duration = 0.3f;
                lens.RelativeIntensity = false;
                lens.RemapIntensityZero = 0f;
                lens.RemapIntensityOne = 0.3f;
                lens.Intensity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));
                var flash = p.AddFeedback(typeof(MMF_BeautifyVignette)) as MMF_BeautifyVignette;
                flash.Label = "Kill Flash (Beautify)";
                flash.Duration = 0.35f;
                flash.Reach = 0.2f;
                flash.PulseColor = KillFlashColor;
                flash.DotOpacity = 0.35f;
            });
            so.FindProperty("killStopSeconds").vector2Value = new Vector2(0.12f, 0.22f);
            so.FindProperty("bodyLandFeel").objectReferenceValue = Player(root, "Body Land", p =>
            {
                Sound(p, "Body Thud");
                Impulse(p, new Vector3(0f, -0.4f, 0f), 0.22f);
            });
            so.FindProperty("respawnFeel").objectReferenceValue = Player(root, "Respawn", p =>
            {
                Sound(p, "Respawn");
                Chromatic(p, "Chromatic Shimmer", 0.45f, 0.35f);
            });

            // ---- world particles: every client
            var overBody = new Vector3(0f, 0.7f, 0f);
            // The words are a death's own: K.O. for a death, WHAM! for a critical one, THUD! as a thrown body comes down.
            so.FindProperty("deathWorldFeel").objectReferenceValue = Smeared(World(world, DeathPlayer,
                new Fx("PFX_Death_Burst"), ComicAt("PFX_Mark_Death"), ComicAt("PFX_Word_KO", 0.9f)), SmearKind.DeathThrow);
            so.FindProperty("critDeathWorldFeel").objectReferenceValue = Smeared(World(world, CritDeathPlayer,
                new Fx("PFX_CritDeath_Blast"), ComicAt("PFX_Mark_Death"), ComicAt("PFX_Word_Wham", 0.9f),
                OnGround("PFX_CritKill_Shockwave")), SmearKind.DeathThrow);
            so.FindProperty("bodyLandWorldFeel").objectReferenceValue = World(world, "Body Land",
                Dust("PFX_Dust_Land"), new Fx("PFX_Death_Stars", overBody, follow: true));
            so.FindProperty("critBodyLandWorldFeel").objectReferenceValue = World(world, "Critical Body Land",
                OnGround("PFX_CritDeath_Crater"), Dust("PFX_Dust_Land"), Dust("PFX_Land_Ring"), ComicAt("PFX_Word_Thud", 0.9f),
                new Fx("PFX_Death_Stars", overBody, follow: true));
            so.FindProperty("respawnWorldFeel").objectReferenceValue = World(world, "Respawn",
                AtFeet("PFX_Respawn"), AtFeet("PFX_Cast_PowerUp"));

            so.FindProperty("critDeathTrailVfx").objectReferenceValue = Load(Vfx(DeathTrailName));
        }

        // ---------------------------------------------------------------- pets

        /// <summary>The sparkles that come off an evolved pet. White: PetFollower tints them with the stage's colour.</summary>
        public const string PetGlowPath = VfxFolder + "/PFX_Pet_Glow.prefab";
        /// <summary>The cloud under one foot of a pet that stands: small cartoon puffs that hold it up and drift away behind it.</summary>
        public const string PetCloudPath = VfxFolder + "/PFX_Pet_Cloud.prefab";
        /// <summary>The Emberwing's own effect: cinders shed from its wing tips and tail, left behind in the air.</summary>
        public const string PetCindersPath = VfxFolder + "/PFX_Pet_Cinders.prefab";
        /// <summary>The Drone Core's own effect: chips of data dropped behind it, which blink and go out.</summary>
        public const string PetCrumbsPath = VfxFolder + "/PFX_Pet_Crumbs.prefab";
        private const string OldPetAuraPath = VfxFolder + "/PFX_Pet_Aura.prefab"; // orbiting ribbons, replaced 6 Oct 2026
        private static readonly string[] PetCameraPlayers = { "Pet Evolved" };
        private static readonly string[] PetWorldPlayers = { "Pet Evolved" };

        /// <summary>A pet reaching its next growth stage: a burst and rays on the pet, and the glow it wears afterwards.</summary>
        private static void BuildPetVfx()
        {
            Make("FX_LevelUp_01", "PFX_Pet_Evolve", 0.42f);
            Make("FX_LightRayRound_01", "PFX_Pet_EvolveRays", 0.45f, Yellow);
            Make("FX_Sparkles_Large_01", "PFX_Pet_EvolveSparkles", 0.5f);
            Make("FX_Sparkles_Small_01", "PFX_Pet_Glow", 1f, Color.white, loop: true, then: ShapePetGlow);
            BuildPetCloud();
            BuildPetEffects();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(OldPetAuraPath) != null) AssetDatabase.DeleteAsset(OldPetAuraPath);
        }

        /// <summary>Builds only the effects that are a pet's own mark; nothing else of the game feel is touched.</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Pet Effects")]
        public static void BuildPetEffects()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            Make("FX_Steam_01", "PFX_Pet_Cinders", 1f, loop: true, then: ShapePetCinders);
            Make("FX_Steam_01", "PFX_Pet_Crumbs", 1f, loop: true, then: ShapePetCrumbs);
            AssetDatabase.SaveAssets();
        }

        /// <summary>One system of solid comic particles, everything else of the source effect removed.</summary>
        private static ParticleSystem OneSolidSystem(GameObject root)
        {
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem keep = systems.Length > 0 ? systems[0] : null;
            foreach (var extra in systems)
                if (extra != keep && extra != null && extra.gameObject != root) UnityEngine.Object.DestroyImmediate(extra.gameObject);
            if (keep == null) return null;
            var renderer = keep.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) ComicPuff(keep, renderer);
            foreach (bool _ in new[] { true })
            {
                var force = keep.forceOverLifetime;
                force.enabled = false;
                var trails = keep.trails;
                trails.enabled = false;
                var sub = keep.subEmitters;
                sub.enabled = false;
                var limit = keep.limitVelocityOverLifetime;
                limit.enabled = false;
            }
            return keep;
        }

        /// <summary>
        /// The Emberwing's cinders: small flat flakes, solid and inked like the fox's puffs, shed where the effect is
        /// (PetFollower wears one on each wing tip and one on the tail) and left behind in the world. They tumble,
        /// rise a little as hot ash does, and shrink away within a second. A trickle while the bird hangs in the
        /// air, a stream when it flies: they are let go by the distance covered as well as by the clock.
        /// Sizes are for a footprint 1 m across, which PetFollower sizes to the pet (the bird is about 0.55 m): a
        /// flake comes out some 4 cm long. White: PetFollower tints them with the growth stage's colour.
        /// </summary>
        private static void ShapePetCinders(GameObject root)
        {
            ParticleSystem keep = OneSolidSystem(root);
            if (keep == null) return;

            var main = keep.main;
            main.loop = true;
            main.duration = 2f;
            main.playOnAwake = true;
            main.prewarm = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.42f, 0.64f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.1f, 0.15f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.26f, 0.38f);
            main.startColor = new Color(1f, 0.62f, 0.2f);
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // left where they were shed
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            main.maxParticles = 48;

            var emission = keep.emission;
            emission.enabled = true;
            emission.rateOverTime = 4f;
            emission.rateOverDistance = 3.5f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            var shape = keep.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;
            shape.radius = 0.04f;
            shape.radiusThickness = 1f;

            // Up, slowly: hot ash.
            var velocity = keep.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var rotation = keep.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-3.5f, 3.5f);
            rotation.y = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            rotation.z = new ParticleSystem.MinMaxCurve(-3.5f, 3.5f);
            var noise = keep.noise;
            noise.enabled = true;
            noise.separateAxes = false;
            noise.strength = 0.12f;
            noise.frequency = 2f;
            noise.scrollSpeed = 0.8f;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        /// <summary>
        /// The Drone Core's data chips: little solid cubes dropped one at a time behind the body while it moves, which
        /// hang where they were dropped, blink twice and go out. Nothing is dropped while it rests: they are let go
        /// by the distance covered only. Sizes are for a footprint 1 m across (the drone is about 0.25 m): a chip
        /// comes out some 2 cm. White: PetFollower tints them with the growth stage's colour.
        /// </summary>
        private static void ShapePetCrumbs(GameObject root)
        {
            ParticleSystem keep = OneSolidSystem(root);
            if (keep == null) return;
            var renderer = keep.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                renderer.alignment = ParticleSystemRenderSpace.World;
            }

            var main = keep.main;
            main.loop = true;
            main.duration = 2f;
            main.playOnAwake = true;
            main.prewarm = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSpeed = 0f;
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.13f); // the cube is a metre across, the puff 13 cm
            main.startColor = new Color(0.31f, 0.82f, 1f);
            main.startRotation3D = true;
            main.startRotationX = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 0.5f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // they hang where they were dropped
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            main.maxParticles = 30;

            var emission = keep.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 2.2f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            // Not in a dead straight line: each a little off the path.
            var shape = keep.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;
            shape.radius = 0.22f;
            shape.radiusThickness = 1f;

            var velocity = keep.velocityOverLifetime;
            velocity.enabled = false;
            var rotation = keep.rotationOverLifetime;
            rotation.enabled = false;
            var noise = keep.noise;
            noise.enabled = false;
            // It pops in whole, blinks twice near the end and goes out.
            var size = keep.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(0.08f, 1f), new Keyframe(0.56f, 1f), new Keyframe(0.62f, 0.25f),
                new Keyframe(0.7f, 1f), new Keyframe(0.8f, 0.25f), new Keyframe(0.88f, 1f), new Keyframe(1f, 0f)));
        }

        /// <summary>
        /// Soft sparkles born on the pet's body that drift out and up and fade: no orbit, no ribbons. One system, in
        /// world space so they stay behind when the pet moves. How many there are is set per stage at run time.
        /// </summary>
        private static void ShapePetGlow(GameObject root)
        {
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem keep = systems.Length > 0 ? systems[0] : null;
            foreach (var extra in systems)
                if (extra != keep && extra != null && extra.gameObject != root) UnityEngine.Object.DestroyImmediate(extra.gameObject);
            if (keep == null) return;

            var main = keep.main;
            main.loop = true;
            main.duration = 2f;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.18f);
            main.startColor = Color.white;
            main.gravityModifier = -0.04f; // they rise a little
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 60;

            var emission = keep.emission;
            emission.enabled = true;
            emission.rateOverTime = 8f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            var shape = keep.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.7f;
            shape.radiusThickness = 0.2f; // from the surface, not the middle

            var velocity = keep.velocityOverLifetime;
            velocity.enabled = false;
            var trails = keep.trails;
            trails.enabled = false;
            var sub = keep.subEmitters;
            sub.enabled = false;
            var noise = keep.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.6f;

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            var colour = keep.colorOverLifetime;
            colour.enabled = true;
            colour.color = fade;
            var size = keep.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));

            var renderer = keep.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.trailMaterial = null;
        }

        /// <summary>Builds only the cloud a standing pet has under each foot; nothing else of the game feel is touched.</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Pet Cloud")]
        public static void BuildPetCloud()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            Make("FX_Steam_01", "PFX_Pet_Cloud", 1f, loop: true, then: ShapePetCloud);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The cloud under one foot: a few small solid white cartoon puffs that swell up under the paw and drift
        /// slowly straight back from the pet, never down, sideways or forwards, with only a slight waver from noise,
        /// shrinking away as they go, as if they were what holds the pet in the air.
        /// PetFollower wears one under each foot, facing the way the pet faces (so -Z is behind it), and keeps the
        /// puffs in the pet's space, so speeds here are in the pet's own units. Sizes are for a footprint 1 m long,
        /// which PetFollower sizes to the pet; the puff mesh is 13 cm across, hence the numbers: under the fox
        /// (0.55 m long) a puff is 3.5 to 6 cm and drifts some 8 cm in its two seconds.
        /// </summary>
        private static void ShapePetCloud(GameObject root)
        {
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem keep = systems.Length > 0 ? systems[0] : null;
            foreach (var extra in systems)
                if (extra != keep && extra != null && extra.gameObject != root) UnityEngine.Object.DestroyImmediate(extra.gameObject);
            if (keep == null) return;
            var renderer = keep.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) ComicPuff(keep, renderer);

            var main = keep.main;
            main.loop = true;
            main.duration = 2f;
            main.playOnAwake = true;
            main.prewarm = true; // the cloud is there when the pet comes out
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.7f, 2.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            main.startColor = Color.white;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 16;

            var emission = keep.emission;
            emission.enabled = true;
            emission.rateOverTime = 5f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            // Born right under the paw.
            var shape = keep.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.position = new Vector3(0f, -0.015f, 0f);
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;
            shape.radius = 0.035f;
            shape.radiusThickness = 1f;

            // Straight back, slowly.
            var velocity = keep.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.06f, -0.04f);
            var force = keep.forceOverLifetime;
            force.enabled = false;
            var rotation = keep.rotationOverLifetime;
            rotation.enabled = false;
            var trails = keep.trails;
            trails.enabled = false;
            var sub = keep.subEmitters;
            sub.enabled = false;
            var limit = keep.limitVelocityOverLifetime;
            limit.enabled = false;
            var noise = keep.noise;
            noise.enabled = true;
            // A waver across and up and down only, far weaker than the drift: along the pet it would send puffs forwards.
            noise.separateAxes = true;
            noise.strengthX = 0.012f;
            noise.strengthY = 0.012f;
            noise.strengthZ = 0f;
            noise.frequency = 5f;
            noise.scrollSpeed = 1.2f;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        /// <summary>
        /// Rebuilds the pet evolution effects and their players on the game feel prefab without regenerating the rest
        /// of it (a full build rewrites every effect and the whole prefab, tuning included).
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Pet Feel")]
        public static void RebuildPetFeel()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            BuildPetVfx();
            AssetDatabase.SaveAssets();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                GameObject root = scope.prefabContentsRoot;
                var presenter = root.GetComponent<GameFeelPresenter>();
                Transform world = root.transform.Find("World");
                if (presenter == null || world == null)
                {
                    Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first.");
                    return;
                }
                Remove(root.transform, PetCameraPlayers);
                Remove(world, PetWorldPlayers);

                var so = new SerializedObject(presenter);
                AddPetFeel(root, world.gameObject, so);
                so.ApplyModifiedPropertiesWithoutUndo();
                AddPetChatter(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Pet evolution feel rebuilt on {PrefabPath}. Assign a sound on the new MMF_Sound slot.");
        }

        /// <summary>Puts the pet's reactions on the game feel prefab without regenerating anything else of it.</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Pet Chatter")]
        public static void RebuildPetChatter()
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath)) AddPetChatter(scope.prefabContentsRoot);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Pet chatter is on {PrefabPath}. How often a pet speaks: {PetChatterSettingsPath}.");
        }

        /// <summary>
        /// The pet's reactions to what its owner does (<see cref="PetChatterDirector"/>): on the game feel prefab,
        /// with the settings asset that says how often a pet may speak (made with its defaults when missing).
        /// </summary>
        private static void AddPetChatter(GameObject root)
        {
            var settings = AssetDatabase.LoadAssetAtPath<KryticalNova.Pets.PetChatterSettings>(PetChatterSettingsPath);
            if (settings == null)
            {
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(PetChatterSettingsPath).Replace('\\', '/'));
                settings = ScriptableObject.CreateInstance<KryticalNova.Pets.PetChatterSettings>();
                AssetDatabase.CreateAsset(settings, PetChatterSettingsPath);
            }
            if (!root.TryGetComponent(out PetChatterDirector director)) director = root.AddComponent<PetChatterDirector>();
            var so = new SerializedObject(director);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddPetFeel(GameObject root, GameObject world, SerializedObject so)
        {
            // ---- camera / screen / sound: the local player's own pet
            so.FindProperty("petEvolveFeel").objectReferenceValue = Player(root, "Pet Evolved", p =>
            {
                Sound(p, "Pet Evolved");
                Impulse(p, new Vector3(0f, 0.14f, 0f), 0.25f);
                Chromatic(p, "Chromatic Shimmer", 0.6f, 0.45f);
            });

            // ---- world particles: every client, riding on the pet
            so.FindProperty("petEvolveWorldFeel").objectReferenceValue = World(world, "Pet Evolved",
                new Fx("PFX_Pet_Evolve", Vector3.zero, follow: true),
                new Fx("PFX_Pet_EvolveRays", Vector3.zero, follow: true),
                new Fx("PFX_Pet_EvolveSparkles", Vector3.zero, follow: true),
                ComicAt("PFX_Word_Evolved", 0.75f, ramped: true)); // in the colour of the stage it grew into
        }

        // ---------------------------------------------------------------- the forge

        private static readonly string[] ForgeCameraPlayers = { "Forge Success", "Forge Fail", "Forge Break" };
        private static readonly string[] ForgeWorldPlayers = { "Forge Success", "Forge Fail", "Forge Saved", "Forge Break" };
        private static readonly Color ForgeAsh = new Color(0.62f, 0.64f, 0.7f);

        /// <summary>An attempt at the forge: a success, a fail, the flash of a Failsafe holding, a piece breaking.</summary>
        private static void BuildForgeVfx()
        {
            Make("FX_LevelUp_01", "PFX_Forge_Success", 0.55f, Yellow);           // the work took: light rising round the character
            Make("FX_LightRayRound_01", "PFX_Forge_Rays", 0.6f, Yellow);
            Make("FX_Sparkles_Large_01", "PFX_Forge_Sparkles", 0.6f, Yellow);
            Make("FX_Sparks_01", "PFX_Forge_Fail", 0.75f, ForgeAsh);             // it did not: a dull spit of sparks
            Make("FX_MagicBlast_02", "PFX_Forge_Saved", 0.6f, Cyan);             // the Failsafe holding
            Make("FX_Explosion_Body_01", "PFX_Forge_Break", 0.6f, Red);          // the piece gone
        }

        /// <summary>
        /// Rebuilds the forge effects and their players on the game feel prefab without regenerating the rest of it
        /// (a full build rewrites every effect and the whole prefab, tuning included).
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Forge Feel")]
        public static void RebuildForgeFeel()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            BuildForgeVfx();
            AssetDatabase.SaveAssets();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                GameObject root = scope.prefabContentsRoot;
                var presenter = root.GetComponent<GameFeelPresenter>();
                Transform world = root.transform.Find("World");
                if (presenter == null || world == null)
                {
                    Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first.");
                    return;
                }
                Remove(root.transform, ForgeCameraPlayers);
                Remove(world, ForgeWorldPlayers);

                var so = new SerializedObject(presenter);
                AddForgeFeel(root, world.gameObject, so);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Forge feel rebuilt on {PrefabPath}. Assign sounds on the new MMF_Sound slots.");
        }

        private static void AddForgeFeel(GameObject root, GameObject world, SerializedObject so)
        {
            // ---- camera / screen / sound: the player whose piece it was
            so.FindProperty("forgeSuccessFeel").objectReferenceValue = Player(root, "Forge Success", p =>
            {
                Sound(p, "Forge Success");
                Impulse(p, new Vector3(0f, 0.14f, 0f), 0.24f);
                Chromatic(p, "Chromatic Shimmer", 0.45f, 0.4f);
            });
            so.FindProperty("forgeFailFeel").objectReferenceValue = Player(root, "Forge Fail", p =>
            {
                Sound(p, "Forge Fail");
                Impulse(p, new Vector3(0f, -0.14f, 0f), 0.18f);
            });
            so.FindProperty("forgeBreakFeel").objectReferenceValue = Player(root, "Forge Break", p =>
            {
                Sound(p, "Forge Break");
                Impulse(p, new Vector3(0.5f, 0.32f, 0f), 0.36f);
                Chromatic(p, "Chromatic Punch", 0.3f, 0.8f);
                var v = p.AddFeedback(typeof(MMF_BeautifyVignette)) as MMF_BeautifyVignette;
                v.Label = "Break Vignette (Beautify)";
                v.Duration = HurtVignetteSeconds;
                v.Reach = HurtVignetteReach;
                v.PulseColor = HurtVignetteColor;
                v.DotOpacity = HurtVignetteDots;
            });

            // ---- world particles: every client, riding on the character
            // The words are the forge's own: FORGED!, SAVED! when a Failsafe held, CRACK! when the piece broke.
            so.FindProperty("forgeSuccessWorldFeel").objectReferenceValue = World(world, "Forge Success",
                AtFeet("PFX_Forge_Success"), AtFeet("PFX_Forge_Rays"), OnCaster("PFX_Forge_Sparkles"), ComicAt("PFX_Word_Forged", 2.3f));
            so.FindProperty("forgeFailWorldFeel").objectReferenceValue = World(world, "Forge Fail",
                OnCaster("PFX_Forge_Fail"), Dust("PFX_Dust_Run"));
            so.FindProperty("forgeSavedWorldFeel").objectReferenceValue = World(world, "Forge Saved",
                OnCaster("PFX_Forge_Fail"), OnCaster("PFX_Forge_Saved"), ComicAt("PFX_Word_Saved", 2.3f));
            so.FindProperty("forgeBreakWorldFeel").objectReferenceValue = World(world, "Forge Break",
                OnCaster("PFX_Forge_Break"), ComicAt("PFX_Word_Crack", 2.3f));
        }

        // ---------------------------------------------------------------- the Herald: Ascension and Nova Standings

        private static readonly string[] NovaCameraPlayers = { "Nova Rise", "Nova Tier", "Nova Hold", "Nova Slip", "Nova Held", "Standing Rank" };
        private static readonly string[] NovaWorldPlayers =
            { "Nova Rise", "Nova Tier", "Nova Tier Ground", "Nova Hold", "Nova Slip", "Nova Held", "Standing Rank" };
        // The washes that come in from the corners. The presenter gives the first three the colour of the moment
        // (the Nova tier's, or the Standings'); what is set here is how strong each gets.
        private static readonly Color NovaRiseWash = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color NovaTierWash = new Color(1f, 1f, 1f, 0.3f);
        private static readonly Color StandingWash = new Color(1f, 0.8f, 0.34f, 0.2f);
        private static readonly Color NovaHeldWash = new Color(0.31f, 0.89f, 0.94f, 0.16f);
        private static readonly Color NovaSlipWash = new Color(0.75f, 0.05f, 0.08f, 0.2f);

        /// <summary>
        /// A moment at the Herald. The effects that take the Nova's colour are made white: the presenter tints them
        /// with the tier's colour as they spawn (MMF_PooledVfx.TintWithMoment), so one set serves all twelve tiers.
        /// </summary>
        private static void BuildNovaVfx()
        {
            Make("FX_Cartoony_Rings_01", "PFX_Nova_Ring", 0.9f, Color.white, then: GroundRings);       // a ring of light off the Nova
            Make("FX_Sparkles_Large_01", "PFX_Nova_Sparkles", 0.7f, Color.white);                      // sparks rising from it
            Make("FX_MagicBlast_02", "PFX_Nova_Burst", 0.55f, Color.white);                            // a new tier: the star bursts,
            Make("FX_LightRayRound_01", "PFX_Nova_Rays", 0.7f, Color.white);                           // light stands round the character,
            Make("FX_LevelUp_01", "PFX_Nova_Column", 0.7f, Color.white);                               // rises through it,
            Make("FX_Cartoony_Rings_01", "PFX_Nova_Shockwave", 1.9f, Color.white, then: GroundRings);  // and a ring runs out over the ground
            Make("FX_Sparks_01", "PFX_Nova_Dud", 0.45f, ForgeAsh);                                     // nothing changed: a dull spit
            Make("FX_ShardMagic_Explosion_01", "PFX_Nova_Shards", 0.13f, Red);                         // a level lost: the light breaks
            Make("FX_MagicBlast_02", "PFX_Nova_Held", 0.35f, Cyan);                                    // a slip the Nova held
            Make("FX_Ritual_Circle_01", "PFX_Standing_Seal", 0.55f, Color.white);                      // a Standing rank: a seal at the feet
        }

        /// <summary>
        /// Rebuilds the Ascension and Nova Standing effects and their players on the game feel prefab without
        /// regenerating the rest of it (a full build rewrites every effect and the whole prefab, tuning included).
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Nova Feel")]
        public static void RebuildNovaFeel()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            BuildNovaVfx();
            AssetDatabase.SaveAssets();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                GameObject root = scope.prefabContentsRoot;
                var presenter = root.GetComponent<GameFeelPresenter>();
                Transform world = root.transform.Find("World");
                if (presenter == null || world == null)
                {
                    Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first.");
                    return;
                }
                Remove(root.transform, NovaCameraPlayers);
                Remove(world, NovaWorldPlayers);

                var so = new SerializedObject(presenter);
                AddNovaFeel(root, world.gameObject, so);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Ascension and Standing feel rebuilt on {PrefabPath}. Assign sounds on the new MMF_Sound slots.");
        }

        private static void AddNovaFeel(GameObject root, GameObject world, SerializedObject so)
        {
            // ---- camera / screen / sound: the player whose moment it is
            so.FindProperty("novaRiseFeel").objectReferenceValue = Player(root, "Nova Rise", p =>
            {
                Sound(p, "Ascension Rise");
                Impulse(p, new Vector3(0f, 0.16f, 0f), 0.24f);
                Chromatic(p, "Chromatic Shimmer", 0.45f, 0.4f);
                Wash(p, "Rise Wash (Beautify)", 0.5f, 0.16f, NovaRiseWash, 0f);
            });
            // The big one: the view punches out and settles, the lens bulges, colour comes in from the corners.
            so.FindProperty("novaTierFeel").objectReferenceValue = Player(root, "Nova Tier", p =>
            {
                Sound(p, "Nova Tier");
                Impulse(p, new Vector3(0f, 0.5f, 0f), 0.42f);
                Chromatic(p, "Chromatic Slam", 0.4f, 0.9f);
                var aftershock = p.AddFeedback(typeof(MMF_ChromaticAberration_URP)) as MMF_ChromaticAberration_URP;
                aftershock.Label = "Chromatic Aftershock";
                aftershock.Timing.InitialDelay = 0.4f;
                aftershock.Duration = 0.7f;
                aftershock.RemapIntensityOne = 0.55f;
                aftershock.Intensity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(0.45f, 0.25f),
                    new Keyframe(0.6f, 0.7f), new Keyframe(1f, 0f));
                var fov = p.AddFeedback(typeof(MMF_CameraFieldOfView)) as MMF_CameraFieldOfView;
                fov.Label = "FOV Punch";
                fov.Duration = 0.7f;
                fov.RelativeFieldOfView = true;
                fov.RemapFieldOfViewZero = 0f;
                fov.RemapFieldOfViewOne = 6f;
                fov.ShakeFieldOfView = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f));
                var lens = p.AddFeedback(typeof(MMF_LensDistortion_URP)) as MMF_LensDistortion_URP;
                lens.Label = "Lens Punch";
                lens.Duration = 0.35f;
                lens.RelativeIntensity = false;
                lens.RemapIntensityZero = 0f;
                lens.RemapIntensityOne = 0.28f;
                lens.Intensity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));
                Wash(p, "Tier Wash (Beautify)", 1f, 0.34f, NovaTierWash, 0.4f);
            });
            so.FindProperty("novaHoldFeel").objectReferenceValue = Player(root, "Nova Hold", p =>
            {
                Sound(p, "Ascension Hold");
                Impulse(p, new Vector3(0f, -0.08f, 0f), 0.14f);
            });
            so.FindProperty("novaSlipFeel").objectReferenceValue = Player(root, "Nova Slip", p =>
            {
                Sound(p, "Ascension Slip");
                Impulse(p, new Vector3(0.3f, -0.25f, 0f), 0.3f);
                Chromatic(p, "Chromatic Punch", 0.3f, 0.6f);
                Wash(p, "Slip Wash (Beautify)", 0.5f, 0.24f, NovaSlipWash, 0.4f);
            });
            so.FindProperty("novaHeldFeel").objectReferenceValue = Player(root, "Nova Held", p =>
            {
                Sound(p, "Nova Held");
                Impulse(p, new Vector3(0f, -0.1f, 0f), 0.16f);
                Chromatic(p, "Chromatic Shimmer", 0.3f, 0.3f);
                Wash(p, "Held Wash (Beautify)", 0.45f, 0.16f, NovaHeldWash, 0f);
            });
            so.FindProperty("standingFeel").objectReferenceValue = Player(root, "Standing Rank", p =>
            {
                Sound(p, "Standing Rank");
                Impulse(p, new Vector3(0f, 0.2f, 0f), 0.28f);
                Chromatic(p, "Chromatic Shimmer", 0.5f, 0.4f);
                Wash(p, "Standing Wash (Beautify)", 0.6f, 0.2f, StandingWash, 0.2f);
            });

            // ---- world particles: every client. Those "at the Nova" are played at the star and ride with the character.
            so.FindProperty("novaRiseWorldFeel").objectReferenceValue = World(world, "Nova Rise",
                AtNova("PFX_Nova_Ring"), AtNova("PFX_Nova_Sparkles"));
            so.FindProperty("novaTierWorldFeel").objectReferenceValue = World(world, "Nova Tier",
                AtNova("PFX_Nova_Burst"), AtNova("PFX_Nova_Ring"), AtNova("PFX_Nova_Sparkles"));
            so.FindProperty("novaTierGroundWorldFeel").objectReferenceValue = World(world, "Nova Tier Ground",
                new Fx("PFX_Nova_Column", Feet, follow: true, tint: true), new Fx("PFX_Nova_Rays", Feet, follow: true, tint: true),
                new Fx("PFX_Nova_Shockwave", ground: true, tint: true));
            so.FindProperty("novaHoldWorldFeel").objectReferenceValue = World(world, "Nova Hold",
                new Fx("PFX_Nova_Dud", follow: true));
            so.FindProperty("novaSlipWorldFeel").objectReferenceValue = World(world, "Nova Slip",
                new Fx("PFX_Nova_Shards", follow: true), new Fx("PFX_Nova_Dud", follow: true));
            so.FindProperty("novaHeldWorldFeel").objectReferenceValue = World(world, "Nova Held",
                new Fx("PFX_Nova_Held", follow: true));
            so.FindProperty("standingWorldFeel").objectReferenceValue = World(world, "Standing Rank",
                new Fx("PFX_Standing_Seal", ground: true, tint: true), new Fx("PFX_Nova_Burst", Feet, follow: true, tint: true),
                new Fx("PFX_Nova_Rays", Feet, follow: true, tint: true), new Fx("PFX_Nova_Sparkles", Chest, follow: true, tint: true));
        }

        /// <summary>An effect at the Nova itself (the player is played there), riding with the character, in the tier's colour.</summary>
        private static Fx AtNova(string prefab) => new Fx(prefab, follow: true, tint: true);

        /// <summary>A soft wash of colour in from the corners of the screen (Beautify's vignette), with or without comic dots.</summary>
        private static void Wash(MMF_Player player, string label, float seconds, float reach, Color colour, float dots)
        {
            var wash = player.AddFeedback(typeof(MMF_BeautifyVignette)) as MMF_BeautifyVignette;
            wash.Label = label;
            wash.Duration = seconds;
            wash.Reach = reach;
            wash.PulseColor = colour;
            wash.DotOpacity = dots;
        }

        /// <summary>A buff aura: a looping POLYGON effect that rides on the character for as long as the buff lasts.</summary>
        private readonly struct AuraRecipe
        {
            public readonly string Source, Name;
            public readonly float Scale;
            /// <summary>Null keeps the effect exactly as POLYGON made it: its own colours and its full particle counts.</summary>
            public readonly Color? Tint;
            public readonly Action<GameObject> Then;
            public AuraRecipe(string source, string name, float scale, Color? tint, Action<GameObject> then = null)
            {
                Source = source; Name = name; Scale = scale; Tint = tint; Then = then;
            }
        }

        private static readonly AuraRecipe[] Auras =
        {
            new AuraRecipe("FX_GlowSpot_01", "PFX_Aura_Default", 0.8f, Yellow),
            new AuraRecipe("FX_Healing_Cirle_02", "PFX_Aura_AzureWall", 0.8f, Cyan),          // ring + column: the wall
            new AuraRecipe("FX_Electricity_01", "PFX_Aura_LightningFlash", 0.3f, Cyan),
            // Rage held ready: a small flame over the head until it is spent, embers rising off it.
            new AuraRecipe("FX_Fire_Small_01", "PFX_Aura_SpiritualRage", RageFlameScale, null, AddRageEmbers),
            new AuraRecipe("FX_Healing_Cirle_02", "PFX_Aura_Serenity", 1f, null),             // a circle to meditate in, as POLYGON made it
            new AuraRecipe("FX_Healing_Cirle_02", "PFX_Aura_Shield", 1.05f, Yellow),          // a wider golden barrier
            new AuraRecipe("FX_Swirl_03", "PFX_Aura_Blow", 1f, null),                         // gathering force at the feet, as POLYGON made it
            new AuraRecipe("FX_Sparkles_Large_01", "PFX_Aura_Prayer", 0.8f, Yellow),
            new AuraRecipe("FX_Electricity_01", "PFX_Aura_Repulsion", 0.3f, Yellow),          // crackling charge: the hit thrown back
            new AuraRecipe("FX_Embers_01", "PFX_Aura_Destruction", 0.8f, Violet),
            new AuraRecipe("FX_Swirl_03", "PFX_Aura_ReverseFlow", 1.3f, Teal),
        };

        private static void Make(AuraRecipe aura)
        {
            WeatherVfxBuilder.Build(ParticleFx + aura.Source + ".prefab", Vfx(aura.Name), root =>
            {
                Configure(root, aura.Scale, aura.Tint, true, keepParticleCounts: !aura.Tint.HasValue);
                aura.Then?.Invoke(root);
            });
        }

        private const float RageFlameScale = 0.38f;
        private const float RageEmberScale = 0.55f; // relative to the flame they rise from
        private const float RageEmberSpread = 0.35f;
        private const float RageFlameLife = 0.45f;  // a short plume that stays at the head instead of a tall column

        /// <summary>Spiritual Rage's embers, pulled in to a tight plume so they read as heat coming off the flame.</summary>
        private static void AddRageEmbers(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startLifetimeMultiplier *= RageFlameLife;
            }
            var source = Load(ParticleFx + "FX_Embers_01.prefab");
            if (source == null) return;
            var embers = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(embers, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            embers.name = "Embers";
            Loop(embers, RageEmberScale, null);
            foreach (var ps in embers.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startLifetimeMultiplier *= RageFlameLife;
            }
            embers.transform.SetParent(root.transform, false);
            embers.transform.localPosition = Vector3.zero;
            foreach (var ps in embers.GetComponentsInChildren<ParticleSystem>(true))
            {
                var shape = ps.shape;
                shape.radius *= RageEmberSpread;
                shape.scale *= RageEmberSpread;
            }
        }

        /// <summary>Spiritual Rage gathering while the key is held: red energy drawn in at the chest, a red swirl at the feet.</summary>
        private static void BuildRageCharge()
        {
            Make("FX_PowerDraw_01", "PFX_Charge_SpiritualRage", 0.55f, Red, loop: true);
            Make("FX_Swirl_Fast_01", "PFX_Charge_Ground", 0.7f, Red, loop: true);
        }

        /// <summary>
        /// Rebuilds chosen buff auras and points their skills at them, without regenerating the rest of the game feel
        /// (a full build rewrites every effect and the whole prefab, tuning included). No menu item: call it with the
        /// ids of the skills whose aura recipe changed, e.g. <c>RebuildSkillAuras("SpiritualRage")</c>.
        /// </summary>
        public static void RebuildSkillAuras(params string[] skillIds)
        {
            var looks = new List<SkillLook>();
            foreach (string id in skillIds)
            {
                int i = Array.FindIndex(Skills, s => s.Id == id);
                if (i < 0 || Skills[i].Aura == null) { Debug.LogWarning($"[GameFeel] No aura look for skill '{id}'."); continue; }
                int a = Array.FindIndex(Auras, r => r.Name == Skills[i].Aura);
                if (a < 0) { Debug.LogWarning($"[GameFeel] No aura recipe named '{Skills[i].Aura}'."); continue; }
                Make(Auras[a]);
                looks.Add(Skills[i]);
            }
            AssetDatabase.SaveAssets();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                var catalog = scope.prefabContentsRoot.GetComponentInChildren<SkillFeelCatalog>(true);
                if (catalog == null) { Debug.LogError($"[GameFeel] {PrefabPath} has no SkillFeelCatalog."); return; }
                var so = new SerializedObject(catalog);
                var list = so.FindProperty("skills");
                foreach (SkillLook look in looks)
                {
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var entry = list.GetArrayElementAtIndex(i);
                        if (entry.FindPropertyRelative("skillId").stringValue != look.Id) continue;
                        entry.FindPropertyRelative("aura").objectReferenceValue = Load(Vfx(look.Aura));
                        entry.FindPropertyRelative("auraAnchor").enumValueIndex = (int)look.Anchor;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Rebuilt {looks.Count} skill aura(s): {string.Join(", ", skillIds)}.");
        }

        /// <summary>
        /// Swaps a prefab's legacy particle materials for URP copies in place, keeping everything else about it (the
        /// basic combo's slash is a hand-tuned variant of the Synty prefab and still pointed at the legacy shaders).
        /// </summary>
        private static void UseUrpMaterials(string prefabPath)
        {
            if (Load(prefabPath) == null) return;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                foreach (var renderer in scope.prefabContentsRoot.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    renderer.sharedMaterial = WeatherVfxBuilder.ToUrp(renderer.sharedMaterial);
                    if (renderer.trailMaterial != null) renderer.trailMaterial = WeatherVfxBuilder.ToUrp(renderer.trailMaterial);
                }
            }
        }

        /// <summary>URP copy of a POLYGON effect, scaled, optionally re-tinted; one-shot unless <paramref name="loop"/>.</summary>
        private static void Make(string source, string name, float scale, Color? tint = null, bool loop = false, Action<GameObject> then = null)
        {
            WeatherVfxBuilder.Build(ParticleFx + source + ".prefab", Vfx(name), root =>
            {
                if (loop) Loop(root, scale, tint);
                else OneShot(root, scale, tint);
                then?.Invoke(root);
            });
        }

        private static void OneShot(GameObject root, float scale, Color? tint) => Configure(root, scale, tint, false);

        /// <summary>Loops ride on a character until released, so they stay light and never stop by themselves.</summary>
        private static void Loop(GameObject root, float scale, Color? tint) => Configure(root, scale, tint, true);

        private static void Configure(GameObject root, float scale, Color? tint, bool loop, bool keepParticleCounts = false)
        {
            root.transform.localScale = Vector3.one * scale;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = loop;
                main.playOnAwake = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (!keepParticleCounts) main.maxParticles = Mathf.Min(main.maxParticles, loop ? 60 : 120);
                main.stopAction = ParticleSystemStopAction.None; // the VFX pool recycles it
                // Loops ride on a character whose kinematic Rigidbody never reports a velocity: emit by the distance
                // the transform actually covers.
                if (loop) main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh != null
                    && renderer.mesh.name.StartsWith("FX_Sphere_", StringComparison.Ordinal))
                    ComicPuff(ps, renderer);
            }
            if (tint.HasValue) Recolor(root, tint.Value);
        }

        /// <summary>
        /// POLYGON's cartoon puffs are sphere meshes drawn with Unity's Default-Particle material. Every vertex of the
        /// mesh maps to a single point in the soft rim of that material's dot texture, so in URP the puffs come out
        /// almost transparent. They get an opaque flat material instead: solid cartoon clouds that write depth, so the
        /// comic outline pass inks them, coloured through the particles (see GroundSurfaces). Opaque puffs cannot
        /// fade, so they pop in and shrink away.
        /// </summary>
        private static void ComicPuff(ParticleSystem ps, ParticleSystemRenderer renderer)
        {
            renderer.sharedMaterial = WeatherVfxBuilder.OpaqueParticleMaterial("M_ComicPuff", Color.white);
            var main = ps.main;
            main.startColor = DustPalette.Default.paving; // re-tinted at run time for the ground it comes off
            var colour = ps.colorOverLifetime;
            colour.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f), new Keyframe(0.55f, 0.8f), new Keyframe(1f, 0f)));
        }

        /// <summary>Flat ground billboards (the landing shockwave) draw a crisp ring instead of the default soft dot.</summary>
        private static void GroundRings(GameObject root)
        {
            Material ring = WeatherVfxBuilder.ParticleMaterial("M_GroundRing", RingTexture, additive: false);
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (renderer.renderMode == ParticleSystemRenderMode.HorizontalBillboard) renderer.sharedMaterial = ring;
        }

        /// <summary>
        /// The reach of an area attack: a ring and a faint disc lying on the ground, 1 m in radius at unit scale (the
        /// presenter scales it to the skill's range). One second long: it grows to full size at
        /// <see cref="AreaRingGrowTime"/> (the presenter plays it so that this lands on the hit), flashes and fades.
        /// </summary>
        private static void BuildAreaRing()
        {
            var go = new GameObject(AreaRingName);
            try
            {
                GroundDisc(go, WeatherVfxBuilder.ParticleMaterial("M_AreaRing_Edge", RingTexture, additive: false), Coral, 1f, 1f);
                var fill = new GameObject("Fill");
                fill.transform.SetParent(go.transform, false);
                GroundDisc(fill, WeatherVfxBuilder.ParticleMaterial("M_AreaRing_Fill", DiscTexture, additive: false), Coral, 0.14f, 0.45f);
                PrefabUtility.SaveAsPrefabAsset(go, Vfx(AreaRingName));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>One flat quad on the ground that grows to a 1 m radius, flashes at full size and fades.</summary>
        private static void GroundDisc(GameObject go, Material material, Color tint, float alpha, float flashAlpha)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 2f / GroundArtRadius; // quad width whose art is 1 m in radius
            main.startColor = Color.white;
            main.maxParticles = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var shape = ps.shape;
            shape.enabled = false;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var grow = new AnimationCurve(new Keyframe(0f, 0.2f, 0f, 2.6f), new Keyframe(AreaRingGrowTime, 1f, 0f, 0f), new Keyframe(1f, 1.03f));
            size.size = new ParticleSystem.MinMaxCurve(1f, grow);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(tint, 0f), new GradientColorKey(tint, AreaRingGrowTime - 0.05f),
                    new GradientColorKey(Color.white, AreaRingGrowTime + 0.02f), new GradientColorKey(tint, AreaRingGrowTime + 0.2f),
                    new GradientColorKey(tint, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.08f), new GradientAlphaKey(alpha, AreaRingGrowTime - 0.05f),
                    new GradientAlphaKey(flashAlpha, AreaRingGrowTime + 0.02f), new GradientAlphaKey(0f, 1f),
                });
            colour.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        /// <summary>
        /// Gives the effect one hue. Most POLYGON effects carry their colour in the material, so multiplying the
        /// particles' start colour only darkens them (a green swirl tinted yellow stays green). The renderer gets a
        /// tinted copy of its URP material instead, and the particles go white so the material colour shows as is.
        /// </summary>
        private static void Recolor(GameObject root, Color tint)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                Material urp = WeatherVfxBuilder.ToUrp(renderer.sharedMaterial);
                var main = ps.main;
                var start = main.startColor;
                if (!urp.HasProperty("_BaseColor"))
                {
                    // Shader Graph materials (no base colour to replace): fall back to tinting the particles.
                    start.colorMin *= tint;
                    start.colorMax *= tint;
                    start.color *= tint;
                    main.startColor = start;
                    continue;
                }
                renderer.sharedMaterial = Tinted(urp, tint);
                float alpha = Mathf.Max(start.color.a, start.colorMax.a);
                main.startColor = new Color(1f, 1f, 1f, alpha > 0f ? alpha : 1f);
            }
        }

        private static Material Tinted(Material source, Color tint)
        {
            string path = $"{MaterialFolder}/{source.name}_{ColorUtility.ToHtmlStringRGB(tint)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(source);
            else mat.CopyPropertiesFromMaterial(source);
            tint.a = source.GetColor("_BaseColor").a;
            mat.SetColor("_BaseColor", tint);
            // Unity mirrors the base colour into the legacy _Color when it next saves the material; write it now so
            // a rebuild leaves the file exactly as it was.
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Takes the Cartoon FX pack's authoring components off a copy of one of its effects: they rebuild the effect
        /// in the editor and turn it to the camera once in Start, neither of which suits a pooled instance. The
        /// particle systems and their materials are all the effect needs; it is turned to the camera on every play.
        /// </summary>
        private static void StripAuthoring(GameObject root)
        {
            // Components can require each other, so keep removing until a pass removes nothing.
            for (int pass = 0; pass < 8; pass++)
            {
                bool removed = false;
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null) continue;
                    string ns = behaviour.GetType().Namespace ?? string.Empty;
                    if (!ns.StartsWith("CTFX2", StringComparison.Ordinal) && !ns.StartsWith("PSD", StringComparison.Ordinal)) continue;
                    if (ComponentUtility.IsRequiredByOthers(behaviour)) continue;
                    Object.DestroyImmediate(behaviour);
                    removed = true;
                }
                if (!removed) break;
            }
        }

        private static class ComponentUtility
        {
            /// <summary>True while another component on the same object still lists this one's type in RequireComponent.</summary>
            public static bool IsRequiredByOthers(Component component)
            {
                Type type = component.GetType();
                foreach (var other in component.GetComponents<Component>())
                {
                    if (other == null || other == component) continue;
                    foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                        if (Requires(require.m_Type0, type) || Requires(require.m_Type1, type) || Requires(require.m_Type2, type)) return true;
                }
                return false;
            }

            private static bool Requires(Type required, Type type) => required != null && required.IsAssignableFrom(type);
        }

        /// <summary>
        /// The blade trail's material: a first-party copy of All In 1 Shader Nodes' slash material (scrolling energy
        /// ribbon with a colour ramp), which reads far better on a trail than a plain swipe texture. Falls back to
        /// the POLYGON swipe on a URP particle material when that pack is not in the project.
        /// </summary>
        private static Material BladeTrailMaterial()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(AllIn1SlashMaterial);
            if (source == null) return WeatherVfxBuilder.ParticleMaterial("M_BladeTrail_Swipe", SwipeTexture, additive: true);

            string path = $"{MaterialFolder}/M_BladeTrail.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        /// <summary>The ribbon the blade draws while it cuts: a view-facing trail that fades from white to the blade's cyan.</summary>
        private static void BuildBladeTrail()
        {
            var go = new GameObject(BladeTrailName);
            try
            {
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = BladeTrailMaterial();
                trail.time = 0.22f;
                trail.minVertexDistance = 0.04f;
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.numCapVertices = 2;
                trail.widthMultiplier = 0.6f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Cyan, 0.35f), new GradientColorKey(Cyan, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.55f, 0.4f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.lightProbeUsage = LightProbeUsage.Off;
                trail.emitting = false;
                trail.autodestruct = false;
                PrefabUtility.SaveAsPrefabAsset(go, Vfx(BladeTrailName));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- per-skill looks

        /// <summary>One pooled effect inside a world player.</summary>
        private readonly struct Fx
        {
            public readonly string Prefab;
            public readonly Vector3 Offset;
            public readonly bool Ground, Follow, FaceCamera, Dust, Tint;
            /// <summary>A comic mark or word (it lives in <see cref="ComicFolder"/>), and whether it wears the ramp of the moment.</summary>
            public readonly bool FromComic, Ramp;
            /// <summary>Copies kept ready in the pool.</summary>
            public readonly int Warm;
            public Fx(string prefab, Vector3 offset = default, bool ground = false, bool follow = false, bool faceCamera = false, bool dust = false,
                bool tint = false, bool fromComic = false, bool ramp = false, int warm = 2)
            {
                Prefab = prefab; Offset = offset; Ground = ground; Follow = follow; FaceCamera = faceCamera; Dust = dust; Tint = tint;
                FromComic = fromComic; Ramp = ramp; Warm = warm;
            }
        }

        /// <summary>The same effect with another number of copies kept ready (the ones that play many at a time).</summary>
        private static Fx Warm(Fx fx, int warm) =>
            new Fx(fx.Prefab, fx.Offset, fx.Ground, fx.Follow, fx.FaceCamera, fx.Dust, fx.Tint, fx.FromComic, fx.Ramp, warm);

        private readonly struct SkillLook
        {
            public readonly string Id, Aura, Charge, ChargeGround;
            public readonly AuraAnchor Anchor;
            public readonly Fx[] Cast, Hit;
            public SkillLook(string id, Fx[] cast = null, Fx[] hit = null, string aura = null, AuraAnchor anchor = AuraAnchor.Feet,
                string charge = null, string chargeGround = null)
            {
                Id = id; Cast = cast; Hit = hit; Aura = aura; Anchor = anchor; Charge = charge; ChargeGround = chargeGround;
            }
        }

        private static Fx Slash(string prefab = "PFX_Cast_Slash") => new Fx(prefab, SlashOrigin);
        /// <summary>A comic mark or word, turned to the camera, <paramref name="up"/> metres above where the moment plays.</summary>
        private static Fx ComicAt(string prefab, float up = 0f, bool ramped = false) =>
            new Fx(prefab, new Vector3(0f, up, 0f), faceCamera: true, fromComic: true, ramp: ramped);
        private static Fx OnCaster(string prefab) => new Fx(prefab, Chest, follow: true);
        /// <summary>Rising effects (pillars, columns) start at the feet so they wrap the body instead of the head.</summary>
        private static Fx AtFeet(string prefab) => new Fx(prefab, Feet, follow: true);
        private static Fx OnGround(string prefab) => new Fx(prefab, ground: true);
        /// <summary>Puffs kicked up off the ground: they take the colour of the surface they come from.</summary>
        private static Fx Dust(string prefab) => new Fx(prefab, ground: true, dust: true);

        /// <summary>
        /// What each skill looks like. Attacks: a cast burst and a hit burst. Buffs: a cast burst and an aura that
        /// lasts for the buff's duration. Charged skills: what gathers while the key is held. Skills missing here fall
        /// back to the presenter's default players.
        /// </summary>
        private static readonly SkillLook[] Skills =
        {
            // No slash burst on the two area skills: the blade trail and the ground ring carry them. Fury Strike's
            // empty cast keeps the presenter from falling back to the default slash.
            new SkillLook("FuryStrike", cast: new Fx[0]),
            new SkillLook("HaeseoStroke", cast: new[] { Slash() }),
            // A skill's hit burst is what is its own (thunder on the ground): the mark and the word of the hit come
            // from the weapon class that cast it (HitLookSettings), and every hit already has its sparks.
            new SkillLook("GentlemansPen", cast: new[] { Slash() }, hit: new Fx[0]),
            new SkillLook("DevilsStrike", cast: new[] { Slash() }, hit: new Fx[0]),
            new SkillLook("FlashOfRage", cast: new[] { Slash(), OnCaster("PFX_Cast_Fire") }, hit: new Fx[0]),
            new SkillLook("SunraysStroke", cast: new[] { Slash(), AtFeet("PFX_Cast_PowerUp") }, hit: new Fx[0]),
            new SkillLook("PhantomThunder", cast: new[] { Slash() }, hit: new[] { OnGround("PFX_Cast_Thunder") }),
            new SkillLook("ThunderousDragon", cast: new[] { OnGround("PFX_Cast_GroundCrack"), OnGround("PFX_Cast_Thunder") },
                hit: new[] { OnGround("PFX_Cast_Thunder") }),

            new SkillLook("AzureWall", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_AzureWall"),
            new SkillLook("LightningFlash", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_LightningFlash", anchor: AuraAnchor.Chest),
            new SkillLook("SpiritualRage", cast: new[] { OnCaster("PFX_Cast_Fire") }, aura: "PFX_Aura_SpiritualRage", anchor: AuraAnchor.Head,
                charge: "PFX_Charge_SpiritualRage", chargeGround: "PFX_Charge_Ground"),
            new SkillLook("HeavenlySerenity", cast: new[] { AtFeet("PFX_Heal") }, aura: "PFX_Aura_Serenity"),
            new SkillLook("HeavenlyShield", cast: new[] { AtFeet("PFX_Cast_PowerUp") }, aura: "PFX_Aura_Shield"),
            new SkillLook("HeavenlyBlow", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_Blow"),
            new SkillLook("GarudasPrayer", cast: new[] { AtFeet("PFX_Cast_PowerUp") }, aura: "PFX_Aura_Prayer", anchor: AuraAnchor.Chest),
            new SkillLook("HeavenlyRepulsion", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_Repulsion", anchor: AuraAnchor.Chest),
            new SkillLook("HeavenlyDestruction", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_Destruction", anchor: AuraAnchor.Chest),
            new SkillLook("ReverseFlow", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_ReverseFlow"),
        };

        // ---------------------------------------------------------------- prefab

        private static void BuildPrefab()
        {
            var root = new GameObject("PFX_GameFeel");
            try
            {
                var presenter = root.AddComponent<GameFeelPresenter>();
                var catalog = root.AddComponent<SkillFeelCatalog>();
                var so = new SerializedObject(presenter);

                // ---- camera / screen / sound: the local player's own moments only
                so.FindProperty("swingFeel").objectReferenceValue = Player(root, "Swing", p =>
                {
                    Sound(p, "Swing Whoosh");
                    Impulse(p, new Vector3(0.04f, 0.03f, 0f), 0.15f);
                });
                so.FindProperty("swingMissFeel").objectReferenceValue = Player(root, "Swing Miss", p => Sound(p, "Swing Miss"));
                so.FindProperty("skillMissFeel").objectReferenceValue = Player(root, "Skill Miss", p => Sound(p, "Skill Miss"));
                so.FindProperty("hitFeel").objectReferenceValue = Player(root, "Hit", p =>
                {
                    Sound(p, "Hit");
                    Impulse(p, new Vector3(0.18f, 0.12f, 0f), 0.18f);
                });
                so.FindProperty("heavyHitFeel").objectReferenceValue = Player(root, "Heavy Hit", p =>
                {
                    Sound(p, "Heavy Hit");
                    Impulse(p, new Vector3(0.45f, 0.3f, 0f), 0.28f);
                    Chromatic(p, "Chromatic Punch", 0.22f, 0.7f);
                });
                so.FindProperty("damagedFeel").objectReferenceValue = Player(root, "Damaged", p =>
                {
                    Sound(p, "Hurt");
                    Impulse(p, new Vector3(0.3f, 0.22f, 0f), 0.25f);
                    var v = p.AddFeedback(typeof(MMF_BeautifyVignette)) as MMF_BeautifyVignette;
                    v.Label = "Hurt Vignette (Beautify)";
                    v.Duration = HurtVignetteSeconds;
                    v.Reach = HurtVignetteReach;
                    v.PulseColor = HurtVignetteColor;
                    v.DotOpacity = HurtVignetteDots;
                });
                so.FindProperty("clickFeel").objectReferenceValue = Player(root, "Click", p => Sound(p, "Click Tick"));
                so.FindProperty("selectFeel").objectReferenceValue = Player(root, "Select", p => Sound(p, "Select"));
                so.FindProperty("runFeel").objectReferenceValue = Player(root, "Run Start", p =>
                {
                    Sound(p, "Footstep Burst");
                    var fov = p.AddFeedback(typeof(MMF_CameraFieldOfView)) as MMF_CameraFieldOfView;
                    fov.Label = "FOV Kick";
                    fov.Duration = 0.4f;
                    fov.RelativeFieldOfView = true;
                    fov.RemapFieldOfViewZero = 0f;
                    fov.RemapFieldOfViewOne = 4f;
                    fov.ShakeFieldOfView = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f));
                });
                so.FindProperty("jumpFeel").objectReferenceValue = Player(root, "Jump", p => Sound(p, "Jump"));
                so.FindProperty("landFeel").objectReferenceValue = Player(root, "Land", p =>
                {
                    Sound(p, "Land Thud");
                    Impulse(p, new Vector3(0f, -0.22f, 0f), 0.16f);
                });
                so.FindProperty("attackCastFeel").objectReferenceValue = Player(root, "Attack Skill", p =>
                {
                    Sound(p, "Skill Slash");
                    Impulse(p, new Vector3(0.1f, 0.08f, 0f), 0.2f);
                });
                so.FindProperty("buffCastFeel").objectReferenceValue = Player(root, "Buff Skill", p =>
                {
                    Sound(p, "Buff Cast");
                    Chromatic(p, "Chromatic Shimmer", 0.3f, 0.25f);
                });
                so.FindProperty("chargeFeel").objectReferenceValue = Player(root, "Charge", p =>
                {
                    Sound(p, "Charge Build-up");
                    // One second long; the presenter stretches it to the skill's charge time. The view closes in while
                    // the charge fills and springs back as it is released.
                    var fov = p.AddFeedback(typeof(MMF_CameraFieldOfView)) as MMF_CameraFieldOfView;
                    fov.Label = "FOV Pull-in";
                    fov.Duration = 1f;
                    fov.RelativeFieldOfView = true;
                    fov.RemapFieldOfViewZero = 0f;
                    fov.RemapFieldOfViewOne = -3f;
                    fov.ShakeFieldOfView = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
                });

                // ---- world particles: every client
                var world = Group(root, "World");
                AddHitFeel(root, world, so);
                so.FindProperty("looks").objectReferenceValue = AssetDatabase.LoadAssetAtPath<HitLookSettings>(HitLookSettingsPath);
                so.FindProperty("runWorldFeel").objectReferenceValue = Smeared(World(world, RunStartPlayer, Dust("PFX_Dust_Run")), SmearKind.TakeOff);
                so.FindProperty("jumpWorldFeel").objectReferenceValue = World(world, "Jump", Dust("PFX_Dust_Jump"), Dust("PFX_Jump_Burst"));
                so.FindProperty("landWorldFeel").objectReferenceValue = World(world, "Land", Dust("PFX_Dust_Land"), Dust("PFX_Land_Ring"));
                so.FindProperty("attackCastWorldFeel").objectReferenceValue = World(world, "Attack Skill", Slash());
                so.FindProperty("buffCastWorldFeel").objectReferenceValue = World(world, "Buff Skill", AtFeet("PFX_Cast_Buff"));
                so.FindProperty("buffEndWorldFeel").objectReferenceValue = World(world, "Buff End", OnCaster("PFX_Buff_End"));
                so.FindProperty("healWorldFeel").objectReferenceValue = World(world, "Heal", AtFeet("PFX_Heal"));
                so.FindProperty("chiWorldFeel").objectReferenceValue = World(world, "Chi", AtFeet("PFX_Chi"));
                AddDeathFeel(root, world, so);
                AddPetFeel(root, world, so);
                AddPetChatter(root);
                AddForgeFeel(root, world, so);
                AddNovaFeel(root, world, so);

                so.FindProperty("clickVfx").objectReferenceValue = Load(Vfx("PFX_Click_Ring"));
                so.FindProperty("selectHostileVfx").objectReferenceValue = Load(Vfx("PFX_Select_Hostile"));
                so.FindProperty("selectFriendlyVfx").objectReferenceValue = Load(Vfx("PFX_Select_Friendly"));

                so.FindProperty("areaRingVfx").objectReferenceValue = Load(Vfx(AreaRingName));
                so.FindProperty("areaRingGrowTime").floatValue = AreaRingGrowTime;

                so.FindProperty("skills").objectReferenceValue = catalog;
                so.FindProperty("defaultAuraVfx").objectReferenceValue = Load(Vfx("PFX_Aura_Default"));
                so.FindProperty("defaultChargeVfx").objectReferenceValue = Load(Vfx("PFX_Charge_Default"));
                so.FindProperty("runLoopVfx").objectReferenceValue = Load(Vfx("PFX_Run_Loop"));
                so.FindProperty("airborneLoopVfx").objectReferenceValue = Load(Vfx("PFX_Air_Loop"));
                so.FindProperty("bladeTrail").objectReferenceValue = Load(Vfx(BladeTrailName)).GetComponent<TrailRenderer>();
                so.ApplyModifiedPropertiesWithoutUndo();

                BuildCatalog(root, catalog);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildCatalog(GameObject root, SkillFeelCatalog catalog)
        {
            var group = Group(root, "Skills");
            var so = new SerializedObject(catalog);
            var list = so.FindProperty("skills");
            list.arraySize = Skills.Length;
            for (int i = 0; i < Skills.Length; i++)
            {
                SkillLook look = Skills[i];
                var entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("skillId").stringValue = look.Id;
                entry.FindPropertyRelative("cast").objectReferenceValue = look.Cast != null ? World(group, look.Id + " Cast", look.Cast) : null;
                entry.FindPropertyRelative("hit").objectReferenceValue = look.Hit != null ? World(group, look.Id + " Hit", look.Hit) : null;
                entry.FindPropertyRelative("aura").objectReferenceValue = look.Aura != null ? Load(Vfx(look.Aura)) : null;
                entry.FindPropertyRelative("auraAnchor").enumValueIndex = (int)look.Anchor;
                entry.FindPropertyRelative("charge").objectReferenceValue = look.Charge != null ? Load(Vfx(look.Charge)) : null;
                entry.FindPropertyRelative("chargeGround").objectReferenceValue = look.ChargeGround != null ? Load(Vfx(look.ChargeGround)) : null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Group(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        private static MMF_Player Player(GameObject root, string name, Action<MMF_Player> build)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var player = go.AddComponent<MMF_Player>();
            player.InitializationMode = MMFeedbacks.InitializationModes.Awake;
            player.CanPlayWhileAlreadyPlaying = true;
            build(player);
            return player;
        }

        /// <summary>A world player: nothing but pooled particles, so it is safe to play on every client.</summary>
        private static MMF_Player World(GameObject group, string name, params Fx[] effects)
        {
            return Player(group, name, p =>
            {
                foreach (Fx fx in effects)
                {
                    GameObject prefab = Load(fx.FromComic ? ComicPath(fx.Prefab) : Vfx(fx.Prefab));
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[GameFeel] Missing VFX '{fx.Prefab}' for '{name}'.");
                        continue;
                    }
                    var vfx = p.AddFeedback(typeof(MMF_PooledVfx)) as MMF_PooledVfx;
                    vfx.Label = fx.Prefab;
                    vfx.Prefab = prefab;
                    vfx.Offset = fx.Offset;
                    vfx.SnapToGround = fx.Ground;
                    vfx.FollowTarget = fx.Follow;
                    vfx.FaceCamera = fx.FaceCamera;
                    vfx.Upright = fx.Ground || fx.Follow;
                    vfx.TintWithGroundDust = fx.Dust;
                    vfx.TintWithMoment = fx.Tint;
                    vfx.RampWithMoment = fx.Ramp;
                    vfx.Warm = fx.Warm;
                }
            });
        }

        // ---------------------------------------------------------------- smear

        /// <summary>
        /// Puts the comic motion smear on the moments that have one (taking off at a run, the throw of a death)
        /// without regenerating the rest of the game feel prefab, and makes its settings asset if there is none.
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Smear Feel")]
        public static void RebuildSmearFeel()
        {
            if (EnsureSmearSettings() == null) return;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                Transform world = scope.prefabContentsRoot.transform.Find("World");
                if (world == null)
                {
                    Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first.");
                    return;
                }
                int done = 0;
                foreach (var (name, kind) in new[] { (RunStartPlayer, SmearKind.TakeOff), (DeathPlayer, SmearKind.DeathThrow), (CritDeathPlayer, SmearKind.DeathThrow) })
                {
                    Transform child = world.Find(name);
                    if (child == null || !child.TryGetComponent(out MMF_Player player))
                    {
                        Debug.LogWarning($"[GameFeel] No '{name}' player under World: no smear put on it.");
                        continue;
                    }
                    Smeared(player, kind);
                    done++;
                }
                Debug.Log($"[GameFeel] Smear put on {done} moment(s) of {PrefabPath}. Tune it in {SmearSettingsPath}.");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>The player with exactly one smear feedback of this kind on it.</summary>
        private static MMF_Player Smeared(MMF_Player player, SmearKind kind)
        {
            SmearSettings settings = EnsureSmearSettings();
            if (player == null || settings == null) return player;
            player.FeedbacksList.RemoveAll(feedback => feedback is MMF_Smear);
            var smear = player.AddFeedback(typeof(MMF_Smear)) as MMF_Smear;
            smear.Label = kind == SmearKind.TakeOff ? "Smear (take-off)" : "Smear (death throw)";
            smear.Settings = settings;
            smear.Kind = kind;
            EditorUtility.SetDirty(player);
            return player;
        }

        /// <summary>
        /// The smear's settings asset, made on first use with the smear package's shader linked. Null (and an error)
        /// when the package is not in the project: nothing is put in its place.
        /// </summary>
        private static SmearSettings EnsureSmearSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SmearSettings>(SmearSettingsPath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SmearShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[GameFeel] The smear package's shader is not at {SmearShaderPath}: no smear was set up.");
                return null;
            }
            if (settings == null)
            {
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(SmearSettingsPath).Replace('\\', '/'));
                settings = ScriptableObject.CreateInstance<SmearSettings>();
                AssetDatabase.CreateAsset(settings, SmearSettingsPath);
            }
            if (settings.shader != shader)
            {
                settings.shader = shader;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            return settings;
        }

        private static void Impulse(MMF_Player player, Vector3 velocity, float duration)
        {
            var impulse = player.AddFeedback(typeof(MMF_CinemachineImpulse)) as MMF_CinemachineImpulse;
            impulse.Label = "Camera Impulse";
            impulse.Velocity = velocity;
            impulse.m_ImpulseDefinition.ImpulseDuration = duration;
        }

        private static void Chromatic(MMF_Player player, string label, float duration, float peak)
        {
            var ca = player.AddFeedback(typeof(MMF_ChromaticAberration_URP)) as MMF_ChromaticAberration_URP;
            ca.Label = label;
            ca.Duration = duration;
            ca.RemapIntensityOne = peak;
        }

        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        // ---------------------------------------------------------------- scene wiring

        /// <summary>Scene half of <see cref="BuildAll"/> on its own, for wiring a second scene without rebuilding assets.</summary>
        public static void WireScene()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var cam = Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == OrbitCameraName) ?? Object.FindAnyObjectByType<CinemachineCamera>();
            if (cam != null)
            {
                if (cam.GetComponent<CinemachineImpulseListener>() == null) Undo.AddComponent<CinemachineImpulseListener>(cam.gameObject);
                if (cam.GetComponent<MMCinemachineFieldOfViewShaker>() == null) Undo.AddComponent<MMCinemachineFieldOfViewShaker>(cam.gameObject);
                var fov = cam.GetComponent<MMCinemachineFieldOfViewShaker>();
                Undo.RecordObject(fov, "Park FOV Shaker");
                fov.RelativeFieldOfView = true;
                fov.RemapFieldOfViewZero = 0f;
                fov.RemapFieldOfViewOne = 0f;
                Park(fov);
            }
            else Debug.LogWarning("[GameFeel] No CinemachineCamera in the scene: camera impulses / FOV kicks will not show.");

            var volume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => v.isGlobal && v.sharedProfile != null && AssetDatabase.GetAssetPath(v.sharedProfile) == VolumeProfilePath);
            if (volume != null)
            {
                var urpVignette = volume.GetComponent<MMVignetteShaker_URP>();
                if (urpVignette != null) Undo.DestroyObjectImmediate(urpVignette); // Beautify owns the vignette now
                if (volume.GetComponent<BeautifyVignettePulse>() == null) Undo.AddComponent<BeautifyVignettePulse>(volume.gameObject);
                if (volume.GetComponent<MMChromaticAberrationShaker_URP>() == null) Undo.AddComponent<MMChromaticAberrationShaker_URP>(volume.gameObject);
                var ca = volume.GetComponent<MMChromaticAberrationShaker_URP>();
                Undo.RecordObject(ca, "Park Chromatic Aberration Shaker");
                ca.RelativeIntensity = true;
                ca.RemapIntensityZero = 0f;
                ca.RemapIntensityOne = 0f;
                Park(ca);
                EnsureChromaticAberration(volume.sharedProfile);

                if (volume.GetComponent<MMLensDistortionShaker_URP>() == null) Undo.AddComponent<MMLensDistortionShaker_URP>(volume.gameObject);
                var lens = volume.GetComponent<MMLensDistortionShaker_URP>();
                Undo.RecordObject(lens, "Park Lens Distortion Shaker");
                lens.RelativeIntensity = true;
                lens.RemapIntensityZero = 0f;
                lens.RemapIntensityOne = 0f;
                Park(lens);
                EnsureLensDistortion(volume.sharedProfile);
            }
            else Debug.LogWarning("[GameFeel] Global volume using SampleSceneProfile not found: screen pulses will not show.");

            var existing = Object.FindAnyObjectByType<GameFeelPresenter>(FindObjectsInactive.Include);
            if (existing != null && !PrefabUtility.IsPartOfPrefabInstance(existing)) Undo.DestroyObjectImmediate(existing.gameObject);
            if (Object.FindAnyObjectByType<GameFeelPresenter>(FindObjectsInactive.Include) == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load(PrefabPath), scene);
                Undo.RegisterCreatedObjectUndo(instance, "Game Feel");
                var header = scene.GetRootGameObjects().FirstOrDefault(r => r.name == SystemsHeader);
                if (header != null) instance.transform.SetParent(header.transform, false);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Saves a shaker disabled, with resting values that change nothing. FEEL parks shakers in Awake, but with Scene
        /// Reload off (Enter Play Mode options) an [ExecuteAlways] component never gets that Awake, so an enabled shaker
        /// plays its own inspector values on entering Play: the FOV shaker's 60 → 120 default was a zoom out and back in.
        /// A feedback re-enables the shaker and supplies its own values when it plays.
        /// </summary>
        private static void Park(MMShaker shaker)
        {
            shaker.PlayOnAwake = false;
            shaker.Shaking = false;
            shaker.enabled = false;
            EditorUtility.SetDirty(shaker);
        }

        /// <summary>The CA shaker needs an override to drive; it rests at 0 so nothing changes until a heavy hit.</summary>
        private static void EnsureChromaticAberration(VolumeProfile profile)
        {
            if (!profile.TryGet(out ChromaticAberration ca))
            {
                ca = profile.Add<ChromaticAberration>(false);
                AssetDatabase.AddObjectToAsset(ca, profile);
            }
            ca.active = true;
            ca.intensity.Override(0f);
            EditorUtility.SetDirty(profile);
        }

        /// <summary>The lens shaker needs an override to drive too; it rests at 0 until the local player kills.</summary>
        private static void EnsureLensDistortion(VolumeProfile profile)
        {
            if (!profile.TryGet(out LensDistortion lens))
            {
                lens = profile.Add<LensDistortion>(false);
                AssetDatabase.AddObjectToAsset(lens, profile);
            }
            lens.active = true;
            lens.intensity.Override(0f);
            EditorUtility.SetDirty(profile);
        }

        /// <summary>Points the player's basic-combo hit and slash VFX at the URP copies.</summary>
        private static void UseUrpVfxOnPlayer()
        {
            var stab = Load(Vfx("PFX_Hit_SwordStab"));
            if (stab == null) return;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PlayerPrefabPath))
            {
                var melee = scope.prefabContentsRoot.GetComponent<Combat.PlayerMeleeCombat>();
                if (melee == null) return;
                var so = new SerializedObject(melee);
                var prop = so.FindProperty("hitVfxPrefab");
                if (prop != null && prop.objectReferenceValue != stab)
                {
                    prop.objectReferenceValue = stab;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
