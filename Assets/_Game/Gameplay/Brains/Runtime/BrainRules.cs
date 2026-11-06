using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Brains
{
    /// <summary>The think clock's rules (pure, Burst): which tier a brain is at and when it thinks.</summary>
    public static class BrainRules
    {
        /// <summary>The tier the distance to the nearest player asks for: 0 near or fighting, 1, 2, then 3 (never thinks).</summary>
        public static byte TierFor(float nearestSq, bool engaged, in BrainConfig c)
        {
            if (engaged || nearestSq <= c.TierRadiusSq.x) return 0;
            if (nearestSq <= c.TierRadiusSq.y) return 1;
            if (nearestSq <= c.TierRadiusSq.z) return 2;
            return 3;
        }

        public static int IntervalFor(byte tier, in BrainConfig c) => tier == 0 ? c.TierIntervalTicks.x : tier == 1 ? c.TierIntervalTicks.y : c.TierIntervalTicks.z;

        /// <summary>
        /// One tick of the clock at <paramref name="now"/> (ticks start at 1): the first tier and a nearer one at once, a farther one only
        /// after the hysteresis; an event this tick puts the brain at tier 0 and makes it think now; otherwise it thinks every interval of its tier,
        /// the first time staggered by its stable id so brains of one tier spread over the ticks.
        /// </summary>
        public static void Step(ref BrainClock clock, byte wanted, bool eventNow, uint now, in BrainConfig c)
        {
            if (eventNow) wanted = 0;
            // a brain never scheduled yet (new, or never near a player) takes the tier it wants at once; the hysteresis is for changes
            bool placed = clock.NextThinkTick != 0;
            if (!placed || wanted <= clock.Tier)
            {
                clock.Tier = wanted;
                clock.TierSinceTick = 0;
            }
            else
            {
                if (clock.TierSinceTick == 0) clock.TierSinceTick = now;
                if (now - clock.TierSinceTick >= c.TierDownHysteresisTicks)
                {
                    clock.Tier = wanted;
                    clock.TierSinceTick = 0;
                }
            }

            clock.ThinkNow = 0;
            if (clock.Tier >= 3 && !eventNow) return;
            uint interval = (uint)IntervalFor(clock.Tier, c);
            if (eventNow)
            {
                clock.ThinkNow = 1;
                clock.NextThinkTick = now + interval;
                return;
            }
            if (clock.NextThinkTick == 0)
            {
                uint offset = clock.StableId % interval;
                clock.NextThinkTick = now + offset;
                if (offset != 0) return;
            }
            if (now < clock.NextThinkTick) return;
            clock.ThinkNow = 1;
            clock.NextThinkTick = now + interval;
        }
    }

    /// <summary>Each brain's dice: from its stable id and the world's seed, so a replay rolls the same.</summary>
    public static class BrainDiceRules
    {
        public static Random Create(uint stableId, uint worldSeed) => Random.CreateFromIndex(stableId ^ worldSeed);
    }

    /// <summary>Lookups in <see cref="SenseWorld"/>'s player grid (pure, Burst). Distances are flat (x and z).</summary>
    public static class SenseQueries
    {
        public static int2 CellOf(float3 p, float cell) => new int2((int)math.floor(p.x / cell), (int)math.floor(p.z / cell));

        /// <summary>The indices (into <see cref="SenseWorld.Players"/>) of every player within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static void Within(in SenseWorld world, float3 center, float radius, float cell, NativeList<int> into)
        {
            into.Clear();
            float rSq = radius * radius;
            int2 lo = CellOf(center - new float3(radius, 0f, radius), cell), hi = CellOf(center + new float3(radius, 0f, radius), cell);
            for (int x = lo.x; x <= hi.x; x++)
            for (int z = lo.y; z <= hi.y; z++)
            {
                if (!world.PlayerGrid.TryGetFirstValue(new int2(x, z), out int i, out var it)) continue;
                do
                {
                    if (FlatSq(world.Players[i].Feet, center) <= rSq) into.Add(i);
                } while (world.PlayerGrid.TryGetNextValue(out i, ref it));
            }
        }

        /// <summary>The flat squared distance to the nearest living player within <paramref name="radius"/>, or float.MaxValue.</summary>
        public static float NearestSq(in SenseWorld world, float3 center, float radius, float cell)
        {
            float best = float.MaxValue;
            int2 lo = CellOf(center - new float3(radius, 0f, radius), cell), hi = CellOf(center + new float3(radius, 0f, radius), cell);
            for (int x = lo.x; x <= hi.x; x++)
            for (int z = lo.y; z <= hi.y; z++)
            {
                if (!world.PlayerGrid.TryGetFirstValue(new int2(x, z), out int i, out var it)) continue;
                do
                {
                    SensedPlayer p = world.Players[i];
                    if ((p.Flags & SensedFlags.Alive) == 0) continue;
                    best = math.min(best, FlatSq(p.Feet, center));
                } while (world.PlayerGrid.TryGetNextValue(out i, ref it));
            }
            return best;
        }

        public static float FlatSq(float3 a, float3 b)
        {
            float2 d = a.xz - b.xz;
            return math.dot(d, d);
        }
    }
}
