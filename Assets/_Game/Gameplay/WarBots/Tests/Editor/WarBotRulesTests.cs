using System.Collections.Generic;
using KryticalNova.Factions;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.WarBots.Tests
{
    /// <summary>
    /// The War Bots' decisions (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part B and the owner's corrections): the level and gear
    /// spread, target picking, staying alive, playing the class, the squad and manners. Pure rules, no world.
    /// </summary>
    public class WarBotRulesTests
    {
        WarBotSettings s;

        const FactionId Us = FactionId.Hegemony;
        const FactionId Them = FactionId.Null;

        [SetUp]
        public void SetUp() => s = ScriptableObject.CreateInstance<WarBotSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(s);

        static WarBotView Me(Vector3 at, float health = 1f, WarBotStyle style = WarBotStyle.Careful, WarBotRole role = WarBotRole.Damage, float skill = 1f) => new WarBotView
        {
            GhostId = 1, Side = Us, Position = at, Alive = true, Health = health,
            Profile = new WarBotProfile { Style = style, Role = role, Skill = skill, ReactFast = 0.3f, ReactSlow = 0.6f },
            Rally = new Vector3(-100f, 0f, 0f),
        };

        static WarBotCombatant Foe(int id, float x, float z = 0f, float health = 1f, int target = 0) =>
            new WarBotCombatant { GhostId = id, Side = Them, Position = new Vector3(x, 0f, z), Alive = true, Health = health, TargetGhostId = target };

        static WarBotCombatant Friend(int id, float x, float z = 0f, int target = 0) =>
            new WarBotCombatant { GhostId = id, Side = Us, Position = new Vector3(x, 0f, z), Alive = true, Health = 1f, TargetGhostId = target };

        // ------------------------------------------------------------------ level and gear spread

        [Test]
        public void LevelsAndGear_LieBetweenTheLowestAndHighestRealPlayer()
        {
            WarBotRules.RangeOf(new List<int> { 112, 109, 110 }, 1, 1, out int min, out int max);
            Assert.AreEqual(109, min);
            Assert.AreEqual(112, max);
            var rng = new System.Random(5);
            var seen = new HashSet<int>();
            for (int i = 0; i < 400; i++)
            {
                WarBotProfile p = WarBotRules.RollProfile(s, WarBotRole.Damage, min, max, 250, 480, rng);
                Assert.That(p.Level, Is.InRange(109, 112));
                Assert.That(p.GearScore, Is.InRange(250, 480));
                Assert.That(p.Skill, Is.InRange(s.skillMin, s.skillMax));
                Assert.That(p.ReactFast, Is.LessThanOrEqualTo(p.ReactSlow));
                seen.Add(p.Level);
            }
            Assert.AreEqual(4, seen.Count, "every level of the range comes up, so not every bot is the same");
        }

        [Test]
        public void NoRealPlayers_FallsBackToTheBracket()
        {
            WarBotRules.RangeOf(new List<int>(), 100, 112, out int min, out int max);
            Assert.AreEqual(100, min);
            Assert.AreEqual(112, max);
        }

        [Test]
        public void GearScore_RaritySplitsBackAndBetterRarityAlwaysScoresHigher()
        {
            int epicNoTemper = WarBotRules.GearScore(3, 0, s.scorePerRarity);
            int rareFullTemper = WarBotRules.GearScore(2, 120, s.scorePerRarity);
            Assert.Greater(epicNoTemper, rareFullTemper);
            Assert.AreEqual(WarBotRules.GearScore(2, 60, 200), WarBotRules.GearScoreOf(new List<(int, int)> { (2, 40), (2, 80) }, 200));
            WarBotRules.SplitGearScore(2 * 200 + 75, 200, 4, 120, out int rarity, out int temper);
            Assert.AreEqual(2, rarity);
            Assert.AreEqual(75, temper);
            WarBotRules.SplitGearScore(2 * 200 + 190, 200, 4, 120, out _, out temper);
            Assert.AreEqual(120, temper, "held to the Temper cap");
        }

        [Test]
        public void Attributes_AddUpToThePointsAndFollowTheRole()
        {
            int[] tank = WarBotRules.SplitPoints(101, s.AttributesOf(WarBotRole.Tank));
            Assert.AreEqual(101, tank[0] + tank[1] + tank[2] + tank[3]);
            Assert.Greater(tank[1], tank[0], "a tank puts most into Vitality");
            int[] ranged = WarBotRules.SplitPoints(80, s.AttributesOf(WarBotRole.Ranged));
            Assert.Greater(ranged[2], ranged[0], "ranged puts most into Agility");
        }

        [Test]
        public void Personalities_FollowTheWeights()
        {
            s.aggressiveWeight = 0f; s.carefulWeight = 0f; s.supportWeight = 1f; s.loneWolfWeight = 0f;
            for (int i = 0; i < 10; i++) Assert.AreEqual(WarBotStyle.Support, WarBotRules.PickStyle(s, i / 10d));
        }

        // ------------------------------------------------------------------ target picking

        [Test]
        public void Target_PrefersTheLowHealthEnemy()
        {
            var all = new List<WarBotCombatant> { Foe(10, 10f), Foe(11, 14f, health: 0.1f) };
            Assert.AreEqual(11, WarBotRules.PickTarget(Me(Vector3.zero), all, s, 0.99, out string why));
            Assert.AreEqual("low", why);
        }

        [Test]
        public void Target_PrefersTheIsolatedEnemy()
        {
            var all = new List<WarBotCombatant> { Foe(10, 10f), Foe(12, 11f, 3f), Foe(11, 16f, 40f) };
            WarBotView me = Me(Vector3.zero, style: WarBotStyle.LoneWolf);
            me.Position = new Vector3(10f, 0f, 20f);
            Assert.AreEqual(11, WarBotRules.PickTarget(me, all, s, 0.99, out string why));
            Assert.AreEqual("isolated", why);
        }

        [Test]
        public void Target_HelpsATeammateUnderAttack()
        {
            var all = new List<WarBotCombatant> { Friend(2, 30f), Foe(10, 8f), Foe(11, 26f, target: 2) };
            Assert.AreEqual(11, WarBotRules.PickTarget(Me(Vector3.zero, style: WarBotStyle.Support), all, s, 0.99, out string why));
            Assert.AreEqual("help", why);
        }

        [Test]
        public void Target_LeavesAFightItWouldClearlyLose_UnlessAggressive()
        {
            // one enemy alone far off, five together near: the crowd is a lost fight for a lone bot
            var all = new List<WarBotCombatant> { Foe(10, 12f), Foe(11, 13f, 1f), Foe(12, 12f, -1f), Foe(13, 14f), Foe(14, 13f, 2f), Foe(20, 45f, 30f) };
            Assert.AreEqual(20, WarBotRules.PickTarget(Me(Vector3.zero), all, s, 0.99, out _), "careful: the crowd is a lost fight, the lone one far off is not");
            Assert.AreEqual(10, WarBotRules.PickTarget(Me(Vector3.zero, style: WarBotStyle.Aggressive), all, s, 0.99, out _), "aggressive: straight into the nearest");
        }

        [Test]
        public void Target_NeverAFriendNorAnyoneOutOfSight()
        {
            var all = new List<WarBotCombatant> { Friend(2, 3f), Foe(10, s.sightMeters + 5f) };
            Assert.AreEqual(0, WarBotRules.PickTarget(Me(Vector3.zero), all, s, 0.99, out _));
        }

        [Test]
        public void Target_AWeakBotSometimesTakesTheSecondBest()
        {
            var all = new List<WarBotCombatant> { Foe(10, 10f), Foe(11, 20f) };
            Assert.AreEqual(10, WarBotRules.PickTarget(Me(Vector3.zero, skill: 1f), all, s, 0.0, out _), "a perfect bot never slips");
            Assert.AreEqual(11, WarBotRules.PickTarget(Me(Vector3.zero, skill: 0f), all, s, 0.0, out string why), "a weak bot's slip");
            StringAssert.Contains("slip", why);
        }

        // ------------------------------------------------------------------ staying alive

        [Test]
        public void Low_UsesAStimFirst()
        {
            WarBotView me = Me(Vector3.zero, health: 0.3f);
            me.StimReady = true;
            Assert.AreEqual(WarBotAct.Stim, WarBotRules.Decide(me, new List<WarBotCombatant> { Foe(10, 3f, target: 1) }, s, 0.5).Act);
        }

        [Test]
        public void SwarmedAndLow_RunsTowardTheRallyPoint_AndKeepsRunningUntilHealed()
        {
            var all = new List<WarBotCombatant> { Foe(10, 3f, target: 1), Foe(11, 2f, 2f, target: 1), Foe(12, 4f, -1f, target: 1) };
            WarBotPlan plan = WarBotRules.Decide(Me(Vector3.zero, health: 0.3f), all, s, 0.5);
            Assert.AreEqual(WarBotAct.Retreat, plan.Act);
            Assert.Less(plan.MoveTo.x, 0f, "toward the Rally Point, away from the enemies");
            WarBotView healing = Me(Vector3.zero, health: 0.6f);
            healing.Retreating = true;
            Assert.AreEqual(WarBotAct.Retreat, WarBotRules.Decide(healing, new List<WarBotCombatant>(), s, 0.5).Act);
            healing.Health = 0.8f;
            Assert.AreNotEqual(WarBotAct.Retreat, WarBotRules.Decide(healing, new List<WarBotCombatant>(), s, 0.5).Act);
        }

        [Test]
        public void AggressiveStaysLongerThanCareful()
        {
            var all = new List<WarBotCombatant> { Foe(10, 3f, target: 1), Foe(11, 2f, 2f, target: 1), Foe(12, 4f, -1f, target: 1) };
            Assert.AreEqual(WarBotAct.Retreat, WarBotRules.Decide(Me(Vector3.zero, 0.3f, WarBotStyle.Careful), all, s, 0.5).Act);
            Assert.AreNotEqual(WarBotAct.Retreat, WarBotRules.Decide(Me(Vector3.zero, 0.3f, WarBotStyle.Aggressive), all, s, 0.5).Act);
        }

        // ------------------------------------------------------------------ the weapon class

        [Test]
        public void Ranged_KitesAnEnemyThatCloses()
        {
            var all = new List<WarBotCombatant> { Foe(10, 2f, target: 1) };
            WarBotPlan plan = WarBotRules.Decide(Me(Vector3.zero, role: WarBotRole.Ranged), all, s, 0.5);
            Assert.AreEqual(WarBotAct.Move, plan.Act);
            Assert.AreEqual("kite", plan.Why);
            Assert.Greater(WarBotRules.FlatDistance(plan.MoveTo, all[0].Position), s.kiteWithinMeters);
        }

        [Test]
        public void Melee_AttacksTheSameEnemyInsteadOfKiting()
        {
            var all = new List<WarBotCombatant> { Foe(10, 2f, target: 1) };
            WarBotPlan plan = WarBotRules.Decide(Me(Vector3.zero, role: WarBotRole.Damage), all, s, 0.5);
            Assert.AreEqual(WarBotAct.Attack, plan.Act);
            Assert.AreEqual(10, plan.TargetGhostId);
        }

        [Test]
        public void Buffs_BetweenFights_AndASupportBotFirstInOne()
        {
            WarBotView calm = Me(Vector3.zero);
            calm.BuffReady = true;
            Assert.AreEqual(WarBotAct.Buff, WarBotRules.Decide(calm, new List<WarBotCombatant>(), s, 0.5).Act);
            var fight = new List<WarBotCombatant> { Foe(10, 5f) };
            Assert.AreEqual(WarBotAct.Attack, WarBotRules.Decide(calm, fight, s, 0.5).Act, "a damage bot fights first");
            WarBotView support = Me(Vector3.zero, style: WarBotStyle.Support);
            support.BuffReady = true;
            Assert.AreEqual(WarBotAct.Buff, WarBotRules.Decide(support, fight, s, 0.5).Act);
        }

        [Test]
        public void TheAttackUnderWayGoesOn_WithASkillWhenOneIsReady()
        {
            var all = new List<WarBotCombatant> { Foe(10, 3f) };
            WarBotView me = Me(Vector3.zero);
            me.TargetGhostId = 10;
            me.TargetValid = true;
            Assert.AreEqual(WarBotAct.Wait, WarBotRules.Decide(me, all, s, 0.5).Act);
            me.SkillReady = true;
            Assert.AreEqual(WarBotAct.Skill, WarBotRules.Decide(me, all, s, 0.5).Act);
        }

        [Test]
        public void NothingInSight_HuntsTheNearestEnemyOfTheWar()
        {
            var all = new List<WarBotCombatant> { Foe(10, 200f) };
            WarBotPlan plan = WarBotRules.Decide(Me(Vector3.zero), all, s, 0.5);
            Assert.AreEqual(WarBotAct.Move, plan.Act);
            Assert.AreEqual("hunt", plan.Why);
        }

        // ------------------------------------------------------------------ Cadence combo

        [Test]
        public void Combo_GoesOneTwoThreeHits_WhenTheBotTriesIt()
        {
            var ready = new List<int> { 3, 1, 2 };
            Assert.AreEqual(1, WarBotRules.NextComboSkill(ready, 0, true), "1-hit first");
            Assert.AreEqual(2, WarBotRules.NextComboSkill(ready, 1, true), "then 2-hit");
            Assert.AreEqual(0, WarBotRules.NextComboSkill(ready, 2, true), "then 3-hit");
            Assert.AreEqual(1, WarBotRules.NextComboSkill(ready, 3, true), "and round again");
            Assert.AreEqual(0, WarBotRules.NextComboSkill(ready, 1, false), "not trying: any ready skill");
            Assert.AreEqual(-1, WarBotRules.NextComboSkill(new List<int>(), 0, true));
        }

        [Test]
        public void Combo_TheBestBotKeepsTheBeatCloserThanTheWeakest()
        {
            double best = System.Math.Abs(WarBotRules.BeatError(s, 1f, 1.0));
            double worst = System.Math.Abs(WarBotRules.BeatError(s, 0f, 1.0));
            Assert.AreEqual(s.beatErrorBestSeconds, best, 1e-6);
            Assert.AreEqual(s.beatErrorWorstSeconds, worst, 1e-6);
            Assert.IsFalse(WarBotRules.TriesCombo(s, 0f, 0.0), "a weak bot does not try the combo");
            Assert.IsTrue(WarBotRules.TriesCombo(s, 1f, 0.5));
        }

        [Test]
        public void Reaction_LiesInTheBotsOwnWindow()
        {
            var p = new WarBotProfile { ReactFast = 0.3f, ReactSlow = 0.6f };
            Assert.AreEqual(0.3f, WarBotRules.ReactionSeconds(p, 0), 1e-5);
            Assert.AreEqual(0.6f, WarBotRules.ReactionSeconds(p, 1), 1e-5);
        }

        // ------------------------------------------------------------------ the squad

        [Test]
        public void Invites_FromOwnSideOnly_RealPlayersWinOverABotSquad()
        {
            Assert.IsTrue(WarBotRules.AcceptsInvite(s, fromRealPlayer: true, sameSide: true, inParty: false, inPartyWithRealPlayer: false));
            Assert.IsTrue(WarBotRules.AcceptsInvite(s, true, true, inParty: true, inPartyWithRealPlayer: false), "leaves a bots-only squad for a player");
            Assert.IsFalse(WarBotRules.AcceptsInvite(s, true, true, true, inPartyWithRealPlayer: true));
            Assert.IsFalse(WarBotRules.AcceptsInvite(s, true, sameSide: false, false, false));
            Assert.IsTrue(WarBotRules.AcceptsInvite(s, fromRealPlayer: false, true, inParty: false, false));
            Assert.IsFalse(WarBotRules.AcceptsInvite(s, false, true, inParty: true, false));
        }

        [Test]
        public void Orders_AreWholeWordsOfTheLeader()
        {
            Assert.AreEqual(WarBotOrder.Follow, WarBotRules.ParseOrder("Follow me!", s));
            Assert.AreEqual(WarBotOrder.Follow, WarBotRules.ParseOrder("everyone ON ME", s));
            Assert.AreEqual(WarBotOrder.Hold, WarBotRules.ParseOrder("hold here", s));
            Assert.AreEqual(WarBotOrder.Attack, WarBotRules.ParseOrder("attack!", s));
            Assert.AreEqual(WarBotOrder.None, WarBotRules.ParseOrder("good game", s));
            Assert.AreEqual(WarBotOrder.None, WarBotRules.ParseOrder("gold", s), "a word inside another word is no order");
        }

        [Test]
        public void InASquad_FollowsAndTakesTheLeadersTarget()
        {
            WarBotView me = Me(Vector3.zero);
            me.HasLeader = true;
            me.Order = WarBotOrder.Follow;
            me.LeaderPosition = new Vector3(30f, 0f, 0f);
            WarBotPlan follow = WarBotRules.Decide(me, new List<WarBotCombatant>(), s, 0.5);
            Assert.AreEqual(WarBotAct.Move, follow.Act);
            Assert.AreEqual("follow", follow.Why);

            var all = new List<WarBotCombatant> { Foe(10, 5f), Foe(11, 25f) };
            me.LeaderPosition = new Vector3(3f, 0f, 0f);
            me.LeaderTargetGhostId = 11;
            WarBotPlan attack = WarBotRules.Decide(me, all, s, 0.5);
            Assert.AreEqual(WarBotAct.Attack, attack.Act);
            Assert.AreEqual(11, attack.TargetGhostId, "the leader's target, not the nearest");
            Assert.AreEqual("leader", attack.Why);
        }

        [Test]
        public void Hold_OnlyFightsInsideTheHoldRing()
        {
            WarBotView me = Me(Vector3.zero);
            me.HasLeader = true;
            me.Order = WarBotOrder.Hold;
            me.HoldAt = Vector3.zero;
            var far = new List<WarBotCombatant> { Foe(10, s.holdMeters + 10f) };
            Assert.AreEqual(0, WarBotRules.PickTarget(me, far, s, 0.99, out _));
            Assert.AreNotEqual(WarBotAct.Move, WarBotRules.Decide(me, far, s, 0.5).Act, "it does not chase out of the ring");
        }

        [Test]
        public void OutnumberedLeader_TheSquadRegroups()
        {
            WarBotView me = Me(Vector3.zero);
            me.HasLeader = true;
            me.Order = WarBotOrder.Attack;
            me.LeaderPosition = new Vector3(40f, 0f, 0f);
            var all = new List<WarBotCombatant> { Foe(10, 41f), Foe(11, 42f), Foe(12, 40f, 2f), Foe(13, 39f, -2f) };
            WarBotPlan plan = WarBotRules.Decide(me, all, s, 0.5);
            Assert.AreEqual(WarBotAct.Move, plan.Act);
            Assert.AreEqual("regroup", plan.Why);
        }

        // ------------------------------------------------------------------ backfill and manners

        [Test]
        public void Backfill_IdleBotsNotWithAPlayerGoFirst()
        {
            var bots = new List<(bool, bool)> { (true, false), (false, true), (false, false), (true, true) };
            CollectionAssert.AreEqual(new[] { 2 }, WarBotRules.PickReleases(bots, 1));
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, WarBotRules.PickReleases(bots, 3));
            Assert.AreEqual(4, WarBotRules.PickReleases(bots, 9).Count, "never more than there are");
        }

        [Test]
        public void Manners_NeverSpam()
        {
            Assert.IsFalse(WarBotRules.MaySay(s, lastSaidAt: 100, now: 100 + s.chatGapSeconds - 1, roll: 0));
            Assert.IsTrue(WarBotRules.MaySay(s, 100, 100 + s.chatGapSeconds, 0));
            Assert.IsFalse(WarBotRules.MayEmote(s, 100, 100 + s.emoteGapSeconds - 1, 0));
            Assert.IsFalse(WarBotRules.MaySay(s, 0, 1000, roll: 0.99), "the chance can say no");
            foreach (string line in s.lowLines) StringAssert.DoesNotContain("heal", line.ToLowerInvariant(), "no healers in the game: a bot never asks for a heal");
        }

        [Test]
        public void NoRealPlayerLeft_AllBotsGoHome_AfterTheGrace_AndAPlayerBackStartsItAgain()
        {
            // Master-24, 16 Nov 2026: bots fight for real players only
            double since = double.NaN;
            Assert.IsFalse(WarBotRules.AllGoHome(2, ref since, 100, s), "real players there: the bots stay");
            Assert.IsTrue(double.IsNaN(since));
            Assert.IsFalse(WarBotRules.AllGoHome(0, ref since, 200, s), "the last one just went: the grace starts");
            Assert.AreEqual(200, since);
            Assert.IsFalse(WarBotRules.AllGoHome(0, ref since, 200 + s.noRealPlayerSeconds - 0.1, s), "inside the grace");
            Assert.IsFalse(WarBotRules.AllGoHome(1, ref since, 200 + s.noRealPlayerSeconds - 0.05, s), "one came back (a relog)");
            Assert.IsTrue(double.IsNaN(since), "the count starts again");
            Assert.IsFalse(WarBotRules.AllGoHome(0, ref since, 300, s));
            Assert.IsTrue(WarBotRules.AllGoHome(0, ref since, 300 + s.noRealPlayerSeconds, s), "nobody for the whole grace: every bot leaves");
        }
    }
}
