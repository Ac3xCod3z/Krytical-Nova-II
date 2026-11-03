using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using KryticalNova.Networking.Entities.Tests;
using NUnit.Framework;

namespace KryticalNova.World.Entities.Tests
{
    /// <summary>
    /// M7.1 world clock and weather: every player gets the server's world state when they come in and whenever it changes (a GM time jump,
    /// a new weather), a player who joins later gets the state as it is now, and the clock every machine reads is the server's.
    /// </summary>
    public class WorldStateSyncTests
    {
        WorldEnvironmentState m_State;

        [SetUp]
        public void SetUp()
        {
            m_State = new WorldEnvironmentState { CycleEpoch = 10, DayLengthSeconds = 3600, TimeOffset01 = 0.25f, TimeScale = 1, DayBase = 300, Seed = 7, ToWeather = 2 };
            WorldStateServerSystem.Source = () => (true, m_State);
            WorldStateClientSystem.Sink = _ => { };
        }

        [TearDown]
        public void TearDown()
        {
            WorldStateServerSystem.Source = null;
            WorldStateClientSystem.Sink = null;
        }

        [Test]
        public void EveryPlayer_GetsTheServersState_OnArrival_OnEveryChange_AndALateJoinerGetsItAsItIsNow()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            tw.Server.GetExistingSystemManaged<WorldStateServerSystem>().Interval = 0f;   // every tick (the test ticks faster than real time)
            tw.Connect();
            var a = tw.Clients[0].GetExistingSystemManaged<WorldStateClientSystem>();
            var b = tw.Clients[1].GetExistingSystemManaged<WorldStateClientSystem>();
            var server = tw.Server.GetExistingSystemManaged<WorldStateServerSystem>();
            tw.TickUntil(() => a.Applied > 0 && b.Applied > 0, 300);
            string why = $"updates {server.Updates}, with a state {server.WithState}, in game {server.LastInGame}, sends {server.Sends}; log: "
                + string.Join(" | ", tw.Interesting);
            Assert.Greater(server.Updates, 0, "the server system updates: " + why);
            Assert.Greater(server.WithState, 0, "the server system read a state: " + why);
            Assert.Greater(server.LastInGame, 0, "the server system sees the players in the game: " + why);
            Assert.Greater(server.Sends, 0, "the server sent the state: " + why);
            Assert.Greater(a.Applied, 0, "the first player got a state");
            Assert.AreEqual(m_State.CycleEpoch, a.Last.CycleEpoch, "epoch");
            Assert.AreEqual(m_State.DayLengthSeconds, a.Last.DayLengthSeconds, "day length");
            Assert.AreEqual(m_State.TimeOffset01, a.Last.TimeOffset01, "time offset");
            Assert.AreEqual(m_State.DayBase, a.Last.DayBase, "calendar day base");
            Assert.AreEqual(m_State.ToWeather, a.Last.ToWeather, "weather");
            Assert.AreEqual(m_State, a.Last, "the first player has the server's sky");
            Assert.AreEqual(m_State, b.Last, "the second too");

            // a GM jumps the clock to midnight and starts rain
            m_State.TimeOffset01 = 0f;
            m_State.CycleEpoch = 99;
            m_State.FromWeather = 2;
            m_State.ToWeather = 4;
            int a0 = a.Applied, b0 = b.Applied;
            tw.TickUntil(() => a.Applied > a0 && b.Applied > b0, 120);
            Assert.AreEqual(m_State, a.Last);
            Assert.AreEqual(m_State, b.Last);
            tw.Tick(30);
            Assert.AreEqual(a0 + 1, a.Applied, "an unchanged state is not sent again");

            var late = tw.AddClient(tw.JoinPayload);   // with the join text, as every player has (null = never approved)
            tw.WaitConnected();
            tw.GoInGame();
            tw.TickUntil(() => late.GetExistingSystemManaged<WorldStateClientSystem>().Applied > 0, 300);
            Assert.AreEqual(m_State, late.GetExistingSystemManaged<WorldStateClientSystem>().Last, "a player who comes later gets the state as it is now");

            Assert.IsTrue(NetClock.IsRunning, "the entity netcode's clock runs");
            Assert.AreSame(EntitiesNetClock.Instance, NetClock.Provider);
            Assert.AreEqual(30, NetClock.TickRate);
        }
    }
}
