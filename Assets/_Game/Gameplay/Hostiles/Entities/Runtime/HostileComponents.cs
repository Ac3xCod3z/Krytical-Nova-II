using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities
{
    // The Hostiles mind on the entity netcode (M5.3, implementation plan s2.2): per-brain state in components only (owner, 5 Nov 2026).

    /// <summary>What a Hostile is doing. Mode: <see cref="HostileMode"/>; Phase while engaged: <see cref="HostilePhase"/>.</summary>
    public struct HostileBrain : IComponentData
    {
        public byte Mode;
        public byte Phase;
        public uint ModeSinceTick;
        public uint PhaseEndTick;
        public uint CommitTick;
        public byte Ability;
        public uint NoticeUntilTick;
        /// <summary>The tick the target first stood beyond the detection radius in this chase (0 = it does not): the give-up clock.</summary>
        public uint UnreachableSinceTick;
        public uint NextScanTick;
        /// <summary>1 when the Hostile returns home because of the leash: it heals and clears its threat there (an Emerald give-up does not).</summary>
        public byte HealOnHome;
        /// <summary>1 on a think tick the sight scan found a player (AttacksOnSight): the decide step turns it into Alert.</summary>
        public byte Saw;
    }

    public static class HostileMode
    {
        public const byte Idle = 0, Patrol = 1, Alert = 2, Engage = 3, Return = 4, Flee = 5, Dead = 6;
    }

    public static class HostilePhase
    {
        public const byte Approach = 0, Windup = 1, Recover = 2, Wait = 3;
    }

    /// <summary>A Hostile's numbers for this life (from its species and level; the server body keeps health and rewards).</summary>
    public struct HostileLife : IComponentData
    {
        public int BodyKey;
        public ushort Species;
        public short Level;
        public byte Tier;
        public float AggroRadius, Walk, Run, AttackRange, ReachAbove;
        public int RadiusMm, HeightMm;
        /// <summary><see cref="HostileBehaviourKind"/>.</summary>
        public byte Behaviour;
        public byte Poise;
        public uint NoticeTicks;
    }

    public static class HostileBehaviourKind
    {
        /// <summary>Never picks a player by sight; turns on whoever hits it (NGO: Emerald relation Neutral).</summary>
        public const byte FightsBack = 0;
        /// <summary>Picks a player in its aggro radius (NGO: Enemy).</summary>
        public const byte AttacksOnSight = 1;
    }

    /// <summary>Where a Hostile lives: its home, how far it wanders, its zone and the leash beyond the zone's edge.</summary>
    public struct HostileHome : IComponentData
    {
        public float3 Home;
        public float WanderRadius;
        public float2 ZoneCentre;
        public float ZoneRadius;
        public float LeashRadius;
    }

    /// <summary>The player a Hostile is after. Flags: <see cref="HostileTargetFlags"/>.</summary>
    public struct HostileTarget : IComponentData
    {
        public Entity Ghost;
        public int GhostId;
        public float3 LastFeet;
        public float DistSq;
        public byte Flags;
    }

    public static class HostileTargetFlags
    {
        public const byte InReach = 1, Facing = 2, Above = 4, Unreachable = 8;
    }

    /// <summary>One player's threat on a Hostile (at most <see cref="HostileRules.ThreatCapacity"/>, the lowest pruned).</summary>
    [InternalBufferCapacity(6)]
    public struct ThreatEntry : IBufferElementData
    {
        public int GhostId;
        public Entity Ghost;
        public float Threat;
        public uint LastTick;
    }

    /// <summary>When each of the species' attacks is ready again (by attack index).</summary>
    public struct HostileCooldowns : IComponentData
    {
        public FixedList32Bytes<uint> ReadyTick;
    }

    /// <summary>A swing that resolved on its target this tick: the bridge rolls it by the game's rules (HostileStrikeRules) on the bodies.</summary>
    public struct PendingStrike : IBufferElementData
    {
        public Entity Hostile;
        public Entity Target;
        public byte Ability;
        public float3 From;
        public byte Heavy;
    }
}
