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

        /// <summary>The owner's numbers (16 Nov 2026): Creds X win 50,000, tie 60 %, loss 40 %, bracket n pays n / 10; EXP 10 / 6 / 4 %.</summary>
        [Test]
        public void TheCreds_GoByResultAndBracket_TheOwnersTable()
        {
            var table = new (int bracket, long win, long tie, long loss)[] { (10, 50000, 30000, 20000), (5, 25000, 15000, 10000), (1, 5000, 3000, 2000) };
            foreach (var row in table)
            {
                Assert.AreEqual(row.win, CrossfireRewards.ResultCreds(m_Settings, CrossfireOutcome.Won, row.bracket), $"bracket {row.bracket} win");
                Assert.AreEqual(row.tie, CrossfireRewards.ResultCreds(m_Settings, CrossfireOutcome.Tied, row.bracket), $"bracket {row.bracket} tie");
                Assert.AreEqual(row.loss, CrossfireRewards.ResultCreds(m_Settings, CrossfireOutcome.Lost, row.bracket), $"bracket {row.bracket} loss");
            }
            Assert.AreEqual(0, CrossfireRewards.ResultCreds(m_Settings, CrossfireOutcome.Won, 0), "no bracket, no pay");
        }

        [Test]
        public void TheEXP_GoesToTheCharacterPetAndMount_AndAMaxedShare_IsPaidInCredsInstead()
        {
            // growing everything, bracket X win: 10 % of a 1,000,000 step, the pet and the mount 0.5 % of their growth, 50,000 Creds
            CrossfirePay pay = CrossfireRewards.Payout(m_Settings, CrossfireOutcome.Won, 10, false, 1_000_000, 0, 0);
            Assert.AreEqual(100_000, pay.CharacterExp);
            Assert.AreEqual(0.5f, pay.PetPercent, 1e-6f);
            Assert.AreEqual(0.5f, pay.MountPercent, 1e-6f);
            Assert.AreEqual(50_000, pay.Creds);

            // the brief's example: bracket X win, everything maxed = 50,000 + 25,000 + 5,000 + 5,000
            pay = CrossfireRewards.Payout(m_Settings, CrossfireOutcome.Won, 10, true, 1_000_000, 1, 1);
            Assert.AreEqual(0, pay.CharacterExp, "at the level cap: no EXP");
            Assert.AreEqual(0f, pay.PetPercent);
            Assert.AreEqual(0f, pay.MountPercent);
            Assert.AreEqual(85_000, pay.Creds);

            // a tie and a loss, no pet out, mount growing
            pay = CrossfireRewards.Payout(m_Settings, CrossfireOutcome.Tied, 10, false, 1_000_000, -1, 0);
            Assert.AreEqual(60_000, pay.CharacterExp, "6 %");
            Assert.AreEqual(0f, pay.PetPercent, "no pet out: nothing for it, and no Creds for it either");
            Assert.AreEqual(0.35f, pay.MountPercent, 1e-6f);
            Assert.AreEqual(30_000, pay.Creds);
            pay = CrossfireRewards.Payout(m_Settings, CrossfireOutcome.Lost, 5, false, 1_000_000, 0, 1);
            Assert.AreEqual(40_000, pay.CharacterExp, "4 %");
            Assert.AreEqual(0.25f, pay.PetPercent, 1e-6f);
            Assert.AreEqual(10_000 + 1_000, pay.Creds, "bracket V loss + the maxed mount's 10 %");
        }

        [Test]
        public void AKillDrops_OneIn2000_NeverWithABot()
        {
            Assert.AreEqual(1f / 2000f, m_Settings.killDropChance, 1e-9f, "the owner's 1 in 2,000");
            Assert.IsTrue(CrossfireRewards.KillDrops(m_Settings, false, false, 0.0004));
            Assert.IsFalse(CrossfireRewards.KillDrops(m_Settings, false, false, 0.0006), "the roll missed");
            Assert.IsFalse(CrossfireRewards.KillDrops(m_Settings, true, false, 0.0), "a War Bot's kill drops nothing");
            Assert.IsFalse(CrossfireRewards.KillDrops(m_Settings, false, true, 0.0), "killing a War Bot drops nothing");
        }
    }
}
