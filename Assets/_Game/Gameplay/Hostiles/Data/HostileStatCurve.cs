using System;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// Every Hostile's numbers by level, in one place. The game's damage is Attack minus Defense, so a table of
    /// plain numbers drifts as soon as gear or stats are retuned. Instead this asset describes the player a
    /// Hostile of each level is made for (stat points, gear of the expected rarity) and says how the fight should
    /// go: how many hits the player needs, how many the Hostile needs, how often each side lands. The Hostile's
    /// stats follow from that. Change a number here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Hostiles/Hostile Stat Curve")]
    public sealed class HostileStatCurve : ScriptableObject
    {
        /// <summary>What a tier multiplies. Attack multiplies the damage that gets through, not the raw stat.</summary>
        [Serializable]
        public struct TierMultipliers
        {
            public HostileTier tier;
            [Min(0f)] public float health;
            [Tooltip("Multiplies the damage a hit does to the reference player, so 1.6 means the player lasts 1.6 times fewer hits.")]
            [Min(0f)] public float damage;
            [Min(0f)] public float defense;
            [Min(0f)] public float hitRate;
            [Min(0f)] public float evasion;
            [Min(0f)] public float experience;
            [Min(0f)] public float creds;
        }

        [Header("The player a Hostile is made for")]
        [Tooltip("Read for the points to attribute ratios and the hit chance limits. Empty = the game's defaults.")]
        public CombatStatTuningData tuning;
        [Tooltip("Read for the XP a level takes and the stat points a level gives.")]
        public CharacterProgressionData progression;
        [Tooltip("Each attribute of a new character.")]
        [Min(0)] public int startingAttribute = 10;
        [Tooltip("Share of the player's stat points in Strength, Vitality, Agility and Spirit. Should add up to 1.")]
        public Vector4 pointShares = new Vector4(0.5f, 0.2f, 0.2f, 0.1f);
        [Tooltip("Attack Power and Defense a class has before gear (CharacterClassData).")]
        public Vector2 classAttackAndDefense = new Vector2(15f, 5f);
        [Tooltip("Max HP a class has before level and Vitality (CharacterClassData).")]
        [Min(1f)] public float classHealth = 100f;
        [Tooltip("Weapon Attack Power per level, before rarity (GearStatTable).")]
        [Min(0f)] public float gearAttackPerLevel = 5.2f;
        [Tooltip("Defense per level of a full set, before rarity: Armor 2.1 plus five pieces at 0.55 (GearStatTable).")]
        [Min(0f)] public float gearDefensePerLevel = 4.85f;
        [Tooltip("Hit Rate per level of weapon and gloves together (GearStatTable).")]
        [Min(0f)] public float gearHitRatePerLevel = 3.5f;
        [Tooltip("Evasion per level of armor and boots together (GearStatTable).")]
        [Min(0f)] public float gearEvasionPerLevel = 0.7f;
        [Tooltip("The rarity multiplier of the gear a player is expected to wear: at level 1 (x), at the level cap (y), at the last Prime level (z).")]
        public Vector3 expectedGearRarity = new Vector3(1f, 1.7f, 2f);
        [Tooltip("The player level cap. Hostiles above it are Prime grounds Hostiles.")]
        [Min(1)] public int levelCap = 112;
        [Tooltip("Prime levels above the cap.")]
        [Min(1)] public int primeLevels = 33;

        [Header("How an even-level fight should go (Normal tier)")]
        [Tooltip("Plain hits (no critical, skill multiplier 1) the reference player needs to kill a Hostile.")]
        [Min(1f)] public float hitsToKill = 5f;
        [Tooltip("The Hostile's Defense as a share of the reference player's Attack Power. Higher punishes weak gear harder.")]
        [Range(0f, 0.9f)] public float defenseShare = 0.3f;
        [Tooltip("Hits a Hostile needs to kill the reference player.")]
        [Min(1f)] public float hitsToDie = 25f;
        [Tooltip("How often the reference player's hits land on a Hostile.")]
        [Range(0.1f, 0.95f)] public float playerHitChance = 0.9f;
        [Tooltip("How often a Hostile's hits land on the reference player.")]
        [Range(0.1f, 0.95f)] public float hostileHitChance = 0.75f;
        [Tooltip("A Hostile's Critical Rate, percent.")]
        [Range(0f, 100f)] public float criticalRate = 5f;

        [Header("Rewards")]
        [Tooltip("Even-level kills to gain a level: this (x) plus this (y) per level.")]
        public Vector2 killsPerLevel = new Vector2(40f, 6f);
        [Tooltip("Creds per kill: this (x) plus this (y) per level times the expected gear rarity.")]
        public Vector2 creds = new Vector2(3f, 1.1f);
        [Tooltip("Creds roll this share above or below the value.")]
        [Range(0f, 1f)] public float credsSpread = 0.2f;
        [Tooltip("Chance that a kill drops Creds at all.")]
        [Range(0f, 1f)] public float credsDropChance = 0.7f;

        [Header("Behaviour")]
        [Tooltip("Aggro radius of attack-on-sight Hostiles, metres: base (x), plus one metre per this many levels (y), never above (z).")]
        public Vector3 aggroRadius = new Vector3(8f, 28f, 12f);
        [Tooltip("Walking speed, metres a second.")]
        [Min(0f)] public float walkSpeed = 1.8f;
        [Tooltip("Running speed, metres a second: base (x) plus this per level (y).")]
        public Vector2 runSpeed = new Vector2(4.2f, 0.006f);
        [Tooltip("Respawn seconds of a Normal Hostile: base (x), plus this per level (y), never under (z) or over (w).")]
        public Vector4 respawnSeconds = new Vector4(8f, 0.12f, 8f, 22f);
        [Tooltip("Respawn seconds of a Bruiser: a random time between these.")]
        public Vector2 bruiserRespawnSeconds = new Vector2(480f, 900f);

        [Header("Tiers")]
        public TierMultipliers[] tiers =
        {
            new TierMultipliers { tier = HostileTier.Normal, health = 1f, damage = 1f, defense = 1f, hitRate = 1f, evasion = 1f, experience = 1f, creds = 1f },
            new TierMultipliers { tier = HostileTier.Bruiser, health = 8f, damage = 1.6f, defense = 1.5f, hitRate = 1.2f, evasion = 1.2f, experience = 6f, creds = 5f },
        };

        /// <summary>The player a Hostile of a level is balanced against.</summary>
        public struct ReferencePlayer
        {
            public float Attack, Defense, HitRate, Evasion, MaxHealth, GearRarity;
        }

        public int MaxLevel => levelCap + primeLevels;

        /// <summary>The multiplier of the gear a player of this level is expected to wear.</summary>
        public float GearRarityAt(int level)
        {
            if (level <= levelCap) return Mathf.Lerp(expectedGearRarity.x, expectedGearRarity.y, level / (float)levelCap);
            return Mathf.Lerp(expectedGearRarity.y, expectedGearRarity.z, Mathf.Clamp01((level - levelCap) / (float)primeLevels));
        }

        public ReferencePlayer ReferenceAt(int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            // A player stops gaining levels at the cap; Prime grounds ask for better gear, not more points.
            int playerLevel = Mathf.Min(level, levelCap);
            float rarity = GearRarityAt(level);
            float points = StatPointsAt(playerLevel);
            float strength = startingAttribute + points * pointShares.x;
            float vitality = startingAttribute + points * pointShares.y;
            float agility = startingAttribute + points * pointShares.z;
            float spirit = startingAttribute + points * pointShares.w;

            float strToAp = tuning != null ? tuning.strengthToAttackPower : 1f;
            float spiritToAp = tuning != null ? tuning.spiritToAttackPower : 1f;
            float strToHit = tuning != null ? tuning.strengthToHitRate : 2f;
            float agiToDp = tuning != null ? tuning.agilityToDefensePower : 1f;
            float agiToEvade = tuning != null ? tuning.agilityToEvasion : 2f;
            float vitToEvade = tuning != null ? tuning.vitalityToEvasion : 1f;
            float vitToHp = tuning != null ? tuning.vitalityToHealth : 20f;
            float hpPerLevel = tuning != null ? tuning.healthPerLevel : 15f;

            return new ReferencePlayer
            {
                GearRarity = rarity,
                Attack = strength * strToAp + spirit * spiritToAp + classAttackAndDefense.x + gearAttackPerLevel * playerLevel * rarity,
                Defense = agility * agiToDp + classAttackAndDefense.y + gearDefensePerLevel * playerLevel * rarity,
                HitRate = strength * strToHit + gearHitRatePerLevel * playerLevel,
                Evasion = agility * agiToEvade + vitality * vitToEvade + gearEvasionPerLevel * playerLevel,
                MaxHealth = classHealth + (playerLevel - 1) * hpPerLevel + vitality * vitToHp,
            };
        }

        public HostileStats Evaluate(int level, HostileTier tier = HostileTier.Normal)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            ReferencePlayer player = ReferenceAt(level);
            TierMultipliers m = MultipliersOf(tier);

            // Defense first: it decides how much of the player's hit gets through, and health is that many hits.
            // It may never swallow the whole hit, whatever a tier multiplies it by.
            float defense = Mathf.Min(player.Attack * defenseShare * m.defense, player.Attack * 0.9f);
            float health = hitsToKill * (player.Attack - player.Attack * defenseShare) * m.health;
            // Attack is what it takes to get past the player's Defense, plus the damage each hit should do.
            float attack = player.Defense + player.MaxHealth / hitsToDie * m.damage;
            // Chance to hit is Hit / (Hit + Evasion): solve for the stat that gives the chance asked for.
            float hitRate = player.Evasion * hostileHitChance / (1f - hostileHitChance) * m.hitRate;
            float evasion = player.HitRate * (1f - playerHitChance) / playerHitChance * m.evasion;

            bool bruiser = tier == HostileTier.Bruiser;
            float respawn = Mathf.Clamp(respawnSeconds.x + respawnSeconds.y * level, respawnSeconds.z, respawnSeconds.w);
            return new HostileStats
            {
                Level = level,
                Tier = tier,
                MaxHealth = Mathf.Round(health),
                Attack = Mathf.Round(attack),
                Defense = Mathf.Round(defense),
                HitRate = Mathf.Round(hitRate),
                Evasion = Mathf.Round(evasion),
                CriticalRate = criticalRate,
                Experience = Mathf.Max(1, Mathf.RoundToInt(ExperienceToNext(level) / Mathf.Max(1f, killsPerLevel.x + killsPerLevel.y * level) * m.experience)),
                Creds = Mathf.RoundToInt((creds.x + creds.y * level * player.GearRarity) * m.creds),
                AggroRadius = Mathf.Min(aggroRadius.z, aggroRadius.x + (aggroRadius.y > 0f ? level / aggroRadius.y : 0f)),
                WalkSpeed = walkSpeed,
                RunSpeed = runSpeed.x + runSpeed.y * level,
                RespawnSeconds = bruiser ? bruiserRespawnSeconds.x : respawn,
                RespawnSecondsMax = bruiser ? bruiserRespawnSeconds.y : respawn,
            };
        }

        /// <summary>The multipliers of a tier; all ones for a tier that has no row.</summary>
        public TierMultipliers MultipliersOf(HostileTier tier)
        {
            if (tiers != null)
                for (int i = 0; i < tiers.Length; i++)
                    if (tiers[i].tier == tier) return tiers[i];
            return new TierMultipliers { tier = tier, health = 1f, damage = 1f, defense = 1f, hitRate = 1f, evasion = 1f, experience = 1f, creds = 1f };
        }

        /// <summary>Stat points a player has had by a level, the starting ones included.</summary>
        private float StatPointsAt(int playerLevel)
        {
            if (progression == null) return 5f * playerLevel;
            float points = progression.startingStatPoints;
            for (int level = 1; level < playerLevel; level++)
                if (progression.TryGetStep(level, out CharacterLevelStep step)) points += step.statPointReward;
            return points;
        }

        /// <summary>XP from a level to the next. At and above the cap, the last level's.</summary>
        private float ExperienceToNext(int level)
        {
            if (progression == null) return 100f * Mathf.Pow(level, 1.5f);
            int row = Mathf.Clamp(level, 1, Mathf.Max(1, progression.MaxLevel - 1));
            return progression.TryGetStep(row, out CharacterLevelStep step) ? step.experienceRequired : 1f;
        }
    }
}
