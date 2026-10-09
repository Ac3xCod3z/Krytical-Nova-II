using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// What decides a Hostile's moves, as the rest of the game sees it. The game knows a Hostile through
    /// <see cref="HostileNet"/> and its CombatEntity only; the AI package behind this interface can be replaced
    /// without touching either. A brain runs where the game is decided (server, host or offline) and nowhere else.
    /// </summary>
    public interface IHostileBrain
    {
        /// <summary>Switches the brain on or off. It is off in the prefab, so a client never runs one.</summary>
        void SetAwake(bool awake);

        /// <summary>Gives the brain a species' behaviour and the numbers it needs (speeds, aggro radius, reach).</summary>
        void Configure(HostileDefinition definition, in HostileStats stats);

        /// <summary>Where it wanders from and goes back to after a chase, and how far it wanders.</summary>
        void SetHome(Vector3 home, int wanderRadius);

        /// <summary>While frozen a Hostile stands still and takes hits (GM tool).</summary>
        bool Frozen { get; set; }

        /// <summary>Who it is fighting, or null.</summary>
        Transform Target { get; }

        /// <summary>Makes it fight this target now, whatever its behaviour.</summary>
        void Aggro(Transform target);

        /// <summary>Drops its target and forgets the fight.</summary>
        void Calm();

        /// <summary>One line on what it is doing, for GM tools.</summary>
        string Describe();
    }
}
