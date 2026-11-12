using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Player;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>GM sight on the entity netcode (wave 1, 12 Nov 2026): NGO's rules (wave1-rules-gmsight.md) as pure functions. Written without a Unity run: not yet run.</summary>
    public class GmSightTests
    {
        [SetUp, TearDown]
        public void Clean()
        {
            GmSightRules.Forget(7);
            GmSightRules.Forget(8);
        }

        static GmSightPlayer P(ulong serial, string name, FactionId faction = FactionId.Hegemony) =>
            new GmSightPlayer { Serial = serial, Name = name, Faction = faction, Position = new Vector3(serial, 2f, -3f) };

        [Test]
        public void OnlyAGmWhoseSightIsOnIsSent()
        {
            Assert.IsTrue(GmSightRules.Sees(true, true));
            Assert.IsFalse(GmSightRules.Sees(true, false));
            Assert.IsFalse(GmSightRules.Sees(false, true), "nobody who is not a GM is ever sent the list");
        }

        [Test]
        public void SightIsOnByDefault_SetPerConnection_AndForgottenWhenTheyLeave()
        {
            Assert.IsTrue(GmSightRules.SeeAll(7));
            GmSightRules.Set(7, false);
            Assert.IsFalse(GmSightRules.SeeAll(7));
            Assert.IsTrue(GmSightRules.SeeAll(8), "another GM is not switched off");
            GmSightRules.Set(7, true);
            Assert.IsTrue(GmSightRules.SeeAll(7));
            GmSightRules.Set(7, false);
            GmSightRules.Forget(7);
            Assert.IsTrue(GmSightRules.SeeAll(7), "back in = on again, as NGO's network variable");
        }

        [Test]
        public void NamesAreCutToThirtyCharactersAndNeverNull()
        {
            Assert.AreEqual(new string('a', 30), GmSightRules.Fit(new string('a', 45)).ToString());
            Assert.AreEqual("Mira", GmSightRules.Fit("Mira").ToString());
            Assert.AreEqual(string.Empty, GmSightRules.Fit(null).ToString());
            Assert.AreEqual(1f, GmSightRules.SendEvery);
            Assert.AreEqual(3.5f, GmSightRules.FreshFor);
        }

        [Test]
        public void EveryPlayerIsInTheListInOrder_NumberedAndTheGmsOwnIsMarked()
        {
            var players = new List<GmSightPlayer> { P(7, "Gm"), P(8, "Other", FactionId.Null), P(9, "Third") };
            List<GmSeenRpc> entries = GmSightList.Entries(players, 8);
            Assert.AreEqual(3, entries.Count, "no cap, nobody dropped");
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(i, entries[i].Index);
                Assert.AreEqual(3, entries[i].Total);
            }
            Assert.AreEqual(0, entries[0].You);
            Assert.AreEqual(1, entries[1].You);
            Assert.AreEqual((byte)FactionId.Null, entries[1].Faction);
            Assert.AreEqual(9ul, entries[2].Serial);
            Assert.AreEqual(9f, entries[2].Position.x);
            Assert.AreEqual("Third", entries[2].Name.ToString());
            Assert.IsEmpty(GmSightList.Entries(new List<GmSightPlayer>(), 7));
        }

        [Test]
        public void TheClientPublishesOnlyWhenTheLastEntryCame_AndANewListStartsClean()
        {
            var collector = new GmSightList.Collector();
            var players = new List<GmSightPlayer> { P(7, "Gm"), P(8, "Other") };
            List<GmSeenRpc> entries = GmSightList.Entries(players, 7);
            Assert.IsFalse(collector.Add(entries[0], out _, out _));
            Assert.IsTrue(collector.Add(entries[1], out List<GmSeenPlayer> list, out ulong self));
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(7ul, self);
            Assert.AreEqual("Other", list[1].Name.ToString());
            Assert.AreEqual(FactionId.Hegemony, list[0].Faction);
            Assert.AreEqual(new Vector3(8f, 2f, -3f), list[1].Position);

            // a list that lost its tail never mixes into the next one
            Assert.IsFalse(collector.Add(entries[0], out _, out _));
            var smaller = GmSightList.Entries(new List<GmSightPlayer> { P(9, "Solo") }, 9);
            Assert.IsTrue(collector.Add(smaller[0], out List<GmSeenPlayer> next, out ulong self2));
            Assert.AreEqual(1, next.Count);
            Assert.AreEqual(9ul, self2);
        }

        [Test]
        public void TheFeedMakesSightActiveAndNamesOwnId()
        {
            var seen = new List<GmSeenPlayer> { new GmSeenPlayer { ClientId = 7, Name = "Gm" }, new GmSeenPlayer { ClientId = 8, Name = "Other" } };
            PlayerGmSight.FeedFromEntities(seen, 7);
            Assert.IsTrue(PlayerGmSight.Active);
            Assert.AreEqual(2, PlayerGmSight.Seen.Count);
            Assert.AreEqual(7ul, PlayerGmSight.SelfId);
        }

        [Test]
        public void GmSeeAllOnTheServerBodyKeepsTheSwitchPerConnection()
        {
            var body = new GameObject("server body stub");
            try
            {
                BodyRoleTag tag = body.AddComponent<BodyRoleTag>();
                tag.Assign(NetRoleKind.ServerBody, default, 7, false);
                var sight = body.AddComponent<PlayerGmSight>();
                Assert.IsTrue(sight.SeeAll);
                sight.SeeAll = false;
                Assert.IsFalse(GmSightRules.SeeAll(7), "gm.seeall false reaches the server's switch");
                Assert.IsFalse(sight.SeeAll);
                sight.SeeAll = true;
                Assert.IsTrue(GmSightRules.SeeAll(7));
            }
            finally { Object.DestroyImmediate(body); }
        }
    }
}
