using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Crossfire.Entities.Tests
{
    /// <summary>
    /// Crossfire's call through the game's announcement system (owner answers 3.4, 13 Nov 2026): at the Call's start, again once a minute
    /// until the gates open, then silent; one gates-closed line when the gates shut. Server world of a KnTestWorld, a clock of its own.
    /// </summary>
    public sealed class CrossfireCallTests
    {
        CrossfireSettings m_Settings;
        double m_Now;
        Action<AnnouncementKind, string, string, byte> m_Had;
        readonly List<(AnnouncementKind kind, string text, double at)> m_Heard = new List<(AnnouncementKind, string, double)>();

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<CrossfireSettings>();
            m_Settings.anchorToNightfall = false;   // fixed cycle from the server's start: Call 1000-1600, Muster 1600-1660, War to 2560
            m_Now = 1000;
            CrossfireClockServerSystem.ClockSource = () => (false, default(WorldEnvironmentState));
            m_Had = ServerChat.Announce;
            m_Heard.Clear();
            ServerChat.Announce = (kind, text, highlight, rarity) => m_Heard.Add((kind, text, m_Now));
        }

        [TearDown]
        public void TearDown()
        {
            ServerChat.Announce = m_Had;
            CrossfireClockServerSystem.ClockSource = null;
            UnityEngine.Object.DestroyImmediate(m_Settings);
        }

        CrossfireClockServerSystem Clock(KnTestWorld tw)
        {
            var clock = tw.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            Assert.IsNotNull(clock);
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            clock.Interval = 0f;   // every tick, so each 5-second step is looked at
            m_Heard.Clear();       // what the world said under the Resources settings before these were put in is not this test's
            return clock;
        }

        /// <summary>Steps the clock to <paramref name="until"/> (5-second steps, 1-second ones round the final countdown), ticking the worlds at each.</summary>
        void RunTo(KnTestWorld tw, double until)
        {
            while (m_Now < until)
            {
                double step = m_Now >= 1640 && m_Now < 1665 ? 1 : 5;
                m_Now = Math.Min(until, m_Now + step);
                tw.Tick(2);
            }
        }

        [Test]
        public void TheCall_OncePerMinute_ThenTheLast10Seconds_ThenTheGatesClosedLine()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            Clock(tw);
            tw.Connect();
            tw.Tick(4);
            RunTo(tw, 2700);   // through the Call, the Muster, the War and into the End / Gap

            var calls = m_Heard.FindAll(h => h.text.StartsWith("Crossfire! The war starts in", StringComparison.Ordinal));
            Assert.IsTrue(m_Heard.TrueForAll(h => h.kind == AnnouncementKind.Crossfire || h.kind == AnnouncementKind.CrossfireCount),
                "every line is a Crossfire announcement");
            // 10-minute Call + 1-minute Muster: a call at the start and once a minute after, 1000 to 1600, none once the gates open at 1660
            Assert.AreEqual(11, calls.Count, string.Join(" / ", m_Heard));
            Assert.AreEqual("Crossfire! The war starts in 11 minutes. Level 10 and up: get to your Muster Yard.", calls[0].text);
            for (int i = 1; i < calls.Count; i++)
                Assert.That(calls[i].at - calls[i - 1].at, Is.InRange(60.0, 65.0), "once a minute");
            Assert.Less(calls[calls.Count - 1].at, 1660, "no call once the gates are open");
            Assert.AreEqual("Crossfire! The war starts in 1 minute. Level 10 and up: get to your Muster Yard.", calls[calls.Count - 1].text);

            // no line names seconds, except the last 10 s: 10 .. 1, one a second, then nothing once the gates open at 1660
            var count = m_Heard.FindAll(h => h.text.StartsWith("Crossfire: the gates open in", StringComparison.Ordinal));
            Assert.AreEqual(10, count.Count, string.Join(" / ", m_Heard));
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual("Crossfire: the gates open in " + (10 - i), count[i].text);
                Assert.AreEqual(AnnouncementKind.CrossfireCount, count[i].kind, "the last seconds stand in place on the news bar");
                Assert.AreEqual(1650 + i, count[i].at, 1e-6, "one a second in the last 10 s");
            }
            Assert.IsFalse(m_Heard.Exists(h => h.text.Contains("second")), "no seconds anywhere");

            var closed = m_Heard.FindAll(h => h.text == m_Settings.gatesClosedText);
            Assert.AreEqual(1, closed.Count, "one gates-closed line");
            Assert.GreaterOrEqual(closed[0].at, 2560, "when the war ends");
        }

        [Test]
        public void ACallCalledOff_SaysNoGatesClosed()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            CrossfireClockServerSystem clock = Clock(tw);
            tw.Connect();
            tw.Tick(4);
            RunTo(tw, 1100);
            Assert.IsTrue(clock.Run(CrossfireCommand.End), "GM end while only called");
            RunTo(tw, 1300);
            Assert.IsFalse(m_Heard.Exists(h => h.text == m_Settings.gatesClosedText), "the gates never opened");
        }

        [Test]
        public void TheWords_ComeFromTheSettings()
        {
            m_Settings.callText = "War in {time} (from {level})";
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            Clock(tw);
            tw.Connect();
            tw.Tick(4);
            RunTo(tw, 1005);
            Assert.AreEqual("War in 11 minutes (from 10)", m_Heard[0].text);
            Assert.AreEqual("1 minute", CrossfireCaller.InWords(40), "never seconds, and never less than a minute");
            Assert.AreEqual("1 minute", CrossfireCaller.InWords(60));
            Assert.AreEqual("1 minute", CrossfireCaller.InWords(61), "whole minutes, never rounded up");
            Assert.AreEqual("2 minutes", CrossfireCaller.InWords(120));
        }

        /// <summary>Owner, 13 Nov 2026: a 2-minute Call with a 15-second Muster said "3 minutes" first. The minutes are exact.</summary>
        [Test]
        public void ACallThatStartsBetweenTwoMinuteMarks_NamesTheWholeMinutesLeft_NeverTheNextOneUp()
        {
            var settings = ScriptableObject.CreateInstance<CrossfireSettings>();
            try
            {
                settings.callRepeatSeconds = 60f;
                settings.finalCountdownSeconds = 10f;
                var caller = new CrossfireCaller();
                var state = new CrossfireState { Known = true, MinLevel = 10, Now = new CrossfireNow { Phase = CrossfirePhase.Call, WarStart = 120 } };
                const double gatesOpenAt = 135;   // a 2-minute Call and a 15-second Muster: 2:15 to go at the start
                var said = new List<(double left, string text, bool countdown)>();
                for (double now = 0; now < gatesOpenAt; now += 0.25)
                {
                    string line = caller.Next(state, now, gatesOpenAt, settings, out bool countdown);
                    if (line != null) said.Add((gatesOpenAt - now, line, countdown));
                }
                var calls = said.FindAll(x => !x.countdown);
                Assert.AreEqual(2, calls.Count, string.Join(" / ", said));
                StringAssert.Contains("starts in 2 minutes", calls[0].text, "2:15 to go is 2 whole minutes, not 3");
                Assert.AreEqual(135, calls[0].left, 1e-6, "said as the Call starts");
                StringAssert.Contains("starts in 1 minute.", calls[1].text);
                Assert.AreEqual(60, calls[1].left, 0.26, "the next pop-up on its own mark, and '2 minutes' is not said twice");
                var count = said.FindAll(x => x.countdown);
                Assert.AreEqual(10, count.Count, "10 .. 1");
                Assert.AreEqual("Crossfire: the gates open in 10", count[0].text);
                Assert.AreEqual("Crossfire: the gates open in 1", count[9].text);
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }
    }
}
