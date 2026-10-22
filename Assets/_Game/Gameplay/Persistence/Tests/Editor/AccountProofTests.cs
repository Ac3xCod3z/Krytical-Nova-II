using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Persistence;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Online phase 2, who a joining player is: a Unity sign-in token checked by the server (signature, time, issuer,
    /// project), the account made from it, the join request that carries it, and the one-time carry-over of the
    /// characters a game played before sign-in. Tokens here are signed with a key made for the test.
    /// </summary>
    public sealed class AccountProofTests
    {
        private const string Project = "f2ed0dbf-063b-4143-9e21-0f4bb3b3aa81";
        private const string Kid = "public:TEST-KEY";
        private static readonly DateTime Now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        private RSA signer;
        private Dictionary<string, RSAParameters> keys;
        private ISaveStore previousStore;

        [SetUp]
        public void SetUp()
        {
            signer = RSA.Create();
            signer.KeySize = 2048;
            keys = new Dictionary<string, RSAParameters> { { Kid, signer.ExportParameters(false) } };
            previousStore = ServerSave.Store;
            ServerSave.Store = new MemorySaveStore();
            ServerSave.CarryOverEnabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            signer.Dispose();
            ServerSave.Store = previousStore;
            ServerSave.CarryOverEnabled = true;
        }

        private string Token(string claims, string kid = Kid, RSA key = null, string alg = "RS256")
        {
            string header = UnityToken.ToBase64Url(Encoding.UTF8.GetBytes("{\"alg\":\"" + alg + "\",\"kid\":\"" + kid + "\",\"typ\":\"JWT\"}"));
            string body = UnityToken.ToBase64Url(Encoding.UTF8.GetBytes(claims));
            byte[] signature = (key ?? signer).SignData(Encoding.ASCII.GetBytes(header + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return header + "." + body + "." + UnityToken.ToBase64Url(signature);
        }

        private static string Claims(string sub = "player-1", long exp = 0, string iss = UnityToken.Issuer, string audience = "upid:" + Project) =>
            "{\"sub\":\"" + sub + "\",\"iss\":\"" + iss + "\",\"exp\":" + (exp != 0 ? exp : new DateTimeOffset(Now.AddHours(1)).ToUnixTimeSeconds()) +
            ",\"nbf\":" + new DateTimeOffset(Now.AddMinutes(-5)).ToUnixTimeSeconds() + ",\"aud\":[\"" + audience + "\",\"envName:production\"]}";

        [Test]
        public void AUnitySignInOfThisProjectProvesThePlayer()
        {
            Assert.IsTrue(UnityToken.Verify(Token(Claims()), keys, Project, Now, out string player, out string reason), reason);
            Assert.AreEqual("player-1", player);
        }

        [Test]
        public void AForgedOrChangedTokenIsRefused()
        {
            using (RSA other = RSA.Create())
                Assert.IsFalse(UnityToken.Verify(Token(Claims(), key: other), keys, Project, Now, out _, out string forged));

            string token = Token(Claims());
            string[] parts = token.Split('.');
            string otherPlayer = UnityToken.ToBase64Url(Encoding.UTF8.GetBytes(Claims(sub: "someone-else")));
            Assert.IsFalse(UnityToken.Verify(parts[0] + "." + otherPlayer + "." + parts[2], keys, Project, Now, out _, out string changed));
            StringAssert.Contains("signature", changed);
            Assert.IsFalse(UnityToken.Verify(Token(Claims(), kid: "public:UNKNOWN"), keys, Project, Now, out _, out _), "a key Unity never published");
            Assert.IsFalse(UnityToken.Verify(Token(Claims(), alg: "none"), keys, Project, Now, out _, out _));
            Assert.IsFalse(UnityToken.Verify("not-a-token", keys, Project, Now, out _, out _));
            Assert.IsFalse(UnityToken.Verify("", keys, Project, Now, out _, out _));
        }

        [Test]
        public void AnExpiredTokenAnotherIssuerOrAnotherProjectIsRefused()
        {
            long anHourAgo = new DateTimeOffset(Now.AddHours(-1)).ToUnixTimeSeconds();
            Assert.IsFalse(UnityToken.Verify(Token(Claims(exp: anHourAgo)), keys, Project, Now, out _, out string expired));
            StringAssert.Contains("expired", expired);
            Assert.IsFalse(UnityToken.Verify(Token(Claims(iss: "https://evil.example")), keys, Project, Now, out _, out _));
            Assert.IsFalse(UnityToken.Verify(Token(Claims(audience: "upid:00000000-0000-0000-0000-000000000000")), keys, Project, Now, out _, out string project));
            StringAssert.Contains("project", project);
            long aMinuteAgo = new DateTimeOffset(Now.AddSeconds(-30)).ToUnixTimeSeconds();
            Assert.IsTrue(UnityToken.Verify(Token(Claims(exp: aMinuteAgo)), keys, Project, Now, out _, out _), "clocks may drift a little");
        }

        [Test]
        public void UnitysPublishedKeysAreRead()
        {
            RSAParameters key = signer.ExportParameters(false);
            string json = "{\"keys\":[{\"kid\":\"" + Kid + "\",\"kty\":\"RSA\",\"alg\":\"RS256\",\"use\":\"sig\",\"n\":\"" +
                          UnityToken.ToBase64Url(key.Modulus) + "\",\"e\":\"" + UnityToken.ToBase64Url(key.Exponent) + "\"}]}";
            Dictionary<string, RSAParameters> read = UnityKeys.Parse(json);
            Assert.IsTrue(UnityToken.Verify(Token(Claims()), read, Project, Now, out _, out string reason), reason);
            Assert.AreEqual(Kid, UnityKeys.KeyIdOf(Token(Claims())));
        }

        [Test]
        public void TheAccountIsMadeFromTheProvenPlayerAndFitsEverySaveKey()
        {
            string account = AccountIds.FromUnityPlayer("player-1");
            Assert.IsTrue(SaveKeys.IsId(account));
            Assert.AreEqual(account, AccountIds.FromUnityPlayer("player-1"));
            Assert.AreNotEqual(account, AccountIds.FromUnityPlayer("player-2"));
        }

        [Test]
        public void TheJoinRequestCarriesTheTokenAndTheOldAccount()
        {
            byte[] payload = JoinRules.Payload("0.1.0 build 7", false, "a.b.c", "0123456789abcdef0123456789abcdef");
            Assert.IsTrue(JoinRules.TryRead(payload, out string build, out bool editor, out string token, out string old));
            Assert.AreEqual("0.1.0 build 7", build);
            Assert.IsFalse(editor);
            Assert.AreEqual("a.b.c", token);
            Assert.AreEqual("0123456789abcdef0123456789abcdef", old);
            Assert.IsNull(JoinRules.Refusal("0.1.0 build 7", false, payload, 0, 100));
            StringAssert.Contains("older", JoinRules.Refusal("0.1.0 build 7", false, Encoding.UTF8.GetBytes("KN1|0.1.0 build 7|0"), 0, 100),
                "a game from before sign-in is told to update");
        }

        [Test]
        public void OldCharactersAreCarriedOverOnceAndNeverTwice()
        {
            const string old = "0123456789abcdef0123456789abcdef";
            string proven = AccountIds.FromUnityPlayer("player-1");
            AccountSave before = ServerSave.LoadAccount(old);
            before.characters.Add(new CharacterSave { characterId = "fedcba9876543210fedcba9876543210" });
            ServerSave.WriteAccount(before);
            var carried = new List<string>();
            Action<string, string> heard = (from, to) => carried.Add(from + ">" + to);
            ServerSave.AccountCarried += heard;
            try
            {
                Assert.IsTrue(ServerSave.CarryOverOnce(old, proven));
                Assert.AreEqual(1, ServerSave.LoadAccount(proven).characters.Count);
                Assert.AreEqual(1, ServerSave.LoadAccount(old).characters.Count, "the old account stays as a backup");
                Assert.IsFalse(ServerSave.CarryOverOnce(old, AccountIds.FromUnityPlayer("player-2")), "nobody can claim it again");
                CollectionAssert.AreEqual(new[] { old + ">" + proven }, carried);
            }
            finally
            {
                ServerSave.AccountCarried -= heard;
            }
        }

        [Test]
        public void NothingIsCarriedOverOntoAnAccountWithSavesOrWhenSwitchedOff()
        {
            const string old = "0123456789abcdef0123456789abcdef";
            string proven = AccountIds.FromUnityPlayer("player-1");
            ServerSave.WriteAccount(ServerSave.LoadAccount(old));
            ServerSave.WriteAccount(ServerSave.LoadAccount(proven));
            Assert.IsFalse(ServerSave.CarryOverOnce(old, proven), "the proven account has saves of its own");
            ServerSave.CarryOverEnabled = false;
            Assert.IsFalse(ServerSave.CarryOverOnce(old, AccountIds.FromUnityPlayer("player-3")));
        }


        // ------------------------------------------------------------------ moving characters between accounts (22 Oct 2026)

        private static string SaveOldAccount(string id, string name)
        {
            AccountSave before = ServerSave.LoadAccount(id);
            before.characters.Add(new CharacterSave { characterId = "fedcba9876543210fedcba9876543210", name = name, level = 80 });
            before.Set("deep-vault", "{\"items\":[1,2,3]}");
            ServerSave.WriteAccount(before);
            return id;
        }

        [Test]
        public void AMoveTakesTheCharactersTheirNamesAndTheVaultOnce()
        {
            string old = SaveOldAccount("0123456789abcdef0123456789abcdef", "Ascension");
            string signedIn = AccountIds.FromUnityPlayer("player-gmail");
            Assert.AreEqual(old, ServerRoster.Names().Holder("ascension").account);

            Assert.IsNull(ServerRoster.MoveCharacters(old, signedIn));
            AccountSave moved = ServerSave.LoadAccount(signedIn);
            Assert.AreEqual(1, moved.characters.Count);
            Assert.AreEqual("Ascension", moved.characters[0].name);
            Assert.AreEqual("{\"items\":[1,2,3]}", moved.Get("deep-vault"), "the Deep Vault comes along");
            Assert.AreEqual(signedIn, ServerRoster.Names().Holder("Ascension").account, "the name follows its character");
            Assert.AreEqual(1, ServerSave.LoadAccount(old).characters.Count, "the old account stays as a backup");
            StringAssert.Contains("moved before", ServerRoster.MoveCharacters(old, AccountIds.FromUnityPlayer("player-3")));
        }

        [Test]
        public void OntoAnAccountWithCharactersTheyJoinWhileSlotsAllowAndSharedThingsAreKeptAside()
        {
            string old = SaveOldAccount("0123456789abcdef0123456789abcdef", "Ascension");
            string other = AccountIds.FromUnityPlayer("player-gmail");
            AccountSave own = ServerSave.LoadAccount(other);
            own.characters.Add(new CharacterSave { characterId = "00000000000000000000000000000001", name = "Acension", slot = 0 });
            own.Set("deep-vault", "{\"items\":[]}");
            ServerSave.WriteAccount(own);

            Assert.IsNull(ServerRoster.MoveCharacters(old, other));
            AccountSave merged = ServerSave.LoadAccount(other);
            Assert.AreEqual(2, merged.characters.Count);
            Assert.AreNotEqual(merged.characters[0].slot, merged.characters[1].slot, "the moved character takes a free slot");
            Assert.AreEqual("{\"items\":[1,2,3]}", merged.Get("deep-vault"), "the old account's vault is kept by default");
            Assert.AreEqual(1, ServerSave.Store.Keys("accounts-set-aside/" + other).Count, "the new account's vault is kept as a copy");
            Assert.AreEqual(other, ServerRoster.Names().Holder("Ascension").account);
        }

        [Test]
        public void NothingIsMovedWhenTheSlotsAreFull()
        {
            string old = SaveOldAccount("0123456789abcdef0123456789abcdef", "Ascension");
            string other = AccountIds.FromUnityPlayer("player-gmail");
            AccountSave own = ServerSave.LoadAccount(other);
            for (int i = 0; i < 3; i++)
                own.characters.Add(new CharacterSave { characterId = "0000000000000000000000000000000" + (i + 1), name = "Full" + i, slot = i });
            ServerSave.WriteAccount(own);
            StringAssert.Contains("slots", ServerRoster.MoveCharacters(old, other));
            Assert.AreEqual(3, ServerSave.LoadAccount(other).characters.Count);
            Assert.AreEqual(old, ServerRoster.Names().Holder("Ascension").account);
        }

        [Test]
        public void ASignInLeftBehindBringsItsCharactersAlongWithTheList()
        {
            string old = SaveOldAccount(AccountIds.FromUnityPlayer("player-this-pc"), "Ascension");
            string signedIn = AccountIds.FromUnityPlayer("player-gmail");
            System.Func<string, string> prove = ServerRoster.ProvePlayer;
            ServerRoster.ProvePlayer = token => token == "left-behind" ? "player-this-pc" : null;
            try
            {
                RosterReply forged = ServerRoster.Handle(signedIn, null, new RosterRequest { op = RosterRequest.List, carry = "made-up" });
                Assert.AreEqual(0, forged.characters.Count, "a sign-in that proves nothing carries nothing");

                RosterReply reply = ServerRoster.Handle(signedIn, null, new RosterRequest { op = RosterRequest.List, carry = "left-behind" });
                Assert.IsTrue(reply.ok);
                Assert.AreEqual(1, reply.characters.Count);
                StringAssert.Contains("from this PC", reply.message);
                Assert.AreEqual(signedIn, ServerRoster.Names().Holder("Ascension").account);
                Assert.AreEqual(old, AccountIds.FromUnityPlayer("player-this-pc"));
            }
            finally
            {
                ServerRoster.ProvePlayer = prove;
            }
        }
    }
}
