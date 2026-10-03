using System.Collections.Generic;
using System.Linq;
using KryticalNova.Combat;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the Blade skill set (attacks and blade buffs), reworks Spiritual Rage into a charged one-hit buff, and
    /// wires everything onto the Player prefab. Safe to re-run: numbers are rewritten from the table below, while
    /// animation clips and VFX are only filled in when empty, so choices made in the Inspector survive a rebuild.
    /// </summary>
    public static class BladeSkillBuilder
    {
        private const string SkillFolder = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/BladeSkills";
        private const string SpiritualRagePath = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/SelfBuffSkills/Skill_SpiritualRage.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        // Placeholder animations (Grruzam katana set). Clip names are generic, so these are picked by length and type.
        private const string Katana = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Katana_Blade/";
        private const string ClipFuryStrike = Katana + "3_Skills/M_katana_Blade@Skill_E.FBX";
        private const string ClipHaeseoStroke = Katana + "2_Attacks/3__Dash_Attack/M_katana_Blade@Dash_Attack_ver_B.FBX";
        private const string ClipGentlemansPen = Katana + "2_Attacks/0__3Combos/M_katana_Blade@Attack_3Combo_ALL_Inplace.FBX";
        private const string ClipDevilsStrike = Katana + "3_Skills/M_katana_Blade@Skill_A.FBX";
        private const string ClipFlashOfRage = Katana + "3_Skills/M_katana_Blade@Skill_D_Fix.FBX";
        private const string ClipSunraysStroke = Katana + "2_Attacks/5__Upper_Attack/M_katana_Blade@UpperAttack_ZeroHeight_Z0.FBX";
        private const string ClipPhantomThunder = Katana + "3_Skills/M_katana_Blade@Skill_H.FBX";
        private const string ClipThunderousDragon = Katana + "3_Skills/M_Katana_Blade@Skill_I.FBX";
        private const string ClipAzureWall = Katana + "1_Movements/1__Idle/M_katana_Blade@Idle_A_to_B.FBX";
        private const string ClipLightningFlash = Katana + "1_Movements/0__Intro/M_katana_Blade@Intro.FBX";
        private const string ClipRageChargeLoop = Katana + "5_Revenges/Guard_Revenges/M_katana_Blade@Revenge_Guard_Loop.FBX";
        private const string ClipRageRelease = Katana + "5_Revenges/Guard_Revenges/M_katana_Blade@Revenge_Guard_End.FBX";

        private const float MeleeReach = 2f;
        private const float SingleHitTime = 0.4f;
        private const float AreaHitTime = 0.45f;
        private static readonly float[] TripleHitTimes = { 0.25f, 0.5f, 0.75f };

        [MenuItem("Krytical Nova/Progression/Build Blade Skills")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(SkillFolder);
            var skills = new List<SkillDefinitionSO>
            {
                // id, name, level, book note (null = trainer), SP to learn, SP to max, chi, damage %, reach / radius
                Attack("FuryStrike", "Fury Strike", 1, null, 1, 30, 16, 167, 120, 234, 3, 5, area: true, hits: 1,
                    cooldown: 0.2f, clip: ClipFuryStrike, speed: 1.6f, castLock: 0.8f, holdToRepeat: true,
                    description: "Sweeps the blade through every enemy around you. Hold the key to keep striking."),
                Attack("HaeseoStroke", "Haeseo Stroke", 1, null, 1, 30, 7, 121, 124, 271, MeleeReach, MeleeReach, area: false, hits: 1,
                    cooldown: 1.5f, clip: ClipHaeseoStroke, speed: 1f, castLock: 0.9f,
                    description: "A single decisive stroke against your target."),
                Attack("GentlemansPen", "Gentleman's Pen", 1, null, 1, 30, 16, 229, 302, 782, MeleeReach, MeleeReach, area: false, hits: 3,
                    cooldown: 3f, clip: ClipGentlemansPen, speed: 1.2f, castLock: 0.95f,
                    description: "Three flowing cuts against your target."),
                Attack("DevilsStrike", "Devil's Strike", 30, null, 1, 35, 74, 170, 135, 326, MeleeReach, MeleeReach, area: false, hits: 1,
                    cooldown: 1.5f, clip: ClipDevilsStrike, speed: 1f, castLock: 0.9f,
                    description: "A heavier single hit for seasoned blades."),
                Attack("FlashOfRage", "Flash of Rage", 30, null, 1, 35, 142, 321, 330, 913, MeleeReach, MeleeReach, area: false, hits: 3,
                    cooldown: 3f, clip: ClipFlashOfRage, speed: 1.1f, castLock: 0.95f,
                    description: "A furious three-hit flurry against your target."),
                Attack("SunraysStroke", "Sunray's Stroke", 60, "Lvl 60 Drop Item (Skill Book)", 1, 40, 99, 194, 145, 336, MeleeReach, MeleeReach, area: false, hits: 1,
                    cooldown: 1.5f, clip: ClipSunraysStroke, speed: 1.2f, castLock: 0.9f, minBonus: 101, maxBonus: 110,
                    description: "A rising single hit charged with Yang energy."),
                Attack("PhantomThunder", "Phantom Thunder", 60, "Lvl 60 Drop Item (Skill Book)", 1, 40, 187, 367, 354, 939, MeleeReach, MeleeReach, area: false, hits: 3,
                    cooldown: 3f, clip: ClipPhantomThunder, speed: 1.1f, castLock: 0.95f, minBonus: 101, maxBonus: 110,
                    description: "Three thunderous cuts charged with Yang energy."),
                Attack("ThunderousDragon", "Thunderous Dragon", 80, "Lvl 80 Wargod Skill Book (Drop)", 50, 120, 806, 1203, 121, 434, 5, 7, area: true, hits: 1,
                    cooldown: 6f, clip: ClipThunderousDragon, speed: 1f, castLock: 0.95f,
                    description: "Wargod technique. A dragon of thunder tears through every enemy around you."),

                Buff("AzureWall", "Azure Wall", 20, 9, 88, 66, 180, ClipAzureWall, 1.2f,
                    "Hardens your guard, raising Elemental Defense.",
                    new RankScaledModifier(StatType.ElementalDefense, ModifierType.PercentAdd, 1f, 10f)),
                Buff("LightningFlash", "Lightning Flash", 28, 17, 353, 64, 180, ClipLightningFlash, 1.8f,
                    "Quickens the blade, raising all damage you deal.",
                    new RankScaledModifier(StatType.AttackPower, ModifierType.PercentAdd, 2f, 20f)),
            };

            var rage = RebuildSpiritualRage();

            AssetDatabase.SaveAssets();
            int added = WirePlayerPrefab(skills);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BladeSkills] {skills.Count} blade skills written to {SkillFolder}" +
                      $"{(rage != null ? ", Spiritual Rage reworked" : string.Empty)}; {added} added to {PlayerPrefabPath}.");
        }

        private static SkillDefinitionSO Attack(string id, string displayName, int level, string bookNote, int pointsToLearn,
            int pointsToMax, float minChi, float maxChi, float minDamage, float maxDamage, float minRange, float maxRange,
            bool area, int hits, float cooldown, string clip, float speed, float castLock, string description,
            bool holdToRepeat = false, float minBonus = 100f, float maxBonus = 100f)
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
            skill.hitTimesNormalized = hits > 1 ? (float[])TripleHitTimes.Clone() : new[] { area ? AreaHitTime : SingleHitTime };
            Presentation(skill, clip, speed, castLock);
            EditorUtility.SetDirty(skill);
            return skill;
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
            skill.acquisitionNote = bookNote ?? "Faction Skill Trainer";
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
