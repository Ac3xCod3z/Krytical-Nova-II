using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Brains
{
    /// <summary>
    /// The Brain's numbers in ticks and squared metres (singleton), made from <see cref="BrainSettings"/> by
    /// <see cref="From(BrainSettings, int)"/>: the settings asset is the only place they are set.
    /// </summary>
    public struct BrainConfig : IComponentData
    {
        public int3 TierIntervalTicks;
        public float3 TierRadiusSq;
        public uint TierDownHysteresisTicks;
        public uint EngagedLingerTicks;
        public float PlayerCell;
        public float BrainCell;
        public uint WorldSeed;

        public static BrainConfig From(BrainSettings s, int tickRate)
        {
            int Ticks(float seconds) => math.max(0, (int)math.ceil(seconds * tickRate));
            return new BrainConfig
            {
                TierIntervalTicks = new int3(math.max(1, s.tierIntervalTicks[0]), math.max(1, s.tierIntervalTicks[1]), math.max(1, s.tierIntervalTicks[2])),
                TierRadiusSq = new float3(s.tierRadius[0] * s.tierRadius[0], s.tierRadius[1] * s.tierRadius[1], s.tierRadius[2] * s.tierRadius[2]),
                TierDownHysteresisTicks = (uint)Ticks(s.tierDownHysteresisSeconds),
                EngagedLingerTicks = (uint)Ticks(s.engagedLingerSeconds),
                PlayerCell = math.max(1f, s.playerCell),
                BrainCell = math.max(0.5f, s.brainCell),
                WorldSeed = s.worldSeed,
            };
        }
    }
}
