namespace KryticalNova.Core
{
    /// <summary>
    /// Marks a Hostile (a creature of a Hostile Zone), so that systems below Hostiles (a pet's kill XP) can tell one
    /// from a player or a training dummy without knowing the Hostiles system. <c>HostileNet</c> carries it.
    /// </summary>
    public interface IHostile
    {
    }
}
