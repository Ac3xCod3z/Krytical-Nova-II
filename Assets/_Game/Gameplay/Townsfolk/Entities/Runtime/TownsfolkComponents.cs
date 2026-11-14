using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// What every client sees of a townsperson (plan 3.3; interpolated ghost, never owned). Its place and facing come with the ghost's
    /// transform; this says who it is (seed, cast, personality: the client builds the same look from them) and what it does. The
    /// server writes it only when a value changes, so a townsperson standing at a stall costs no snapshot bytes.
    /// </summary>
    public struct TownsfolkGhost : IComponentData
    {
        [GhostField] public uint Seed;
        [GhostField] public byte Cast;
        [GhostField] public byte Personality;
        /// <summary><see cref="TownActivity"/>.</summary>
        [GhostField] public byte Activity;
        /// <summary>Ground speed in steps of 0.05 m/s (as the Hostiles').</summary>
        [GhostField] public byte SpeedSteps;
        /// <summary>The chat circle (or spot) it shares with others, 0 none: members face its middle.</summary>
        [GhostField] public ushort GroupId;
    }

    /// <summary>Server only: what the ghost writer remembers between ticks.</summary>
    public struct TownsfolkGhostSource : IComponentData
    {
        public float3 LastPosition;
    }

    public static class TownLifeState
    {
        public const byte Choose = 0, Going = 1, AtSpot = 2, Strolling = 3, GoingHome = 4, Gone = 5;
    }

    /// <summary>
    /// Server only: one townsperson's town-life mind (plan 3.1): which personality, where in its plan it is, the spot slot it holds, how
    /// long it stays, the last spots it used. The Brain's own components (clock, dice, mover, path) do the rest.
    /// </summary>
    public struct TownLife : IComponentData
    {
        public byte Personality;
        public byte State;
        public byte Activity;
        public byte Jogs;
        /// <summary>Its spot and slot, index + 1 (0 none).</summary>
        public ushort Spot, Slot;
        public uint StayUntilTick;
        public uint StateSinceTick;
        public float WalkSpeed, JogSpeed;
        /// <summary>The shift it came out for, game hours (start == end: none).</summary>
        public float ShiftStart, ShiftEnd;
        /// <summary>This one's plan edges move by this many hours (its seed).</summary>
        public float PlanJitter;
        /// <summary>The spawner wants it to go home (over the cap for the hour, rain).</summary>
        public byte SentHome;
        public uint Seed;
        public FixedList32Bytes<ushort> Recent;
    }

    /// <summary>A smart spot of the area (plan 3.5), on the area entity. Slots are <see cref="TownSlot"/> rows <see cref="FirstSlot"/> on.</summary>
    [InternalBufferCapacity(0)]
    public struct TownSpot : IBufferElementData
    {
        public float3 Pos;
        public byte Kind;
        public byte Flags;
        public byte OpenFrom, OpenTo;
        /// <summary>Personality id + 1 that alone may use it (0 anyone).</summary>
        public byte OnlyFor;
        public ushort FirstSlot;
        public byte SlotCount;
        public byte Used;
    }

    /// <summary>One place to stand, sit or lean at a spot, and who holds it.</summary>
    [InternalBufferCapacity(0)]
    public struct TownSlot : IBufferElementData
    {
        public float3 Pos;
        public float Yaw;
        public ushort Spot;
        public Entity ClaimedBy;
        public byte ClaimerPersonality;
        public uint ClaimUntilTick;
    }

    /// <summary>The area singleton (server): its middle and whether its spots are built.</summary>
    public struct TownArea : IComponentData
    {
        public float3 Centre;
        public float Radius;
        public byte Cast;
        public byte Ready;
    }

    /// <summary>The town's clock and weather (server singleton, refreshed once a second from the world's clock; tests set it).</summary>
    public struct TownClock : IComponentData
    {
        public float Hours;
        public byte Period;
        public byte Raining;
    }

    /// <summary>Town life's numbers in ticks (singleton), from <see cref="TownsfolkSettings"/>: the asset is the only place they are set.</summary>
    public struct TownsfolkConfig : IComponentData
    {
        public float4 PeriodStarts;   // dawn, day, dusk, night start hours
        public float PreferredDistance;
        public int PickFromBest;
        public int RecentSpots;
        public uint StaleClaimTicks;
        public float FriendBonus;
        public float ArriveRadius;
        public uint GiveUpWalkTicks;
        public uint FarPlanStepTicks;
        public float TickSeconds;

        public static TownsfolkConfig From(TownsfolkSettings s, int tickRate)
        {
            uint Ticks(float seconds) => (uint)math.max(1, (int)math.ceil(seconds * tickRate));
            return new TownsfolkConfig
            {
                PeriodStarts = new float4(s.dawnStartHour, s.dayStartHour, s.duskStartHour, s.nightStartHour),
                PreferredDistance = math.max(1f, s.preferredSpotDistance),
                PickFromBest = math.clamp(s.pickFromBest, 1, 8),
                RecentSpots = math.clamp(s.recentSpots, 0, 8),
                StaleClaimTicks = Ticks(s.staleClaimSeconds),
                FriendBonus = math.max(0f, s.friendBonus),
                ArriveRadius = math.max(0.2f, s.arriveRadius),
                GiveUpWalkTicks = Ticks(s.giveUpWalkSeconds),
                FarPlanStepTicks = Ticks(s.farPlanStepSeconds),
                TickSeconds = 1f / math.max(1, tickRate),
            };
        }
    }

    /// <summary>A personality as the server's mind reads it (on the config entity; index = order of the world assets' list).</summary>
    [InternalBufferCapacity(0)]
    public struct PersonalityRow : IBufferElementData
    {
        public byte Id;
        public float StopChance;
        public float2 StaySeconds;
        public float Crowding;
        public byte NeedsLight;
        public byte LikesPlayersNear;
        public byte Rain;
        public byte DeclinesInvites;
        public float ChatChance;
        /// <summary>Bit n = it chats with personality id n.</summary>
        public uint ChatsWith;
        /// <summary>Weight of each <see cref="TownSpotKind"/> (index = kind).</summary>
        public FixedList64Bytes<float> Likes;
    }
}
