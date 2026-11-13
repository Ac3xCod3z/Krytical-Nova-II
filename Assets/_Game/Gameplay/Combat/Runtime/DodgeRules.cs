namespace KryticalNova.Combat
{
    /// <summary>How an attack comes in (build 17 item 11.2): what a dodge must do to clear it. Tagged per attack in data.</summary>
    public enum AttackHeight : byte
    {
        /// <summary>Untagged: dodged with a sidestep, like a high one.</summary>
        Any = 0,
        /// <summary>Low (a sweep, a bite at the legs): jumped over.</summary>
        Low = 1,
        /// <summary>High (a swing, a claw, a shot): stepped aside, right or left.</summary>
        High = 2,
    }

    /// <summary>The dodge a player shows for an evaded attack.</summary>
    public enum DodgeMove : byte
    {
        None = 0,
        JumpOver = 1,
        SidestepRight = 2,
        SidestepLeft = 3,
    }

    /// <summary>A player's dodge streak (server body), as the rule keeps it.</summary>
    public struct DodgeStreak
    {
        public int Count;
        public float LastAt;
        /// <summary>The side of the last sidestep, so the next goes the other way (varied).</summary>
        public bool LastWasRight;
    }

    /// <summary>
    /// The dodge rule of build 17 item 11.2 (owner): when the player is not attacking and an attack is evaded, the player dodges by reading
    /// it: a LOW attack is jumped over, a HIGH one stepped aside (right and left in turn). Up to <c>dodgesForCounter</c> in a row; after the
    /// last of them an automatic counter-attack with a little more damage. Pure: the server asks it on an evaded hit, the clip is presentation.
    /// The evasion roll itself is untouched (<c>CombatMath</c> step 1).
    /// </summary>
    public static class DodgeRules
    {
        /// <summary>What the player does for an attack that missed them. <paramref name="counter"/> = the counter-attack fires now.</summary>
        public static DodgeMove OnEvaded(ref DodgeStreak streak, AttackHeight height, bool attacking, float now,
            int dodgesForCounter, float streakSeconds, out bool counter)
        {
            counter = false;
            if (attacking) { streak = default; return DodgeMove.None; }   // busy swinging: the miss just misses
            if (streak.Count > 0 && now - streak.LastAt > streakSeconds) streak.Count = 0;
            streak.Count++;
            streak.LastAt = now;
            DodgeMove move;
            if (height == AttackHeight.Low) move = DodgeMove.JumpOver;
            else
            {
                streak.LastWasRight = !streak.LastWasRight;
                move = streak.LastWasRight ? DodgeMove.SidestepRight : DodgeMove.SidestepLeft;
            }
            if (streak.Count >= (dodgesForCounter < 1 ? 1 : dodgesForCounter))
            {
                counter = true;
                streak.Count = 0;
            }
            return move;
        }

        /// <summary>A hit that lands breaks the streak.</summary>
        public static void OnHitTaken(ref DodgeStreak streak) => streak.Count = 0;
    }
}
