using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// What the gap between a Hostile's level and a player's means, read from <see cref="LevelGapSettings"/>.
    /// Free of networking and of scene objects so it can be tested. Gap = Hostile level minus player level.
    /// </summary>
    public static class LevelGapRules
    {
        public static int Gap(int hostileLevel, int playerLevel) => hostileLevel - playerLevel;

        /// <summary>The band a gap falls in. Below the first band it is the first, above the last the last.</summary>
        public static LevelGapSettings.Band BandOf(LevelGapSettings settings, int gap)
        {
            LevelGapSettings.Band[] bands = settings.bands;
            if (bands == null || bands.Length == 0) return Neutral;
            if (gap < bands[0].fromGap) return bands[0];
            for (int i = 0; i < bands.Length; i++)
                if (gap >= bands[i].fromGap && gap <= bands[i].toGap) return bands[i];
            return bands[bands.Length - 1];
        }

        private static readonly LevelGapSettings.Band Neutral = new LevelGapSettings.Band
        {
            name = "White", nameColor = Color.white, experiencePercent = new[] { 100f }, dropPercent = 100f, gearDrops = true
        };

        /// <summary>XP a kill is worth at this gap, as a percentage of the Hostile's own.</summary>
        public static float ExperiencePercent(LevelGapSettings settings, int gap)
        {
            LevelGapSettings.Band band = BandOf(settings, gap);
            float[] steps = band.experiencePercent;
            if (steps == null || steps.Length == 0) return 100f;
            // One value covers the whole band; several are one per gap, from the band's lowest gap up.
            int index = steps.Length == 1 ? 0 : Mathf.Clamp(gap - band.fromGap, 0, steps.Length - 1);
            return Mathf.Max(0f, steps[index]);
        }

        public static Color NameColor(LevelGapSettings settings, int gap) => BandOf(settings, gap).nameColor;
        public static float DropPercent(LevelGapSettings settings, int gap) => BandOf(settings, gap).dropPercent;
        public static bool GearDrops(LevelGapSettings settings, int gap) => BandOf(settings, gap).gearDrops;
        public static float HitChanceModifier(LevelGapSettings settings, int gap) => BandOf(settings, gap).hitChanceModifier;

        /// <summary>
        /// The gap a Squad member is paid by. A member far below the Squad's highest member is carried: when the
        /// highest member's gap is worse than the member's own by the settings' number of levels or more, the
        /// member is paid by the highest member's gap instead.
        /// </summary>
        public static int SquadGap(LevelGapSettings settings, int memberGap, int highestMemberGap)
        {
            return memberGap - highestMemberGap >= settings.squadCarryLevels ? highestMemberGap : memberGap;
        }

        /// <summary>XP lost on a death while hunting: a share of the level's XP, never more than the player has in it.</summary>
        public static int DeathLoss(LevelGapSettings settings, int experienceForLevel, int experienceHeld)
        {
            if (experienceForLevel <= 0 || experienceHeld <= 0) return 0;
            int loss = Mathf.RoundToInt(experienceForLevel * settings.deathExperienceLossPercent / 100f);
            return Mathf.Clamp(loss, 0, experienceHeld);
        }
    }
}
