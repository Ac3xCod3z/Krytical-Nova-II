using System.Diagnostics;
using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Mathematics;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// M6.6 soak stage 3 (plan s5: "2,000 synthetic", done with the EditMode brain run until M5.7's BrainSoakScene exists): 2,000 Hostiles
    /// on a flat map with 200 players moving through them, 1,000 ticks, every mind tick timed. The Editor's numbers are an upper bound
    /// (safety checks on, Burst compiled on demand): the line it logs goes into the plan; the asserts only catch a broken or runaway mind.
    /// </summary>
    public sealed class HostileSoakTimingTests
    {
        const int Hostiles = 2000, Players = 200, Ticks = 1000, WarmUp = 100;

        [Test]
        public void TwoThousandBrains_TwoHundredPlayers_ATimedThousandTicks()
        {
            using var w = new HostileTestWorld();
            // 2,000 on a 45 x 45 grid 5 m apart (225 m square), half attack on sight, half fight back
            for (int i = 0; i < Hostiles; i++)
                w.Hostile(new float3((i % 45) * 5f, 0f, (i / 45) * 5f), (byte)(i % 2), (uint)(i + 1));
            var players = new (float3, byte)[Players];
            var ms = new double[Ticks];
            int strikesBefore = 0;
            for (int t = -WarmUp; t < Ticks; t++)
            {
                for (int p = 0; p < Players; p++)
                {
                    // each player walks its own lane across the map and back (1.5 m/s at 30 ticks/s)
                    float lane = (p % 20) * 11f, along = math.abs(((t + p * 37) * 0.05f) % 450f - 225f);
                    players[p] = (p % 2 == 0 ? new float3(along, 0f, lane) : new float3(lane, 0f, along), SensedFlags.Alive);
                }
                w.SetPlayers(players);
                if (t == 0) strikesBefore = w.Strikes;
                long start = Stopwatch.GetTimestamp();
                w.Step();
                if (t >= 0) ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            System.Array.Sort(ms);
            double p50 = ms[Ticks / 2], p99 = ms[(int)(Ticks * 0.99)], max = ms[Ticks - 1];
            int strikes = w.Strikes - strikesBefore;
            Debug.Log($"[KnSoak] {Hostiles} brains, {Players} players, {Ticks} ticks in the Editor: mind tick p50 {p50:F3} ms, p99 {p99:F3} ms, " +
                      $"max {max:F3} ms; {strikes} strikes");
            Assert.Greater(strikes, 0, "the players walked into fights");
            Assert.Less(p99, 50.0, "a mind tick of 2,000 stays far below a frame even in the Editor (a runaway would not)");
        }
    }
}
