using System.IO;
using NUnit.Framework;
using Unity.Collections;
using KryticalNova.Networking;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>M2b-1: the plain-C# parts of the server start-up: settings keys, fixed-string cutting, join text, entry options.</summary>
    public class KnServerPureTests
    {
        [Test]
        public void Settings_ReadTheContractKeys_FromTheFile_AndTheFlags()
        {
            string json = "{\"port\": 7800, \"saveService\": \"http://127.0.0.1:7801\", \"saveServiceKeyFile\": \"C:/k/key.txt\"," +
                          "\"certificateFile\": \"c.pem\", \"privateKeyFile\": \"k.pem\", \"serverName\": \"Transit Row\"}";
            var s = KnServerSettings.Parse(json, new[] { "exe", "-batchmode", "-nographics", "-role", "map", "-map", "Transit", "-port", "7799", "-pageport", "7800", "-devbots" });
            Assert.AreEqual(7799, s.port, "the command line wins");
            Assert.AreEqual(7800, s.PagePort);
            Assert.AreEqual("map", s.role);
            Assert.AreEqual("Transit", s.map);
            Assert.IsTrue(s.UsesSaveService);
            Assert.AreEqual("C:/k/key.txt", s.SaveServiceKeyPath);
            Assert.IsTrue(s.WantsEncryption);
            Assert.AreEqual("Transit Row", s.serverName);
            Assert.IsTrue(s.devBots, "-devbots is a flag without a value");
        }

        [Test]
        public void Settings_Defaults_NoSaveService_NoEncryption_NoBots()
        {
            var s = KnServerSettings.Parse(null, new[] { "exe" });
            Assert.IsFalse(s.UsesSaveService);
            Assert.IsFalse(s.WantsEncryption);
            Assert.IsFalse(s.devBots);
            Assert.AreEqual(string.Empty, s.ReadStopKey(), "no save service: no stop key");
        }

        [Test]
        public void StopKey_IsReadFromTheKeyFile_OnlyWhenTheServerUsesTheSaveService()
        {
            string file = Path.Combine(Path.GetTempPath(), "kn-test-key-" + System.Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(file, "  s3cret-key \n");
            try
            {
                var with = KnServerSettings.Parse("{\"saveService\":\"http://127.0.0.1:7802\",\"saveServiceKeyFile\":\"" + file.Replace("\\", "/") + "\"}", new[] { "exe" });
                Assert.AreEqual("s3cret-key", with.ReadStopKey());
                var without = KnServerSettings.Parse("{\"saveServiceKeyFile\":\"" + file.Replace("\\", "/") + "\"}", new[] { "exe" });
                Assert.AreEqual(string.Empty, without.ReadStopKey());
                var missing = KnServerSettings.Parse("{\"saveService\":\"http://127.0.0.1:7802\",\"saveServiceKeyFile\":\"/nope/none.txt\"}", new[] { "exe" });
                Assert.AreEqual(string.Empty, missing.ReadStopKey());
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void EntryOptions_FollowTheSettings_AndAnEditorServerDoesNotRequireProof()
        {
            var s = KnServerSettings.Parse("{\"maxPlayers\": 40}", new[] { "exe", "-devbots" });
            KnJoinCheckOptions real = KnServerEntry.JoinOptions(s, "0.1.0 build 9", isEditor: false);
            Assert.AreEqual(40, real.MaxPlayers);
            Assert.AreEqual("0.1.0 build 9", real.ServerBuild);
            Assert.IsTrue(real.RequireProof);
            Assert.IsTrue(real.AllowDevBots);
            Assert.IsFalse(KnServerEntry.JoinOptions(s, "x", isEditor: true).RequireProof);
            Assert.IsFalse(KnServerEntry.JoinOptions(KnServerSettings.Parse(null, new[] { "exe" }), "x", false).AllowDevBots, "bots are off unless asked for");
        }

        [Test]
        public void Fixed_TextIsCutOnACharacterBoundary_NeverThrows()
        {
            string longAscii = new string('a', 900);
            Assert.AreEqual(KnFixed.Max512, KnFixed.CutUtf8(longAscii, KnFixed.Max512).Length);
            Assert.DoesNotThrow(() => KnFixed.Text512(longAscii));
            Assert.DoesNotThrow(() => KnFixed.Text64(new string('b', 200)));
            Assert.AreEqual(string.Empty, KnFixed.CutUtf8(null, 10));

            string accents = new string('\u00e9', 100); // two bytes each
            string cut = KnFixed.CutUtf8(accents, 11);
            Assert.AreEqual(5, cut.Length, "five two-byte letters fit in 11 bytes");
            string emoji = "ab\U0001F600\U0001F600"; // a surrogate pair is four bytes
            Assert.AreEqual("ab\U0001F600", KnFixed.CutUtf8(emoji, 7));
            Assert.AreEqual("ab", KnFixed.CutUtf8(emoji, 5));
            Assert.AreEqual("hello", KnFixed.Text64("hello").ToString());
        }

        [Test]
        public void JoinText_RoundTrips_WithAndWithoutTheTicket_AndTheOldReaderStaysStrict()
        {
            string plain = JoinRules.PayloadText("0.1.0 build 3", false, "tok", "oldacc");
            Assert.IsTrue(JoinRules.TryReadText(plain, out string build, out bool editor, out string token, out string old, out string request, out string ticket));
            Assert.AreEqual("0.1.0 build 3", build);
            Assert.IsFalse(editor);
            Assert.AreEqual("tok", token);
            Assert.AreEqual("oldacc", old);
            Assert.AreEqual(string.Empty, request);
            Assert.AreEqual(string.Empty, ticket);

            string withTicket = JoinRules.PayloadText("b", true, "t", "o", "", "ticket-1");
            Assert.IsTrue(JoinRules.TryReadText(withTicket, out _, out bool ed, out _, out _, out string req2, out string tk));
            Assert.IsTrue(ed);
            Assert.AreEqual(string.Empty, req2);
            Assert.AreEqual("ticket-1", tk);
            Assert.IsFalse(JoinRules.TryRead(System.Text.Encoding.UTF8.GetBytes(withTicket), out _, out _), "the NGO server takes no ticket part");

            // the bytes the NGO game sends are the same as before
            CollectionAssert.AreEqual(JoinRules.Payload("b", false, "t", "o"), System.Text.Encoding.UTF8.GetBytes(JoinRules.PayloadText("b", false, "t", "o")));
            Assert.IsFalse(JoinRules.TryReadText("garbage", out _, out _, out _, out _, out _, out _));
            Assert.IsFalse(JoinRules.TryReadText("KN2|a|0|t", out _, out _, out _, out _, out _, out _), "four parts are too few");
        }

        [Test]
        public void JoinRules_Refusal_StillGivesTheSameWords_AsBeforeTheSplit()
        {
            byte[] payload = JoinRules.Payload("0.1.0 build 6", false);
            Assert.AreEqual(JoinRules.Words(JoinRules.Verdict.WrongBuild, "0.1.0 build 7", "0.1.0 build 6"), JoinRules.Refusal("0.1.0 build 7", false, payload, 0, 10));
            Assert.AreEqual(JoinRules.Words(JoinRules.Verdict.Full, null, null), JoinRules.Refusal("0.1.0 build 6", false, payload, 10, 10));
            Assert.IsNull(JoinRules.Refusal("0.1.0 build 6", false, payload, 9, 10));
            Assert.AreEqual(JoinRules.Words(JoinRules.Verdict.Unreadable, null, null), JoinRules.Refusal("x", false, new byte[] { 1, 2 }, 0, 10));
        }
    }
}
