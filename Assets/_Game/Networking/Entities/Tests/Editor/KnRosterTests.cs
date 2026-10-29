using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using KryticalNova.Networking;
using static KryticalNova.Networking.Entities.Tests.KnJoinCheckTestSupport;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>The roster that answers: scripted by the test.</summary>
    public sealed class FakeRoster : IServerRoster
    {
        public Func<string, string, string, string> Rule = (account, old, request) => "{\"characters\":[]}";
        public readonly List<string> Asked = new List<string>();

        public string Answer(string account, string oldAccount, string request)
        {
            Asked.Add(account + "|" + oldAccount + "|" + request);
            return Rule(account, oldAccount, request);
        }
    }

    /// <summary>M2b-2: "which characters do I have?" over the join path, then the short-lived client world goes away.</summary>
    public class KnRosterTests
    {
        private const string Request = "{\"op\":\"list\"}";

        [SetUp]
        public void SetUp() => BanLookup.Current = new FakeBans();

        [TearDown]
        public void TearDown() => BanLookup.Current = null;

        // ------------------------------------------------------------------ pure

        [Test]
        public void Pages_SplitAndAssemble_InAnyOrder()
        {
            string text = KnTestQueries.Pattern(KnRosterPages.PageChars * 3 + 17);
            var pages = new List<string>();
            Assert.IsTrue(KnRosterPages.TrySplit(text, pages));
            Assert.AreEqual(4, pages.Count);
            Assert.IsTrue(pages.All(p => p.Length <= KnRosterPages.PageChars));

            var asm = new KnRosterPages.Assembler();
            int[] order = { 2, 0, 3, 1, 2 }; // a duplicate on purpose
            KnRosterPages.Add last = KnRosterPages.Add.Ok;
            foreach (int i in order) last = asm.Take(i, pages.Count, pages[i]);
            Assert.AreEqual(KnRosterPages.Add.Complete, last);
            Assert.IsTrue(asm.IsComplete);
            Assert.AreEqual(text, asm.Text);
        }

        [Test]
        public void Pages_AnAnswerOfMaxPagesFits_OneCharMoreDoesNot()
        {
            var pages = new List<string>();
            Assert.IsTrue(KnRosterPages.TrySplit(new string('a', KnRosterPages.MaxReplyChars), pages));
            Assert.AreEqual(KnRosterPages.MaxPages, pages.Count);
            Assert.IsFalse(KnRosterPages.TrySplit(new string('a', KnRosterPages.MaxReplyChars + 1), pages));
            Assert.IsEmpty(pages);
        }

        [Test]
        public void Assembler_RefusesPagesThatContradictEachOther()
        {
            var asm = new KnRosterPages.Assembler();
            Assert.AreEqual(KnRosterPages.Add.Ok, asm.Take(0, 3, "a"));
            Assert.AreEqual(KnRosterPages.Add.Bad, asm.Take(1, 4, "b"), "the page count changed");
            Assert.IsTrue(asm.IsBad);
            Assert.IsNull(asm.Text);
            Assert.AreEqual(KnRosterPages.Add.Bad, new KnRosterPages.Assembler().Take(3, 3, "x"), "index out of range");
            Assert.AreEqual(KnRosterPages.Add.Bad, new KnRosterPages.Assembler().Take(0, KnRosterPages.MaxPages + 1, "x"), "too many pages");
            Assert.AreEqual(KnRosterPages.Add.Bad, new KnRosterPages.Assembler().Take(0, 0, "x"));
        }

        [Test]
        public void Answer_Rules_NoRoster_Unreadable_Failed_TooLong_AndGood()
        {
            var pages = new List<string>();
            string packed = RosterWire.Pack(Request);
            Assert.IsFalse(KnRosterAnswer.TryMake(null, AccountA, "", packed, pages, out string why));
            Assert.AreEqual(KnRosterAnswer.NoRoster, why);
            var roster = new FakeRoster();
            Assert.IsFalse(KnRosterAnswer.TryMake(roster, AccountA, "", "not-base64!!", pages, out why));
            Assert.AreEqual(KnRosterAnswer.Unreadable, why);
            Assert.IsFalse(KnRosterAnswer.TryMake(roster, "", "", packed, pages, out why), "no account to answer for");
            Assert.AreEqual(KnRosterAnswer.Unreadable, why);
            Assert.AreEqual(0, roster.Asked.Count, "an unreadable question never reaches the roster");

            var lines = new List<string>();
            roster.Rule = (a, o, r) => throw new InvalidOperationException("secret detail");
            Assert.IsFalse(KnRosterAnswer.TryMake(roster, AccountA, "", packed, pages, out why, lines.Add));
            Assert.AreEqual(KnRosterAnswer.Failed, why);
            Assert.IsFalse(lines.Any(l => l.Contains("secret detail")), "the log names the kind of failure, not its message");
            roster.Rule = (a, o, r) => null;
            Assert.IsFalse(KnRosterAnswer.TryMake(roster, AccountA, "", packed, pages, out why));
            Assert.AreEqual(KnRosterAnswer.Failed, why);

            roster.Rule = (a, o, r) => RandomText(20000);
            Assert.IsFalse(KnRosterAnswer.TryMake(roster, AccountA, "", packed, pages, out why, lines.Add));
            Assert.AreEqual(KnRosterAnswer.TooLong, why);
            Assert.IsEmpty(pages);

            roster.Rule = (a, o, r) => "{\"characters\":[1,2]}";
            Assert.IsTrue(KnRosterAnswer.TryMake(roster, AccountA, "b2b2", packed, pages, out why));
            Assert.AreEqual(1, pages.Count);
            Assert.AreEqual(AccountA + "|b2b2|" + Request, roster.Asked.Last());
        }

        [Test]
        public void JoinText_Carries_ThePackedQuestion_AndRefusesWhatCannotBeSent()
        {
            string text = KnRosterClient.BuildJoinText(Ask(), out string error);
            Assert.IsNull(error);
            Assert.IsTrue(JoinRules.TryReadText(text, out string build, out bool editor, out string token, out string old, out string request, out string ticket));
            Assert.AreEqual(Build, build);
            Assert.AreEqual("tok", token);
            Assert.AreEqual(Request, RosterWire.Unpack(request));
            Assert.AreEqual(string.Empty, ticket);

            Assert.IsNull(KnRosterClient.BuildJoinText(new KnRosterAsk { Request = "" }, out error));
            StringAssert.Contains("nothing", error);
            var huge = Ask();
            huge.Request = RandomText(9000);
            Assert.IsNull(KnRosterClient.BuildJoinText(huge, out error));
            StringAssert.Contains("too long", error);
        }

        // ------------------------------------------------------------------ in a test world

        [TestCase("{\"characters\":[]}", TestName = "Roster_ZeroCharacters")]
        [TestCase("{\"characters\":[{\"name\":\"Tolan\",\"level\":12}]}", TestName = "Roster_OneCharacter")]
        [TestCase("{\"characters\":[{\"name\":\"A\"},{\"name\":\"B\"},{\"name\":\"C\"},{\"name\":\"D\"},{\"name\":\"E\"}]}", TestName = "Roster_FiveCharacters")]
        public void TheAnswer_ArrivesWhole_ThenTheWorldIsGone_AndNobodyTookAPlace(string answer)
        {
            using var tw = Setup(out FakeRoster roster, out KnApprovalAssembler assembler);
            roster.Rule = (a, o, r) => answer;
            KnRosterQuery q = StartQuery(tw);
            Assert.GreaterOrEqual(tw.TickUntil(q.Pump, 600), 0, "no result. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.IsTrue(q.Result.Ok, q.Result.Error);
            Assert.AreEqual(answer, q.Result.Answer);
            Assert.AreEqual(1, roster.Asked.Count);
            Assert.AreEqual(AccountA + "||" + Request, roster.Asked[0]);
            Assert.IsNull(q.World, "the short-lived world is destroyed");
            Assert.IsFalse(tw.Clients[0].IsCreated);
            Assert.AreEqual(1, assembler.RosterAnswers);
            tw.Tick(40);
            Assert.AreEqual(0, assembler.ApprovedCount, "a question never takes a place on the server");
            Assert.IsEmpty(KnTestQueries.ServerKeys(tw), "and never gets a PlayerKey");
        }

        [Test]
        public void ALongAnswer_TravelsInSeveralPages_AndIsAssembledExactly()
        {
            using var tw = Setup(out FakeRoster roster, out _);
            string big = "{\"characters\":\"" + RandomText(3200) + "\"}";
            roster.Rule = (a, o, r) => big;
            KnRosterQuery q = StartQuery(tw);
            Assert.GreaterOrEqual(tw.TickUntil(q.Pump, 900), 0, "no result. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.IsTrue(q.Result.Ok, q.Result.Error);
            Assert.AreEqual(big, q.Result.Answer);
            var pages = new List<string>();
            KnRosterPages.TrySplit(RosterWire.Reply(big), pages);
            Assert.GreaterOrEqual(pages.Count, 4, "the test means to cross several pages");
        }

        [Test]
        public void AnAnswerTooBigForThePages_IsTurnedIntoARefusalInWords()
        {
            using var tw = Setup(out FakeRoster roster, out KnApprovalAssembler assembler);
            roster.Rule = (a, o, r) => RandomText(20000);
            KnRosterQuery q = StartQuery(tw);
            Assert.GreaterOrEqual(tw.TickUntil(q.Pump, 600), 0, "no result");
            Assert.IsFalse(q.Result.Ok);
            Assert.AreEqual(KnRosterAnswer.TooLong, q.Result.Error);
            Assert.AreEqual(KnRefuseReason.Refused, q.Result.Reason);
            Assert.AreEqual(0, assembler.RosterAnswers);
            Assert.IsFalse(tw.Clients[0].IsCreated);
        }

        [Test]
        public void WithoutProof_TheQuestionIsRefused_AndTheRosterIsNeverAsked()
        {
            using var tw = Setup(out FakeRoster roster, out _, new ScriptedProof { Rule = (t, p) => KnProof.Failed("bad token") });
            KnRosterQuery q = StartQuery(tw);
            Assert.GreaterOrEqual(tw.TickUntil(q.Pump, 600), 0, "no result");
            Assert.IsFalse(q.Result.Ok);
            Assert.AreEqual(KnRefuseReason.NoProof, q.Result.Reason);
            StringAssert.Contains("Unity sign-in", q.Result.Error);
            Assert.AreEqual(0, roster.Asked.Count);
            Assert.IsFalse(tw.Clients[0].IsCreated);
        }

        [Test]
        public void ABannedAccount_GetsItsBanText_NotItsCharacters()
        {
            ((FakeBans)BanLookup.Current).Banned[AccountA] = "banned for 2 days more";
            using var tw = Setup(out FakeRoster roster, out _);
            KnRosterQuery q = StartQuery(tw);
            Assert.GreaterOrEqual(tw.TickUntil(q.Pump, 600), 0, "no result");
            Assert.AreEqual(KnRefuseReason.Banned, q.Result.Reason);
            Assert.AreEqual("banned for 2 days more", q.Result.Error);
            Assert.AreEqual(0, roster.Asked.Count);
        }

        [Test]
        public void AServerWithNoRoster_SaysSo()
        {
            using var tw = Setup(out _, out KnApprovalAssembler assembler);
            assembler.Roster = null;
            RosterLookup.Current = null;
            KnRosterQuery q = StartQuery(tw);
            Assert.GreaterOrEqual(tw.TickUntil(q.Pump, 600), 0, "no result");
            Assert.AreEqual(KnRosterAnswer.NoRoster, q.Result.Error);
        }

        [Test]
        public void AServerThatNeverAnswers_EndsInATimeout_AndTheWorldIsGone()
        {
            using var tw = new KnTestWorld { JoinPayload = KnRosterClient.BuildJoinText(Ask(), out _) };
            tw.CreateWorlds(1); // nobody listens
            KnRosterQuery q = KnRosterQuery.Attach(tw.Clients[0], 5, () => tw.TickCount * (double)tw.Dt);
            tw.ConnectClient(tw.Clients[0]);
            Assert.GreaterOrEqual(tw.TickUntil(q.Pump, 600), 0, "the question never ended");
            Assert.IsFalse(q.Result.Ok);
            Assert.IsNotEmpty(q.Result.Error);
            Assert.IsFalse(tw.Clients[0].IsCreated);
        }

        [Test]
        public void Cancel_EndsTheQuestion_AndDestroysTheWorld_Once()
        {
            using var tw = Setup(out _, out _);
            KnRosterQuery q = StartQuery(tw);
            int done = 0;
            q.Task.ContinueWith(_ => done++);
            q.Cancel();
            q.Cancel();
            Assert.IsTrue(q.IsDone);
            Assert.IsFalse(tw.Clients[0].IsCreated);
            Assert.IsFalse(q.Result.Ok);
        }

        // ------------------------------------------------------------------ helpers

        private static KnRosterAsk Ask() => new KnRosterAsk { Build = Build, Token = "tok", Request = Request };

        private static KnTestWorld Setup(out FakeRoster roster, out KnApprovalAssembler assembler, IKnAccountProof proof = null)
        {
            var tw = new KnTestWorld { JoinPayload = KnRosterClient.BuildJoinText(Ask(), out string error) };
            Assert.IsNull(error);
            tw.CreateWorlds(1);
            Install(tw, proof ?? new ScriptedProof());
            assembler = tw.Server.GetExistingSystemManaged<KnApprovalAssembler>();
            roster = new FakeRoster();
            assembler.Roster = roster;
            return tw;
        }

        private static KnRosterQuery StartQuery(KnTestWorld tw)
        {
            tw.Listen();
            KnRosterQuery q = KnRosterQuery.Attach(tw.Clients[0], 30, () => tw.TickCount * (double)tw.Dt);
            tw.ConnectClient(tw.Clients[0]);
            return q;
        }

        /// <summary>Letters and digits from a fixed seed: does not compress, so the packed answer is as long as the text.</summary>
        private static string RandomText(int length)
        {
            var rng = new System.Random(1234);
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(alphabet[rng.Next(alphabet.Length)]);
            return sb.ToString();
        }
    }
}
