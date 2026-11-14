using System;
using KryticalNova.Networking.Entities.Tests;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.World.Entities.Tests
{
    /// <summary>
    /// Seasons on two clients (owner's DAY list item 21; design note eval 6): both players get the server's season on arrival and after a GM
    /// switch, a late joiner gets it as it is now, the snow they work out agrees, and an always-winter map keeps its floor.
    /// </summary>
    public class SeasonSyncTests
    {
        SeasonSettings m_Settings;
        SeasonProfile[] m_Profiles;
        SeasonWeatherNow m_Weather;

        static double Unix(int y, int m, int d, int h = 0) => (new DateTime(y, m, d, h, 0, 0, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<SeasonSettings>();
            m_Profiles = new SeasonProfile[4];
            for (int i = 0; i < 4; i++)
            {
                m_Profiles[i] = ScriptableObject.CreateInstance<SeasonProfile>();
                m_Profiles[i].season = (Season)i;
            }
            m_Profiles[3].daylightShare = 0.375f;
            m_Settings.spring = m_Profiles[0]; m_Settings.summer = m_Profiles[1]; m_Settings.autumn = m_Profiles[2]; m_Settings.winter = m_Profiles[3];
            m_Weather = new SeasonWeatherNow { Falling = PrecipitationType.Snow, Intensity = 0.6f, Clock01 = 0.5f };
            SeasonServerSystem.WeatherSource = () => (true, m_Weather);
            SeasonServerSystem.ZoneSource = () => ("Meridian Sprawl", "MeridianSprawl_Z1_TransitRow");
            SeasonClientSystem.Sink = _ => { };
        }

        [TearDown]
        public void TearDown()
        {
            SeasonServerSystem.WeatherSource = null;
            SeasonServerSystem.ZoneSource = null;
            SeasonClientSystem.Sink = null;
            SeasonWeather.Scale = null;
            SeasonWeather.Extra = null;
            UnityEngine.Object.DestroyImmediate(m_Settings);
            foreach (SeasonProfile p in m_Profiles) UnityEngine.Object.DestroyImmediate(p);
        }

        [Test]
        public void TwoClients_GetTheServersSeason_OnArrival_AfterAGmSwitch_AndALateJoinerToo()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            var server = tw.Server.GetExistingSystemManaged<SeasonServerSystem>();
            server.Interval = 0f;
            server.Settings = m_Settings;
            server.UtcNow = () => Unix(2026, 11, 13, 12);   // Autumn, day 5
            tw.Connect();
            var a = tw.Clients[0].GetExistingSystemManaged<SeasonClientSystem>();
            var b = tw.Clients[1].GetExistingSystemManaged<SeasonClientSystem>();
            tw.TickUntil(() => a.Applied > 0 && b.Applied > 0, 300);
            Assert.Greater(server.Sends, 0, "the server sent the season");
            Assert.AreEqual(Season.Autumn, a.Last.To, "13 Nov is Autumn");
            Assert.AreEqual(Season.Autumn, (Season)a.Last.CalendarSeason);
            Assert.AreEqual(server.Current, a.Last, "the first player has the server's season");
            Assert.AreEqual(server.Current, b.Last, "the second too");
            Assert.Greater(a.Last.SnowRate, 0f, "it snows (the test's weather): the cover piles up");
            Assert.IsNotNull(SeasonWeather.Scale, "the weather pick has the season's weights");

            // a GM holds Winter
            int a0 = a.Applied, b0 = b.Applied;
            Assert.IsTrue(server.Hold(Season.Winter, 0f));   // at once, so the melt rate does not change mid-test
            tw.TickUntil(() => a.Applied > a0 && b.Applied > b0, 120);
            Assert.AreEqual(Season.Winter, a.Last.To, "both blend to Winter");
            Assert.IsTrue(a.Last.GmHold, "held by a GM");
            Assert.AreEqual(a.Last, b.Last, "both clients agree");
            double now = server.Now;
            Assert.AreEqual(a.Last.SnowAt(now), b.Last.SnowAt(now), 1e-6, "both work out the same snow cover");
            Assert.AreEqual(a.Last.BlendAt(now), b.Last.BlendAt(now), 1e-6, "and the same blend");

            // the GM sets the snow; then the weather clears: it melts from there
            Assert.IsTrue(server.SetSnow(0.8f));
            m_Weather = new SeasonWeatherNow { Falling = PrecipitationType.None, Intensity = 0f, Clock01 = 0.5f };
            int a1 = a.Applied;
            tw.TickUntil(() => a.Applied > a1 && a.Last.SnowRate < 0f, 120);
            Assert.Less(a.Last.SnowRate, 0f, "dry Winter day: it melts slowly");
            Assert.AreEqual(0.8f, a.Last.SnowCover, 0.01f, "from the cover the GM set");

            // an unchanged state is not sent again
            int a2 = a.Applied;
            tw.Tick(30);
            Assert.AreEqual(a2, a.Applied, "nothing changed, nothing sent");

            // always winter here: the floor holds
            Assert.IsTrue(server.SetClimate(SeasonClimate.AlwaysWinter));
            Assert.IsTrue(server.SetSnow(0f));
            int a3 = a.Applied;
            tw.TickUntil(() => a.Applied > a3, 120);
            Assert.IsTrue(a.Last.AlwaysWinter, "the map is always winter");
            Assert.GreaterOrEqual(a.Last.SnowAt(server.Now + 100000), m_Settings.alwaysWinterFloor - 1e-5f, "never below the floor");

            // back to the calendar
            Assert.IsTrue(server.Auto(0f));
            tw.Tick(5);
            Assert.AreEqual(Season.Autumn, server.Current.To, "the calendar's Autumn again");
            Assert.IsFalse(server.Current.GmHold);

            var late = tw.AddClient(tw.JoinPayload);
            tw.WaitConnected();
            tw.GoInGame();
            var lateSystem = late.GetExistingSystemManaged<SeasonClientSystem>();
            tw.TickUntil(() => lateSystem.Applied > 0, 300);
            Assert.AreEqual(server.Current, lateSystem.Last, "a player who comes later gets the season as it is now");
        }
    }
}
