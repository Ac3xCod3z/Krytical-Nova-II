using System;
using System.Collections.Generic;
using System.Reflection;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace KryticalNova.Crossfire.Entities.Tests
{
    /// <summary>
    /// Crossfire step 6 without the map (owner, 13 Nov 2026: the yard and battlefield are his to make; until then no yard is built or put in
    /// a scene, and the send-back waits for it). Two clients and the server in a <see cref="KnTestWorld"/>, short test times in a settings
    /// copy (never the shipped asset): the clock runs Call > Muster > War > End > Gap on time, both clients get the state on join and on
    /// every change with the same phase times as the server (so their countdowns agree), the GM's line (the one the GM window and the
    /// control page send) starts / skips / ends / reports, never two wars at once, and a Muster Gate follows the phase on the server and
    /// on a client.
    /// </summary>
    public sealed class CrossfireTwoClientTests
    {
        const double Origin = 1000;
        CrossfireSettings m_Settings;
        double m_Now;
        readonly List<GameObject> m_Made = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<CrossfireSettings>();
            m_Settings.anchorToNightfall = false;   // a test world has no world clock
            m_Settings.cycleMinutes = 5f;
            m_Settings.callMinutes = 0.5f;          // Call 30 s
            m_Settings.callRepeatSeconds = 5f;
            m_Settings.joinWindowMinutes = 0.25f;   // its last 15 s
            m_Settings.musterSeconds = 12f;
            m_Settings.warMinutes = 1f;
            m_Settings.endSeconds = 5f;
            m_Settings.minLevel = 1;
            m_Now = Origin;
            CrossfireClockServerSystem.ClockSource = () => (false, default(WorldEnvironmentState));
        }

        [TearDown]
        public void TearDown()
        {
            CrossfireClockServerSystem.ClockSource = null;
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
            Object.DestroyImmediate(m_Settings);
        }

        CrossfireClockServerSystem Clock(KnTestWorld tw)
        {
            var clock = tw.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            Assert.IsNotNull(clock, "the server world has Crossfire's clock");
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            return clock;
        }

        static CrossfireClientSystem Ear(Unity.Entities.World client)
        {
            var ear = client.GetExistingSystemManaged<CrossfireClientSystem>();
            Assert.IsNotNull(ear, "the client world has the Crossfire receiver");
            return ear;
        }

        /// <summary>Both clients hold exactly the server's phase and its start and end (a countdown is PhaseEnd minus the shared NetClock).</summary>
        static void AssertAgree(CrossfireClockServerSystem clock, CrossfireClientSystem a, CrossfireClientSystem b, CrossfirePhase phase, string when)
        {
            CrossfireNow s = clock.Current.Now;
            Assert.AreEqual(phase, s.Phase, when + ": the server's phase");
            foreach (CrossfireClientSystem c in new[] { a, b })
            {
                Assert.AreEqual(phase, c.Last.Now.Phase, when + ": a client's phase");
                Assert.AreEqual(s.PhaseStart, c.Last.Now.PhaseStart, 1e-9, when + ": the phase start a client counts from");
                Assert.AreEqual(s.PhaseEnd, c.Last.Now.PhaseEnd, 1e-9, when + ": the phase end a client counts down to");
                Assert.AreEqual(s.WarStart, c.Last.Now.WarStart, 1e-9, when + ": the war start");
            }
        }

        MusterGate Gate()
        {
            var go = new GameObject("test Muster Gate");
            m_Made.Add(go);
            var blocker = go.AddComponent<NavMeshObstacle>();
            var shut = new GameObject("shut"); shut.transform.SetParent(go.transform);
            var open = new GameObject("open"); open.transform.SetParent(go.transform);
            MusterGate gate = go.AddComponent<MusterGate>();
            gate.blocker = blocker;
            gate.shutLook = shut;
            gate.openLook = open;
            return gate;
        }

        static readonly MethodInfo s_GateUpdate = typeof(MusterGate).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>A gate's own frame (EditMode runs no Update): it follows <see cref="CrossfireView.ForGates"/>.</summary>
        static void Frame(MusterGate gate) => s_GateUpdate.Invoke(gate, null);

        static void AssertGate(MusterGate gate, bool open, string when)
        {
            Frame(gate);
            Assert.AreEqual(open, gate.IsOpen, when + ": the gate");
            Assert.AreEqual(!open, gate.blocker.enabled, when + ": the carved blocker is on only while shut");
            Assert.IsTrue(gate.blocker.carving, when + ": the blocker carves the NavMesh");
            Assert.AreEqual(!open, gate.shutLook.activeSelf, when + ": the shut look");
            Assert.AreEqual(open, gate.openLook.activeSelf, when + ": the open look");
        }

        [Test]
        public void TwoClients_GetEveryPhaseOnTime_WithTheServersTimes_AndTheGateFollows()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            CrossfireClockServerSystem clock = Clock(tw);
            tw.Connect();
            CrossfireClientSystem a = Ear(tw.Clients[0]), b = Ear(tw.Clients[1]);
            MusterGate gate = Gate();

            // on join: the Call (a fixed cycle opens with one from the server's start)
            tw.TickUntil(() => a.Applied > 0 && b.Applied > 0, 300);
            AssertAgree(clock, a, b, CrossfirePhase.Call, "on join");
            double war = Origin + 30, open = war + 12, end = open + 60, gap = end + 5;
            Assert.AreEqual(war, clock.Current.Now.WarStart, 1e-9, "the war 30 s after the server's start");
            Assert.AreEqual(war - 15, a.Last.JoinOpensAt, 1e-9, "the yards take players in the last 15 s of the Call");
            Assert.AreEqual(5f, a.Last.CallRepeatSeconds, "the call repeats every 5 s");
            Assert.AreEqual(1, a.Last.BracketOf(1), "level 1 is called (bracket I) with the test's minimum level");
            AssertGate(gate, false, "the Call");

            // each boundary: a hair before it the old phase holds, at it the new one starts, on the server and on both clients
            foreach ((double at, CrossfirePhase before, CrossfirePhase after) in new[]
                     {
                         (war, CrossfirePhase.Call, CrossfirePhase.Muster), (open, CrossfirePhase.Muster, CrossfirePhase.War),
                         (end, CrossfirePhase.War, CrossfirePhase.End), (gap, CrossfirePhase.End, CrossfirePhase.Gap),
                     })
            {
                m_Now = at - 0.01;
                tw.Tick(12);
                AssertAgree(clock, a, b, before, $"{before} 0.01 s before {at - Origin:F0} s");
                m_Now = at;
                int sends = clock.Sends;
                tw.TickUntil(() => a.Last.Now.Phase == after && b.Last.Now.Phase == after, 120);
                AssertAgree(clock, a, b, after, $"{after} at {at - Origin:F0} s");
                Assert.AreEqual(sends + 2, clock.Sends, $"{after}: one state to each of the two players, once");
                AssertGate(gate, after == CrossfirePhase.War, after.ToString());
            }

            // the gate on a game that runs no server follows the state it received
            CrossfireView.HasServer = false;
            try
            {
                Assert.AreEqual(CrossfirePhase.Gap, CrossfireView.Client.Now.Phase);
                AssertGate(gate, false, "a client's gate in the Gap");
                CrossfireState received = CrossfireView.Client;
                received.Now.Phase = CrossfirePhase.War;
                CrossfireView.Client = received;
                AssertGate(gate, true, "a client's gate told the War");
            }
            finally { CrossfireView.HasServer = true; }
        }

        [Test]
        public void APlayerWhoComesInLater_GetsTheStateAtOnce()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            CrossfireClockServerSystem clock = Clock(tw);
            tw.Connect();
            CrossfireClientSystem a = Ear(tw.Clients[0]);
            tw.TickUntil(() => a.Applied > 0, 300);
            m_Now = Origin + 31;   // the Muster
            tw.TickUntil(() => a.Last.Now.Phase == CrossfirePhase.Muster, 120);

            Unity.Entities.World late = tw.AddClient(tw.JoinPayload);
            tw.WaitConnected();
            tw.GoInGame();
            CrossfireClientSystem b = Ear(late);
            tw.TickUntil(() => b.Applied > 0, 300);
            AssertAgree(clock, a, b, CrossfirePhase.Muster, "a late join in the Muster");
            Assert.AreEqual(1, b.Applied, "one state on join, nothing repeated while nothing changes");
        }

        static string Gm(string what)
        {
            Type commands = Type.GetType("KryticalNova.DevTools.CrossfireCommands, KryticalNova.DevTools");
            Assert.IsNotNull(commands, "the GM commands (KryticalNova.DevTools.CrossfireCommands)");
            MethodInfo line = commands.GetMethod("Crossfire", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            Assert.IsNotNull(line, "gm.crossfire <what>");
            return (string)line.Invoke(null, new object[] { what });
        }

        [Test]
        public void TheGmLine_StartsSkipsEndsAndReports_OnBothClients()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            CrossfireClockServerSystem clock = Clock(tw);
            tw.Connect();
            CrossfireClientSystem a = Ear(tw.Clients[0]), b = Ear(tw.Clients[1]);
            tw.TickUntil(() => a.Applied > 0 && b.Applied > 0, 300);
            m_Now = Origin + 200;   // the Gap after the first war
            tw.TickUntil(() => a.Last.Now.Phase == CrossfirePhase.Gap && b.Last.Now.Phase == CrossfirePhase.Gap, 120);

            StringAssert.StartsWith("Crossfire: Gap", Gm("status"), "status in the Gap");
            StringAssert.Contains("next war in", Gm("status"));
            foreach ((string what, CrossfirePhase expect) in new[]
                     {
                         ("start", CrossfirePhase.Muster), ("skip", CrossfirePhase.War), ("end", CrossfirePhase.End), ("skip", CrossfirePhase.Gap),
                         ("skip", CrossfirePhase.Call), ("end", CrossfirePhase.Gap),
                     })
            {
                string answer = Gm(what);
                tw.TickUntil(() => a.Last.Now.Phase == expect && b.Last.Now.Phase == expect, 120);
                AssertAgree(clock, a, b, expect, $"gm.crossfire {what} ({answer})");
                StringAssert.Contains(expect.ToString(), Gm("status"), $"status after {what}");
            }
            StringAssert.StartsWith("gm.crossfire status", Gm("nonsense"), "an unknown word shows the usage");
        }

        [Test]
        public void AGmStart_NeverRunsTwoWarsAtOnce()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            CrossfireClockServerSystem clock = Clock(tw);
            tw.Connect();
            tw.Tick(5);
            // the scheduled war is called (due at +30); a GM starts one now, at +10
            m_Now = Origin + 10;
            Assert.IsTrue(clock.Run(CrossfireCommand.StartNow));
            double forced = clock.Current.Now.WarStart;
            Assert.AreEqual(Origin + 10, forced, 1e-9);
            // a second start while it runs starts that war again, never a second one beside it
            m_Now = Origin + 15;
            Assert.IsTrue(clock.Run(CrossfireCommand.StartNow));
            forced = clock.Current.Now.WarStart;
            var phases = new List<CrossfirePhase>();
            var wars = new HashSet<double>();
            double tail = 12 + 60 + 5;
            for (double t = Origin + 15; t <= forced + tail + 120; t += 0.5)
            {
                m_Now = t;
                tw.Tick(1);
                CrossfireNow now = clock.Current.Now;
                if (phases.Count == 0 || phases[phases.Count - 1] != now.Phase) phases.Add(now.Phase);
                if (now.Phase != CrossfirePhase.Gap && now.Phase != CrossfirePhase.Call) wars.Add(now.WarStart);
            }
            Assert.AreEqual(new[] { CrossfirePhase.Muster, CrossfirePhase.War, CrossfirePhase.End, CrossfirePhase.Gap }, phases.ToArray(),
                "the GM's war runs through once; the scheduled one it replaced (due at +30) never runs beside or after it");
            Assert.AreEqual(1, wars.Count, "one war in all");
        }
    }
}
