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
    /// folders are never modified. Sound slots (MMF_Sound) are left empty for audio to be assigned on the prefab.
    /// </summary>
    public static class GameFeelBuilder
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
            CartoonImpact("Impact 024", "PFX_Comic_Hit", 1.6f);
            CartoonImpact("Impact 028", "PFX_Comic_HitHeavy", 1.8f);
            CartoonImpact("Impact 023", "PFX_Comic_Slash", 2.2f);
            CartoonText("Text 020", "PFX_Comic_Pow", 2.6f);
            CartoonText("Text 022", "PFX_Comic_Wham", 2.4f);
            CartoonText("Text 021", "PFX_Comic_Zap", 2.8f);
            CartoonText("Text 012", "PFX_Comic_SlashText", 2.8f);

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
            BuildAreaRing();
            BuildBladeTrail();
            UseUrpMaterials($"{GameplayVfxFolder}/FX_ComicSlash.prefab");
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
            so.FindProperty("deathWorldFeel").objectReferenceValue = World(world, "Death",
                new Fx("PFX_Death_Burst"), new Fx("PFX_Hit_Heavy"), Comic("PFX_Comic_HitHeavy"));
            so.FindProperty("critDeathWorldFeel").objectReferenceValue = World(world, "Critical Death",
                new Fx("PFX_CritDeath_Blast"), new Fx("PFX_Hit_Heavy"), Comic("PFX_Comic_HitHeavy"), Comic("PFX_Comic_Wham", 0.9f),
                OnGround("PFX_CritKill_Shockwave"));
            so.FindProperty("bodyLandWorldFeel").objectReferenceValue = World(world, "Body Land",
                Dust("PFX_Dust_Land"), new Fx("PFX_Death_Stars", overBody, follow: true));
            so.FindProperty("critBodyLandWorldFeel").objectReferenceValue = World(world, "Critical Body Land",
                OnGround("PFX_CritDeath_Crater"), Dust("PFX_Dust_Land"), Dust("PFX_Land_Ring"), Comic("PFX_Comic_Pow", 0.9f),
                new Fx("PFX_Death_Stars", overBody, follow: true));
            so.FindProperty("respawnWorldFeel").objectReferenceValue = World(world, "Respawn",
                AtFeet("PFX_Respawn"), AtFeet("PFX_Cast_PowerUp"));

            so.FindProperty("critDeathTrailVfx").objectReferenceValue = Load(Vfx(DeathTrailName));
        }

        // ---------------------------------------------------------------- pets

        /// <summary>The glow that rides on an evolved pet. White: PetFollower tints it with the stage's colour.</summary>
        public const string PetAuraPath = VfxFolder + "/PFX_Pet_Aura.prefab";
        private static readonly string[] PetCameraPlayers = { "Pet Evolved" };
        private static readonly string[] PetWorldPlayers = { "Pet Evolved" };

        /// <summary>A pet reaching its next growth stage: a burst and rays on the pet, and the glow it wears afterwards.</summary>
        private static void BuildPetVfx()
        {
            Make("FX_LevelUp_01", "PFX_Pet_Evolve", 0.42f);
            Make("FX_LightRayRound_01", "PFX_Pet_EvolveRays", 0.45f, Yellow);
            Make("FX_Sparkles_Large_01", "PFX_Pet_EvolveSparkles", 0.5f);
            Make("FX_Sparkle_Orbit_01", "PFX_Pet_Aura", 0.3f, Color.white, loop: true);
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
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Pet evolution feel rebuilt on {PrefabPath}. Assign a sound on the new MMF_Sound slot.");
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
                new Fx("PFX_Pet_EvolveSparkles", Vector3.zero, follow: true));
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

        private static void CartoonImpact(string source, string name, float scale) => Cartoon("Impacts/" + source, name, scale);
        private static void CartoonText(string source, string name, float scale) => Cartoon("Texts/" + source, name, scale);

        /// <summary>
        /// Copy of a Cartoon FX effect with the pack's authoring components removed: they rebuild the effect in the
        /// editor and turn it to the camera once in Start, neither of which suits a pooled instance. The particle
        /// systems and their materials are all the effect needs; MMF_PooledVfx faces it to the camera on every play.
        /// </summary>
        private static void Cartoon(string source, string name, float scale)
        {
            WeatherVfxBuilder.Build(CartoonFx + source + ".prefab", Vfx(name), root =>
            {
                StripAuthoring(root);
                OneShot(root, scale, null);
            });
        }

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
            public readonly bool Ground, Follow, FaceCamera, Dust;
            public Fx(string prefab, Vector3 offset = default, bool ground = false, bool follow = false, bool faceCamera = false, bool dust = false)
            {
                Prefab = prefab; Offset = offset; Ground = ground; Follow = follow; FaceCamera = faceCamera; Dust = dust;
            }
        }

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
        private static Fx Comic(string prefab, float up = 0f) => new Fx(prefab, new Vector3(0f, up, 0f), faceCamera: true);
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
            new SkillLook("GentlemansPen", cast: new[] { Slash() }, hit: new[] { Comic("PFX_Comic_Slash"), new Fx("PFX_Hit_Sparks") }),
            new SkillLook("DevilsStrike", cast: new[] { Slash() }, hit: new[] { Comic("PFX_Comic_HitHeavy"), new Fx("PFX_Hit_Heavy"), Comic("PFX_Comic_Wham", 0.9f) }),
            new SkillLook("FlashOfRage", cast: new[] { Slash(), OnCaster("PFX_Cast_Fire") }, hit: new[] { Comic("PFX_Comic_Slash"), new Fx("PFX_Hit_Sparks") }),
            new SkillLook("SunraysStroke", cast: new[] { Slash(), AtFeet("PFX_Cast_PowerUp") }, hit: new[] { Comic("PFX_Comic_HitHeavy"), new Fx("PFX_Hit_Heavy") }),
            new SkillLook("PhantomThunder", cast: new[] { Slash() }, hit: new[] { Comic("PFX_Comic_Slash"), OnGround("PFX_Cast_Thunder"), Comic("PFX_Comic_Zap", 0.9f) }),
            new SkillLook("ThunderousDragon", cast: new[] { OnGround("PFX_Cast_GroundCrack"), OnGround("PFX_Cast_Thunder") },
                hit: new[] { Comic("PFX_Comic_HitHeavy"), OnGround("PFX_Cast_Thunder"), Comic("PFX_Comic_Zap", 0.9f) }),

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
                so.FindProperty("hitWorldFeel").objectReferenceValue = World(world, "Hit", new Fx("PFX_Hit_Sparks"), Comic("PFX_Comic_Hit"));
                so.FindProperty("heavyHitWorldFeel").objectReferenceValue = World(world, "Heavy Hit", new Fx("PFX_Hit_Heavy"), Comic("PFX_Comic_HitHeavy"));
                so.FindProperty("critWorldFeel").objectReferenceValue = World(world, "Critical", Comic("PFX_Comic_Pow", 0.9f));
                so.FindProperty("runWorldFeel").objectReferenceValue = World(world, "Run Start", Dust("PFX_Dust_Run"));
                so.FindProperty("jumpWorldFeel").objectReferenceValue = World(world, "Jump", Dust("PFX_Dust_Jump"), Dust("PFX_Jump_Burst"));
                so.FindProperty("landWorldFeel").objectReferenceValue = World(world, "Land", Dust("PFX_Dust_Land"), Dust("PFX_Land_Ring"));
                so.FindProperty("attackCastWorldFeel").objectReferenceValue = World(world, "Attack Skill", Slash());
                so.FindProperty("buffCastWorldFeel").objectReferenceValue = World(world, "Buff Skill", AtFeet("PFX_Cast_Buff"));
                so.FindProperty("skillHitWorldFeel").objectReferenceValue = World(world, "Skill Hit", Comic("PFX_Comic_Slash"), new Fx("PFX_Hit_Sparks"));
                so.FindProperty("buffEndWorldFeel").objectReferenceValue = World(world, "Buff End", OnCaster("PFX_Buff_End"));
                so.FindProperty("healWorldFeel").objectReferenceValue = World(world, "Heal", AtFeet("PFX_Heal"));
                so.FindProperty("chiWorldFeel").objectReferenceValue = World(world, "Chi", AtFeet("PFX_Chi"));
                AddDeathFeel(root, world, so);
                AddPetFeel(root, world, so);

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
                    GameObject prefab = Load(Vfx(fx.Prefab));
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
                }
            });
        }

        private static void Sound(MMF_Player player, string label)
        {
            var sound = player.AddFeedback(typeof(MMF_Sound)) as MMF_Sound;
            sound.Label = label + " (assign clip)";
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
