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
            return clock;
        }

        /// <summary>Steps the clock in 5-second steps to <paramref name="until"/>, ticking the worlds at each.</summary>
        void RunTo(KnTestWorld tw, double until)
        {
            while (m_Now < until)
            {
                m_Now = Math.Min(until, m_Now + 5);
                tw.Tick(2);
            }
        }

        [Test]
        public void TheCall_RepeatsOnceAMinute_UntilTheGatesOpen_ThenTheGatesClosedLine()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            Clock(tw);
            tw.Connect();
            tw.Tick(4);
            RunTo(tw, 2700);   // through the Call, the Muster, the War and into the End / Gap

            var calls = m_Heard.FindAll(h => h.text.StartsWith("Crossfire! The war starts in", StringComparison.Ordinal));
            Assert.IsTrue(m_Heard.TrueForAll(h => h.kind == AnnouncementKind.Crossfire), "every line is a Crossfire announcement");
            // 10-minute Call + 1-minute Muster: a call at the start and once a minute after, 1000 to 1600, none once the gates open at 1660
            Assert.AreEqual(11, calls.Count, string.Join(" / ", m_Heard));
            Assert.AreEqual("Crossfire! The war starts in 11 minutes. Level 10 and up: get to your Muster Yard.", calls[0].text);
            for (int i = 1; i < calls.Count; i++)
                Assert.That(calls[i].at - calls[i - 1].at, Is.InRange(60.0, 65.0), "once a minute");
            Assert.Less(calls[calls.Count - 1].at, 1660, "no call once the gates are open");

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
            Assert.AreEqual("40 seconds", CrossfireCaller.InWords(40));
            Assert.AreEqual("1 minute", CrossfireCaller.InWords(60));
        }
    }
}
