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

        // ------------------------------------------------------------------ online step 5: Direct and Relay

        [Test]
        public void RelayCodesAreAFewLettersAndDigits()
        {
            Assert.IsTrue(ServerListing.IsRelayCode("AB12CD"));
            Assert.IsFalse(ServerListing.IsRelayCode(""));
            Assert.IsFalse(ServerListing.IsRelayCode("AB 12"));
            Assert.IsFalse(ServerListing.IsRelayCode("<b>x</b>"));
            Assert.IsFalse(ServerListing.IsRelayCode(new string('A', 13)));
        }

        [Test]
        public void TheServersOwnEntryKeepsItsPlaceWhenItsAddressChanges()
        {
            var before = new ServerListing { name = "Home", address = "203.0.113.5", port = 7777, machine = "abc123" };
            var after = new ServerListing { name = "Home", address = "198.51.100.7", port = 7777, machine = "abc123" };
            Assert.AreEqual(before.Key, after.Key, "LAST PLAYED still points at it");
            Assert.AreNotEqual(new ServerListing { address = "203.0.113.5", port = 7777 }.Key,
                               new ServerListing { address = "198.51.100.7", port = 7777 }.Key);
        }

        [Test]
        public void AutoIsThisServersAddressOnTheList()
        {
            Assert.IsNull(ServerListFile.Problem("Krytical Nova", ServerListing.AutoAddress, 7777));
            Assert.IsTrue(new ServerListing { address = " AUTO " }.IsAuto);
            Assert.IsFalse(new ServerListing { address = "203.0.113.5" }.IsAuto);
        }

        [Test]
        public void ThisMachineIsAShortFingerprint()
        {
            Assert.AreEqual(12, ThisMachine.Id.Length);
            Assert.AreEqual(ThisMachine.Id, ThisMachine.Id);
            StringAssert.DoesNotContain(System.Environment.MachineName.ToLowerInvariant(), ThisMachine.Id, "the PC's name is never published");
            Assert.IsTrue(new ServerListing { machine = ThisMachine.Id }.OnThisMachine);
            Assert.IsFalse(new ServerListing { machine = "" }.OnThisMachine);
        }

        [Test]
        public void TheStatusAnswerCarriesTheRelayCode()
        {
            byte[] request = ServerStatusProtocol.Request();
            var status = new ServerStatusProtocol.Status { name = new string('N', 300), build = Release5, players = 1, max = 100, relay = "AB12CD" };
            byte[] reply = ServerStatusProtocol.Reply(status);
            Assert.LessOrEqual(reply.Length, request.Length);
            Assert.AreEqual("AB12CD", ServerStatusProtocol.ReadReply(reply, reply.Length).relay);
        }

        [Test]
        public void TheServerRunsDirectOrRelay()
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kn-reach-test-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            try
            {
                Assert.IsFalse(ServerLaunchOptions.Load(folder, new string[0], out _).UsesRelay, "direct unless asked");
                Assert.IsTrue(ServerLaunchOptions.Load(folder, new[] { "-mode", "relay" }, out _).UsesRelay);
                ServerLaunchOptions odd = ServerLaunchOptions.Load(folder, new[] { "-mode", "carrier-pigeon" }, out _);
                Assert.AreEqual(ServerLaunchOptions.Direct, odd.connectionMode);
                Assert.IsTrue(odd.SaveConnectionMode(ServerLaunchOptions.Relay, out string error), error);
                Assert.IsTrue(ServerLaunchOptions.Load(folder, new string[0], out _).UsesRelay, "the switch is kept in the settings file");
            }
            finally
            {
                System.IO.Directory.Delete(folder, true);
            }
        }
    }
}
