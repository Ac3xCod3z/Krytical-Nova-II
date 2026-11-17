using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Tests;
using KryticalNova.World;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.Crossfire.Entities.Tests
{
    /// <summary>
    /// The owner's war flow (16 Nov 2026) in a server and its clients: when the join window opens every player of a bracket with a zone
    /// gets the join prompt wherever they are; yes puts them on their own Rally Point; no can still join at the Marshal; a running War
    /// takes late joiners, who earn their share; at the end every real player gets the results screen.
    /// </summary>
    public sealed class CrossfireJoinWorldTests
    {
        const double Origin = 1000;   // the clock starts at 1005: Call (all of it the join window) 1005-1065, Rally to 1075, War to 1135, End to 1137
        CrossfireSettings m_Settings;
        double m_Now;
        IWarBotFill m_HadBots;
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
            m_Settings.lateJoinCutoffMinutes = 0.25f;   // late joins until 1120
            m_Now = Origin;
            m_HadBots = WarBots.Fill;
            CrossfireClockServerSystem.ClockSource = () => (false, default(WorldEnvironmentState));
        }

        [TearDown]
        public void TearDown()
        {
            WarBots.Fill = m_HadBots;
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

        static Vector3 Meters(in PlayerPose p) => new Vector3(p.XMm, p.YMm, p.ZMm) * 0.001f;

        (PlayerRig rig, CrossfireClockServerSystem clock, CrossfireEntryServerSystem entry) Start(int players, System.Func<Entity, int> level)
        {
            var starts = new int3[players];
            for (int i = 0; i < players; i++) starts[i] = new int3(-40000 + 5000 * i, 0, -40000);
            PlayerRig rig = PlayerRig.Create(players, starts: starts);
            RallyPoint(FactionId.Hegemony, new Vector3(100, 0, 0));
            RallyPoint(FactionId.Null, new Vector3(-100, 0, 0));
            RallyPoint(FactionId.Covenant, new Vector3(0, 0, 100));
            var clock = rig.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            var entry = rig.Server.GetExistingSystemManaged<CrossfireEntryServerSystem>();
            WarBots.Fill = null;   // no War Bots here (the real system fills the slot when the server world is made)
            entry.FactionOf = g => FactionId.Hegemony;
            entry.LevelOf = level;
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            clock.Interval = 0f;
            return (rig, clock, entry);
        }

        /// <summary>A GM's war goes straight to the Rally (no join window): the bracket still gets the prompt there, once, and yes joins.</summary>
        [Test]
        public void AWarThatSkipsItsJoinWindow_SendsThePromptInTheRally_Once_AndYesJoins()
        {
            Entity low = Entity.Null;
            var (rig, clock, entry) = Start(3, g => g == low ? 5 : 112);
            using (rig)
            {
                Entity a = rig.ServerGhosts[0];
                low = rig.ServerGhosts[2];
                m_Now = Origin + 66;   // straight into the Rally, as gm.crossfire start does: the join window never ran
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.Muster, clock.Current.Now.Phase);
                Assert.AreEqual(2, entry.PromptsSent, "the bracket's two players are asked in the Rally, not the level-5 one");
                m_Now += 1;
                Settle(rig);
                Assert.AreEqual(2, entry.PromptsSent, "once a war");
                Assert.IsTrue(entry.AnswerPrompt(a, true));
                Settle(rig);
                Assert.IsTrue(CrossfireArea.TryRallyPoint(FactionId.Hegemony, out CrossfireArea yard) && yard.Contains(Meters(rig.ServerPose(0))), "yes lands A on Hegemony's Rally Point");
            }
        }

        [Test]
        public void EveryBracketPlayer_GetsTheJoinPrompt_YesGoesToTheirRallyPoint_NoCanStillJoinAtTheMarshal_AndAllGetTheResults()
        {
            Entity low = Entity.Null;
            var (rig, clock, entry) = Start(3, g => g == low ? 5 : 112);
            using (rig)
            {
                Entity a = rig.ServerGhosts[0], b = rig.ServerGhosts[1];
                low = rig.ServerGhosts[2];   // level 5: below the first bracket, never called
                m_Now = Origin + 5;   // the join window opens with the Call here
                Settle(rig);

                Assert.AreEqual(2, entry.PromptsSent, "a prompt for each bracket player, none for the level-5 one");
                Assert.AreEqual(1, rig.Clients[0].GetExistingSystemManaged<CrossfireJoinClientSystem>().PromptsShown, "it reached player A's game");
                Assert.AreEqual(1, rig.Clients[1].GetExistingSystemManaged<CrossfireJoinClientSystem>().PromptsShown);
                Assert.AreEqual(0, rig.Clients[2].GetExistingSystemManaged<CrossfireJoinClientSystem>().PromptsShown, "not to the low-level player");
                m_Now += 1;
                Settle(rig);
                Assert.AreEqual(2, entry.PromptsSent, "once a war: never sent again");

                // A: yes -> straight onto their own Rally Point, and in the war
                Assert.IsTrue(entry.AnswerPrompt(a, true));
                Settle(rig);
                Assert.IsTrue(CrossfireArea.TryRallyPoint(FactionId.Hegemony, out CrossfireArea yard) && yard.Contains(Meters(rig.ServerPose(0))), "A is on Hegemony's Rally Point");
                Assert.IsTrue(entry.Sides.TryGetSide(a, out FactionId aSide) && aSide == FactionId.Hegemony, "A fights for their own side");
                Assert.IsTrue(entry.TryJoinWay(a, out CrossfireJoinWay way) && way == CrossfireJoinWay.Prompt, "the server knows how A came in");

                // B: no -> stays put, told they can still join at the Marshal; then the Marshal puts them in
                Assert.IsFalse(entry.AnswerPrompt(b, false));
                Settle(rig);
                Assert.IsFalse(entry.Sides.TryGetSide(b, out _), "said no: not in");
                Assert.IsTrue(entry.Told.Exists(t => t.who == b && t.text.Contains("Marshal")), "told the Marshal still takes them");
                Assert.IsTrue(entry.AskToJoin(b, CrossfireJoinWay.Marshal), "the Marshal's JOIN CROSSFIRE");
                Settle(rig);
                Assert.IsTrue(entry.Sides.TryGetSide(b, out _), "in at the Marshal");
                Assert.IsTrue(entry.TryJoinWay(b, out way) && way == CrossfireJoinWay.Marshal);

                // the low-level player asks the Marshal: their bracket has no war
                Assert.IsFalse(entry.AskToJoin(low, CrossfireJoinWay.Marshal));

                // to the End: every real player gets the results screen (no kills here: "war over", nobody paid)
                m_Now = Origin + 136;
                Settle(rig);
                Assert.AreEqual(2, entry.ResultsSent, "a results screen for each real player");
                Assert.AreEqual(1, rig.Clients[0].GetExistingSystemManaged<CrossfireJoinClientSystem>().ResultsShown, "it reached A's game");
                Assert.AreEqual(0, rig.Clients[2].GetExistingSystemManaged<CrossfireJoinClientSystem>().ResultsShown, "not the player who was not in it");
            }
        }

        [Test]
        public void ARunningWar_TakesALateJoinerAtTheMarshal_WhoEarnsTheirShare_UntilItsLastMinutes()
        {
            var (rig, clock, entry) = Start(3, g => 112);
            using (rig)
            {
                Entity a = rig.ServerGhosts[0], late = rig.ServerGhosts[1], tooLate = rig.ServerGhosts[2];
                m_Now = Origin + 5;
                Settle(rig);
                Assert.IsTrue(entry.AnswerPrompt(a, true), "A is in from the start");
                Settle(rig);

                m_Now = Origin + 90;   // the War (1075-1135), 15 s in
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.War, clock.Current.Now.Phase);
                Assert.IsTrue(entry.AskToJoin(late, CrossfireJoinWay.Marshal), "a late joiner at the Marshal");
                Settle(rig);
                Assert.IsTrue(entry.Sides.TryGetSide(late, out _), "in the war");
                Assert.AreEqual(1, entry.LateJoins);
                Assert.AreEqual(1f, entry.ShareOf(a), 1e-4f, "in from the start: all the pay");
                Assert.AreEqual(0.75f, entry.ShareOf(late), 0.02f, "joined 15 s into a 60-s war: about 3/4");
                Assert.IsTrue(entry.Told.Exists(t => t.who == late && t.text.Contains("late")), "told they earn a share");

                // the last 15 s (the cutoff here) take nobody
                m_Now = Origin + 125;
                Settle(rig);
                Assert.IsFalse(entry.AskToJoin(tooLate, CrossfireJoinWay.Marshal), "the War's last minutes take nobody");
                Assert.IsFalse(entry.Sides.TryGetSide(tooLate, out _));
            }
        }
    }
}
