namespace KryticalNova.Hostiles
{
    /// <summary>When a Hostile starts a fight. Append only: the values are saved in assets.</summary>
    public enum HostileBehaviour : byte
    {
        /// <summary>Leaves players alone until one of them hits it.</summary>
        FightsBack = 0,
        /// <summary>Attacks any player that comes within its aggro radius.</summary>
        AttacksOnSight = 1
    }
}
