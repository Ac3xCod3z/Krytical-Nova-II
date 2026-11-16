namespace KryticalNova.Rendering
{
    /// <summary>The World Effects in force on this client. The Graphics page sets it (<c>GraphicsSettingsApplier</c>); the effects read it.</summary>
    public static class WorldEffectsRuntime
    {
        public static WorldEffects Quality { get; private set; } = WorldEffects.High;

        public static int Tier => (int)Quality;

        public static void Set(WorldEffects quality) => Quality = quality;
    }
}
