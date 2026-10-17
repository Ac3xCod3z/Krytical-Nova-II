using System;
using KryticalNova.Economy;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// The clock of a Pass (Autopilot, Autobuff): a licence that ends at a moment in real time, lengthened in days
    /// as a Kiosk is opened with a Permit, and running whether the character plays or not. Free of scene objects,
    /// so a test can read it.
    /// </summary>
    public static class PassClock
    {
        /// <summary>
        /// When the licence ends after a Pass of <paramref name="days"/>: a licence still running is lengthened from
        /// its end; one that has run out starts again from now.
        /// </summary>
        public static long Extend(long nowTicks, long expiresTicks, int days) =>
            KioskRules.ExpiryTicks(Math.Max(nowTicks, expiresTicks), days);

        /// <summary>True when a Pass of <paramref name="days"/> would take the licence past the most days a character may hold.</summary>
        public static bool WouldExceed(long nowTicks, long expiresTicks, int days, int maxDays) =>
            Extend(nowTicks, expiresTicks, days) > KioskRules.ExpiryTicks(nowTicks, maxDays);

        public static bool IsActive(long nowTicks, long expiresTicks) => !KioskRules.IsExpired(nowTicks, expiresTicks);

        /// <summary>When a licence set to run <paramref name="days"/> from now ends; 0 or less ends it now.</summary>
        public static long SetDays(long nowTicks, int days) => days <= 0 ? nowTicks : KioskRules.ExpiryTicks(nowTicks, days);

        /// <summary>Seconds left, 0 when it has run out.</summary>
        public static float SecondsLeft(long nowTicks, long expiresTicks) =>
            (float)Math.Max(0.0, TimeSpan.FromTicks(expiresTicks - nowTicks).TotalSeconds);

        /// <summary>"Autopilot: 2d 13h left." or "No Autopilot time: use an Autopilot Pass."</summary>
        public static string TimeLeftLine(string what, long nowTicks, long expiresTicks) =>
            IsActive(nowTicks, expiresTicks) ? $"{what}: {KioskRules.TimeLeft(nowTicks, expiresTicks)} left." : $"No {what} time: use an {what} Pass.";
    }
}
