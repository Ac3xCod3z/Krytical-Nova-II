using KryticalNova.Factions;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Crossfire.Tests
{
    /// <summary>
    /// The war's rules, pure (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part A and the owner's corrections): 33 a side with War Bots
    /// counted, the 34th refused, a real player taking a bot's place; the team deathmatch (own-side kills never count, the most kills wins,
    /// ties); the bot vote; the pay by outcome and the extremely rare kill drop that War Bots never give.
    /// </summary>
    public sealed class CrossfireWarTests
    {
        CrossfireSettings m_Settings;

        [SetUp] public void SetUp() => m_Settings = ScriptableObject.CreateInstance<CrossfireSettings>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(m_Settings);

        [Test]
        public void ASide_Holds33_The34thIsRefused_AndARealPlayerTakesABotsPlace()
        {
            Assert.AreEqual(33, m_Settings.capPerFaction, "the shipped cap");
            var sides = new CrossfireSides<int>();
            for (int i = 0; i < 33; i++) Assert.AreEqual(CrossfireJoin.Joined, sides.TryJoin(i, FactionId.Hegemony, 33, 0), $"player {i + 1}");
            Assert.AreEqual(CrossfireJoin.Full, sides.TryJoin(33, FactionId.Hegemony, 33, 0), "the 34th");
            Assert.AreEqual(33, sides.Real(FactionId.Hegemony));
            Assert.AreEqual(CrossfireJoin.Joined, sides.TryJoin(33, FactionId.Null, 33, 0), "another side has room");
            Assert.AreEqual(CrossfireJoin.Already, sides.TryJoin(0, FactionId.Hegemony, 33, 0));

            // a side of 1 real player and 32 War Bots: the next real player takes a bot's place, never refused
            var filled = new CrossfireSides<int>();
            filled.TryJoin(100, FactionId.Covenant, 33, 0);
            Assert.AreEqual(CrossfireJoin.TookBotsPlace, filled.TryJoin(101, FactionId.Covenant, 33, 32));
            Assert.AreEqual(CrossfireJoin.Joined, filled.TryJoin(102, FactionId.Covenant, 33, 5), "room left beside the bots: no bot needs to go");
            filled.ForgetWhere(who => who == 100);
            Assert.AreEqual(2, filled.Real(FactionId.Covenant), "a player who left the game leaves the side");
        }

        [Test]
        public void TheTeamDeathmatch_MostKillsWins_TiesAllowed_AndNoSideScoresOnItself()
        {
            var sides = new CrossfireSides<int>();
            Assert.AreEqual(CrossfireOutcome.Tied, sides.Outcome(FactionId.Hegemony), "no kills at all: everyone ties");
            Assert.IsFalse(sides.AddKill(FactionId.Hegemony, FactionId.Hegemony), "a side never scores on its own");
            Assert.IsFalse(sides.AddKill(FactionId.None, FactionId.Null), "someone outside the war");
            Assert.IsTrue(sides.AddKill(FactionId.Hegemony, FactionId.Null));
            Assert.IsTrue(sides.AddKill(FactionId.Hegemony, FactionId.Covenant));
            Assert.IsTrue(sides.AddKill(FactionId.Covenant, FactionId.Null));
            Assert.AreEqual(2, sides.Kills(FactionId.Hegemony));
            Assert.AreEqual(CrossfireOutcome.Won, sides.Outcome(FactionId.Hegemony));
            Assert.AreEqual(CrossfireOutcome.Lost, sides.Outcome(FactionId.Covenant));
            Assert.AreEqual(CrossfireOutcome.Lost, sides.Outcome(FactionId.Null));
            sides.AddKill(FactionId.Covenant, FactionId.Hegemony);
            Assert.AreEqual(CrossfireOutcome.Tied, sides.Outcome(FactionId.Hegemony), "2 - 2 at the top");
            Assert.AreEqual(CrossfireOutcome.Tied, sides.Outcome(FactionId.Covenant));
            Assert.AreEqual(CrossfireOutcome.Lost, sides.Outcome(FactionId.Null));
            sides.Clear();
            Assert.AreEqual(0, sides.Kills(FactionId.Hegemony), "the next war starts at 0");
        }

        [Test]
        public void TheVote_PassesOnMoreYesThanNo_NobodyAnsweringIsANo_AndIsOfferedOnlyToAShortSide()
        {
            Assert.IsTrue(CrossfireVote.Passes(2, 1));
            Assert.IsFalse(CrossfireVote.Passes(1, 1), "a tie is no");
            Assert.IsFalse(CrossfireVote.Passes(0, 0), "nobody answered: no");
            Assert.IsTrue(CrossfireVote.Passes(1, 0));
            Assert.IsTrue(CrossfireVote.Offered(1, 0, 33, true));
            Assert.IsFalse(CrossfireVote.Offered(1, 32, 33, true), "already full with bots");
            Assert.IsFalse(CrossfireVote.Offered(33, 0, 33, true), "full of real players");
            Assert.IsFalse(CrossfireVote.Offered(0, 0, 33, true), "nobody on the side to vote");
            Assert.IsFalse(CrossfireVote.Offered(5, 0, 33, false), "the server runs no War Bots");
        }

        [Test]
        public void TheWinningSide_IsPaidMore_AndAKillDrops_ExtremelyRarely_NeverWithABot()
        {
            int won = CrossfireRewards.Experience(m_Settings, CrossfireOutcome.Won);
            int tied = CrossfireRewards.Experience(m_Settings, CrossfireOutcome.Tied);
            int lost = CrossfireRewards.Experience(m_Settings, CrossfireOutcome.Lost);
            Assert.Greater(won, tied, "the winners get more");
            Assert.Greater(tied, lost);
            Assert.Greater(lost, 0, "everyone who fought is paid something");

            Assert.Less(m_Settings.killDropChance, 0.01f, "extremely rare");
            m_Settings.killDropItemIds = new ushort[] { 501, 502 };
            m_Settings.killDropChance = 0.001f;
            Assert.AreEqual(0, CrossfireRewards.KillDrop(m_Settings, false, false, 0.002, 0.0), "the roll missed");
            Assert.AreEqual(501, CrossfireRewards.KillDrop(m_Settings, false, false, 0.0005, 0.1));
            Assert.AreEqual(502, CrossfireRewards.KillDrop(m_Settings, false, false, 0.0005, 0.9));
            Assert.AreEqual(0, CrossfireRewards.KillDrop(m_Settings, true, false, 0.0, 0.0), "a War Bot's kill drops nothing");
            Assert.AreEqual(0, CrossfireRewards.KillDrop(m_Settings, false, true, 0.0, 0.0), "killing a War Bot drops nothing");
            m_Settings.killDropItemIds = new ushort[0];
            Assert.AreEqual(0, CrossfireRewards.KillDrop(m_Settings, false, false, 0.0, 0.0), "no items named: no drop");
        }
    }
}
