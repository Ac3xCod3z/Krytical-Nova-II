namespace KryticalNova.Rendering
{
    /// <summary>The player's Crowd Detail (Settings, Graphics; owner 13 Nov 2026): how many townsfolk a client draws in full and how finely.</summary>
    public enum CrowdDetail { Low, Medium, High, Ultra }

    /// <summary>
    /// The Crowd Detail in force on this client. The Graphics page sets it (<c>GraphicsSettingsApplier</c>); the townsfolk view reads it and
    /// takes each tier's numbers from its own settings asset. Only what this client draws changes: the server's crowd is the same for all.
    /// High is the authored look.
    /// </summary>
    public static class CrowdDetailRuntime
    {
        public static CrowdDetail Quality { get; private set; } = CrowdDetail.High;

        /// <summary>Goes up at each change, for the things that follow it.</summary>
        public static int Version { get; private set; }

        public static int Tier => (int)Quality;

        public static void Set(CrowdDetail quality)
        {
            if (quality == Quality) return;
            Quality = quality;
            Version++;
        }
    }
}
