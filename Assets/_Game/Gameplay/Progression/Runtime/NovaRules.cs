using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>How one Ascension attempt ended.</summary>
    public enum AscensionOutcome : byte
    {
        /// <summary>A level gained.</summary>
        Rise = 0,
        /// <summary>Nothing changed.</summary>
        Hold = 1,
        /// <summary>A level lost.</summary>
        Slip = 2,
        /// <summary>It would have slipped out of a Nova tier already reached: the Nova held and nothing changed.</summary>
        SlipHeld = 3,
    }

    /// <summary>
    /// A character's two Renown ladders as every machine knows them: the Ascension level, the level held on each
    /// Standings track, and which track's name is worn. The stat bonuses and the Nova are worked out from this alone.
    /// </summary>
    [Serializable]
    public struct NovaState : INetworkSerializable, IEquatable<NovaState>
    {
        public byte ascension;
        public byte vanguard, bulwark, ironblood, mindwell;
        /// <summary>0 = no Standing worn; otherwise the track's number plus one.</summary>
        public byte worn;

        public int Standing(StandingTrack track)
        {
            switch (track)
            {
                case StandingTrack.Vanguard: return vanguard;
                case StandingTrack.Bulwark: return bulwark;
                case StandingTrack.Ironblood: return ironblood;
                case StandingTrack.Mindwell: return mindwell;
                default: return 0;
            }
        }

        public NovaState WithStanding(StandingTrack track, int level)
        {
            NovaState next = this;
            byte value = (byte)Mathf.Clamp(level, 0, byte.MaxValue);
            switch (track)
            {
                case StandingTrack.Vanguard: next.vanguard = value; break;
                case StandingTrack.Bulwark: next.bulwark = value; break;
                case StandingTrack.Ironblood: next.ironblood = value; break;
                case StandingTrack.Mindwell: next.mindwell = value; break;
            }
            return next;
        }

        /// <summary>The track whose name is worn, when one is and the character holds a level on it.</summary>
        public bool TryGetWorn(out StandingTrack track)
        {
            track = (StandingTrack)(worn - 1);
            return worn > 0 && worn <= NovaRules.TrackCount && Standing(track) > 0;
        }

        public NovaState Wearing(StandingTrack track) { NovaState next = this; next.worn = (byte)((int)track + 1); return next; }
        public NovaState WearingNone() { NovaState next = this; next.worn = 0; return next; }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ascension);
            serializer.SerializeValue(ref vanguard);
            serializer.SerializeValue(ref bulwark);
            serializer.SerializeValue(ref ironblood);
            serializer.SerializeValue(ref mindwell);
            serializer.SerializeValue(ref worn);
        }

        public bool Equals(NovaState other) => ascension == other.ascension && vanguard == other.vanguard && bulwark == other.bulwark
            && ironblood == other.ironblood && mindwell == other.mindwell && worn == other.worn;
        public override bool Equals(object obj) => obj is NovaState other && Equals(other);
        public override int GetHashCode() => ascension | (vanguard << 8) | (bulwark << 12) | (ironblood << 16) | (mindwell << 20) | (worn << 24);
    }

    /// <summary>
    /// The rules of Ascension and Nova Standings, free of networking and of chance: the roll is passed in, so the same
    /// inputs always give the same result and the rules can be tested. The server calls them with a real roll; the
    /// Herald's window calls them to show odds, costs and reasons before anything is spent. Numbers come from
    /// <see cref="NovaSettings"/>.
    /// </summary>
    public static class NovaRules
    {
        public const int TrackCount = 4;
        public const string ReasonNoSettings = "The Herald is not set up.";
        public const string ReasonAscensionComplete = "Your Ascension is complete.";
        public const string ReasonStandingComplete = "That Standing is complete.";
        public const string ReasonNoRenown = "You don't have enough Renown.";
        public const string ReasonNoCreds = "You don't have enough Creds.";
        public const string ReasonNotHeld = "You hold no Standing on that track yet.";

        private static readonly string[] Numerals = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };

        // ---------------------------------------------------------------- Ascension

        /// <summary>The Nova tier of an Ascension level: 0 with none, then 1 for the first stretch of levels, and so on.</summary>
        public static int Tier(AscensionSettings settings, int level)
        {
            if (settings == null || level <= 0) return 0;
            return (Mathf.Min(level, settings.maxLevel) - 1) / Mathf.Max(1, settings.levelsPerTier) + 1;
        }

        public static int TierCount(AscensionSettings settings) => settings == null ? 0 : Tier(settings, settings.maxLevel);

        /// <summary>The lowest Ascension level of the tier a level is in: where a slip stops.</summary>
        public static int TierFloor(AscensionSettings settings, int level)
        {
            int tier = Tier(settings, level);
            return tier <= 0 ? 0 : (tier - 1) * Mathf.Max(1, settings.levelsPerTier) + 1;
        }

        /// <summary>Levels still to gain before the Nova changes; 0 at the last tier.</summary>
        public static int LevelsToNextTier(AscensionSettings settings, int level)
        {
            if (settings == null) return 0;
            int tier = Tier(settings, level);
            if (tier >= TierCount(settings)) return 0;
            return tier * Mathf.Max(1, settings.levelsPerTier) + 1 - Mathf.Max(0, level);
        }

        /// <summary>The chances of an attempt made at this level.</summary>
        public static AscensionOdds OddsAt(AscensionSettings settings, int level)
        {
            if (settings?.odds == null || settings.odds.Length == 0) return new AscensionOdds(int.MaxValue, 100f, 0f, 0f);
            foreach (AscensionOdds band in settings.odds)
                if (level <= band.upTo) return band;
            return settings.odds[settings.odds.Length - 1];
        }

        /// <summary>True when a slip at this level costs nothing: there is no level to lose, or the Nova holds its tier.</summary>
        public static bool SlipIsHarmless(AscensionSettings settings, int level) =>
            level <= 0 || (settings != null && settings.slipKeepsTier && level <= TierFloor(settings, level));

        /// <summary>Why an attempt cannot be made, or null when it can.</summary>
        public static string AscendBlocked(AscensionSettings settings, int level, int renown, long creds)
        {
            if (settings == null) return ReasonNoSettings;
            if (level >= settings.maxLevel) return ReasonAscensionComplete;
            if (renown < settings.renownCost) return ReasonNoRenown;
            if (creds < settings.credsCost) return ReasonNoCreds;
            return null;
        }

        /// <summary>
        /// One attempt. <paramref name="roll01"/> is a number from 0 up to (not including) 1; the three chances are
        /// read as shares of their own total, so they need not add up to exactly 100.
        /// </summary>
        public static AscensionOutcome Resolve(AscensionSettings settings, int level, float roll01, out int next)
        {
            next = level;
            if (settings == null || level >= settings.maxLevel) return AscensionOutcome.Hold;
            AscensionOdds odds = OddsAt(settings, level);
            float rise = Mathf.Max(0f, odds.rise), hold = Mathf.Max(0f, odds.hold), slip = Mathf.Max(0f, odds.slip);
            float total = rise + hold + slip;
            if (total <= 0f) return AscensionOutcome.Hold;
            float roll = Mathf.Clamp01(roll01) * total;
            if (roll < rise)
            {
                next = level + 1;
                return AscensionOutcome.Rise;
            }
            if (roll < rise + hold || level <= 0) return AscensionOutcome.Hold;
            if (SlipIsHarmless(settings, level)) return AscensionOutcome.SlipHeld;
            next = level - 1;
            return AscensionOutcome.Slip;
        }

        /// <summary>"V" for 5. The Nova's tier is always shown this way.</summary>
        public static string Numeral(int tier) => tier > 0 && tier < Numerals.Length ? Numerals[tier] : tier.ToString();

        /// <summary>"Ascension +37 · Nova V", or "Ascension +0" with no Nova yet.</summary>
        public static string AscensionLine(AscensionSettings settings, int level)
        {
            int tier = Tier(settings, level);
            return tier > 0 ? $"Ascension +{level}  ·  Nova {Numeral(tier)}" : $"Ascension +{level}";
        }

        // ---------------------------------------------------------------- Nova Standings

        /// <summary>Renown for the level after <paramref name="level"/> on a track, or -1 when the track is complete.</summary>
        public static int StandingCost(StandingSettings settings, int level)
        {
            if (settings?.renownCosts == null || level < 0 || level >= settings.renownCosts.Length) return -1;
            return Mathf.Max(0, settings.renownCosts[level]);
        }

        /// <summary>Why the next level of a track cannot be bought, or null when it can. Levels are bought in order.</summary>
        public static string StandingBlocked(StandingSettings settings, int level, int renown)
        {
            if (settings == null) return ReasonNoSettings;
            int cost = StandingCost(settings, level);
            if (cost < 0) return ReasonStandingComplete;
            return renown < cost ? ReasonNoRenown : null;
        }

        /// <summary>Percent of its stat a track gives at a level.</summary>
        public static float StandingPercent(StandingSettings settings, int level) =>
            settings == null ? 0f : Mathf.Clamp(level, 0, settings.MaxLevel) * settings.percentPerLevel;

        public static bool TryGetTrack(StandingSettings settings, StandingTrack track, out StandingTrackInfo info)
        {
            info = default;
            if (settings?.tracks == null) return false;
            foreach (StandingTrackInfo candidate in settings.tracks)
            {
                if (candidate.track != track) continue;
                info = candidate;
                return true;
            }
            return false;
        }

        /// <summary>What a faction calls a Standing level ("Thane"); the first faction's word when it has none of its own.</summary>
        public static string RankWord(StandingSettings settings, FactionId faction, int level)
        {
            if (settings?.rankWords == null || level <= 0) return null;
            string[] words = null;
            foreach (FactionRankWords set in settings.rankWords)
            {
                if (set.words == null || set.words.Length == 0) continue;
                if (words == null) words = set.words;
                if (set.faction != faction) continue;
                words = set.words;
                break;
            }
            return words == null ? null : words[Mathf.Min(level, words.Length) - 1];
        }

        /// <summary>The name a Standing is worn as: track and rank word ("Vanguard Thane"). Null below level 1.</summary>
        public static string StandingName(StandingSettings settings, FactionId faction, StandingTrack track, int level)
        {
            if (level <= 0 || !TryGetTrack(settings, track, out StandingTrackInfo info)) return null;
            string word = RankWord(settings, faction, level);
            return string.IsNullOrEmpty(word) ? info.name : info.name + " " + word;
        }

        /// <summary>The Standing a character wears, or null when it wears none.</summary>
        public static string WornName(StandingSettings settings, FactionId faction, in NovaState state) =>
            state.TryGetWorn(out StandingTrack track) ? StandingName(settings, faction, track, state.Standing(track)) : null;

        // ---------------------------------------------------------------- what both ladders give

        /// <summary>
        /// Every stat bonus the two ladders give: the four attributes from Ascension, and a percent of its stat from
        /// every Standings track the character holds a level on (worn or not).
        /// </summary>
        public static void AddModifiers(NovaSettings settings, in NovaState state, object source, List<StatModifier> results)
        {
            if (settings == null || results == null) return;
            if (settings.ascension != null && state.ascension > 0 && settings.ascension.attributesPerLevel > 0)
            {
                float each = Mathf.Min(state.ascension, settings.ascension.maxLevel) * settings.ascension.attributesPerLevel;
                results.Add(new StatModifier(StatType.Strength, ModifierType.Flat, each, source));
                results.Add(new StatModifier(StatType.Agility, ModifierType.Flat, each, source));
                results.Add(new StatModifier(StatType.Vitality, ModifierType.Flat, each, source));
                results.Add(new StatModifier(StatType.Spirit, ModifierType.Flat, each, source));
            }
            if (settings.standings?.tracks == null) return;
            foreach (StandingTrackInfo info in settings.standings.tracks)
            {
                float percent = StandingPercent(settings.standings, state.Standing(info.track));
                if (percent > 0f) results.Add(new StatModifier(info.stat, ModifierType.PercentAdd, percent, source));
            }
        }
    }
}
