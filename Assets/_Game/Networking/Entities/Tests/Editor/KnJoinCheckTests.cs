using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using KryticalNova.Networking;
using static KryticalNova.Networking.Entities.Tests.KnJoinCheckTestSupport;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>M2b-1: the real join rule (<see cref="KnJoinCheck"/>) first alone, then through the approval of a test world.</summary>
    public class KnJoinCheckTests
    {
        private FakeBans m_Bans;

        [SetUp]
        public void SetUp()
        {
            m_Bans = new FakeBans();
            BanLookup.Current = m_Bans;
        }

        [TearDown]
        public void TearDown()
        {
            BanLookup.Current = null;
        }

        // ------------------------------------------------------------------ alone

        [Test]
        public void GoodJoin_IsAccepted_WithTheProvenAccount()
        {
            var check = Check(new ScriptedProof());
            KnJoinVerdict v = check.Poll(Conn(1), Payload(oldAccount: "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2"));
            Assert.AreEqual(KnJoinStatus.Accepted, v.Status);
            Assert.AreEqual(AccountA, v.Account);
            Assert.AreEqual("b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2", v.OldAccount);
            Assert.IsTrue(v.Proven);
        }

        [Test]
        public void ANotAnAccountId_InTheOldAccountSlot_IsDropped()
        {
            var check = Check(new ScriptedProof());
            KnJoinVerdict v = check.Poll(Conn(1), Payload(oldAccount: "../../etc"));
            Assert.AreEqual(KnJoinStatus.Accepted, v.Status);
            Assert.AreEqual(string.Empty, v.OldAccount);
        }

        [Test]
        public void UnreadablePayload_IsRefused_AsBadRequest()
        {
            var check = Check(new ScriptedProof());
            KnJoinVerdict v = check.Poll(Conn(1), "not a join request");
            Assert.AreEqual(KnJoinStatus.Refused, v.Status);
            Assert.AreEqual(KnRefuseReason.BadRequest, v.Reason);
            StringAssert.Contains("launcher", v.Detail);
        }

        [Test]
        public void WrongBuild_IsRefused_ButAnEditorClientMayJoinAnyBuild()
        {
            var check = Check(new ScriptedProof());
            KnJoinVerdict wrong = check.Poll(Conn(1), Payload(build: "0.1.0 build 6"));
            Assert.AreEqual(KnJoinStatus.Refused, wrong.Status);
            Assert.AreEqual(KnRefuseReason.WrongVersion, wrong.Reason);
            StringAssert.Contains("build 7", wrong.Detail);
            StringAssert.Contains("build 6", wrong.Detail);

            KnJoinVerdict editor = check.Poll(Conn(2), Payload(build: "anything", editor: true));
            Assert.AreEqual(KnJoinStatus.Accepted, editor.Status);
        }

        [Test]
        public void FullServer_IsRefused_AsServerFull()
        {
            var check = Check(new ScriptedProof(), online: 3, max: 3);
            KnJoinVerdict v = check.Poll(Conn(1), Payload());
            Assert.AreEqual(KnRefuseReason.ServerFull, v.Reason);
            Assert.AreEqual(KnJoinStatus.Accepted, Check(new ScriptedProof(), online: 2, max: 3).Poll(Conn(1), Payload()).Status);
            Assert.AreEqual(KnJoinStatus.Accepted, Check(new ScriptedProof(), online: 500, max: 0).Poll(Conn(1), Payload()).Status, "0 = no limit");
        }

        [Test]
        public void CharacterQuestion_IsRefused_UntilTheRosterPathLands()
        {
            var check = Check(new ScriptedProof());
            KnJoinVerdict v = check.Poll(Conn(1), Payload(request: "packed-roster-request"));
            Assert.AreEqual(KnJoinStatus.Refused, v.Status);
            Assert.AreEqual(KnRefuseReason.Refused, v.Reason);
        }

        [Test]
        public void BannedAccount_IsRefused_WithTheBanText()
        {
            m_Bans.Banned[AccountA] = "This account is banned from this server for 3 days more: cheating";
            KnJoinVerdict v = Check(new ScriptedProof()).Poll(Conn(1), Payload());
            Assert.AreEqual(KnRefuseReason.Banned, v.Reason);
            Assert.AreEqual("This account is banned from this server for 3 days more: cheating", v.Detail);

            m_Bans.Banned.Clear();
            Assert.AreEqual(KnJoinStatus.Accepted, Check(new ScriptedProof()).Poll(Conn(2), Payload()).Status);
        }

        [Test]
        public void AToken_ThatIsPendingForAWhile_IsAnsweredPending_ThenAccepted()
        {
            var proof = new ScriptedProof { Rule = (token, polls) => polls < 4 ? KnProof.Pending : KnProof.Proven(AccountA) };
            var check = Check(proof);
            Assert.AreEqual(KnJoinStatus.Pending, check.Poll(Conn(1), Payload()).Status);
            Assert.AreEqual(KnJoinStatus.Pending, check.Poll(Conn(1), Payload()).Status);
            Assert.AreEqual(KnJoinStatus.Pending, check.Poll(Conn(1), Payload()).Status);
            KnJoinVerdict v = check.Poll(Conn(1), Payload());
            Assert.AreEqual(KnJoinStatus.Accepted, v.Status);
            Assert.AreEqual(AccountA, v.Account);
            Assert.AreEqual(1, proof.Tokens.Count, "the token is handed to the proof once per connection");
        }

        [Test]
        public void ABadToken_IsRefused_WithThePlainWords_NotTheTechnicalReason()
        {
            var proof = new ScriptedProof { Rule = (token, polls) => KnProof.Failed("bad signature") };
            KnJoinVerdict v = Check(proof).Poll(Conn(1), Payload());
            Assert.AreEqual(KnRefuseReason.NoProof, v.Reason);
            Assert.AreEqual(KnJoinCheckOptions.NoProofText, v.Detail);
            StringAssert.DoesNotContain("signature", v.Detail);
        }

        [Test]
        public void WithoutProofRequired_AGameThatCannotProveItself_PlaysAsItsMadeUpAccount()
        {
            var proof = new ScriptedProof { Rule = (token, polls) => KnProof.Failed("no sign-in token") };
            var check = Check(proof, requireProof: false);
            KnJoinVerdict v = check.Poll(Conn(1), Payload(token: "", oldAccount: "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2"));
            Assert.AreEqual(KnJoinStatus.Accepted, v.Status);
            Assert.AreEqual("b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2", v.Account);
            Assert.IsFalse(v.Proven);
        }

        [Test]
        public void TheTravelTicket_ReachesTheTicketCheck_AndItCanRefuseOrDelay()
        {
            var tickets = new ScriptedTickets { Rule = (account, ticket, polls) => polls < 3 ? KnJoinVerdict.Pending : (ticket == "good" ? KnJoinVerdict.Accept : KnJoinVerdict.Refuse(KnRefuseReason.None, "no such ticket")) };
            var check = Check(new ScriptedProof(), tickets);
            Assert.AreEqual(KnJoinStatus.Pending, check.Poll(Conn(1), Payload(ticket: "good")).Status);
            Assert.AreEqual(KnJoinStatus.Pending, check.Poll(Conn(1), Payload(ticket: "good")).Status);
            Assert.AreEqual(KnJoinStatus.Accepted, check.Poll(Conn(1), Payload(ticket: "good")).Status);
            CollectionAssert.AreEqual(new[] { AccountA + "/good" }, tickets.Seen);

            var other = Check(new ScriptedProof(), new ScriptedTickets { Rule = (a, t, p) => KnJoinVerdict.Refuse(KnRefuseReason.None, "no such ticket") });
            KnJoinVerdict refused = other.Poll(Conn(5), Payload(ticket: "bad"));
            Assert.AreEqual(KnRefuseReason.BadTicket, refused.Reason, "a ticket refusal without a reason is a BadTicket");
        }

        [Test]
        public void TheDefaultTicketRule_AcceptsWithAndWithoutATicket()
        {
            var check = Check(new ScriptedProof(), new AcceptAllTickets());
            Assert.AreEqual(KnJoinStatus.Accepted, check.Poll(Conn(1), Payload()).Status);
            Assert.AreEqual(KnJoinStatus.Accepted, check.Poll(Conn(2), Payload(ticket: "whatever")).Status);
        }

        [Test]
        public void ADevBotToken_IsRefusedUnlessTheServerTakesBots()
        {
            var off = Check(new ScriptedProof { Rule = (t, p) => KnProof.Failed("not a Unity token") }, devBots: false);
            Assert.AreEqual(KnRefuseReason.NoProof, off.Poll(Conn(1), Payload(token: KnDevBots.TokenFor(3))).Reason);

            var on = Check(new ScriptedProof { Rule = (t, p) => KnProof.Failed("not a Unity token") }, devBots: true);
            KnJoinVerdict v = on.Poll(Conn(1), Payload(token: KnDevBots.TokenFor(3)));
            Assert.AreEqual(KnJoinStatus.Accepted, v.Status);
            Assert.IsFalse(v.Proven, "a bot's account is named, not proven");
            Assert.IsTrue(SaveKeys.IsId(v.Account));
            Assert.AreNotEqual(v.Account, on.Poll(Conn(2), Payload(token: KnDevBots.TokenFor(4))).Account);
            Assert.AreEqual(KnRefuseReason.NoProof, on.Poll(Conn(3), Payload(token: "kn-devbot:x1")).Reason, "only numbers name a bot");
        }

        [Test]
        public void Forget_DropsWhatWasKept()
        {
            var proof = new ScriptedProof();
            var check = Check(proof);
            check.Poll(Conn(1), Payload());
            check.Forget(Conn(1));
            Assert.AreEqual(1, proof.Forgotten);
        }

        // ------------------------------------------------------------------ the Unity token proof

        [Test]
        public void UnityAccountProof_ProvesASignedToken_AndRefusesAnExpiredOrForeignOne()
        {
            using RSA rsa = RSA.Create(2048);
            UnityKeys.UseForTests(new System.Collections.Generic.Dictionary<string, RSAParameters> { ["kid-1"] = rsa.ExportParameters(false) });
            try
            {
                DateTime now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
                var proof = new UnityAccountProof("project-1", () => now);
                string token = MakeToken(rsa, "kid-1", "player-42", "project-1", now.AddHours(1));
                KnProof good = proof.Prove(Conn(1), token);
                Assert.AreEqual(KnProofStatus.Proven, good.Status, good.Why);
                Assert.AreEqual(AccountIds.FromUnityPlayer("player-42"), good.Account);

                Assert.AreEqual(KnProofStatus.Failed, proof.Prove(Conn(2), MakeToken(rsa, "kid-1", "player-42", "project-1", now.AddHours(-1))).Status, "expired");
                Assert.AreEqual(KnProofStatus.Failed, proof.Prove(Conn(3), MakeToken(rsa, "kid-1", "player-42", "other-project", now.AddHours(1))).Status, "foreign project");
                Assert.AreEqual(KnProofStatus.Failed, proof.Prove(Conn(4), "").Status, "no token");
                Assert.AreEqual(KnProofStatus.Failed, proof.Prove(Conn(5), good.Account).Status, "not a token");
            }
            finally { UnityKeys.UseForTests(null); }
        }

        [Test]
        public void UnityAccountProof_WaitsForTheKeys_WithoutBlocking_ThenGivesUpIfTheyNeverCome()
        {
            using RSA rsa = RSA.Create(2048);
            UnityKeys.UseForTests(null); // no keys known
            try
            {
                DateTime now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
                var fetch = new TaskCompletionSource<bool>();
                int asked = 0;
                var proof = new UnityAccountProof("project-1", () => now, kid => { asked++; return fetch.Task; });
                string token = MakeToken(rsa, "kid-9", "player-1", "project-1", now.AddHours(1));

                Assert.AreEqual(KnProofStatus.Pending, proof.Prove(Conn(1), token).Status);
                Assert.AreEqual(KnProofStatus.Pending, proof.Prove(Conn(1), token).Status);
                Assert.AreEqual(1, asked, "the fetch starts once per connection");

                fetch.SetResult(false); // the fetch finished and found nothing
                KnProof failed = proof.Prove(Conn(1), token);
                Assert.AreEqual(KnProofStatus.Failed, failed.Status);
                StringAssert.Contains("keys", failed.Why);

                // keys arrive later: a new connection proves at once
                UnityKeys.UseForTests(new System.Collections.Generic.Dictionary<string, RSAParameters> { ["kid-9"] = rsa.ExportParameters(false) });
                Assert.AreEqual(KnProofStatus.Proven, proof.Prove(Conn(2), token).Status);
            }
            finally { UnityKeys.UseForTests(null); }
        }

        private static string MakeToken(RSA rsa, string kid, string player, string project, DateTime expires)
        {
            string header = UnityToken.ToBase64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"" + kid + "\"}"));
            long exp = new DateTimeOffset(expires).ToUnixTimeSeconds();
            string claims = UnityToken.ToBase64Url(Encoding.UTF8.GetBytes(
                "{\"sub\":\"" + player + "\",\"iss\":\"" + UnityToken.Issuer + "\",\"exp\":" + exp + ",\"project_id\":\"" + project + "\"}"));
            byte[] signature = rsa.SignData(Encoding.ASCII.GetBytes(header + "." + claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return header + "." + claims + "." + UnityToken.ToBase64Url(signature);
        }

        // ------------------------------------------------------------------ through the approval of a test world

        [Test]
        public void World_BannedAccount_IsTurnedAway_WithTheBanText()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload(build: Build) };
            tw.CreateWorlds(1);
            m_Bans.Banned[AccountA] = "banned for testing";
            Install(tw, new ScriptedProof());
            tw.Listen();
            tw.ConnectClient(tw.Clients[0]);
            AssertRefused(tw, tw.Clients[0], KnRefuseReason.Banned);
            Assert.AreEqual("banned for testing", KnTestWorld.Singleton<KnJoinRefusal>(tw.Clients[0]).Detail.ToString());
            Assert.AreEqual(0, PlayerKeys.Count);
        }

        [Test]
        public void World_WrongVersion_IsTurnedAway()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload(build: "0.1.0 build 6") };
            tw.CreateWorlds(1);
            Install(tw, new ScriptedProof());
            tw.Listen();
            tw.ConnectClient(tw.Clients[0]);
            AssertRefused(tw, tw.Clients[0], KnRefuseReason.WrongVersion);
        }

        [Test]
        public void World_ServerFull_SecondPlayerIsTurnedAway_FirstStays()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload() };
            tw.CreateWorlds(1);
            Install(tw, new ScriptedProof(), max: 1);
            tw.Listen();
            tw.ConnectClient(tw.Clients[0]);
            AssertJoined(tw, tw.Clients[0]);
            tw.Tick(5);

            World second = tw.AddClient(Payload(), connect: true);
            AssertRefused(tw, second, KnRefuseReason.ServerFull);
            Assert.AreEqual(1, KnTestWorld.Count<NetworkId>(tw.Server), "the first player is still in");
            Assert.IsTrue(KnTestWorld.HasNetworkId(tw.Clients[0]));
        }

        [Test]
        public void World_TokenPendingThenApproved_AccountReachesCoreAndIsForgottenOnLeave()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload(oldAccount: "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2") };
            tw.CreateWorlds(1);
            var proof = new ScriptedProof { Rule = (token, polls) => polls < 12 ? KnProof.Pending : KnProof.Proven(AccountA) };
            Install(tw, proof);
            tw.Listen();
            tw.ConnectClient(tw.Clients[0]);
            tw.Tick(6);
            Assert.IsFalse(KnTestWorld.HasNetworkId(tw.Clients[0]), "still pending: no NetworkId yet");
            AssertJoined(tw, tw.Clients[0]);
            tw.Tick(3);

            var keys = KnTestQueries.ServerKeys(tw);
            Assert.AreEqual(1, keys.Count);
            var key = new PlayerKey(keys[0].key);
            Assert.IsTrue(ServerAccounts.TryGet(key, out ServerAccounts.Entry entry), "the proven account should be in Core's ServerAccounts");
            Assert.AreEqual(AccountA, entry.Account);
            Assert.AreEqual("b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2", entry.OldAccount);

            KnTestQueries.Disconnect(tw.Clients[0]);
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<NetworkId>(tw.Server) == 0, 300), 0);
            tw.Tick(3);
            Assert.IsFalse(ServerAccounts.TryGet(key, out _), "the account is forgotten once the connection is closed");
        }

        [Test]
        public void World_TravelTicket_RidesInTheJoinAndReachesTheTicketCheck()
        {
            using var tw = new KnTestWorld { JoinPayload = Payload(ticket: "ticket-123") };
            tw.CreateWorlds(1);
            var tickets = new ScriptedTickets();
            Install(tw, new ScriptedProof(), tickets);
            tw.Connect();
            CollectionAssert.AreEqual(new[] { AccountA + "/ticket-123" }, tickets.Seen);
        }

        // ------------------------------------------------------------------ thin clients (bots)

        [Test]
        public void ThinClient_IsNotCreated_WhileBotsAreOff()
        {
            KnDevBots.Enabled = false;
            using var tw = new KnTestWorld();
            tw.CreateWorlds(0);
            Assert.IsNull(KnDevBots.CreateWorld(), "the guard: no thin-client world unless bots are switched on");
            Assert.IsNull(tw.AddThinClient(Payload(token: KnDevBots.TokenFor(1))));
        }

        [Test]
        public void ThinClient_JoinsADevServer_WithATestToken_AndIsRefusedByAServerThatDoesNotTakeBots()
        {
            KnDevBots.Enabled = true;
            try
            {
                using var tw = new KnTestWorld();
                tw.CreateWorlds(0);
                Install(tw, new ScriptedProof { Rule = (t, p) => KnProof.Failed("not a Unity token") }, devBots: true);
                tw.Listen();
                World bot = tw.AddThinClient(Payload(token: KnDevBots.TokenFor(1)));
                Assert.IsNotNull(bot);
                AssertJoined(tw, bot);

                // a server that was not told to take bots turns the same token away
                Install(tw, new ScriptedProof { Rule = (t, p) => KnProof.Failed("not a Unity token") }, devBots: false);
                World second = tw.AddThinClient(Payload(token: KnDevBots.TokenFor(2)));
                AssertRefused(tw, second, KnRefuseReason.NoProof);
            }
            finally { KnDevBots.Enabled = false; }
        }

        [Test]
        public void TheBotCode_IsCompiledOutOfReleaseBuilds()
        {
            // Release builds define neither KN_DEV_BOTS nor UNITY_EDITOR. Every mention of KnDevBots in code (not comments) and every
            // thin-client system filter must sit inside an #if that names KN_DEV_BOTS, and the file with the bots must be wrapped whole.
            string folder = System.IO.Path.Combine(UnityEngine.Application.dataPath, "_Game", "Networking", "Entities", "Runtime");
            var problems = new System.Collections.Generic.List<string>();
            foreach (string file in System.IO.Directory.GetFiles(folder, "*.cs"))
            {
                string name = System.IO.Path.GetFileName(file);
                var gates = new System.Collections.Generic.List<bool>(); // one entry per open #if: does it name KN_DEV_BOTS
                int line = 0;
                foreach (string raw in System.IO.File.ReadAllLines(file))
                {
                    line++;
                    string text = raw.Trim();
                    if (text.StartsWith("#if", StringComparison.Ordinal)) gates.Add(text.Contains("KN_DEV_BOTS"));
                    else if (text.StartsWith("#else", StringComparison.Ordinal) || text.StartsWith("#elif", StringComparison.Ordinal)) { if (gates.Count > 0) gates[gates.Count - 1] = false; }
                    else if (text.StartsWith("#endif", StringComparison.Ordinal)) { if (gates.Count > 0) gates.RemoveAt(gates.Count - 1); }
                    else if (!text.StartsWith("//", StringComparison.Ordinal) && (text.Contains("KnDevBots") || text.Contains("ThinClientSimulation")))
                    {
                        if (!gates.Contains(true)) problems.Add($"{name}:{line}: {text}");
                    }
                }
            }
            Assert.IsEmpty(problems, "bot code outside #if KN_DEV_BOTS || UNITY_EDITOR:\n" + string.Join("\n", problems));
            Assert.IsTrue(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "KnDevBots.cs")).TrimStart().StartsWith("#if KN_DEV_BOTS || UNITY_EDITOR", StringComparison.Ordinal),
                "KnDevBots.cs must be wrapped whole in #if KN_DEV_BOTS || UNITY_EDITOR");
        }
    }
}
