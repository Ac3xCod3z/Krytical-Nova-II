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
    /// A whole Crossfire war on a server with real player ghosts and clients (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part A): the cap
    /// refuses the side's next real player and puts them back, the short side's vote fills it with War Bots (a stand-in for coder-12's),
    /// a real player takes a bot's place, kills count per side and reach the clients for the HUD, the winners are paid more, and a kill
    /// drops an item only between real players. Short times in a settings copy (never the shipped asset).
    /// </summary>
    public sealed class CrossfireWarWorldTests
    {
        const double Origin = 1000;   // the clock starts at 1005: Call 1005-1065, Rally to 1075, War to 1135, End to 1137
        CrossfireSettings m_Settings;
        double m_Now;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Object> m_Made2 = new List<Object>();
        IWarBotFill m_HadBots;

        /// <summary>coder-12's War Bots stand-in: counts per side, the calls Crossfire made, and bot ghosts of its own.</summary>
        sealed class FakeBots : IWarBotFill
        {
            public readonly Dictionary<FactionId, int> Bots = new Dictionary<FactionId, int>();
            public readonly Dictionary<Entity, FactionId> Ghosts = new Dictionary<Entity, FactionId>();
            public readonly List<WarBotRequest> Fills = new List<WarBotRequest>();
            public int RealPlayersSeen, Released, Ended;

            public int Fill(in WarBotRequest request, IReadOnlyList<Entity> realPlayers)
            {
                Fills.Add(request);
                RealPlayersSeen = realPlayers.Count;
                Bots[request.Faction] = Count(request.Faction) + request.Count;
                return request.Count;
            }

            public int Release(FactionId faction, int count)
            {
                int gone = math.min(count, Count(faction));
                Bots[faction] = Count(faction) - gone;
                Released += gone;
                return gone;
            }

            public int Count(FactionId faction) => Bots.TryGetValue(faction, out int n) ? n : 0;
            public bool IsWarBot(Entity ghost) => Ghosts.ContainsKey(ghost);
            public FactionId SideOf(Entity ghost) => Ghosts.TryGetValue(ghost, out FactionId f) ? f : FactionId.None;
            public void EndWar() { Ended++; Bots.Clear(); }
        }

        [OneTimeSetUp]
        public void WarmUp()
        {
            using PlayerRig warm = PlayerRig.Create(1);   // the rig's assets are made once per run; see CrossfirePortWorldTests.WarmUp
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
            m_Settings.voteSeconds = 3f;
            m_Settings.warMinutes = 1f;
            m_Settings.endSeconds = 2f;
            m_Now = Origin;
            CrossfireClockServerSystem.ClockSource = () => (false, default(WorldEnvironmentState));
            m_HadBots = WarBots.Fill;
        }

        [TearDown]
        public void TearDown()
        {
            WarBots.Fill = m_HadBots;
            CrossfireClockServerSystem.ClockSource = null;
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
            foreach (Object o in m_Made2) if (o != null) Object.DestroyImmediate(o);
            m_Made2.Clear();
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

        static readonly System.Reflection.BindingFlags Inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        /// <summary>The vote's inner state, for a failed vote count's message.</summary>
        static string VoteWhy(CrossfireEntryServerSystem entry, CrossfireClockServerSystem clock)
        {
            object F(string n) => typeof(CrossfireEntryServerSystem).GetField(n, Inst)?.GetValue(entry);
            CrossfireState c = clock.Current;
            return $" [voteOpen {F("m_VoteOpen")} for {F("m_VoteFor")} closes {F("m_VoteCloses")}; clock now {clock.Now} phase {c.Now.Phase} war {c.Now.WarStart} joinOpens {c.JoinOpensAt}; real H {entry.Sides.Real(FactionId.Hegemony)} N {entry.Sides.Real(FactionId.Null)}; bots {(WarBots.Fill == null ? "none" : WarBots.Fill.GetType().Name)}]";
        }

        static void Settle(PlayerRig rig) => rig.Tick(30);

        (PlayerRig rig, CrossfireClockServerSystem clock, CrossfireEntryServerSystem entry) Start(FactionId[] factions)
        {
            int n = factions.Length;
            var starts = new int3[n];
            for (int i = 0; i < n; i++) starts[i] = new int3(-40000 + 5000 * i, 0, -40000);
            PlayerRig rig = PlayerRig.Create(n, starts: starts);
            RallyPoint(FactionId.Hegemony, new Vector3(100, 0, 0));
            RallyPoint(FactionId.Null, new Vector3(-100, 0, 0));
            RallyPoint(FactionId.Covenant, new Vector3(0, 0, 100));
            var clock = rig.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            var entry = rig.Server.GetExistingSystemManaged<CrossfireEntryServerSystem>();
            var sideOf = new Dictionary<Entity, FactionId>();
            for (int i = 0; i < n; i++) sideOf[rig.ServerGhosts[i]] = factions[i];
            entry.FactionOf = g => sideOf.TryGetValue(g, out FactionId f) ? f : FactionId.None;
            entry.LevelOf = g => 112;
            entry.Random = new System.Random(7);
            var drop = ScriptableObject.CreateInstance<KryticalNova.Items.ItemDefinition>();
            drop.hideFlags = HideFlags.DontUnloadUnusedAsset;
            drop.itemId = 777;
            drop.displayName = "Test Gear";
            m_Made2.Add(drop);
            entry.PickDrop = (killer, level, pick) => level == 112 ? drop : null;   // the mobs' rule is HostileLoot.PickGear; here a known piece
            entry.GrowthOf = g => (false, 1_000_000, 0, 1);   // character and pet growing, mount fully grown
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            clock.Interval = 0f;
            m_Now = Origin + 5;
            Settle(rig);
            Assert.AreEqual(CrossfirePhase.Call, clock.Current.Now.Phase);
            return (rig, clock, entry);
        }

        void Port(PlayerRig rig, CrossfireEntryServerSystem entry, int i, FactionId f, bool expectMoved, string why)
        {
            Assert.IsTrue(entry.Port(rig.ServerGhosts[i], 112, f, m_Settings.portPortalId, out ZoneTransferResult r), why);
            Assert.AreEqual(expectMoved, r.Transferred, why + ": " + r.Message);
        }

        [Test]
        public void TheCap_RefusesTheSidesNextRealPlayer_AtThePortAndAtTheYard()
        {
            m_Settings.capPerFaction = 1;
            m_Settings.offerMercs = false;   // this test is the plain refusal (Mercs: CrossfireMercWorldTests)
            var (rig, _, entry) = Start(new[] { FactionId.Hegemony, FactionId.Hegemony, FactionId.Null });
            WarBots.Fill = null;   // no real War Bots in a test of the cap
            using (rig)
            {
                PlayerPose before1 = rig.ServerPose(1);
                Port(rig, entry, 0, FactionId.Hegemony, true, "the first of its side");
                Settle(rig);
                Assert.IsTrue(entry.Sides.TryGetSide(rig.ServerGhosts[0], out FactionId side) && side == FactionId.Hegemony, "joined Hegemony");
                Port(rig, entry, 1, FactionId.Hegemony, false, "the side is full: refused at the port");
                Port(rig, entry, 2, FactionId.Null, true, "another side has room");

                // walking (here: put) into the full side's yard: refused and sent back where it stood
                rig.Teleport(1, new int3(100000, 0, 0));
                Settle(rig);
                Assert.IsFalse(entry.Sides.TryGetSide(rig.ServerGhosts[1], out _), "never joined");
                Assert.LessOrEqual(math.distance(new float2(rig.ServerPose(1).XMm, rig.ServerPose(1).ZMm), new float2(before1.XMm, before1.ZMm)), 5f, "put back where it came from");
                Assert.AreEqual(2, entry.Refused);
                Assert.IsTrue(entry.Told.Exists(t => t.who == rig.ServerGhosts[1] && t.text.Contains("your side is full")), "told plainly");
            }
        }

        [Test]
        public void AShortSide_VotesYes_BotsFillIt_ARealPlayerTakesABotsPlace_KillsScore_AndTheWinnersArePaidMore()
        {
            m_Settings.capPerFaction = 3;
            m_Settings.killDropChance = 1f;   // every real-on-real kill drops, so the test sees it
            var bots = new FakeBots();
            var (rig, clock, entry) = Start(new[] { FactionId.Hegemony, FactionId.Null, FactionId.Hegemony });
            WarBots.Fill = bots;   // after the worlds: the real War Bots system fills the slot when the server world is made
            using (rig)
            {
                Port(rig, entry, 0, FactionId.Hegemony, true, "Hegemony's first");
                Port(rig, entry, 1, FactionId.Null, true, "Null's first");
                Settle(rig);
                Assert.AreEqual(2, entry.Sides.Count);

                // the Rally: both short sides are asked; Hegemony says yes, Null does not answer
                m_Now = Origin + 66;
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.Muster, clock.Current.Now.Phase);
                Assert.AreEqual(2, entry.VotesSent, "one vote to each short side's real player" + VoteWhy(entry, clock));
                Assert.IsTrue(entry.Vote(rig.ServerGhosts[0], true));
                Assert.IsFalse(entry.Vote(rig.ServerGhosts[0], true), "one vote each");
                m_Now = Origin + 70;   // past the vote's 3 seconds, before the gates open
                Settle(rig);
                Assert.AreEqual(2, bots.Fills.Count, "Covenant (nobody real) at once, then the side that voted yes; Null said nothing");
                Assert.AreEqual(FactionId.Covenant, bots.Fills[0].Faction, "a side with no real player is filled without a vote");
                Assert.AreEqual(3, bots.Fills[0].Count);
                WarBotRequest yes = bots.Fills[1];
                Assert.AreEqual(FactionId.Hegemony, yes.Faction);
                Assert.AreEqual(2, yes.Count, "filled to the cap: 1 real + 2 bots");
                Assert.AreEqual(10, yes.Bracket, "bracket X");
                Assert.AreEqual(new Vector3(100, 0, 0), yes.RallyAt, "at the side's own Rally Point");
                Assert.AreEqual(2, bots.RealPlayersSeen, "the bots see every real player of the war");
                Assert.IsTrue(entry.Told.Exists(t => t.who == rig.ServerGhosts[1] && t.text.Contains("voted no")), "Null was told it stays short");

                // a real player comes to the full-with-bots side: takes a bot's place
                Port(rig, entry, 2, FactionId.Hegemony, true, "a bot's place");
                Settle(rig);
                Assert.AreEqual(1, bots.Released, "one bot left for the real player");
                Assert.AreEqual(1, bots.Count(FactionId.Hegemony));

                // the War: kills per side, on the state every client gets
                m_Now = Origin + 80;
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.War, clock.Current.Now.Phase);
                var bot = rig.Server.EntityManager.CreateEntity();
                bots.Ghosts[bot] = FactionId.Null;
                Assert.IsTrue(entry.RecordKill(rig.ServerGhosts[0], rig.ServerGhosts[1]), "Hegemony kills Null");
                Assert.AreEqual(1, entry.DropsRolled, "a real player's kill of a real player dropped (chance 1 here)");
                Assert.AreEqual(777, entry.LastDrop);
                Assert.AreEqual(0, entry.Drops, "no Pack here to take it: nothing was given");
                Assert.IsFalse(entry.Told.Exists(t => t.text.Contains("dropped")), "and nothing that was not given is announced (review 1, item 5)");
                Assert.IsTrue(entry.RecordKill(rig.ServerGhosts[2], bot), "Hegemony kills a Null bot");
                Assert.AreEqual(1, entry.DropsRolled, "killing a bot drops nothing");
                Assert.IsTrue(entry.RecordKill(bot, rig.ServerGhosts[0]), "a Null bot kills Hegemony: it counts for Null");
                Assert.AreEqual(1, entry.DropsRolled, "a bot's kill drops nothing");
                Assert.IsFalse(entry.RecordKill(rig.ServerGhosts[0], rig.ServerGhosts[2]), "Hegemony on Hegemony never counts");
                Settle(rig);
                Assert.AreEqual(2, clock.Current.KillsHegemony);
                Assert.AreEqual(1, clock.Current.KillsNull);
                foreach (Unity.Entities.World client in rig.Clients)
                {
                    CrossfireState seen = client.GetExistingSystemManaged<CrossfireClientSystem>().Last;
                    Assert.AreEqual(2, seen.KillsHegemony, "a client's HUD counter");
                    Assert.AreEqual(1, seen.KillsNull);
                    Assert.AreEqual(0, seen.KillsCovenant);
                }

                // the End: Hegemony won 2-1; its real players are paid the win, Null's the loss (bracket X; the mount fully grown)
                m_Now = Origin + 136;
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.End, clock.Current.Now.Phase);
                foreach (int i in new[] { 0, 2 })
                {
                    CrossfirePay won = entry.LastPay[rig.ServerGhosts[i]];
                    Assert.AreEqual(100_000, won.CharacterExp, "10 % of the step");
                    Assert.AreEqual(0.5f, won.PetPercent, 1e-6f);
                    Assert.AreEqual(0f, won.MountPercent, "fully grown: Creds instead");
                    Assert.AreEqual(50_000 + 5_000, won.Creds, "X win + the maxed mount's 10 %");
                }
                CrossfirePay lost = entry.LastPay[rig.ServerGhosts[1]];
                Assert.AreEqual(40_000, lost.CharacterExp);
                Assert.AreEqual(20_000 + 2_000, lost.Creds);
                Assert.Greater(entry.LastPay[rig.ServerGhosts[0]].Creds, lost.Creds, "the winners are paid more");
                Assert.IsTrue(entry.Told.Exists(t => t.who == rig.ServerGhosts[0] && t.text.Contains("WON")), "told of the win");
                Assert.AreEqual(0, entry.LastGiven[rig.ServerGhosts[0]].Creds, "no wallet here: no Creds given, and none announced (review 1, item 5)");
                Assert.IsFalse(entry.Told.Exists(t => t.text.Contains("Creds")), "only what was given is told");

                // over: the sides, the score and the bots go
                m_Now = Origin + 200;
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.Gap, clock.Current.Now.Phase);
                Assert.AreEqual(1, bots.Ended, "every war bot goes when the war is over");
                Assert.AreEqual(0, entry.Sides.Count);
                Assert.AreEqual(0, clock.Current.KillsHegemony, "the next war starts at 0");
            }
        }

        /// <summary>Review 1, item 2: leaving the war area during the war is leaving the war: the place goes, no pay, no send-back.</summary>
        [Test]
        public void APlayerWhoLeavesTheWarArea_LosesThePlace_AndIsNotPaid()
        {
            m_Settings.capPerFaction = 1;
            var (rig, clock, entry) = Start(new[] { FactionId.Hegemony, FactionId.Hegemony });
            WarBots.Fill = null;
            using (rig)
            {
                Port(rig, entry, 0, FactionId.Hegemony, true, "in");
                Settle(rig);
                Assert.IsTrue(entry.Sides.TryGetSide(rig.ServerGhosts[0], out _), "joined");
                rig.Teleport(0, new int3(-60000, 0, -60000));   // walks off (here: is put) out of the war area
                Settle(rig);
                Assert.IsFalse(entry.Sides.TryGetSide(rig.ServerGhosts[0], out _), "left the war");
                Assert.AreEqual(1, entry.Left);
                Assert.IsTrue(entry.Told.Exists(t => t.who == rig.ServerGhosts[0] && t.text.Contains("you left the war")));
                Port(rig, entry, 1, FactionId.Hegemony, true, "the place is free again");
                Settle(rig);
                m_Now = Origin + 136;   // the End: only the one still in the war is paid
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.End, clock.Current.Now.Phase);
                Assert.IsFalse(entry.LastPay.ContainsKey(rig.ServerGhosts[0]), "the leaver is not paid");
                Assert.IsTrue(entry.Sides.TryGetSide(rig.ServerGhosts[1], out _), "the one who stayed is still in the war at its end (no kills here, so nobody is paid: AWarWithNoKills_PaysNobody)");
                Assert.IsFalse(entry.Sides.TryGetSide(rig.ServerGhosts[0], out _), "the leaver is not");
            }
        }

        /// <summary>Owner, 16 Nov 2026: one real player starts a war; sides with nobody real are filled with War Bots, the real side votes.</summary>
        [Test]
        public void OneRealPlayer_StartsTheWar_TheEmptySidesAreFilled_TheRealSideVotes()
        {
            var bots = new FakeBots();
            var (rig, clock, entry) = Start(new[] { FactionId.Hegemony });
            WarBots.Fill = bots;
            using (rig)
            {
                Port(rig, entry, 0, FactionId.Hegemony, true, "the one real player");
                Settle(rig);
                m_Now = Origin + 66;   // the Rally
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.Muster, clock.Current.Now.Phase);
                Assert.AreEqual(2, entry.SidesAutoFilled, "Null and Covenant, with nobody real, are filled at once");
                Assert.AreEqual(33, bots.Count(FactionId.Null));
                Assert.AreEqual(33, bots.Count(FactionId.Covenant));
                Assert.AreEqual(1, entry.VotesSent, "Hegemony, the side with a real player, gets the vote" + VoteWhy(entry, clock));
                Assert.AreEqual(0, bots.Count(FactionId.Hegemony), "and has no bots until it votes yes");
                Assert.IsTrue(bots.Fills.TrueForAll(f => f.Bracket == 10), "bracket X");
            }
        }

        /// <summary>Owner, 16 Nov 2026: a war left with no real player ends early and pays nobody (kills or not).</summary>
        [Test]
        public void AWarLeftWithNoRealPlayer_EndsEarly_AndPaysNobody()
        {
            var (rig, clock, entry) = Start(new[] { FactionId.Hegemony, FactionId.Null });
            WarBots.Fill = null;
            using (rig)
            {
                Port(rig, entry, 0, FactionId.Hegemony, true, "in");
                Port(rig, entry, 1, FactionId.Null, true, "in");
                Settle(rig);
                m_Now = Origin + 80;   // the War
                Settle(rig);
                Assert.IsTrue(entry.RecordKill(rig.ServerGhosts[0], rig.ServerGhosts[1]), "a kill, so it is not the no-kills rule that holds the pay");
                rig.Teleport(0, new int3(-60000, 0, -60000));
                rig.Teleport(1, new int3(-55000, 0, -60000));
                Settle(rig);
                Assert.AreEqual(0, entry.Sides.Count, "everyone left");
                Assert.AreEqual(CrossfirePhase.War, clock.Current.Now.Phase, "not yet: the grace time runs");
                m_Now = Origin + 80 + m_Settings.endWithoutRealPlayersSeconds + 1;
                Settle(rig);
                Assert.AreEqual(1, entry.EndedEmpty);
                Assert.AreEqual(CrossfirePhase.End, clock.Current.Now.Phase, "the war ended early");
                Assert.AreEqual(0, entry.LastPay.Count, "nobody is paid");
            }
        }

        /// <summary>Owner, 16 Nov 2026: a war that ends with no kill on any side pays nobody.</summary>
        [Test]
        public void AWarWithNoKills_PaysNobody()
        {
            var (rig, clock, entry) = Start(new[] { FactionId.Hegemony, FactionId.Null });
            WarBots.Fill = null;
            using (rig)
            {
                Port(rig, entry, 0, FactionId.Hegemony, true, "in");
                Port(rig, entry, 1, FactionId.Null, true, "in");
                Settle(rig);
                m_Now = Origin + 136;   // the End, nobody killed anybody
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.End, clock.Current.Now.Phase);
                Assert.AreEqual(0, entry.LastPay.Count, "no EXP, no Creds for anyone");
                Assert.AreEqual(2, entry.Told.FindAll(t => t.text == m_Settings.noKillsText).Count, "both told why");
            }
        }

        /// <summary>Review 1, item 4: a War Bot in a Rally Point is never admitted as a real player.</summary>
        [Test]
        public void AWarBot_IsNeverAdmittedAsARealPlayer()
        {
            var bots = new FakeBots();
            var (rig, _, entry) = Start(new[] { FactionId.Hegemony, FactionId.Hegemony });
            WarBots.Fill = bots;
            using (rig)
            {
                bots.Ghosts[rig.ServerGhosts[1]] = FactionId.Hegemony;   // this ghost is a bot
                rig.Teleport(1, new int3(100000, 0, 0));                 // stands in Hegemony's yard
                Settle(rig);
                Assert.IsFalse(entry.Sides.TryGetSide(rig.ServerGhosts[1], out _), "a bot is no real player");
                Assert.AreEqual(0, entry.Sides.Count);
                Assert.AreEqual(0, entry.Refused, "nor refused and put back");
            }
        }

        [Test]
        public void WithoutWarBots_NoVoteIsOffered()
        {
            var (rig, clock, entry) = Start(new[] { FactionId.Hegemony });
            WarBots.Fill = null;   // after the worlds, as above
            using (rig)
            {
                Port(rig, entry, 0, FactionId.Hegemony, true, "in");
                Settle(rig);
                m_Now = Origin + 66;
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.Muster, clock.Current.Now.Phase);
                Assert.AreEqual(0, entry.VotesSent, "the server runs no War Bots: nothing to vote on");
            }
        }
    }
}
