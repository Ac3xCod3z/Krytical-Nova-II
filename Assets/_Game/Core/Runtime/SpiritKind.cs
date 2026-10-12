namespace KryticalNova.Core
{
    /// <summary>What leaves a body when it dies: the spirit released over it (GameFeel's SpiritReleaser).</summary>
    public enum SpiritKind : byte
    {
        /// <summary>A beast or monster: several spirits burst out of it, light green.</summary>
        Creature = 0,
        /// <summary>A person of flesh (players of the living lineages, humanoid Hostiles): one small spirit leaves by the mouth, light blue.</summary>
        Living = 1,
        /// <summary>A robot or a skeleton: one small spirit leaves by the mouth, reddish.</summary>
        Unliving = 2,
    }

    /// <summary>
    /// A body has died and its spirit may leave it. Raised on every machine that shows the body by whatever knows of
    /// the death and cannot reach the game feel itself (a Hostile); heard by GameFeel's SpiritReleaser.
    /// </summary>
    public static class SpiritMoments
    {
        public static event System.Action<UnityEngine.Transform> BodyDied;

        public static void Raise(UnityEngine.Transform body)
        {
            if (body != null) BodyDied?.Invoke(body);
        }
    }
}
