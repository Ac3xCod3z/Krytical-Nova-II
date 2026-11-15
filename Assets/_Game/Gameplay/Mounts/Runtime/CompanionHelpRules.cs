namespace KryticalNova.Mounts
{
    /// <summary>
    /// Phase 5 of the Mount Companion (owner, 14 Nov 2026): a Hostile kill the companion helped with gives the selected mount
    /// <c>MountSettings.companionHelpedKillXpMultiplier</c> (1.5) times its kill EXP, multiplied with the Rider and Convoy Boosters
    /// (1.5 x 2 = 3). "Helped" = in the 10 s before the kill (<c>companionHelpSeconds</c>) the companion struck that Hostile (DPS),
    /// taunted it (TANK) or pulsed a heal on its owner while the owner fought it (HEALER). FREE never helps (and a combat stance acts as
    /// FREE in PvP), so it never earns the bonus. Pure: the server's <see cref="PlayerMounts"/> keeps a <see cref="CompanionHelpLog"/>.
    /// </summary>
    public static class CompanionHelpRules
    {
        /// <summary>Whether the help of a companion acting in <paramref name="acting"/> counts: DPS, HEALER and TANK do, FREE does not.</summary>
        public static bool Counts(CompanionStance acting) =>
            acting == CompanionStance.Dps || acting == CompanionStance.Healer || acting == CompanionStance.Tank;

        /// <summary>Whether help given at <paramref name="helpedAt"/> still counts for a kill at <paramref name="now"/> (seconds, one clock).</summary>
        public static bool InWindow(float helpedAt, float now, float windowSeconds) =>
            now >= helpedAt && now - helpedAt <= windowSeconds;

        /// <summary>The multiplier a kill's mount EXP gets: <paramref name="multiplier"/> when helped, 1 otherwise (never below 1).</summary>
        public static float KillMultiplier(bool helped, float multiplier) => helped && multiplier > 1f ? multiplier : 1f;
    }

    /// <summary>
    /// The Hostiles a companion helped with lately, by a key the server picks (the Hostile's body, by reference), with when. A fixed ring of
    /// <see cref="Capacity"/> slots made once: noting help allocates nothing; when it is full the oldest note gives way.
    /// </summary>
    public sealed class CompanionHelpLog
    {
        public const int Capacity = 16;
        readonly object[] keys = new object[Capacity];
        readonly float[] times = new float[Capacity];
        int count, next;

        /// <summary>Notes that the companion, acting in <paramref name="acting"/>, helped with Hostile <paramref name="key"/> at <paramref name="now"/>. FREE notes nothing.</summary>
        public void Note(object key, float now, CompanionStance acting)
        {
            if (key == null || !CompanionHelpRules.Counts(acting)) return;
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(keys[i], key)) { times[i] = now; return; }
            keys[next] = key;
            times[next] = now;
            next = (next + 1) % Capacity;
            if (count < Capacity) count++;
        }

        /// <summary>Whether Hostile <paramref name="key"/> was helped with in the <paramref name="windowSeconds"/> before <paramref name="now"/>; the note is used up.</summary>
        public bool TakeHelped(object key, float now, float windowSeconds)
        {
            if (key == null) return false;
            for (int i = 0; i < count; i++)
            {
                if (!ReferenceEquals(keys[i], key)) continue;
                bool helped = CompanionHelpRules.InWindow(times[i], now, windowSeconds);
                keys[i] = null;
                return helped;
            }
            return false;
        }

        public void Clear()
        {
            System.Array.Clear(keys, 0, Capacity);
            count = next = 0;
        }
    }
}
