using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Tests;
using KryticalNova.World;
using KryticalNova.World.Portals;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.Crossfire.Entities.Tests
{
    /// <summary>
    /// Crossfire Mercs (owner, 16 Nov 2026, final order): the first players of a side get in up to the cap; the next is offered a Merc place
    /// on a short side; yes puts them on that side's Rally Point and they fight for it (their ghost carries the side to every client, and
    /// their kills count for it); no, or no answer, misses this war; at the war's end every Merc is their home faction again.
    /// </summary>
    public sealed class CrossfireMercWorldTests
    {
        const double Origin = 1000;   // the clock starts at 1005: Call 1005-1065, Rally to 1075, War to 1135, End to 1137
        CrossfireSettings m_Settings;
        double m_Now;
        readonly List<GameObject> m_Made = new List<GameObject>();

        [OneTimeSetUp]
        public void WarmUp()
        {
            using PlayerRig warm = PlayerRig.Create(1);   // see CrossfirePortWorldTests.WarmUp
            warm.Tick(10);
        }

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<CrossfireSettings>();
            m_Settings.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m_Settings.anchorToNightfall = false;
            m_Settings.callMinutes = 1f;
            m_Settings.joinWindowMinutes = 1f;
            m_Settings.musterSeconds = 10f;
            m_Settings.warMinutes = 1f;
            m_Settings.endSeconds = 2f;
            m_Settings.capPerFaction = 1;
            m_Settings.mercOfferSeconds = 5f;
            m_Now = Origin;
            CrossfireClockServerSystem.ClockSource = () => (false, default(WorldEnvironmentState));
        }

        [TearDown]
        public void TearDown()
        {
            CrossfireClockServerSystem.ClockSource = null;
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
            foreach (CrossfireArea a in new List<CrossfireArea>(CrossfireArea.All)) CrossfireArea.Untrack(a);
            Object.DestroyImmediate(m_Settings);
        }

        void RallyPoint(FactionId faction, Vector3 landingAt)
        {
            var go = new GameObject("test Rally Point " + faction);
            m_Made.Add(go);
            go.transform.position = landingAt + new Vector3(0, 2, 0);
            var yard = go.AddComponent<CrossfireArea>();
            yard.kind = CrossfireArea.AreaKind.MusterYard;
            yard.faction = faction;
            yard.size = new Vector3(20, 10, 20);
            var landing = new GameObject("landing");
            landing.transform.SetParent(go.transform, false);
            landing.transform.position = landingAt;
            yard.landing = landing.transform;
            CrossfireArea.Track(yard);
        }

        static void Settle(PlayerRig rig) => rig.Tick(30);

        [Test]
        public void TheSidesNextPlayer_IsOfferedAMercPlace_YesFightsForTheShortSide_NoMissesTheWar_AndTheWarsEndSendsThemHome()
        {
            var starts = new int3[3];
            for (int i = 0; i < 3; i++) starts[i] = new int3(-40000 + 5000 * i, 0, -40000);
            using PlayerRig rig = PlayerRig.Create(3, starts: starts);
            RallyPoint(FactionId.Hegemony, new Vector3(100, 0, 0));
            RallyPoint(FactionId.Null, new Vector3(-100, 0, 0));
            RallyPoint(FactionId.Covenant, new Vector3(0, 0, 100));
            var clock = rig.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            var entry = rig.Server.GetExistingSystemManaged<CrossfireEntryServerSystem>();
            entry.FactionOf = g => FactionId.Hegemony;   // three Hegemony players, one place on Hegemony's side
            entry.LevelOf = g => 112;
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            clock.Interval = 0f;
            m_Now = Origin + 5;
            Settle(rig);
            Entity a = rig.ServerGhosts[0], b = rig.ServerGhosts[1], c = rig.ServerGhosts[2];
            PlayerPose cBefore = rig.ServerPose(2);

            Assert.IsTrue(entry.Port(a, 112, FactionId.Hegemony, m_Settings.portPortalId, out ZoneTransferResult r) && r.Transferred, "the first gets in");
            Settle(rig);

            // the second is refused at the port and offered a Merc place on the shortest side (Null or Covenant, both empty)
            Assert.IsTrue(entry.Port(b, 112, FactionId.Hegemony, m_Settings.portPortalId, out r));
            Assert.IsFalse(r.Transferred, "refused by their own full side");
            Assert.IsTrue(entry.HasOffer(b), "offered a Merc place");
            Settle(rig);
            Assert.AreEqual(1, rig.Clients[1].GetExistingSystemManaged<CrossfireVoteClientSystem>().OffersShown, "the offer reached that player's game");
            Assert.AreEqual(0, rig.Clients[0].GetExistingSystemManaged<CrossfireVoteClientSystem>().OffersShown, "and nobody else's");

            Assert.IsTrue(entry.AnswerMerc(b, true), "yes");
            Settle(rig);
            Assert.IsTrue(entry.Sides.TryGetSide(b, out FactionId bSide) && bSide != FactionId.Hegemony, "fights for another side");
            Assert.IsTrue(CrossfireArea.TryRallyPoint(bSide, out CrossfireArea yard) && yard.Contains(new Vector3(rig.ServerPose(1).XMm, rig.ServerPose(1).YMm, rig.ServerPose(1).ZMm) * 0.001f),
                "put on the adopted side's Rally Point");
            Assert.AreEqual((byte)bSide, rig.Get<PvpSwitch>(rig.Server, b).WarSide, "the server's ghost carries the side");
            for (int viewer = 0; viewer < 3; viewer++)
                Assert.AreEqual((byte)bSide, rig.Get<PvpSwitch>(rig.Clients[viewer], rig.GhostIn(viewer, 1)).WarSide, $"client {viewer} sees the Merc's side");
            Assert.AreEqual(2, entry.Joined, "the Merc joined the war too (its entry kept for the send-back)");

            // the third: offered the last short side, says no: misses this war
            rig.Teleport(2, new int3(100000, 0, 0));   // walks into Hegemony's full yard
            Settle(rig);
            Assert.IsTrue(entry.HasOffer(c), "offered too");
            Assert.LessOrEqual(math.distance(new float2(rig.ServerPose(2).XMm, rig.ServerPose(2).ZMm), new float2(cBefore.XMm, cBefore.ZMm)), 5f, "put back while it decides");
            Assert.IsFalse(entry.AnswerMerc(c, false), "no");
            Assert.IsFalse(entry.Sides.TryGetSide(c, out _), "misses this war");
            Assert.IsTrue(entry.Told.Exists(t => t.who == c && t.text.Contains("miss this war")));

            // the War: the Merc's kill counts for its adopted side
            m_Now = Origin + 80;
            Settle(rig);
            Assert.IsTrue(entry.RecordKill(b, a), "the Merc kills a Hegemony player");
            Assert.AreEqual(1, entry.Sides.Kills(bSide));
            Assert.AreEqual(0, entry.Sides.Kills(FactionId.Hegemony));

            // over: every Merc is their home faction again, on every client
            m_Now = Origin + 200;
            Settle(rig);
            Assert.AreEqual(CrossfirePhase.Gap, clock.Current.Now.Phase);
            Assert.AreEqual(0, entry.Mercs.Count);
            Assert.AreEqual(0, rig.Get<PvpSwitch>(rig.Server, b).WarSide);
            for (int viewer = 0; viewer < 3; viewer++)
                Assert.AreEqual(0, rig.Get<PvpSwitch>(rig.Clients[viewer], rig.GhostIn(viewer, 1)).WarSide, $"client {viewer}: home again");
        }

        [Test]
        public void AnOfferNobodyAnswers_RunsOut_AndMissesTheWar()
        {
            using PlayerRig rig = PlayerRig.Create(2, starts: new[] { new int3(-40000, 0, -40000), new int3(-35000, 0, -40000) });
            RallyPoint(FactionId.Hegemony, new Vector3(100, 0, 0));
            RallyPoint(FactionId.Null, new Vector3(-100, 0, 0));
            var clock = rig.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            var entry = rig.Server.GetExistingSystemManaged<CrossfireEntryServerSystem>();
            entry.FactionOf = g => FactionId.Hegemony;
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            clock.Interval = 0f;
            m_Now = Origin + 5;
            Settle(rig);
            entry.Port(rig.ServerGhosts[0], 112, FactionId.Hegemony, m_Settings.portPortalId, out _);
            Settle(rig);
            entry.Port(rig.ServerGhosts[1], 112, FactionId.Hegemony, m_Settings.portPortalId, out _);
            Assert.IsTrue(entry.HasOffer(rig.ServerGhosts[1]));
            m_Now = Origin + 11;   // past the offer's 5 seconds
            Settle(rig);
            Assert.IsFalse(entry.HasOffer(rig.ServerGhosts[1]), "run out");
            Assert.AreEqual(1, entry.MercsDeclined, "no answer is a no");
            Assert.IsFalse(entry.Sides.TryGetSide(rig.ServerGhosts[1], out _));
        }

        [Test]
        public void WithMercsOff_TheNextPlayerIsOnlyRefused()
        {
            m_Settings.offerMercs = false;
            using PlayerRig rig = PlayerRig.Create(2, starts: new[] { new int3(-40000, 0, -40000), new int3(-35000, 0, -40000) });
            RallyPoint(FactionId.Hegemony, new Vector3(100, 0, 0));
            RallyPoint(FactionId.Null, new Vector3(-100, 0, 0));
            var clock = rig.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            var entry = rig.Server.GetExistingSystemManaged<CrossfireEntryServerSystem>();
            entry.FactionOf = g => FactionId.Hegemony;
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            clock.Interval = 0f;
            m_Now = Origin + 5;
            Settle(rig);
            entry.Port(rig.ServerGhosts[0], 112, FactionId.Hegemony, m_Settings.portPortalId, out _);
            Settle(rig);
            Assert.IsTrue(entry.Port(rig.ServerGhosts[1], 112, FactionId.Hegemony, m_Settings.portPortalId, out ZoneTransferResult r));
            Assert.IsFalse(entry.HasOffer(rig.ServerGhosts[1]));
            StringAssert.Contains("your side is full", r.Message);
            Assert.AreEqual(0, entry.MercOffers);
        }
    }
}
