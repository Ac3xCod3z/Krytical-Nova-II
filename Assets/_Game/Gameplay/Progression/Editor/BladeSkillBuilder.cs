using System.Collections.Generic;
using System.Linq;
using KryticalNova.Combat;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the Blade skill set (attacks and blade buffs), reworks Spiritual Rage into a charged one-hit buff, gives
    /// every buff that has no animation of its own the shared buff cast, gives Heavenly Serenity the kneel it holds
    /// while it channels, and wires everything onto the Player prefab.
    /// Safe to re-run: numbers are rewritten from the table below, while animation clips and VFX are only filled in
    /// when empty, so choices made in the Inspector survive a rebuild.
    /// </summary>
    public static class BladeSkillBuilder
    {
        private const string SkillFolder = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/BladeSkills";
        private const string SpiritualRagePath = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/SelfBuffSkills/Skill_SpiritualRage.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string PlayerModelPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";
        private const string SkillRoot = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills";
        private const string HeavenlySerenityPath = SkillRoot + "/SelfBuffSkills/Skill_HeavenlySerenity.asset";

        // One non-combat cast shared by every buff without its own animation: a weaponless spell gesture (Emerald AI
        // demo set), copied into the player's animation folder so the skills never point into a vendor demo.
        private const string BuffCastSource = "Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Sorceress/Animations/Sorceress@Attack1.FBX";
        private const string BuffCastPath = "Assets/_Game/Characters/Player/Animations/BuffCast.anim";
        private const float BuffCastSpeed = 1.2f;
        private const float BuffCastLock = 0.85f;
        private const float RageAttackCancel = 0.15f; // the rage is already gathered: an attack may cut the release short

        // Placeholder animations (Grruzam katana set). Clip names are generic, so these are picked by length and type.
        private const string Katana = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Katana_Blade/";
        private const string ClipFuryStrike = Katana + "3_Skills/M_katana_Blade@Skill_H.FBX";
        private const string ClipHaeseoStroke = Katana + "2_Attacks/3__Dash_Attack/M_katana_Blade@Dash_Attack_ver_B.FBX";
        private const string ClipGentlemansPen = Katana + "2_Attacks/0__3Combos/M_katana_Blade@Attack_3Combo_ALL_Inplace.FBX";
        private const string ClipDevilsStrike = Katana + "2_Attacks/5__Upper_Attack/M_katana_Blade@UpperAttack_ZeroHeight_Z0.FBX";
        private const string ClipFlashOfRage = Katana + "3_Skills/M_katana_Blade@Skill_B.FBX";
        private const string ClipSunraysStroke = Katana + "3_Skills/M_katana_Blade@Skill_A.FBX";
        private const string ClipPhantomThunder = Katana + "3_Skills/M_katana_Blade@Skill_G.FBX";
        private const string ClipThunderousDragon = Katana + "3_Skills/M_katana_Blade@Skill_D_Fix.FBX";
        private const string ClipAzureWall = Katana + "1_Movements/1__Idle/M_katana_Blade@Idle_A_to_B.FBX";
        private const string ClipLightningFlash = Katana + "1_Movements/0__Intro/M_katana_Blade@Intro.FBX";
        private const string ClipRageChargeLoop = Katana + "5_Revenges/Guard_Revenges/M_katana_Blade@Revenge_Guard_Loop.FBX";
        private const string ClipRageRelease = Katana + "5_Revenges/Guard_Revenges/M_katana_Blade@Revenge_Guard_End.FBX";
        private const string ClipSerenityEnter = Katana + "1_Movements/14__Idle_To_Crouch/M_katana_Blade@Idle_ver_A_To_Crouch_ver_A_Idle.FBX";
        private const string ClipSerenityLoop = Katana + "1_Movements/13__Crouch/Inplace/M_katana_Blade@Crouch_ver_A_Idle.FBX";

        private const float MeleeReach = 2f;
        private const float SingleHitTime = 0.4f;
        private const float AreaHitTime = 0.45f;
        private static readonly float[] TripleHitTimes = { 0.25f, 0.5f, 0.75f };
        // Last impact of the cast clip, as a fraction of it (measured from the blade hand's speed, frame by frame).
        private const float FuryStrikeHitTime = 0.32f;       // Skill_H: the overhead cut lands on frame 23 of 71
        private const float ThunderousDragonHitTime = 0.52f; // Skill_D: the leap comes down on frame 34 of 65
        private const float DevilsStrikeHitTime = 0.16f;     // UpperAttack: the rising cut connects on frame 12 of 75
        private const float SunraysStrikeHitTime = 0.33f;    // Skill_A: the heavy cut lands on frame 15 of 45
        // Skill_B: an opening cut (frame 5 of 50), then two in quick succession (17 and 19).
        private static readonly float[] FlashOfRageHitTimes = { 0.10f, 0.34f, 0.38f };
        // Skill_G: a rising cut (frame 8 of 63), a cut at the top of the leap (27) and the blow coming down (32).
        private static readonly float[] PhantomThunderHitTimes = { 0.13f, 0.43f, 0.51f };

        [MenuItem("Krytical Nova/Progression/Build Blade Skills")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(SkillFolder);
            var skills = new List<SkillDefinitionSO>
            {
                // id, name, level, book note (null = trainer), SP to learn, SP to max, chi, damage %, reach / radius
                Attack("FuryStrike", "Perimeter Cut", 1, null, 1, 30, 16, 167, 120, 234, 3, 5, area: true, hits: 1,
                    cooldown: 0.2f, clip: ClipFuryStrike, speed: 1.6f, castLock: 0.85f, holdToRepeat: true,
                    hitTimes: new[] { FuryStrikeHitTime }, canCrit: false,
                    description: "Sweeps the blade through every enemy around you. Hold the key to keep striking."),
                Attack("HaeseoStroke", "Clean Cut", 1, null, 1, 30, 7, 121, 124, 271, MeleeReach, MeleeReach, area: false, hits: 1,
                    cooldown: 1.5f, clip: ClipHaeseoStroke, speed: 1f, castLock: 0.9f,
                    description: "A single decisive stroke against your target."),
                Attack("GentlemansPen", "Signature Series", 1, null, 1, 30, 16, 229, 302, 782, MeleeReach, MeleeReach, area: false, hits: 3,
                    cooldown: 3f, clip: ClipGentlemansPen, speed: 1.2f, castLock: 0.95f,
                    description: "Three flowing cuts against your target."),
                Attack("DevilsStrike", "Severance", 30, null, 1, 35, 74, 170, 135, 326, MeleeReach, MeleeReach, area: false, hits: 1,
                    cooldown: 1.5f, clip: ClipDevilsStrike, speed: 1.2f, castLock: 0.9f, hitTimes: new[] { DevilsStrikeHitTime },
                    description: "A heavier single hit for seasoned blades."),
                Attack("FlashOfRage", "Hostile Takeover", 30, null, 1, 35, 142, 321, 330, 913, MeleeReach, MeleeReach, area: false, hits: 3,
                    cooldown: 3f, clip: ClipFlashOfRage, speed: 1.1f, castLock: 0.95f, hitTimes: FlashOfRageHitTimes,
                    description: "A furious three-hit flurry against your target."),
                // The id keeps its first name ("Stroke"): saves, the icon and the feel catalog are keyed on it.
                Attack("SunraysStroke", "Zero Hour", 60, "Lvl 60 Drop (Skill Chip)", 1, 40, 99, 194, 145, 336, MeleeReach, MeleeReach, area: false, hits: 1,
                    cooldown: 1.5f, clip: ClipSunraysStroke, speed: 1f, castLock: 0.9f, minBonus: 101, maxBonus: 110,
                    hitTimes: new[] { SunraysStrikeHitTime },
                    description: "A rising single hit that lands with extra force."),
                Attack("PhantomThunder", "Liquidation", 60, "Lvl 60 Drop (Skill Chip)", 1, 40, 187, 367, 354, 939, MeleeReach, MeleeReach, area: false, hits: 3,
                    cooldown: 3f, clip: ClipPhantomThunder, speed: 1.1f, castLock: 0.95f, minBonus: 101, maxBonus: 110,
                    hitTimes: PhantomThunderHitTimes,
                    description: "Three cuts that each land with extra force."),
                Attack("ThunderousDragon", "Market Crash", 80, "Lvl 80 War God Dossier", 50, 120, 806, 1203, 121, 434, 5, 7, area: true, hits: 1,
                    cooldown: 2.5f, clip: ClipThunderousDragon, speed: 1f, castLock: 0.95f,
                    hitTimes: new[] { ThunderousDragonHitTime }, leaps: true, holdToRepeat: true,
                    description: "War God technique. A leaping strike that crashes down on every enemy around you. Hold the key to keep striking."),

                Buff("AzureWall", "Firewall", 20, 9, 88, 66, 180, ClipAzureWall, 1.2f,
                    "Hardens your guard, raising Elemental Defense.",
                    new RankScaledModifier(StatType.ElementalDefense, ModifierType.PercentAdd, 1f, 10f)),
                Buff("LightningFlash", "Overclock", 28, 17, 353, 64, 180, ClipLightningFlash, 1.8f,
                    "Quickens the blade, raising all damage you deal.",
                    new RankScaledModifier(StatType.AttackPower, ModifierType.PercentAdd, 2f, 20f)),
            };

            var rage = RebuildSpiritualRage();
            GiveSerenityItsKneel();
            int animated = GiveBuffsSharedCast();

            AssetDatabase.SaveAssets();
            int added = WirePlayerPrefab(skills);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BladeSkills] {skills.Count} blade skills written to {SkillFolder}" +
                      $"{(rage != null ? ", Spiritual Rage reworked" : string.Empty)}; {animated} buffs given the shared cast; " +
                      $"{added} added to {PlayerPrefabPath}.");
        }

        /// <summary>
        /// Heavenly Serenity is a 20-second channel, so it gets a pose of its own instead of the shared cast: the body
        /// lowers into a kneel, holds it for the whole channel and rises when the channel ends. Left alone once it
        /// has a held pose, so a clip chosen in the Inspector survives a rebuild.
        /// </summary>
        [MenuItem("Krytical Nova/Progression/Give Heavenly Serenity Its Kneel")]
        public static bool GiveSerenityItsKneel()
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(HeavenlySerenityPath);
            if (skill == null) return false;
            if (skill.channelLoopClip != null) return true;
            AnimationClip enter = LoadClip(ClipSerenityEnter), loop = LoadClip(ClipSerenityLoop);
            if (enter == null || loop == null) return false;
            skill.castClip = enter;
            skill.castClipSpeed = 1f;
            skill.castLockNormalized = 1f;
            skill.channelLoopClip = loop;
            EditorUtility.SetDirty(skill);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Buffs without a cast clip are instant and silent. Each gets the shared buff cast: the whole body plays it
        /// and the caster is held in place for most of the clip, like an attack skill. Buffs that already have a clip
        /// (or get one in the Inspector) are left alone.
        /// </summary>
        [MenuItem("Krytical Nova/Progression/Give Buffs The Shared Cast Animation")]
        public static int GiveBuffsSharedCast()
        {
            AnimationClip cast = SharedBuffCast();
            if (cast == null) return 0;

            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:SkillDefinitionSO", new[] { SkillRoot }))
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (skill == null || skill.IsAttack || skill.castClip != null) continue;
                skill.castClip = cast;
                skill.castClipSpeed = BuffCastSpeed;
                skill.castLockNormalized = BuffCastLock;
                EditorUtility.SetDirty(skill);
                count++;
            }
            AssetDatabase.SaveAssets();
            return count;
        }

        private static AnimationClip SharedBuffCast()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BuffCastPath);
            if (clip == null)
            {
                AnimationClip source = LoadClip(BuffCastSource);
                if (source == null) return null;
                clip = Object.Instantiate(source);
                clip.name = "BuffCast";
                AssetDatabase.CreateAsset(clip, BuffCastPath);
            }
            // The source calls Emerald AI's ability spawner through an animation event; the player has no receiver.
            if (AnimationUtility.GetAnimationEvents(clip).Length > 0)
            {
                AnimationUtility.SetAnimationEvents(clip, System.Array.Empty<AnimationEvent>());
                EditorUtility.SetDirty(clip);
            }
            return clip;
        }

        private static SkillDefinitionSO Attack(string id, string displayName, int level, string bookNote, int pointsToLearn,
            int pointsToMax, float minChi, float maxChi, float minDamage, float maxDamage, float minRange, float maxRange,
            bool area, int hits, float cooldown, string clip, float speed, float castLock, string description,
            bool holdToRepeat = false, float minBonus = 100f, float maxBonus = 100f, float[] hitTimes = null, bool canCrit = true, bool leaps = false)
        {
            var skill = LoadOrCreate(id);
            Identity(skill, id, displayName, description, SkillCategory.Combat, level, bookNote, pointsToLearn, pointsToMax);
            skill.targetType = area ? SkillTargetType.AreaOfEffect : SkillTargetType.SingleTarget;
            skill.executionType = SkillExecutionType.Instant;
            skill.minChiCost = minChi;
            skill.maxChiCost = maxChi;
            skill.cooldown = cooldown;
            skill.minDuration = skill.maxDuration = 0f;
            skill.minDamagePercent = minDamage;
            skill.maxDamagePercent = maxDamage;
            skill.hitCount = hits;
            skill.minRange = minRange;
            skill.maxRange = maxRange;
            skill.minBonusDamagePercent = minBonus;
            skill.maxBonusDamagePercent = maxBonus;
            skill.holdToRepeat = holdToRepeat;
            skill.canCrit = canCrit;
            // Hits land on the skill's own measured impacts when it has them, else on the shared defaults.
            skill.hitTimesNormalized = hitTimes != null ? (float[])hitTimes.Clone()
                : hits > 1 ? (float[])TripleHitTimes.Clone() : new[] { area ? AreaHitTime : SingleHitTime };
            Presentation(skill, clip, speed, castLock);
            skill.castLiftCurve = leaps ? BakeLift(skill.castClip) : new AnimationCurve();
            EditorUtility.SetDirty(skill);
            return skill;
        }

        /// <summary>
        /// How far a humanoid clip carries the body up and down, in metres over the clip's time. The player's animator
        /// does not apply root motion, so a leap in the clip never leaves the ground unless the skill puts this back.
        /// </summary>
        public static AnimationCurve BakeLift(AnimationClip clip)
        {
            var curve = new AnimationCurve();
            if (clip == null) return curve;
            // A clip imported with its height baked into the pose already rises and falls on its own; lifting it as
            // well would double the leap.
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) is ModelImporter importer)
            {
                ModelImporterClipAnimation[] takes = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                foreach (ModelImporterClipAnimation take in takes)
                    if (take.name == clip.name && take.lockRootHeightY) return curve;
            }
            AnimationCurve body = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.y"));
            if (body == null || body.length < 2)
            {
                Debug.LogWarning($"[BladeSkills] {clip.name} has no body-height curve to bake a leap from.");
                return curve;
            }

            // The curve is in humanoid units; the player's rig says how many metres one of them is.
            float scale = 1f;
            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (modelPrefab != null)
            {
                var instance = Object.Instantiate(modelPrefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman) scale = animator.humanScale;
                Object.DestroyImmediate(instance);
            }

            const float step = 1f / 30f;
            float rest = body.Evaluate(0f);
            int frames = Mathf.CeilToInt(clip.length / step);
            for (int i = 0; i <= frames; i++)
            {
                float t = Mathf.Min(i * step, clip.length);
                curve.AddKey(new Keyframe(t, (body.Evaluate(t) - rest) * scale));
            }
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        private static SkillDefinitionSO Buff(string id, string displayName, int pointsToMax, float minChi, float maxChi,
            float minDuration, float maxDuration, string clip, float speed, string description, params RankScaledModifier[] modifiers)
        {
            var skill = LoadOrCreate(id);
            Identity(skill, id, displayName, description, SkillCategory.WeaponStance, 1, null, 1, pointsToMax);
            skill.targetType = SkillTargetType.Self;
            skill.executionType = SkillExecutionType.Instant;
            skill.minChiCost = minChi;
            skill.maxChiCost = maxChi;
            skill.cooldown = 1f;
            skill.minDuration = minDuration;
            skill.maxDuration = maxDuration;
            skill.rankScaledModifiers = modifiers;
            Presentation(skill, clip, speed, 0.9f);
            EditorUtility.SetDirty(skill);
            return skill;
        }

        /// <summary>Spiritual Rage: hold to charge, then the next hit deals up to double damage with a crit bonus.</summary>
        private static SkillDefinitionSO RebuildSpiritualRage()
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(SpiritualRagePath);
            if (skill == null)
            {
                Debug.LogWarning($"[BladeSkills] {SpiritualRagePath} not found; Spiritual Rage left as it is.");
                return null;
            }

            skill.description = "Hold to gather spiritual rage. Your next hit deals greatly increased damage with a higher critical rate "
                              + "(only the first hit of a multi-hit skill). The rage fades if unused.";
            skill.requiredCharacterLevel = 1;
            skill.requiresSkillBook = false;
            skill.minRank = 1;
            skill.maxRank = 20;
            skill.skillPointsToLearn = 1;
            skill.totalSkillPointsToMax = 20;
            skill.minChiCost = 8f;
            skill.maxChiCost = 117f;
            skill.cooldown = 1f;
            skill.minDuration = 15f;
            skill.maxDuration = 30f;
            skill.statModifiers = System.Array.Empty<StatModifierPayload>(); // replaced by the rank-scaled values below
            skill.rankScaledModifiers = new[]
            {
                new RankScaledModifier(StatType.AttackPower, ModifierType.PercentAdd, 14f, 100f),
                new RankScaledModifier(StatType.CriticalRate, ModifierType.Flat, 1.5f, 30f),
            };
            skill.consumedOnNextHit = true;
            skill.chargeTime = 1f;
            if (skill.chargeLoopClip == null) skill.chargeLoopClip = LoadClip(ClipRageChargeLoop);
            Presentation(skill, ClipRageRelease, 1f, 1f);
            skill.attackCancelNormalized = RageAttackCancel;
            EditorUtility.SetDirty(skill);
            return skill;
        }

        private static void Identity(SkillDefinitionSO skill, string id, string displayName, string description,
            SkillCategory category, int level, string bookNote, int pointsToLearn, int pointsToMax)
        {
            skill.skillId = id;
            skill.displayName = displayName;
            skill.description = description;
            skill.category = category;
            skill.requiredCharacterLevel = level;
            skill.requiresSkillBook = bookNote != null;
            skill.acquisitionMethod = bookNote != null ? SkillAcquisitionMethod.DropItem : SkillAcquisitionMethod.FactionTrainer;
            skill.acquisitionNote = bookNote ?? "Faction Instructor";
            skill.minRank = 1;
            // One point buys the first rank; every later rank costs one more, up to the skill's point cap.
            skill.maxRank = pointsToMax - pointsToLearn + 1;
            skill.skillPointsToLearn = pointsToLearn;
            skill.totalSkillPointsToMax = pointsToMax;
        }

        private static void Presentation(SkillDefinitionSO skill, string clipPath, float speed, float castLock)
        {
            if (skill.castClip != null) return; // keep an animation chosen in the Inspector
            skill.castClip = LoadClip(clipPath);
            skill.castClipSpeed = speed;
            skill.castLockNormalized = castLock;
        }

        private static SkillDefinitionSO LoadOrCreate(string id)
        {
            string path = $"{SkillFolder}/Skill_{id}.asset";
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(path);
            if (skill != null) return skill;
            skill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            AssetDatabase.CreateAsset(skill, path);
            return skill;
        }

        private static AnimationClip LoadClip(string fbxPath)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Debug.LogWarning($"[BladeSkills] No animation clip found in {fbxPath}.");
            return clip;
        }

        /// <summary>Adds the skills to the player's castable list and makes sure it can execute attack skills.</summary>
        private static int WirePlayerPrefab(List<SkillDefinitionSO> skills)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var combatSkills = root.GetComponent<PlayerCombatSkills>();
                if (combatSkills == null)
                {
                    Debug.LogError($"[BladeSkills] {PlayerPrefabPath} has no PlayerCombatSkills.");
                    return 0;
                }
                if (root.GetComponent<PlayerSkillAttacks>() == null) root.AddComponent<PlayerSkillAttacks>();

                var so = new SerializedObject(combatSkills);
                var list = so.FindProperty("availableSkills");
                var present = new HashSet<Object>();
                for (int i = 0; i < list.arraySize; i++) present.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

                int added = 0;
                foreach (var skill in skills)
                {
                    if (!present.Add(skill)) continue;
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = skill;
                    added++;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                return added;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
