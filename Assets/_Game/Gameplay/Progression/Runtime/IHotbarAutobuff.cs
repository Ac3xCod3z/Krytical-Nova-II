namespace KryticalNova.Progression
{
    /// <summary>
    /// What the Hotbar asks of the Autobuff tile on it (the Autopilot system answers; the Hotbar knows no more of it
    /// than this, as with <see cref="IHotbarItemSource"/> for items).
    /// </summary>
    public interface IHotbarAutobuff
    {
        /// <summary>The licence is running: the key does something.</summary>
        bool IsAvailable { get; }
        /// <summary>What the slot says when it has no picture.</summary>
        string Label { get; }
        /// <summary>The slot's picture, or null for the label.</summary>
        UnityEngine.Sprite Icon { get; }
        /// <summary>How many buff skills are slotted into it.</summary>
        int SlottedCount { get; }
        /// <summary>A press is still casting its buffs.</summary>
        bool IsRunning { get; }
        /// <summary>The key was pressed. False when nothing could start (and the player has been told why).</summary>
        bool Press();
        /// <summary>The hover card's lines: the time left, what a press does, and the buffs slotted.</summary>
        string Describe(string key);
    }
}
