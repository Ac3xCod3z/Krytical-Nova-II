using System;

namespace KryticalNova.Crossfire
{
    /// <summary>Crossfire's phases, in order. Gap is the time between two wars.</summary>
    public enum CrossfirePhase : byte
    {
        Gap = 0,
        /// <summary>The war is coming: eligible players are told; in its last minutes the yards take players in.</summary>
        Call = 1,
        /// <summary>Everyone in their Muster Yard, gates shut.</summary>
        Muster = 2,
        /// <summary>The gates are open: the war runs.</summary>
        War = 3,
        /// <summary>Results; then everyone is sent back where they entered.</summary>
        End = 4,
    }

    /// <summary>The phase lengths in seconds (from <see cref="CrossfireSettings.Times"/>).</summary>
    public struct CrossfireTimes
    {
        public double CallSeconds, JoinWindowSeconds, MusterSeconds, WarSeconds, EndSeconds;

        /// <summary>From the war's start (nightfall) to the end of its End phase.</summary>
        public double Tail => MusterSeconds + WarSeconds + EndSeconds;
    }

    /// <summary>When the wars fall: every <see cref="Period"/> seconds from <see cref="FirstStart"/> (a war's start = the Muster's start).</summary>
    public struct CrossfireCycle
    {
        public double FirstStart, Period;
    }

    /// <summary>Where the clock stands: the phase, its start and end, and the war it belongs to (clock seconds, <c>NetClock</c>).</summary>
    public struct CrossfireNow : IEquatable<CrossfireNow>
    {
        public CrossfirePhase Phase;
        public double PhaseStart, PhaseEnd;
        /// <summary>The start (Muster) of the war this phase belongs to; in the Gap, the next war's.</summary>
        public double WarStart;

        public bool Equals(CrossfireNow o) => Phase == o.Phase && PhaseStart == o.PhaseStart && PhaseEnd == o.PhaseEnd && WarStart == o.WarStart;
        public override bool Equals(object o) => o is CrossfireNow n && Equals(n);
        public override int GetHashCode() => HashCode.Combine((int)Phase, PhaseStart, PhaseEnd, WarStart);
    }

    /// <summary>A GM's hand on the clock (server only): a war forced to start at a time, and the wars already done or called off.</summary>
    public struct CrossfireOverride
    {
        /// <summary>A forced war's start, NaN for none.</summary>
        public double ForcedStart;
        /// <summary>Scheduled wars starting at or before this are not run again (one a GM ended, skipped or replaced).</summary>
        public double DoneThrough;

        public static CrossfireOverride None => new CrossfireOverride { ForcedStart = double.NaN, DoneThrough = double.NegativeInfinity };
    }

    /// <summary>
    /// Crossfire's clock, pure (owner's DAY list item 17): the phase at a time from the schedule, the next nightfall on the world clock,
    /// the GM's start / skip / end, and who is called (brackets). The server runs it; clients only show what the server sent.
    /// </summary>
    public static class CrossfireRules
    {
        /// <summary>
        /// The war schedule anchored on the world clock's nightfall: the world's time of day is
        /// <c>offset01 + (now - epoch) * timeScale / dayLengthSeconds</c> (<c>WorldEnvironmentState.TimeOfDay01</c>), so nightfall
        /// (<paramref name="hour"/>) comes every <c>dayLengthSeconds / timeScale</c> real seconds.
        /// </summary>
        public static CrossfireCycle Nightfalls(double epoch, double offset01, double timeScale, double dayLengthSeconds, double hour)
        {
            if (dayLengthSeconds <= 0 || timeScale <= 0) return new CrossfireCycle { FirstStart = double.NaN, Period = 0 };
            double period = dayLengthSeconds / timeScale;
            double target = hour / 24.0 - offset01;
            target -= Math.Floor(target);
            return new CrossfireCycle { FirstStart = epoch + target * period, Period = period };
        }

        /// <summary>A fixed cycle (no nightfall anchor): the first war <paramref name="callSeconds"/> after <paramref name="origin"/>.</summary>
        public static CrossfireCycle Fixed(double origin, double callSeconds, double cycleSeconds) =>
            new CrossfireCycle { FirstStart = origin + callSeconds, Period = cycleSeconds };

        /// <summary>The schedule's war whose End is not over yet at <paramref name="now"/> (the current one, or the next).</summary>
        public static double ScheduledStart(in CrossfireCycle cycle, in CrossfireTimes t, double now)
        {
            if (cycle.Period <= 0 || double.IsNaN(cycle.FirstStart)) return double.PositiveInfinity;
            double n = Math.Floor((now - t.Tail - cycle.FirstStart) / cycle.Period) + 1;
            double start = cycle.FirstStart + n * cycle.Period;
            while (start + t.Tail <= now) start += cycle.Period;   // rounding at a boundary
            return start;
        }

        /// <summary>The war the clock is on at <paramref name="now"/>: a GM's forced one while it runs, else the schedule's, skipping the done ones.</summary>
        public static double WarStart(in CrossfireCycle cycle, in CrossfireTimes t, in CrossfireOverride o, double now)
        {
            if (!double.IsNaN(o.ForcedStart) && now < o.ForcedStart + t.Tail) return o.ForcedStart;
            double start = ScheduledStart(cycle, t, now);
            if (cycle.Period > 0)
                while (start <= o.DoneThrough) start += cycle.Period;
            return start;
        }

        /// <summary>The phase at <paramref name="now"/> of the war starting at <paramref name="warStart"/>, with its start and end.</summary>
        public static CrossfireNow PhaseOf(double warStart, in CrossfireTimes t, double now)
        {
            double call = warStart - t.CallSeconds, war = warStart + t.MusterSeconds, end = war + t.WarSeconds, gap = end + t.EndSeconds;
            var at = new CrossfireNow { WarStart = warStart };
            if (now < call) { at.Phase = CrossfirePhase.Gap; at.PhaseStart = double.NegativeInfinity; at.PhaseEnd = call; }
            else if (now < warStart) { at.Phase = CrossfirePhase.Call; at.PhaseStart = call; at.PhaseEnd = warStart; }
            else if (now < war) { at.Phase = CrossfirePhase.Muster; at.PhaseStart = warStart; at.PhaseEnd = war; }
            else if (now < end) { at.Phase = CrossfirePhase.War; at.PhaseStart = war; at.PhaseEnd = end; }
            else if (now < gap) { at.Phase = CrossfirePhase.End; at.PhaseStart = end; at.PhaseEnd = gap; }
            else { at.Phase = CrossfirePhase.Gap; at.PhaseStart = gap; at.PhaseEnd = double.PositiveInfinity; }
            return at;
        }

        /// <summary>Where the clock stands at <paramref name="now"/>.</summary>
        public static CrossfireNow At(in CrossfireCycle cycle, in CrossfireTimes t, in CrossfireOverride o, double now) =>
            PhaseOf(WarStart(cycle, t, o, now), t, now);

        /// <summary>The yards take players in: the last <see cref="CrossfireTimes.JoinWindowSeconds"/> of the Call, and the Muster.</summary>
        public static bool JoinOpen(in CrossfireNow at, in CrossfireTimes t, double now) =>
            at.Phase == CrossfirePhase.Muster || (at.Phase == CrossfirePhase.Call && now >= at.WarStart - t.JoinWindowSeconds);

        /// <summary>The gates stand open only while the war runs.</summary>
        public static bool GatesOpen(CrossfirePhase phase) => phase == CrossfirePhase.War;

        // ------------------------------------------------------------------ the GM's hand (start now, skip, end)

        /// <summary>
        /// Every scheduled war that would be called or run between <paramref name="from"/> and <paramref name="to"/> is set aside (a GM's
        /// own war takes its place: never two wars at once, and none half-way through when the GM's ends).
        /// </summary>
        static void SetAside(in CrossfireCycle cycle, in CrossfireTimes t, ref CrossfireOverride o, double from, double to)
        {
            if (cycle.Period <= 0 || double.IsNaN(cycle.FirstStart)) return;
            for (double s = ScheduledStart(cycle, t, from); s - t.CallSeconds < to; s += cycle.Period)
                if (s > o.DoneThrough) o.DoneThrough = s;
        }

        /// <summary>A GM's war starting at <paramref name="start"/> (it may lie in the past or the future).</summary>
        static void Force(in CrossfireCycle cycle, in CrossfireTimes t, ref CrossfireOverride o, double now, double start)
        {
            SetAside(cycle, t, ref o, now, start + t.Tail);
            o.ForcedStart = start;
        }

        /// <summary>The war the clock is on is done (called off, or its End skipped).</summary>
        static void Finish(ref CrossfireOverride o, in CrossfireNow at)
        {
            if (at.WarStart == o.ForcedStart) o.ForcedStart = double.NaN;
            if (at.WarStart > o.DoneThrough) o.DoneThrough = at.WarStart;
        }

        /// <summary>GM "start now": the war starts now (the Muster, gates shut, then the War).</summary>
        public static void StartNow(in CrossfireCycle cycle, in CrossfireTimes t, ref CrossfireOverride o, double now) =>
            Force(cycle, t, ref o, now, now);

        /// <summary>GM "skip": on to the next phase now (Gap > Call > Muster > War > End > Gap).</summary>
        public static void Skip(in CrossfireCycle cycle, in CrossfireTimes t, ref CrossfireOverride o, double now)
        {
            CrossfireNow at = At(cycle, t, o, now);
            switch (at.Phase)
            {
                case CrossfirePhase.Gap: Force(cycle, t, ref o, now, now + t.CallSeconds); break;
                case CrossfirePhase.Call: Force(cycle, t, ref o, now, now); break;
                case CrossfirePhase.Muster: Force(cycle, t, ref o, now, now - t.MusterSeconds); break;
                case CrossfirePhase.War: Force(cycle, t, ref o, now, now - t.MusterSeconds - t.WarSeconds); break;
                case CrossfirePhase.End: Finish(ref o, at); break;
            }
        }

        /// <summary>GM "end": a war under way goes to its End now; a war only called is called off. Nothing in the Gap or the End.</summary>
        public static void End(in CrossfireCycle cycle, in CrossfireTimes t, ref CrossfireOverride o, double now)
        {
            CrossfireNow at = At(cycle, t, o, now);
            if (at.Phase == CrossfirePhase.Call) Finish(ref o, at);
            else if (at.Phase == CrossfirePhase.Muster || at.Phase == CrossfirePhase.War) Force(cycle, t, ref o, now, now - t.MusterSeconds - t.WarSeconds);
        }

        // ------------------------------------------------------------------ who

        /// <summary>The bracket of <paramref name="level"/> (1 = I ...), 0 below the war's level: ten levels each, the last takes all above.</summary>
        public static int BracketOf(int level, int minLevel, int bracketSize, int bracketCount)
        {
            if (level < minLevel || bracketSize < 1 || bracketCount < 1) return 0;
            return Math.Min(bracketCount, 1 + (level - minLevel) / bracketSize);
        }

        /// <summary>The roman numeral of a bracket, for the call (I to X and on).</summary>
        public static string Roman(int bracket)
        {
            if (bracket <= 0) return string.Empty;
            string[] tens = { "", "X", "XX", "XXX" }, ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return bracket >= 40 ? bracket.ToString() : tens[bracket / 10] + ones[bracket % 10];
        }
    }
}
