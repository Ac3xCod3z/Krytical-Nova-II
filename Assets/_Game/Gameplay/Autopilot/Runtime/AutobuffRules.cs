using System;
using KryticalNova.Progression;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// The rules of the Autobuff (owner, 15 Oct 2026): a licence by the clock, bought in days with an Autobuff Pass
    /// as a Kiosk is opened with a Permit, and up to eight buff skills that one Hotbar key casts in turn. Free of
    /// scene objects, so a test can read them; <see cref="PlayerAutobuff"/> and <see cref="AutobuffCaster"/> apply them.
    /// </summary>
    public static class AutobuffRules
    {
        public const int Slots = 8;
        private const int LongestId = 64;

        /// <summary>When the licence ends after a Pass of <paramref name="days"/> (<see cref="PassClock.Extend"/>).</summary>
        public static long Extend(long nowTicks, long expiresTicks, int days) => PassClock.Extend(nowTicks, expiresTicks, days);

        /// <summary>True when a Pass would take the licence past the most days a character may hold.</summary>
        public static bool WouldExceed(long nowTicks, long expiresTicks, int days, int maxDays) =>
            PassClock.WouldExceed(nowTicks, expiresTicks, days, maxDays);

        public static bool IsActive(long nowTicks, long expiresTicks) => PassClock.IsActive(nowTicks, expiresTicks);

        /// <summary>"Autobuff: 2d 13h left." or "No Autobuff time: use an Autobuff Pass."</summary>
        public static string TimeLeftLine(long nowTicks, long expiresTicks) => PassClock.TimeLeftLine("Autobuff", nowTicks, expiresTicks);

        /// <summary>
        /// The eight cells as they are kept: exactly eight, each a skill id or empty, none too long, and no skill twice
        /// (the first of a pair stays).
        /// </summary>
        public static string[] Sanitise(string[] ids)
        {
            var clean = new string[Slots];
            for (int i = 0; i < Slots; i++)
            {
                string id = ids != null && i < ids.Length && ids[i] != null ? ids[i].Trim() : string.Empty;
                if (id.Length > LongestId) id = string.Empty;
                for (int j = 0; j < i && id.Length > 0; j++)
                    if (clean[j] == id) id = string.Empty;
                clean[i] = id;
            }
            return clean;
        }

        /// <summary>A skill the Autobuff takes: one that is not an attack (a buff, a shield, a stance, a channel).</summary>
        public static bool Accepts(SkillDefinitionSO skill) => skill != null && !skill.IsAttack && skill.category != SkillCategory.Combat;

        /// <summary>
        /// The cell of the next buff to cast: the first that is filled, not up on the character, and castable now.
        /// -1 when there is none.
        /// </summary>
        public static int NextToCast(string[] ids, Func<string, bool> isUp, Func<string, bool> canCast)
        {
            if (ids == null) return -1;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id)) continue;
                if (isUp != null && isUp(id)) continue;
                if (canCast != null && !canCast(id)) continue;
                return i;
            }
            return -1;
        }

        /// <summary>How many cells hold a skill.</summary>
        public static int Count(string[] ids)
        {
            int count = 0;
            if (ids != null) foreach (string id in ids) if (!string.IsNullOrEmpty(id)) count++;
            return count;
        }
    }
}
