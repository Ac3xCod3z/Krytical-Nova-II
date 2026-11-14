namespace KryticalNova.Rendering
{
    /// <summary>
    /// The player's World Effects (Settings, Graphics; owner 13 Nov 2026): how much the world's little effects do on this client, the
    /// swings that move the grass first (day list item 20), then seasons and sky events. High is the authored look; Low keeps each
    /// effect, simpler; Ultra may add. Each effect takes its tier's numbers from its own settings asset.
    /// </summary>
    public enum WorldEffects { Low, Medium, High, Ultra }

    /// <summary>The World Effects in force on this client. The Graphics page sets it (<c>GraphicsSettingsApplier</c>); the effects read it.</summary>
    public static class WorldEffectsRuntime
    {
        public static WorldEffects Quality { get; private set; } = WorldEffects.High;

        public static int Tier => (int)Quality;

        public static void Set(WorldEffects quality) => Quality = quality;
    }
}
