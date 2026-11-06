using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Brains
{
    // The Brain (M5.1): the shared core of every server-side mind (Hostiles first, townsfolk later). It knows no mind: it keeps the
    // think clock, the sense grids, the scores and the dice; a mind's systems read them. All per-brain state lives in components
    // (owner, 5 Nov 2026: nothing that blocks jobs). Implementation plan s2.1.

    /// <summary>Marks a brain entity (server only).</summary>
    public struct BrainTag : IComponentData { }

    /// <summary>On = the brain thinks; off = asleep (its zone asleep, frozen by sleep, dead).</summary>
    public struct BrainActive : IComponentData, IEnableableComponent { }

    /// <summary>
    /// When the brain thinks next. <see cref="Tier"/> 0-2 thinks every BrainConfig interval of its tier, 3 never (no player near);
    /// <see cref="ThinkNow"/> is 1 on the ticks it thinks (an event makes it think on the tick it arrives).
    /// </summary>
    public struct BrainClock : IComponentData
    {
        public uint NextThinkTick;
        public byte Tier;
        /// <summary>The tick the brain first wanted a farther tier than it has (0 = it does not): a tier goes down only after the hysteresis.</summary>
        public uint TierSinceTick;
        /// <summary>Until this tick the brain counts as engaged (fighting): tier 0 whatever the distance.</summary>
        public uint EngagedUntilTick;
        public byte ThinkNow;
        /// <summary>A stable number for the brain (its stagger and its dice), given when it is made.</summary>
        public uint StableId;
    }

    /// <summary>The brain's own dice: the same seed and the same events give the same rolls.</summary>
    public struct BrainDice : IComponentData
    {
        public Random Rng;
    }

    /// <summary>Where the brain wants its body to go and how. Flags: <see cref="BrainMoverFlags"/>.</summary>
    public struct BrainMover : IComponentData
    {
        public float3 Dest;
        public float Speed;
        public float TurnRateRad;
        public float Yaw;
        public byte Flags;
    }

    public static class BrainMoverFlags
    {
        public const byte Stop = 1, Run = 2, Face = 4;
    }

    [InternalBufferCapacity(16)]
    public struct BrainWaypoint : IBufferElementData
    {
        public float3 P;
    }

    /// <summary>A path the brain asked for. Status: <see cref="BrainPathStatus"/>.</summary>
    public struct BrainPath : IComponentData
    {
        public float3 To;
        public uint RequestTick;
        public uint ReadyTick;
        public byte Status;
        public byte Priority;
    }

    public static class BrainPathStatus
    {
        public const byte None = 0, Queued = 1, Ready = 2, Failed = 3;
    }

    /// <summary>Something that happened to the brain this tick (a hit, a GM order...). Kind: <see cref="BrainEventKind"/>.</summary>
    public struct BrainEvent : IBufferElementData
    {
        public byte Kind;
        public int GhostId;
        public float Amount;
        public uint Tick;
    }

    public static class BrainEventKind
    {
        public const byte Damaged = 1, Aggro = 2, Calm = 3, Freeze = 4, Unfreeze = 5, Killed = 6, Assist = 7, Healed = 8, Taunt = 9;
    }

    /// <summary>One player as the brains sense it this tick. Flags: <see cref="SensedFlags"/>.</summary>
    public struct SensedPlayer
    {
        public Entity Ghost;
        public int GhostId;
        public float3 Feet;
        public byte Flags;
        public byte Faction;
    }

    public static class SensedFlags
    {
        public const byte Alive = 1, Neutral = 2, OnWall = 4, Ignored = 8, Dead = 16;
    }

    /// <summary>
    /// What the brains sense, rebuilt every tick by <see cref="SenseBuildSystem"/> (singleton, it owns the containers). A mind's feeder
    /// fills <see cref="Players"/> before it (the Hostiles mind from the player ghosts, M5.3; tests directly).
    /// </summary>
    public struct SenseWorld : IComponentData
    {
        public NativeList<SensedPlayer> Players;
        public NativeParallelMultiHashMap<int2, int> PlayerGrid;
        public NativeParallelMultiHashMap<int2, Entity> BrainGrid;
        public NativeParallelHashMap<int, int> ByGhostId;
    }

    /// <summary>The server tick the brains run on (filled from NetworkTime in a netcode world; tests set it).</summary>
    public struct BrainTick : IComponentData
    {
        public uint Tick;
    }
}
