using System;
using System.Collections.Generic;
using System.IO;
using EmeraldAI;
using EmeraldAI.Utility;
using KryticalNova.Hostiles;
using KryticalNova.Hostiles.Emerald;
using KryticalNova.Progression;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools.Hostiles
{
    /// <summary>
    /// Builds everything a Hostile species needs from one row of the roster below: its definition asset, its own
    /// copies of the animation clips (with the attack event at the hit frame and nothing else), its Emerald AI
    /// Animation Profile and Animator Controller, and the networked prefab the server spawns. Nothing it makes is
    /// edited by hand: change the roster or a settings asset and build again.
    /// The prefab holds the Emerald AI parts the game uses and no others, all of them switched off: only
    /// <see cref="HostileBrainLink"/> switches them on, and only where the game is decided.
    /// </summary>
    public static class HostileAssetBuilder
    {
        private const string DataFolder = "Assets/_Game/Gameplay/Hostiles/Data/Assets";
        public const string CurvePath = DataFolder + "/HostileStatCurve.asset";
        public const string LevelGapPath = DataFolder + "/LevelGapSettings.asset";
        private const string SpeciesFolder = DataFolder + "/Species";
        private const string MeleeAbilityPath = DataFolder + "/Emerald/HostileMelee.asset";
        private const string CharacterFolder = "Assets/_Game/Characters/Hostiles";
        private const string TuningPath = "Assets/_Game/Gameplay/Progression/Data/Assets/CombatTuning/CombatStatTuning.asset";
        private const string ProgressionPath = "Assets/_Game/Gameplay/Progression/Data/Assets/CharacterData/CharacterProgression.asset";

        // Vendor sources. Clips are copied, never referenced: a vendor clip carries its own demo's events.
        private const string Locomotion = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/";
        private const string Unarmed = "Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Animations/Unarmed/RPG-Character@Unarmed-";
        private const string PlayerDeaths = "Assets/_Game/Characters/Player/Animations/Death/";
        private const string MeleeAbilitySource = "Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Demo Abilities/Ability Objects/Unarmed No Stun (Melee).asset";
        private const string AttackEvent = "CreateAbility"; // EmeraldCombat's receiver

        private const int HostileLayer = 0;     // Default: what the player's pointer picks
        private const int HostileFaction = 0;   // any entry of Emerald's faction list that is not the players'
        private const string GroundLayerName = "Ground";

        private struct AttackRow
        {
            public string Clip;
            public float Windup, Damage;
            public bool Heavy;
            public int Weight;
        }

        /// <summary>One species. The roster is the list of these.</summary>
        private struct Species
        {
            public ushort Id;
            public string Asset, DisplayName, Model;
            public int Level;
            public HostileTier Tier;
            public HostileBehaviour Behaviour;
            public float Scale, AttackRange;
            public string Idle, Walk, Run, TurnLeft, TurnRight, Stunned;
            public AttackRow[] Attacks;
            public string[] Hits, Deaths;
        }

        // The unarmed humanoid set every species uses until it has clips of its own.
        private static readonly AttackRow[] UnarmedAttacks =
        {
            new AttackRow { Clip = Unarmed + "Attack-L1.FBX", Windup = 0.25f, Damage = 1f, Weight = 45 },
            new AttackRow { Clip = Unarmed + "Attack-R1.FBX", Windup = 0.25f, Damage = 1f, Weight = 40 },
            new AttackRow { Clip = Unarmed + "Attack-R3.FBX", Windup = 0.55f, Damage = 1.5f, Heavy = true, Weight = 15 },
        };
        private static readonly string[] UnarmedHits = { Unarmed + "GetHit-F1.FBX", Unarmed + "GetHit-F2.FBX" };
        private static readonly string[] HumanoidDeaths = { PlayerDeaths + "Death_Front.anim", PlayerDeaths + "Death_Back.anim" };

        /// <summary>
        /// The species that exist. Only a stand-in until the owner picks the roster from the model inventory.
        /// Ids are stable: never reuse one.
        /// </summary>
        private static readonly Species[] Roster =
        {
            new Species
            {
                Id = 1, Asset = "Scavenger", DisplayName = "Scavenger", Level = 1, Tier = HostileTier.Normal,
                Behaviour = HostileBehaviour.FightsBack, Scale = 1f, AttackRange = 1.8f,
                Model = "Assets/Synty/PolygonSciFiWorlds/Prefabs/Characters/SM_Chr_ScifiWorlds_Scavenger_01.prefab",
                Idle = Locomotion + "Idle/A_Idle_Standing_Masc.fbx",
                Walk = Locomotion + "Locomotion/Walk/A_Walk_F_Masc.fbx",
                Run = Locomotion + "Locomotion/Run/A_Run_F_Masc.fbx",
                Stunned = Unarmed + "Stunned.FBX",
                Attacks = UnarmedAttacks, Hits = UnarmedHits, Deaths = HumanoidDeaths,
            },
        };

        [MenuItem("Krytical Nova/Hostiles/Build Hostiles")]
        public static void BuildAll()
        {
            HostileStatCurve curve = EnsureCurve();
            EnsureAsset<LevelGapSettings>(LevelGapPath);
            MeleeAbility melee = EnsureMeleeAbility();
            int built = 0;
            foreach (Species species in Roster)
            {
                try
                {
                    Build(species, curve, melee);
                    built++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Hostiles] {species.Asset} was not built: {e.Message}\n{e.StackTrace}");
                }
            }
            AssetDatabase.SaveAssets();
            GameplayPrefabsSetup.EnsureRegistered(); // a prefab the clients do not know never appears on them
            AssetDatabase.SaveAssets();
            Debug.Log($"[Hostiles] Built {built} of {Roster.Length} species.");
        }

        // ------------------------------------------------------------------ shared assets

        private static HostileStatCurve EnsureCurve()
        {
            var curve = EnsureAsset<HostileStatCurve>(CurvePath);
            if (curve.tuning == null) curve.tuning = AssetDatabase.LoadAssetAtPath<CombatStatTuningData>(TuningPath);
            if (curve.progression == null) curve.progression = AssetDatabase.LoadAssetAtPath<CharacterProgressionData>(ProgressionPath);
            EditorUtility.SetDirty(curve);
            return curve;
        }

        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            WeatherVfxBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>
        /// The one ability every melee attack uses. It only marks the moment a hit lands: the game rolls the
        /// damage, and knockback, slow and stun stay off so those rules live in the game's combat code alone.
        /// </summary>
        private static MeleeAbility EnsureMeleeAbility()
        {
            var ability = AssetDatabase.LoadAssetAtPath<MeleeAbility>(MeleeAbilityPath);
            if (ability == null)
            {
                WeatherVfxBuilder.EnsureFolder(Path.GetDirectoryName(MeleeAbilityPath).Replace('\\', '/'));
                if (!AssetDatabase.CopyAsset(MeleeAbilitySource, MeleeAbilityPath))
                    throw new InvalidOperationException($"Could not copy {MeleeAbilitySource}.");
                ability = AssetDatabase.LoadAssetAtPath<MeleeAbility>(MeleeAbilityPath);
            }
            ability.KnockbackSettings.Enabled = false;
            ability.SlowedSettings.Enabled = false;
            ability.StunnedSettings.Enabled = false;
            ability.DamageSettings.Enabled = true;
            ability.CooldownSettings.Enabled = false;
            ability.MeleeSettings.MaxDamageDistance = 4f; // the attack range of the species is what holds it back
            ability.MeleeSettings.MaxDamageAngle = 75;
            EditorUtility.SetDirty(ability);
            return ability;
        }

        // ------------------------------------------------------------------ one species

        private static void Build(Species species, HostileStatCurve curve, MeleeAbility melee)
        {
            string folder = CharacterFolder + "/" + species.Asset;
            WeatherVfxBuilder.EnsureFolder(folder);
            WeatherVfxBuilder.EnsureFolder(SpeciesFolder);
            WeatherVfxBuilder.EnsureFolder(GameplayPrefabsSetup.HostilePrefabFolder);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(species.Model);
            if (model == null) throw new InvalidOperationException($"No model at {species.Model}.");

            string definitionPath = $"{SpeciesFolder}/Hostile_{species.Asset}.asset";
            HostileDefinition definition = EnsureAsset<HostileDefinition>(definitionPath);
            definition.id = species.Id;
            definition.displayName = species.DisplayName;
            definition.tier = species.Tier;
            definition.level = species.Level;
            definition.behaviour = species.Behaviour;
            definition.attackRange = species.AttackRange;
            definition.model = model;
            definition.scale = species.Scale;
            definition.idle = definition.combatIdle = CopyClip(species.Idle, folder, species.Asset + "_Idle", true, -1f);
            definition.walk = CopyClip(species.Walk, folder, species.Asset + "_Walk", true, -1f);
            definition.run = CopyClip(species.Run, folder, species.Asset + "_Run", true, -1f);
            definition.turnLeft = CopyClip(species.TurnLeft, folder, species.Asset + "_TurnLeft", false, -1f);
            definition.turnRight = CopyClip(species.TurnRight, folder, species.Asset + "_TurnRight", false, -1f);
            definition.stunned = CopyClip(species.Stunned, folder, species.Asset + "_Stunned", true, -1f);
            definition.attacks = new HostileDefinition.Attack[species.Attacks.Length];
            for (int i = 0; i < species.Attacks.Length; i++)
            {
                AttackRow row = species.Attacks[i];
                AnimationClip clip = CopyClip(row.Clip, folder, $"{species.Asset}_Attack_{i + 1}", false, row.Windup);
                if (clip == null) throw new InvalidOperationException($"No attack clip at {row.Clip}.");
                definition.attacks[i] = new HostileDefinition.Attack
                {
                    clip = clip, windupSeconds = Mathf.Min(row.Windup, clip.length * 0.9f), damageMultiplier = row.Damage,
                    heavy = row.Heavy, weight = Mathf.Max(1, row.Weight)
                };
            }
            definition.hitReactions = CopyClips(species.Hits, folder, species.Asset + "_Hit");
            definition.deaths = CopyClips(species.Deaths, folder, species.Asset + "_Death");
            if (definition.idle == null || definition.walk == null || definition.run == null)
                throw new InvalidOperationException("Idle, walk and run clips are all needed.");
            if (definition.deaths.Length == 0)
                throw new InvalidOperationException("A death clip is needed: without one Emerald AI falls back to a ragdoll.");

            AnimationProfile profile = BuildProfile(species, definition, folder);
            string prefabPath = $"{GameplayPrefabsSetup.HostilePrefabFolder}/Hostile_{species.Asset}.prefab";
            definition.prefab = BuildPrefab(species, definition, curve, melee, profile, prefabPath);
            EditorUtility.SetDirty(definition);
        }

        // ------------------------------------------------------------------ clips

        private static AnimationClip[] CopyClips(string[] sources, string folder, string name)
        {
            var clips = new List<AnimationClip>();
            for (int i = 0; sources != null && i < sources.Length; i++)
            {
                AnimationClip clip = CopyClip(sources[i], folder, $"{name}_{i + 1}", false, -1f);
                if (clip != null) clips.Add(clip);
            }
            return clips.ToArray();
        }

        /// <summary>
        /// Our own copy of a clip: looping or not as asked, every event of the source removed, and for an attack
        /// the one event that makes Emerald AI strike, at the moment the hit lands. Kept at the same path on a
        /// rebuild so what points at it stays valid.
        /// </summary>
        private static AnimationClip CopyClip(string sourcePath, string folder, string name, bool loop, float attackEventTime)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            AnimationClip source = LoadClip(sourcePath);
            if (source == null)
            {
                Debug.LogWarning($"[Hostiles] No animation clip at {sourcePath}.");
                return null;
            }

            AnimationClip copy = UnityEngine.Object.Instantiate(source);
            copy.name = name;
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AnimationUtility.SetAnimationEvents(copy, attackEventTime >= 0f
                ? new[] { new AnimationEvent { functionName = AttackEvent, time = Mathf.Min(attackEventTime, copy.length * 0.9f), stringParameter = string.Empty } }
                : Array.Empty<AnimationEvent>());

            string path = $"{folder}/{name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(copy, path);
                return copy;
            }
            EditorUtility.CopySerialized(copy, existing);
            existing.name = name;
            UnityEngine.Object.DestroyImmediate(copy);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static AnimationClip LoadClip(string path)
        {
            if (path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            return null;
        }

        // ------------------------------------------------------------------ Emerald AI animation

        private static AnimationProfile BuildProfile(Species species, HostileDefinition definition, string folder)
        {
            string profilePath = $"{folder}/AP_{species.Asset}.asset";
            string controllerPath = $"{folder}/AC_{species.Asset}.controller";
            AnimationProfile profile = EnsureAsset<AnimationProfile>(profilePath);

            profile.NonCombatAnimations = Animations(definition, definition.idle, false);
            profile.Type1Animations = Animations(definition, definition.combatIdle != null ? definition.combatIdle : definition.idle, true);
            profile.Type2Animations = Animations(definition, definition.idle, false);
            // A server has no screen: an Animator that waits to be seen would never reach the attack events.
            profile.AnimatorCullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Emerald's own controller, with this species' clips written into its states.
            string master = AssetDatabase.GetAssetPath(Resources.Load("Emerald Animator Controller"));
            if (string.IsNullOrEmpty(master)) throw new InvalidOperationException("Emerald AI's master Animator Controller was not found.");
            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath) != null) AssetDatabase.DeleteAsset(controllerPath);
            if (!AssetDatabase.CopyAsset(master, controllerPath)) throw new InvalidOperationException($"Could not write {controllerPath}.");
            profile.AIAnimator = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            profile.FilePath = controllerPath;
            EmeraldAnimatorGenerator.GenerateAnimatorController(profile);
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(profile.AIAnimator);
            return profile;
        }

        private static AnimationParentClass Animations(HostileDefinition d, AnimationClip idle, bool withAttacks)
        {
            AnimationClass One(AnimationClip clip) => new AnimationClass(1f, clip, false);
            AnimationClip turnLeft = d.turnLeft != null ? d.turnLeft : idle, turnRight = d.turnRight != null ? d.turnRight : idle;
            var set = new AnimationParentClass
            {
                IdleStationary = One(idle), IdleWarning = One(idle),
                WalkLeft = One(d.walk), WalkForward = One(d.walk), WalkRight = One(d.walk), WalkBack = One(d.walk),
                RunLeft = One(d.run), RunForward = One(d.run), RunRight = One(d.run),
                TurnLeft = One(turnLeft), TurnRight = One(turnRight),
                StrafeLeft = One(d.walk), StrafeRight = One(d.walk),
                BlockIdle = One(null), BlockHit = One(null),
                DodgeLeft = One(null), DodgeBack = One(null), DodgeRight = One(null),
                Recoil = One(null), Stunned = One(d.stunned),
                CoverIdle = One(null), CoverHit = One(null), PutAwayWeapon = One(null), PullOutWeapon = One(null),
            };
            set.IdleList.Add(One(idle));
            foreach (AnimationClip clip in d.hitReactions) set.HitList.Add(One(clip));
            foreach (AnimationClip clip in d.deaths) set.DeathList.Add(One(clip));
            if (withAttacks)
                foreach (HostileDefinition.Attack attack in d.attacks) set.AttackList.Add(One(attack.clip));
            return set;
        }

        // ------------------------------------------------------------------ the prefab

        private static GameObject BuildPrefab(Species species, HostileDefinition definition, HostileStatCurve curve,
            MeleeAbility melee, AnimationProfile profile, string prefabPath)
        {
            GameObject root = UnityEngine.Object.Instantiate(definition.model);
            try
            {
                root.name = "Hostile_" + species.Asset;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = definition.model.transform.localScale * definition.scale;
                root.layer = HostileLayer;
                root.tag = "Untagged";

                Animator animator = RootAnimator(root);
                animator.runtimeAnimatorController = profile.AIAnimator;
                animator.applyRootMotion = false; // the NavMeshAgent moves it
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // Adding the system brings its seven required parts, the collider, the agent and the audio source.
                var system = root.AddComponent<EmeraldSystem>();
                float radius = definition.bodySize.x, height = definition.bodySize.y;

                var box = root.GetComponent<BoxCollider>();
                box.center = new Vector3(0f, height * 0.5f, 0f);
                box.size = new Vector3(radius * 2f, height, radius * 2f);

                var agent = root.GetComponent<NavMeshAgent>();
                agent.radius = radius;
                agent.height = height;
                agent.baseOffset = 0f;
                agent.enabled = false;

                var audio = root.GetComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f;

                HostileStats stats = definition.StatsFrom(curve);
                ConfigureBrain(root, definition, stats, melee, profile);

                var sheet = root.AddComponent<CharacterCombatStats>();
                SetField(sheet, "tuningData", curve.tuning);
                var entity = root.AddComponent<CombatEntity>();
                SetField(entity, "entityName", definition.displayName);
                SetField(entity, "tuningData", curve.tuning);
                SetField(entity, "overrideMaxHealth", stats.MaxHealth);
                SetField(entity, "useCombatStatsForMaxHealth", false);

                root.AddComponent<NetworkObject>();
                var netTransform = root.AddComponent<NetworkTransform>();
                netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
                netTransform.SyncRotAngleX = netTransform.SyncRotAngleZ = false; // it only ever turns about the vertical
                netTransform.Interpolate = true;

                var net = root.AddComponent<HostileNet>();
                SetField(net, "definition", definition);
                SetField(net, "curve", curve);
                SetField(net, "animator", animator);
                root.AddComponent<HostileBrainLink>();

                // Off in the prefab: a client never runs a brain, and nothing has to be switched off after spawn.
                foreach (Type part in new[] { typeof(EmeraldSystem), typeof(EmeraldAnimation), typeof(EmeraldBehaviors), typeof(EmeraldCombat),
                             typeof(EmeraldDetection), typeof(EmeraldHealth), typeof(EmeraldMovement), typeof(EmeraldSounds) })
                    ((Behaviour)root.GetComponent(part)).enabled = false;

                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureBrain(GameObject root, HostileDefinition definition, in HostileStats stats, MeleeAbility melee, AnimationProfile profile)
        {
            root.GetComponent<EmeraldAnimation>().m_AnimationProfile = profile;

            var movement = root.GetComponent<EmeraldMovement>();
            movement.MovementType = EmeraldMovement.MovementTypes.NavMeshDriven;
            movement.WanderType = EmeraldMovement.WanderTypes.Dynamic;
            movement.WalkSpeed = stats.WalkSpeed;
            movement.RunSpeed = stats.RunSpeed;
            movement.StoppingDistance = definition.attackRange;

            // Aggressive for every species: one that only fights back holds players as Neutral instead
            // (HostileBrainLink.Configure). Emerald's Passive never fights at all.
            root.GetComponent<EmeraldBehaviors>().CurrentBehaviorType = EmeraldBehaviors.BehaviorTypes.Aggressive;

            var detection = root.GetComponent<EmeraldDetection>();
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSystemsSetup.PlayerPrefabPath);
            if (player == null) throw new InvalidOperationException("The player prefab was not found: a Hostile would see nobody.");
            detection.DetectionLayerMask = 1 << player.layer;
            detection.PlayerTag = player.tag;
            int ground = LayerMask.NameToLayer(GroundLayerName);
            detection.ObstructionDetectionLayerMask = ground >= 0 ? 1 << ground : 0; // open ground: only the terrain hides a player
            detection.CurrentFaction = HostileFaction;
            detection.DetectionRadius = Mathf.Max(1, Mathf.CeilToInt(stats.AggroRadius));
            detection.FactionRelationsList.Clear();
            detection.FactionRelationsList.Add(new FactionClass(KNEmeraldPlayerBridge.PlayerFaction,
                (int)(definition.behaviour == HostileBehaviour.AttacksOnSight ? RelationTypes.Enemy : RelationTypes.Neutral)));
            detection.HeadTransform = FindHead(root.transform);

            var combat = root.GetComponent<EmeraldCombat>();
            combat.AttackDistance = definition.attackRange;
            combat.Type1Attacks = new AttackClass { AttackPickType = AttackPickTypes.Odds };
            for (int i = 0; i < definition.attacks.Length; i++)
            {
                combat.Type1Attacks.AttackDataList.Add(new AttackClass.AttackData
                {
                    AbilityObject = melee, AttackAnimation = i, AttackOdds = definition.attacks[i].weight,
                    AttackDistance = definition.attackRange, TooCloseDistance = 0.5f
                });
            }

            var health = root.GetComponent<EmeraldHealth>();
            health.StartingHealth = health.CurrentHealth = Mathf.Max(1, Mathf.CeilToInt(stats.MaxHealth));
            health.HealRate = 0;
        }

        /// <summary>Emerald AI wants the Animator on the object it is on. A model that keeps it on a child gives it up.</summary>
        private static Animator RootAnimator(GameObject root)
        {
            if (root.TryGetComponent(out Animator own)) return own;
            Animator child = root.GetComponentInChildren<Animator>(true);
            Animator animator = root.AddComponent<Animator>();
            if (child == null) return animator;
            animator.avatar = child.avatar;
            UnityEngine.Object.DestroyImmediate(child);
            return animator;
        }

        private static Transform FindHead(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, "Head", StringComparison.OrdinalIgnoreCase)) return t;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }

        private static void SetField(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) throw new InvalidOperationException($"{target.GetType().Name} has no field '{field}'.");
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetField(UnityEngine.Object target, string field, string value) => Edit(target, field, p => p.stringValue = value);
        private static void SetField(UnityEngine.Object target, string field, float value) => Edit(target, field, p => p.floatValue = value);
        private static void SetField(UnityEngine.Object target, string field, bool value) => Edit(target, field, p => p.boolValue = value);

        private static void Edit(UnityEngine.Object target, string field, Action<SerializedProperty> set)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) throw new InvalidOperationException($"{target.GetType().Name} has no field '{field}'.");
            set(property);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
