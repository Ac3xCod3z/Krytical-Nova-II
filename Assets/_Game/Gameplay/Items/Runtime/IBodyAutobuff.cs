namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's Autoward licence and its eight cells, for the entity netcode (M8 8.3.11): the server body keeps them (its Pass use, its
    /// save), runs the owner's new cells, and shows both on the owner's view. Here, below Autopilot, as <see cref="IBodyStorage"/>.
    /// </summary>
    public interface IBodyAutobuff
    {
        long ExpiresUtc { get; }
        string[] SkillIds { get; }
        void RunSetSlots(string[] skillIds);
        void MirrorState(long expiresUtc, string[] skillIds);
    }
}
