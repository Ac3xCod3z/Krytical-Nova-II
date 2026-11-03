using KryticalNova.Core;
using NUnit.Framework;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// M7.2 the zone music: every player gets the server's music state when they come in and whenever a GM changes it (a forced track, a nudge),
    /// an unchanged state is not sent again, a player who joins later gets it as it is now, and a client of its own may not change it.
    /// </summary>
    public class MusicStateSyncTests
    {
        MusicSync m_State;

        [SetUp]
        public void SetUp()
        {
            m_State = new MusicSync { Epoch = 12.5, Forced = -1, Nudge = 0f };
            MusicStateServerSystem.Source = () => (true, m_State);
            MusicStateClientSystem.Sink = _ => { };
        }

        [TearDown]
        public void TearDown()
        {
            MusicStateServerSystem.Source = null;
            MusicStateClientSystem.Sink = null;
            MusicStateLink.RemoteAuthority = false;
        }

        [Test]
        public void EveryPlayer_HasTheServersMusic_OnArrival_OnAGmChange_AndALateJoinerToo()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            tw.Connect();
            var a = tw.Clients[0].GetExistingSystemManaged<MusicStateClientSystem>();
            var b = tw.Clients[1].GetExistingSystemManaged<MusicStateClientSystem>();
            var server = tw.Server.GetExistingSystemManaged<MusicStateServerSystem>();
            Assert.IsNotNull(server, "the server world has the music sync");
            tw.TickUntil(() => a.Applied > 0 && b.Applied > 0, 300);
            Assert.Greater(server.Sends, 0, "the server sent the state");
            Assert.IsTrue(m_State.Same(a.Last), "the first player has the server's music");
            Assert.IsTrue(m_State.Same(b.Last), "the second too");
            Assert.IsFalse(MusicStateLink.RemoteAuthority, "with the server in this process, the music is changed here");

            // a GM forces track 2 from clock time 40 and nudges every beat by 30 ms
            m_State = new MusicSync { Epoch = 40.0, Forced = 2, Nudge = 0.03f };
            int a0 = a.Applied, b0 = b.Applied;
            tw.TickUntil(() => a.Applied > a0 && b.Applied > b0, 120);
            Assert.IsTrue(m_State.Same(a.Last), "the GM's change reached the first player");
            Assert.IsTrue(m_State.Same(b.Last), "and the second");
            tw.Tick(30);
            Assert.AreEqual(a0 + 1, a.Applied, "an unchanged state is not sent again");

            var late = tw.AddClient(tw.JoinPayload);
            tw.WaitConnected();
            tw.GoInGame();
            var c = late.GetExistingSystemManaged<MusicStateClientSystem>();
            tw.TickUntil(() => c.Applied > 0, 300);
            Assert.IsTrue(m_State.Same(c.Last), "a player who comes later gets the music as it is now");
        }
    }
}
