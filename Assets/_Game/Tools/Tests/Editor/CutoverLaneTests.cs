using System;
using System.IO;
using System.Linq;
using KryticalNova.Core;
using KryticalNova.EditorTools;
using KryticalNova.Networking;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The release lane of the DOTS cutover (12 Nov 2026): the rules that keep the real server real and the test lane test, stated without a build.</summary>
    public class CutoverLaneTests
    {
        const string NgoSettings =
            "{\"serverName\":\"Nova\",\"port\":7777,\"pagePort\":8090,\"maxPlayers\":50,\"pagePassword\":\"hunter2-page\"," +
            "\"gameMasters\":[{\"name\":\"Gm One\",\"account\":\"acc1\",\"password\":\"hunter2-gm\"}]," +
            "\"saveService\":\"http://127.0.0.1:7801\",\"mapServers\":[{\"name\":\"Transit Row\",\"map\":\"MeridianSprawl_Z1_TransitRow\",\"port\":7779,\"pagePort\":8092}]," +
            "\"serverList\":[{\"name\":\"Nova\",\"address\":\"auto\",\"port\":7777,\"statusPort\":0}],\"callLimits\":{}}";

        // ------------------------------------------------------------------ the lanes' defines

        [Test]
        public void ReleaseServerHasNoTestLaneDefine_TestLaneServerHasIt()
        {
            CollectionAssert.DoesNotContain(EntityReleaseServer.Defines, "KN_TEST_LANE", "the real lane takes the real ports, publishes the list, makes no max-level kit");
            CollectionAssert.Contains(EntityReleaseServer.Defines, "KN_ENTITIES_SERVER");
            CollectionAssert.Contains(ServerBuildBuilder.EntityDefines, "KN_TEST_LANE", "the test lane stays as it is");
            CollectionAssert.Contains(ServerBuildBuilder.EntityDefines, "KN_ENTITIES_SERVER");
        }

        [Test]
        public void TheSkyLiftIsOffInTheReleaseServerAndTheCutoverClient_NotInTheTestLane()
        {
            Assert.AreEqual("KN_SKY_LIFT_OFF", SkyLiftLink.OffDefine);
            CollectionAssert.Contains(EntityReleaseServer.Defines, SkyLiftLink.OffDefine);
            CollectionAssert.Contains(CutoverPublisher.Defines, SkyLiftLink.OffDefine);
            CollectionAssert.DoesNotContain(ServerBuildBuilder.EntityDefines, SkyLiftLink.OffDefine);
            Assert.IsTrue(SkyLiftLink.Enabled, "the Editor and development builds keep the Sky Lift");
        }

        [Test]
        public void TheCutoverClientCarriesNoServerOnlyDefine()
        {
            Assert.IsEmpty(CutoverPublisher.Defines.Intersect(DotsChannelPublisher.ServerOnlyDefines).ToArray(), "server code never takes on client code");
            CollectionAssert.Contains(CutoverPublisher.Defines, "KN_ENTITIES_CLIENT");
        }

        // ------------------------------------------------------------------ the release settings

        static readonly int[] RealLanePorts = { 7777, 7778, 7801, 8090 };   // KnServerSettings.RealLanePorts

        [Test]
        public void ReleaseSettings_AreTheRealLane()
        {
            string json = EntityReleaseServer.SettingsJson(null);
            var options = new ServerLaunchOptions();
            JsonUtility.FromJsonOverwrite(json, options);
            Assert.AreEqual(7777, options.port);
            Assert.AreEqual(8090, options.pagePort);
            Assert.AreEqual("http://127.0.0.1:7801", options.saveService);
            Assert.AreEqual("direct", options.connectionMode);
            StringAssert.DoesNotContain("plainTransport", json, "DTLS stays on");
            StringAssert.Contains("\"netcode\": \"entities\"", json, "the Server app runs it the entity way");
            CollectionAssert.Contains(RealLanePorts, options.port, "a test-lane build would refuse to start on these settings: they are the real server's");
            CollectionAssert.Contains(RealLanePorts, options.pagePort);
        }

        [Test]
        public void TestLaneSettings_StayOffTheRealPorts()
        {
            string json = ServerBuildBuilder.EntityTestSettingsJson();
            var options = new ServerLaunchOptions();
            JsonUtility.FromJsonOverwrite(json, options);
            CollectionAssert.DoesNotContain(RealLanePorts, options.port);
            CollectionAssert.DoesNotContain(RealLanePorts, options.pagePort);
            Assert.AreEqual("http://127.0.0.1:7802", options.saveService);
            StringAssert.Contains("\"plainTransport\": true", json);
            Assert.IsEmpty(options.serverList, "the test lane never lists itself");
        }

        [Test]
        public void ReleaseSettings_ListNovaAsAnEntityServerOnItsPagePort()
        {
            var options = new ServerLaunchOptions();
            JsonUtility.FromJsonOverwrite(EntityReleaseServer.SettingsJson(NgoSettings), options);
            Assert.AreEqual(1, options.serverList.Length);
            ServerListing nova = options.serverList[0];
            Assert.AreEqual("Nova", nova.name);
            Assert.IsTrue(nova.entities);
            Assert.IsTrue(nova.IsAuto, "the server fills in its public address when it publishes");
            Assert.AreEqual(7777, nova.port);
            Assert.AreEqual(7778, nova.statusPort, "the status question is answered on 7778 (already forwarded by the router), never on the page's port");
            Assert.AreEqual(7778, options.statusPort, "an auto entry takes the server's own status port");
            Assert.IsEmpty(options.mapServers, "Transit Row is left out");
        }

        [Test]
        public void ReleaseSettings_CarryTheGmsAndTheNameButNeitherThePagePasswordNorTheNgoPorts()
        {
            var options = new ServerLaunchOptions();
            JsonUtility.FromJsonOverwrite(EntityReleaseServer.SettingsJson(NgoSettings), options);
            Assert.AreEqual(1, options.gameMasters.Length);
            Assert.AreEqual("Gm One", options.gameMasters[0].name);
            Assert.AreEqual("acc1", options.gameMasters[0].account);
            Assert.AreEqual(50, options.maxPlayers);
            Assert.AreEqual(string.Empty, options.pagePassword, "the page password is not copied");
        }

        [Test]
        public void ReleaseSettings_SurviveAnUnreadableNgoFile()
        {
            var options = new ServerLaunchOptions();
            JsonUtility.FromJsonOverwrite(EntityReleaseServer.SettingsJson("not json at all"), options);
            Assert.AreEqual(7777, options.port);
            Assert.AreEqual("Nova", options.serverList[0].name);
        }

        // ------------------------------------------------------------------ the list publish

        [Test]
        public void TheReleaseLanePublishesTheList_TheTestLaneNever()
        {
            Assert.IsNull(ControlPageRules.PublishRefusal(false, 1));
            Assert.IsNotNull(ControlPageRules.PublishRefusal(true, 1));
            Assert.IsNotNull(ControlPageRules.PublishRefusal(false, 0), "an empty list would wipe the live one");
        }

        // ------------------------------------------------------------------ the client's list and join

        [Test]
        public void TheEntityGameListsEntityServersOnly()
        {
            var entity = new ServerListing { name = "Nova", address = "1.2.3.4", port = 7777, entities = true };
            var ngo = new ServerListing { name = "Old", address = "1.2.3.4", port = 7777 };
            Assert.IsTrue(EntityListRules.Shows(entity, true));
            Assert.IsFalse(EntityListRules.Shows(ngo, true), "an NGO server could not be joined by the entity game");
            Assert.IsTrue(EntityListRules.Shows(ngo, false), "the NGO game lists what it did");
            Assert.IsFalse(EntityListRules.Shows(null, true));
        }

        [Test]
        public void TheTestServerOfThisPcIsOnlyInDevelopmentBuilds()
        {
            Assert.IsTrue(EntityListRules.ShowsTestEntry(true, true));
            Assert.IsFalse(EntityListRules.ShowsTestEntry(true, false), "a release build shows the published list only");
            Assert.IsFalse(EntityListRules.ShowsTestEntry(false, true));
        }

        [Test]
        public void EveryEntityServerIsJoinedWithDtls_ExceptTheTestLanesOwn()
        {
            var test = new ServerListing { name = "t", address = "127.0.0.1", port = EntityServerLink.TestPort, entities = true };
            Assert.IsFalse(EntityListRules.Encrypted(test));
            var remote = new ServerListing { name = "Nova", address = "203.0.113.9", port = 7777, entities = true };
            Assert.IsTrue(EntityListRules.Encrypted(remote));
            // the owner plays on the server's own PC: his listing is the server's own (machine set), joined on loopback, and still DTLS
            var own = new ServerListing { name = "Nova", address = "203.0.113.9", port = 7777, statusPort = 8090, entities = true, machine = ThisMachine.Id };
            Assert.AreEqual("127.0.0.1", EntityListRules.JoinAddress(own));
            Assert.IsTrue(EntityListRules.Encrypted(own));
            var loopbackReal = new ServerListing { name = "Nova", address = "127.0.0.1", port = 7777, entities = true };
            Assert.IsTrue(EntityListRules.Encrypted(loopbackReal), "loopback to the real port is the real server");
        }

        // ------------------------------------------------------------------ the publisher

        [Test]
        public void TheCutoverPublishesToTestOnly()
        {
            Assert.AreEqual("test", CutoverPublisher.Channel);
            Assert.IsNull(CutoverPublisher.ChannelProblem("test"));
            Assert.IsNotNull(CutoverPublisher.ChannelProblem("live"));
            Assert.IsNotNull(CutoverPublisher.ChannelProblem("dots"));
        }

        [Test]
        public void TheCutoverBuildNumberContinuesTheRealStore()
        {
            Assert.AreEqual(15, CutoverPublisher.NextBuild(14, 0));
            Assert.AreEqual(15, CutoverPublisher.NextBuild(14, 3));
            Assert.AreEqual(21, CutoverPublisher.NextBuild(14, 20), "never below what is staged here");
            Assert.AreEqual(1, CutoverPublisher.NextBuild(0, 0));
            Assert.AreEqual(14, CutoverPublisher.BuildOf("{\"format\":1,\"build\":14,\"version\":\"v0.1.0 build 14\"}"));
            Assert.AreEqual(0, CutoverPublisher.BuildOf(null));
            Assert.AreEqual(0, CutoverPublisher.BuildOf("{}"));
        }

        // ------------------------------------------------------------------ folders and secrets

        [Test]
        public void TheNgoServerFolderIsNeverBuiltInto()
        {
            string ngo = Path.Combine(EntityReleaseServer.NgoWindowsFolder, "KryticalNovaServer");
            Assert.IsNotNull(ServerBuildBuilder.MainBuildsGuard(ngo));
            Assert.IsNotNull(ServerBuildBuilder.MainBuildsGuard(EntityReleaseServer.NgoWindowsFolder));
            Assert.IsNull(ServerBuildBuilder.MainBuildsGuard(Path.Combine(Path.GetTempPath(), "KnReleaseGuard", "KryticalNovaServer")));
        }

        [Test]
        public void TheReleaseServerLivesInBuildsServerWindowsOfThisProject()
        {
            string output = EntityReleaseServer.OutputFolder.Replace('\\', '/');
            StringAssert.EndsWith("/Builds/Server/Windows/KryticalNovaServer", output);
            StringAssert.EndsWith("/Builds/Server/Windows/certificates", EntityReleaseServer.CertificatesFolder.Replace('\\', '/'));
        }

        [Test]
        public void TheCertificateIsCopiedAsItIsAndNeverMadeUp()
        {
            string root = Path.Combine(Path.GetTempPath(), "KnCutoverCert-" + Guid.NewGuid().ToString("N"));
            try
            {
                string from = Path.Combine(root, "ngo"), to = Path.Combine(root, "release");
                Directory.CreateDirectory(from);
                Assert.IsFalse(EntityReleaseServer.CopyCertificates(from, to, out string noCrt));
                StringAssert.Contains("server.crt", noCrt);
                File.WriteAllText(Path.Combine(from, "server.crt"), "CRT");
                Assert.IsFalse(EntityReleaseServer.CopyCertificates(from, to, out string noKey));
                StringAssert.Contains("server.key", noKey);
                File.WriteAllText(Path.Combine(from, "server.key"), "KEY");
                Assert.IsTrue(EntityReleaseServer.CopyCertificates(from, to, out string none));
                Assert.IsNull(none);
                Assert.AreEqual("CRT", File.ReadAllText(Path.Combine(to, "server.crt")));
                Assert.AreEqual("KEY", File.ReadAllText(Path.Combine(to, "server.key")));
                Assert.IsFalse(EntityReleaseServer.CopyCertificates(Path.Combine(root, "missing"), to, out string absent));
                StringAssert.Contains("no certificates folder", absent);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
