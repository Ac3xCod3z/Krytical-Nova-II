namespace KryticalNova.Rendering
{
    /// <summary>
    /// The player's World Effects (Settings, Graphics; owner 13 Nov 2026): how much the world's little effects do on this client, the
    /// swings that move the grass first (day list item 20), then seasons and sky events. High is the authored look; Low keeps each
    /// effect, simpler; Ultra may add. Each effect takes its tier's numbers from its own settings asset.
    /// </summary>
    public enum WorldEffects { Low, Medium, High, Ultra }
    // Lives in Core (owner rule, no visual code in the server build): World's SeasonSettings, which the server compiles, keys its numbers by it.
}
