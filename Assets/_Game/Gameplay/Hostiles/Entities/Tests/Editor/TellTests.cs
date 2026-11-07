using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// Q3 option A (owner, 7 Nov 2026): a swing whose windup alone shows less than the minimum warning on screen gets a tell first, so every
    /// swing is visible for at least 0.35 s after the latency (brain plan s3.8); a heavy one (long enough) gets none.
    /// </summary>
    public sealed class TellTests
    {
        const byte Alive = SensedFlags.Alive;

        [Test]
        public void TellTicks_FillTheWarningUpTo035_AfterTheLatency()
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<HostileBrainSettings>();
            HostileBrainConfig c = HostileBrainConfig.From(settings, 30);
            UnityEngine.Object.DestroyImmediate(settings);
            Assert.AreEqual(11u, c.MinVisibleWarningTicks, "0.35 s");
            Assert.AreEqual(5u, c.WarningLatencyTicks, "0.15 s");
            Assert.AreEqual(4u, HostileRules.TellTicks(12, c.MinVisibleWarningTicks, c.WarningLatencyTicks), "light 0.40 s: 4 ticks of tell");
            Assert.AreEqual(0u, HostileRules.TellTicks(17, c.MinVisibleWarningTicks, c.WarningLatencyTicks), "heavy 0.55 s: none");
            for (uint w = 1; w < 40; w++)
                Assert.GreaterOrEqual(w + HostileRules.TellTicks(w, c.MinVisibleWarningTicks, c.WarningLatencyTicks) - c.WarningLatencyTicks,
                    c.MinVisibleWarningTicks, "windup " + w + ": the warning on screen");
        }

        [Test]
        public void ALightSwing_TellsFirst_ThenWindsUp_AndStrikesAtTellPlusWindup()
        {
            using var w = new HostileTestWorld();
            w.SetPlayers((new float3(0f, 0f, 1.5f), Alive));
            Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack);   // its row: windup 12 ticks
            w.Hit(h, 100, 10f);
            w.Step();
            Assert.AreEqual(HostilePhase.Tell, w.Brain(h).Phase, "the tell first");
            uint tellStart = w.Tick;
            Assert.AreEqual(0u, w.Brain(h).AttackStartTick, "the swing has not started");
            Assert.IsTrue((HostileGhostRules.Of(w.Brain(h), default, w.Target(h), 0f, 0, 0, 0).Flags & HostileGhostFlags.Tell) != 0, "the ghost tells the views");
            while (w.Brain(h).Phase == HostilePhase.Tell && w.Tick < tellStart + 20) w.Step();
            Assert.AreEqual(HostilePhase.Windup, w.Brain(h).Phase);
            Assert.AreEqual(tellStart + 4, w.Brain(h).AttackStartTick, "the windup starts after 4 ticks of tell");
            while (w.Strikes == 0 && w.Tick < tellStart + 40) w.Step();
            Assert.AreEqual(1, w.Strikes, "struck");
            Assert.AreEqual(tellStart + 4 + 12, w.Tick, "at the tell plus the windup");
        }

        [Test]
        public void ATargetThatRunsClearDuringTheTell_CallsItOff_NoStrike()
        {
            using var w = new HostileTestWorld();
            w.SetPlayers((new float3(0f, 0f, 1.5f), Alive));
            Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack);
            w.Hit(h, 100, 10f);
            w.Step();
            Assert.AreEqual(HostilePhase.Tell, w.Brain(h).Phase);
            w.SetPlayers((new float3(0f, 0f, 9f), Alive));
            w.Step();
            Assert.AreEqual(HostilePhase.Approach, w.Brain(h).Phase, "a feint: called off");
            w.Step(20);
            Assert.AreEqual(0, w.Strikes);
        }
    }
}
