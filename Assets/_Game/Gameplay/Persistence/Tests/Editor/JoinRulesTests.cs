using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Online step 4: who may join (the same build only), the status answer, the server list's checks.</summary>
    public sealed class JoinRulesTests
    {
        private const string Release5 = "0.1.0 build 5";

        [Test]
        public void TheSameBuildJoins()
        {
            Assert.IsNull(JoinRules.Refusal(Release5, false, JoinRules.Payload(Release5, false), 3, 100));
        }

        [Test]
        public void AnotherBuildIsToldToUpdate()
        {
            string refusal = JoinRules.Refusal(Release5, false, JoinRules.Payload("0.1.0 build 4", false), 3, 100);
            StringAssert.Contains("build 5", refusal);
            StringAssert.Contains("build 4", refusal);
            StringAssert.Contains("launcher", refusal);
        }

        [Test]
        public void ADevelopmentBuildMeetsAReleaseServerAsAnotherBuild()
        {
            StringAssert.Contains("a development build", JoinRules.Refusal(Release5, false, JoinRules.Payload("0.1.0", false), 0, 100));
        }

        [Test]
        public void TheEditorMayJoinOrHostAnyBuild()
        {
            Assert.IsNull(JoinRules.Refusal(Release5, false, JoinRules.Payload("0.1.0", true), 0, 100));
            Assert.IsNull(JoinRules.Refusal("0.1.0", true, JoinRules.Payload(Release5, false), 0, 100));
        }

        [Test]
        public void AFullServerSaysSo()
        {
            StringAssert.Contains("full", JoinRules.Refusal(Release5, false, JoinRules.Payload(Release5, false), 100, 100));
            Assert.IsNull(JoinRules.Refusal(Release5, false, JoinRules.Payload(Release5, false), 99, 100));
        }

        [Test]
        public void AGameThatSendsNothingIsTooOld()
        {
            StringAssert.Contains("older", JoinRules.Refusal(Release5, false, null, 0, 100));
            StringAssert.Contains("older", JoinRules.Refusal(Release5, false, new byte[] { 1, 2, 3 }, 0, 100));
        }

        [Test]
        public void BuildNumbersAreReadFromTheVersion()
        {
            Assert.AreEqual(5, GameBuild.NumberIn(Release5));
            Assert.AreEqual(0, GameBuild.NumberIn("0.1.0"));
            Assert.AreEqual("build 12", GameBuild.Describe("0.1.0 build 12"));
        }

        [Test]
        public void TheStatusAnswerIsNeverLongerThanTheQuestion()
        {
            byte[] request = ServerStatusProtocol.Request();
            Assert.IsTrue(ServerStatusProtocol.IsRequest(request, request.Length));
            Assert.IsFalse(ServerStatusProtocol.IsRequest(request, 20), "a short question gets no answer");
            var status = new ServerStatusProtocol.Status { name = new string('N', 300), build = Release5, players = 3, max = 100 };
            byte[] reply = ServerStatusProtocol.Reply(status);
            Assert.LessOrEqual(reply.Length, request.Length);
            ServerStatusProtocol.Status read = ServerStatusProtocol.ReadReply(reply, reply.Length);
            Assert.AreEqual(3, read.players);
            Assert.AreEqual(Release5, read.build);
        }

        [Test]
        public void AServerOnTheListNeedsANameAnAddressAndAPort()
        {
            Assert.IsNull(ServerListFile.Problem("Krytical Nova Server", "203.0.113.5", 7777));
            Assert.IsNull(ServerListFile.Problem("Home", "play.example.com", 7790));
            Assert.IsNotNull(ServerListFile.Problem("", "203.0.113.5", 7777));
            Assert.IsNotNull(ServerListFile.Problem("Home", "", 7777));
            Assert.IsNotNull(ServerListFile.Problem("Home", "http://x/<script>", 7777));
            Assert.IsNotNull(ServerListFile.Problem("Home", "203.0.113.5", 0));
            Assert.IsNotNull(ServerListFile.Problem("Bad <b>name</b>", "203.0.113.5", 7777), "no formatting codes in a name");
            Assert.IsNull(ServerListFile.Problem("Karus's Server (EU)", "203.0.113.5", 7777));
        }

        [Test]
        public void TheListFileReadsBack()
        {
            var file = new ServerListFile { servers = new[] { new ServerListing { name = "A", address = "1.2.3.4", port = 7777 } } };
            ServerListFile read = ServerListFile.Parse(UnityEngine.JsonUtility.ToJson(file));
            Assert.AreEqual(1, read.servers.Length);
            Assert.AreEqual(7778, read.servers[0].StatusPort);
            Assert.IsNull(ServerListFile.Parse("not json"));
        }
    }
}
