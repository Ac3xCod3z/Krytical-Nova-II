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
        public void EachAttack_MaySetItsOwnWarning_OrSwitchItOff_EmptyIsTheSettingsMinimum()
        {
            // owner, 7 Nov 2026: per-attack warning; empty (an attack saved before the field, all zeros) = the global minimum + latency
            var settings = UnityEngine.ScriptableObject.CreateInstance<HostileBrainSettings>();
            try
            {
                HostileBrainConfig c = HostileBrainConfig.From(settings, 30);
                var row = new HostileAbilityRow { WindupTicks = 12 };
                Assert.AreEqual(4u, HostileRules.TellTicks(row, c.MinVisibleWarningTicks, c.WarningLatencyTicks), "empty: the settings' minimum");
                row.Warning = (byte)HostileDefinition.SwingWarning.Own;
                row.WarningTicks = 20;   // 0.667 s of its own
                Assert.AreEqual(13u, HostileRules.TellTicks(row, c.MinVisibleWarningTicks, c.WarningLatencyTicks), "own: 20 + 5 - 12");
                row.WarningTicks = 3;
                Assert.AreEqual(0u, HostileRules.TellTicks(row, c.MinVisibleWarningTicks, c.WarningLatencyTicks), "own, shorter than the windup shows");
                row.Warning = (byte)HostileDefinition.SwingWarning.Off;
                row.WarningTicks = 20;
                Assert.AreEqual(0u, HostileRules.TellTicks(row, c.MinVisibleWarningTicks, c.WarningLatencyTicks), "off: no tell whatever it says");

                var attack = new HostileDefinition.Attack { windupSeconds = 0.4f };
                Assert.AreEqual(0.5f, settings.WarningLeadSeconds(attack), 1e-5f, "empty: 0.35 + 0.15 on screen");
                attack.warning = HostileDefinition.SwingWarning.Own;
                attack.warningSeconds = 0.8f;
                Assert.AreEqual(0.95f, settings.WarningLeadSeconds(attack), 1e-5f, "own: 0.8 + 0.15");
                attack.warning = HostileDefinition.SwingWarning.Off;
                Assert.AreEqual(0f, settings.WarningLeadSeconds(attack), "off: the view shows no tell");
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void AnAttackWithItsWarningOff_WindsUpAtOnce_AndOneWithALongOwnWarning_TellsLonger()
        {
            foreach (var (warning, ticks, tell) in new[] { (HostileDefinition.SwingWarning.Off, 0u, 0u), (HostileDefinition.SwingWarning.Own, 20u, 13u) })
            {
                using var w = new HostileTestWorld();
                w.SetPlayers((new float3(0f, 0f, 1.5f), Alive));
                Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack);   // its row: windup 12 ticks
                DynamicBuffer<HostileAbilityRow> rows = w.Em.GetBuffer<HostileAbilityRow>(h);
                HostileAbilityRow row = rows[0];
                row.Warning = (byte)warning;
                row.WarningTicks = ticks;
                rows[0] = row;
                w.Hit(h, 100, 10f);
                w.Step();
                uint first = w.Tick;
                while (w.Brain(h).Phase != HostilePhase.Windup && w.Tick < first + 40) w.Step();
                Assert.AreEqual(HostilePhase.Windup, w.Brain(h).Phase, warning + ": winds up");
                Assert.AreEqual(first + tell, w.Brain(h).AttackStartTick, warning + ": the windup starts after " + tell + " ticks of tell");
                while (w.Strikes == 0 && w.Tick < first + 60) w.Step();
                Assert.AreEqual(1, w.Strikes, warning + ": struck");
                Assert.AreEqual(first + tell + 12, w.Tick, warning + ": at the tell plus the windup");
            }
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
