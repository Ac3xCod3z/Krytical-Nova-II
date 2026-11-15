using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// The season as the server decides it and every client draws it (owner's DAY list item 21): which season blends to which and when,
    /// the calendar's season (for "day 5 of 14"), and the snow cover as an anchor plus a rate, so every machine works out the same cover
    /// and blend from the shared clock and nothing is sent per frame. Times are on the shared clock (<c>NetClock</c>).
    /// </summary>
    public struct SeasonState : IEquatable<SeasonState>
    {
        public const byte FlagGmHold = 1, FlagAlwaysWinter = 2, FlagGmTemperature = 4, FlagGmSpell = 8;
        /// <summary><see cref="Spell"/> when the season has no spells.</summary>
        public const byte NoSpell = 255;

        public byte FromSeason, ToSeason;
        public double BlendStart;
        public float BlendSeconds;
        /// <summary>When the calendar's season began and how long it lasts (shared clock).</summary>
        public double SeasonStart;
        public float SeasonSeconds;
        /// <summary>The calendar's season (the GM hold aside).</summary>
        public byte CalendarSeason;
        public float SnowCover;
        public double SnowAnchor;
        public float SnowRate;
        public float SnowFloor;
        /// <summary>The cover a build-up stops at (light snow stops low, a blizzard high); 1 while it melts.</summary>
        public float SnowCeiling;
        /// <summary>A GM's fixed share of the season (gm.season progress), 0-1; below 0 = the calendar's.</summary>
        public float Progress;
        public byte Flags;
        /// <summary>The air temperature now (deg C, in the settings' steps; owner 14 Nov: snow builds below 0 and thaws above).</summary>
        public float Temperature;
        /// <summary>Today's weather spell: its index in <see cref="SpellSeason"/>'s list (<see cref="NoSpell"/> = none).</summary>
        public byte Spell, SpellSeason;
        /// <summary>Dry days in a row up to today (golden summer grass).</summary>
        public byte DryDays;

        public Season From => (Season)FromSeason;
        public Season To => (Season)ToSeason;
        public bool GmHold => (Flags & FlagGmHold) != 0;
        public bool AlwaysWinter => (Flags & FlagAlwaysWinter) != 0;
        public bool GmTemperature => (Flags & FlagGmTemperature) != 0;
        public bool GmSpell => (Flags & FlagGmSpell) != 0;

        /// <summary>The blend from <see cref="From"/> to <see cref="To"/> at <paramref name="now"/>, 0-1.</summary>
        public float BlendAt(double now) => SeasonRules.Blend(BlendStart, BlendSeconds, now);

        /// <summary>The season that shows more at <paramref name="now"/>.</summary>
        public Season DominantAt(double now) => SeasonRules.Dominant(From, To, BlendAt(now));

        /// <summary>The snow cover at <paramref name="now"/>, 0-1.</summary>
        public float SnowAt(double now) => SeasonRules.SnowCover(SnowCover, SnowRate, SnowAnchor, now, SnowFloor, SnowCeiling);

        /// <summary>
        /// Whether the sky may show its aurora now (owner, via the Master 14 Nov 2026: the aurora comes in Winter, on the sky's own
        /// schedule): while Winter shows more than the season it blends from, and all year on an always-winter map.
        /// </summary>
        public bool AuroraAllowedAt(double now) => AlwaysWinter || DominantAt(now) == Season.Winter;

        /// <summary>How far into its season the world is at <paramref name="now"/>, 0-1 (a GM's fixed share, else the calendar's).</summary>
        public float ProgressAt(double now)
        {
            if (Progress >= 0f) return Math.Clamp(Progress, 0f, 1f);
            if (SeasonSeconds <= 0f) return 0.5f;
            return (float)Math.Clamp((now - SeasonStart) / SeasonSeconds, 0.0, 1.0);
        }

        /// <summary>
        /// The leaves at <paramref name="now"/>: the season blended from (as at its end) into the one blended to (at its progress). An
        /// always-winter map keeps Winter's.
        /// </summary>
        public FoliageNow FoliageAt(double now, in FoliageCurve curve)
        {
            if (AlwaysWinter) return SeasonRules.FoliageAt(Season.Winter, 0.5f, curve);
            FoliageNow to = SeasonRules.FoliageAt(To, ProgressAt(now), curve);
            float b = BlendAt(now);
            if (b >= 1f || From == To) return to;
            return FoliageNow.Lerp(SeasonRules.FoliageAt(From, 1f, curve), to, b);
        }

        public bool Equals(SeasonState o) =>
            FromSeason == o.FromSeason && ToSeason == o.ToSeason && BlendStart.Equals(o.BlendStart) && BlendSeconds.Equals(o.BlendSeconds)
            && SeasonStart.Equals(o.SeasonStart) && SeasonSeconds.Equals(o.SeasonSeconds) && CalendarSeason == o.CalendarSeason
            && SnowCover.Equals(o.SnowCover) && SnowAnchor.Equals(o.SnowAnchor) && SnowRate.Equals(o.SnowRate) && SnowFloor.Equals(o.SnowFloor)
            && SnowCeiling.Equals(o.SnowCeiling) && Progress.Equals(o.Progress) && Flags == o.Flags && Temperature.Equals(o.Temperature)
            && Spell == o.Spell && SpellSeason == o.SpellSeason && DryDays == o.DryDays;

        public override bool Equals(object obj) => obj is SeasonState o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(FromSeason, ToSeason, BlendStart, SnowAnchor, Flags);
    }

    /// <summary>
    /// What this machine knows of the season (owner's DAY list item 21): the server's own (GM status) and the one a client got (the look).
    /// The entity netcode's season systems fill it; the presenter, the GM window and the sun read it. No season known = everything as before
    /// seasons (summer light, no snow).
    /// </summary>
    public static class SeasonView
    {
        /// <summary>The state this client last got from the server.</summary>
        public static SeasonState Client;
        public static bool HasClient;
        /// <summary>The state the server in this process works with (null-free default when none).</summary>
        public static SeasonState Server;
        public static bool HasServer;

        /// <summary>Share of the day the sun is up now, on this client (0.5 = as before seasons). Set by the presenter.</summary>
        public static float DaylightShare = 0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Client = default; HasClient = false; Server = default; HasServer = false; DaylightShare = 0.5f;
        }

        /// <summary>The clock's time of day as the sun sees it (short winter days); unchanged with no season.</summary>
        public static float SunTime(float clock01) =>
            Mathf.Approximately(DaylightShare, 0.5f) ? clock01 : SeasonRules.SunTime(clock01, DaylightShare);
    }

    /// <summary>
    /// The one seam the seasons have in the weather pick (<c>WorldEnvironmentNetwork.AdvanceWeather</c>): the season server system sets the
    /// season's weights and extra choices; null = the pick is exactly as before seasons. Server only (a client never rolls weather).
    /// </summary>
    public static class SeasonWeather
    {
        public static Func<WeatherProfile, float> Scale;
        public static IReadOnlyList<WeatherProfile.NextWeather> Extra;
        /// <summary>Set when a new weather spell begins: the weather rolls again at once instead of waiting out the old weather.</summary>
        public static bool Reroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Scale = null; Extra = null; Reroll = false; }
    }
}
