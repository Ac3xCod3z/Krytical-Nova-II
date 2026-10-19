namespace KryticalNova.Core
{
    /// <summary>
    /// "Save this character soon": what a player system asks of the save agent on its own object after a change that
    /// must not be lost but may come in bursts. In Core so that systems below Persistence (Progression's Nova and
    /// Serums) can ask without naming the agent; <c>PlayerSaveAgent</c> is the one there is.
    /// </summary>
    public interface ISaveScheduler
    {
        /// <summary>Server (or offline): saves within a moment instead of at the next autosave.</summary>
        void SaveSoon(float withinSeconds = 2f);
    }
}
