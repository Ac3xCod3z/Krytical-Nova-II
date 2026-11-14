using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>
    /// The Mount Companion ghost (day list item 19): everything a view needs to draw, animate and show it. Its place and facing ride the
    /// ghost's <c>LocalTransform</c>. Interpolated for everyone (no prediction, not even on the owner's view); no <c>GhostOwner</c>, so the
    /// one-owned-ghost rule stays true; the owner is <see cref="OwnerGhostId"/>.
    /// </summary>
    public struct CompanionView : IComponentData
    {
        /// <summary>The ghost id of its owner's player ghost.</summary>
        [GhostField] public int OwnerGhostId;
        /// <summary>The mount's box (1-4), so a view knows which <c>MountDefinition</c> to draw.</summary>
        [GhostField] public byte Box;
        [GhostField] public byte Stage;
        /// <summary>Ground speed in 0.25 m/s steps (the walk or run clip and its speed).</summary>
        [GhostField] public byte SpeedStep;
        /// <summary><see cref="CompanionState"/>.</summary>
        [GhostField] public byte State;
        /// <summary><see cref="CompanionStance"/>.</summary>
        [GhostField] public byte Stance;
        /// <summary>HP left, permille of its max (phase 2: the HP bar).</summary>
        [GhostField] public ushort HpPermille;
        /// <summary>Rises by one at each warp (a one-off moment as a counter, never a flipping bool): views play the vanish and form.</summary>
        [GhostField] public byte Warps;
        /// <summary>Phase 3, DPS: rises by one at each of its strikes; views play the swing (its feel) toward <see cref="FightGhostId"/>.</summary>
        [GhostField] public byte Strikes;
        /// <summary>Phase 3, HEALER: rises by one at each heal pulse on its owner; views play the heal's feel on the owner.</summary>
        [GhostField] public byte Pulses;
        /// <summary>Phase 3: the Hostile it fights (DPS) or the fight it heals in (HEALER), 0 none.</summary>
        [GhostField] public int FightGhostId;
        /// <summary>Phase 3: the stance it acts in now (<see cref="CompanionStance"/>): a combat stance shows FREE while its owner is in PvP.</summary>
        [GhostField] public byte ActingStance;
        /// <summary>Phase 4, TANK: rises by one at each of its taunts; views play the taunt (a rear-up and the swing feel).</summary>
        [GhostField] public byte Taunts;

        public const float SpeedStepMetres = 0.25f;
    }

    /// <summary>The Mount Companion ghost prefab, known to the world as a singleton (baked in the player ghost subscene).</summary>
    public struct MountCompanionGhostPrefab : IComponentData
    {
        public Entity Prefab;
    }

    /// <summary>The four boxes' companion HP and rest for the Mount window, for its own player only (beside <c>MountBookRpc</c>).</summary>
    public struct MountCompanionBookRpc : IRpcCommand
    {
        public byte Out, Stance;
        public ushort Hp1, Hp2, Hp3, Hp4;
        public uint Rest1, Rest2, Rest3, Rest4;
    }
}
