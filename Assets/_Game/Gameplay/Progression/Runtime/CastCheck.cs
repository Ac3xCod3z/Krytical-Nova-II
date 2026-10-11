namespace KryticalNova.Progression
{
    /// <summary>What stands between a skill and its cast right now (<see cref="PlayerCombatSkills.CheckCast"/>).</summary>
    public enum CastCheck
    {
        /// <summary>Nothing: it can be cast.</summary>
        Ready,
        /// <summary>Not yet: the caster is still in a swing or a cast. Nothing for the player to put right.</summary>
        Busy,
        /// <summary>Everything is in order but the distance: the target is alive and hostile, and too far.</summary>
        OutOfReach,
        /// <summary>It cannot be cast: not learned, on cooldown, no Flux, no target. The reason says which.</summary>
        Refused,
    }
}
