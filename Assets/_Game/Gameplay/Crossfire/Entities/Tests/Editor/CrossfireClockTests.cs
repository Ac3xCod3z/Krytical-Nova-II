using KryticalNova.Networking.Entities.Tests;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Crossfire.Entities.Tests
{
    /// <summary>Crossfire's clock in a server and client world (owner's DAY list item 17): the client gets the phase, and every GM change.</summary>
    public sealed class CrossfireClockTests
    {
        CrossfireSettings m_Settings;
        double m_Now;

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<CrossfireSettings>();
            m_Settings.anchorToNightfall = false;   // a test world has no world clock
            m_Now = 1000;
            CrossfireClockServerSystem.ClockSource = () => (false, default(WorldEnvironmentState));
        }

        [TearDown]
        public void TearDown()
        {
            CrossfireClockServerSystem.ClockSource = null;
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

        [Test]
        public void AClient_GetsTheCall_ThenEveryGmChange()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            CrossfireClockServerSystem clock = Clock(tw);
            tw.Connect();
            var client = tw.Clients[0].GetExistingSystemManaged<CrossfireClientSystem>();
            Assert.IsNotNull(client, "the client world has the Crossfire receiver");

            tw.TickUntil(() => client.Applied > 0, 300);
            Assert.AreEqual(CrossfirePhase.Call, client.Last.Now.Phase, "a fixed cycle opens with a Call from the server's start");
            Assert.AreEqual(1600, client.Last.Now.WarStart, 1e-6, "the war at the end of the 10-minute call");
            Assert.AreEqual(1420, client.Last.JoinOpensAt, 1e-6, "the yards open 3 minutes before");
            Assert.AreEqual(10, client.Last.MinLevel);

            Assert.IsTrue(clock.Run(CrossfireCommand.StartNow));
            tw.TickUntil(() => client.Last.Now.Phase == CrossfirePhase.Muster, 300);
            Assert.AreEqual(CrossfirePhase.Muster, client.Last.Now.Phase, "GM start: the muster, gates shut");
            Assert.IsFalse(client.Last.GatesOpen);

            Assert.IsTrue(clock.Run(CrossfireCommand.Skip));
            tw.TickUntil(() => client.Last.Now.Phase == CrossfirePhase.War, 300);
            Assert.IsTrue(client.Last.GatesOpen, "GM skip: the war, gates open");

            Assert.IsTrue(clock.Run(CrossfireCommand.End));
            tw.TickUntil(() => client.Last.Now.Phase == CrossfirePhase.End, 300);
            Assert.AreEqual(CrossfirePhase.End, client.Last.Now.Phase, "GM end");
            Assert.IsFalse(client.Last.GatesOpen);
        }

        [Test]
        public void TheClock_MovesOnByItself()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            CrossfireClockServerSystem clock = Clock(tw);
            tw.Connect();
            tw.Tick(10);
            Assert.AreEqual(CrossfirePhase.Call, clock.Current.Now.Phase);
            m_Now = 1600 + 61;   // past the muster
            tw.Tick(20);
            Assert.AreEqual(CrossfirePhase.War, clock.Current.Now.Phase);
            m_Now = 1600 + 60 + 900 + 21;   // past the end
            tw.Tick(20);
            Assert.AreEqual(CrossfirePhase.Gap, clock.Current.Now.Phase);
            Assert.IsTrue(clock.UsingFixedCycle);
        }
    }
}
