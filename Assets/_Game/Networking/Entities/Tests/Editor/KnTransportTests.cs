using System;
using System.IO;
using NUnit.Framework;
using Unity.Entities;
using KryticalNova.Networking;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// M2b-2, the pure parts of the transport: which transport a server and a client choose, what happens when the certificate is
    /// missing, and that no secret reaches a log line. Real DTLS and Relay can only be proven in a built player (migration plan,
    /// "M2b-2 notes", manual test list).
    /// </summary>
    public class KnTransportTests
    {
        private const string KeyBody = "SUPERSECRETKEYBODY0123456789";
        private string m_Folder;

        [SetUp]
        public void SetUp()
        {
            m_Folder = Path.Combine(Path.GetTempPath(), "kn-transport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Folder);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(m_Folder, true); } catch (IOException) { /* temp */ }
        }

        private string WritePair(string certName = "server.crt", string keyName = "server.key")
        {
            File.WriteAllText(Path.Combine(m_Folder, certName), "-----BEGIN CERTIFICATE-----\nMIIFAKE\n-----END CERTIFICATE-----\n");
            File.WriteAllText(Path.Combine(m_Folder, keyName), "-----BEGIN PRIVATE KEY-----\n" + KeyBody + "\n-----END PRIVATE KEY-----\n");
            return m_Folder;
        }

        private static KnServerSettings Settings(string json = null, params string[] args) => KnServerSettings.Parse(json, args);

        // ------------------------------------------------------------------ settings

        [Test]
        public void Settings_ReadTheTransportKeys_FromFileAndArguments()
        {
            KnServerSettings s = Settings("{\"connectionMode\":\"RELAY\",\"plainTransport\":true}");
            Assert.AreEqual("relay", s.connectionMode);
            Assert.IsTrue(s.plainTransport);
            Assert.AreEqual("direct", Settings().connectionMode);
            Assert.AreEqual("direct", Settings("{\"connectionMode\":\"banana\"}").connectionMode, "anything but relay is direct");
            Assert.AreEqual("relay", Settings(null, "-connectionmode", "relay").connectionMode);
            Assert.IsTrue(Settings(null, "-plaintransport").plainTransport);
        }

        // ------------------------------------------------------------------ the server's plan

        [Test]
        public void Direct_WithTheFiles_IsSecure()
        {
            string server = Path.Combine(m_Folder, "server");
            Directory.CreateDirectory(server);
            Directory.CreateDirectory(Path.Combine(m_Folder, "certificates"));
            File.WriteAllText(Path.Combine(m_Folder, "certificates", "server.crt"), "c");
            File.WriteAllText(Path.Combine(m_Folder, "certificates", "server.key"), "k");
            KnServerTransportPlan plan = KnTransportPlanner.PlanServer(Settings(), true, false, server);
            Assert.AreEqual(KnTransportMode.Secure, plan.Mode, "the default folder is certificates/ beside the server folder");
            Assert.IsTrue(plan.CanStart);
            Assert.IsNull(plan.Warning);
        }

        [Test]
        public void NamedFiles_AreUsed_AndOnlyOneNamedIsNamed()
        {
            WritePair("a.crt", "a.key");
            var s = Settings("{\"certificateFile\":" + KnJson.Quote(Path.Combine(m_Folder, "a.crt")) + ",\"privateKeyFile\":" + KnJson.Quote(Path.Combine(m_Folder, "a.key")) + "}");
            Assert.IsTrue(s.WantsEncryption);
            Assert.AreEqual(KnTransportMode.Secure, KnTransportPlanner.PlanServer(s, true, false, m_Folder).Mode);

            s.privateKeyFile = "";
            KnServerTransportPlan half = KnTransportPlanner.PlanServer(s, true, false, m_Folder, _ => true);
            Assert.AreEqual(KnTransportMode.Plain, half.Mode, "one file named is a mistake, never silently half encryption");
            Assert.IsFalse(half.CanStart);
            StringAssert.Contains("both are needed", half.Problem);
        }

        [Test]
        public void MissingCertificate_ReleaseRefusesToStart_DevelopmentWarns_AndPlainTransportIsAnExplicitOptOut()
        {
            KnServerSettings s = Settings();
            KnServerTransportPlan release = KnTransportPlanner.PlanServer(s, true, false, m_Folder, _ => false);
            Assert.IsFalse(release.CanStart);
            StringAssert.Contains("will not start without encryption", release.Problem);

            KnServerTransportPlan dev = KnTransportPlanner.PlanServer(s, false, false, m_Folder, _ => false);
            Assert.IsTrue(dev.CanStart);
            Assert.AreEqual(KnTransportMode.Plain, dev.Mode);
            StringAssert.Contains("NOT encrypted", dev.Warning);

            s.plainTransport = true;
            KnServerTransportPlan optOut = KnTransportPlanner.PlanServer(s, true, false, m_Folder, _ => false);
            Assert.IsTrue(optOut.CanStart);
            StringAssert.Contains("NOT ENCRYPTED", optOut.Warning);

            s.plainTransport = true;
            KnServerTransportPlan optOutWithFiles = KnTransportPlanner.PlanServer(s, true, false, m_Folder, _ => true);
            Assert.AreEqual(KnTransportMode.Plain, optOutWithFiles.Mode, "plainTransport wins even when the files are there");
        }

        [Test]
        public void RelayMode_NeedsTheAllocation_AndNeverTouchesCertificates()
        {
            KnServerSettings s = Settings("{\"connectionMode\":\"relay\"}");
            KnServerTransportPlan waiting = KnTransportPlanner.PlanServer(s, true, false, m_Folder, _ => false);
            Assert.AreEqual(KnTransportMode.Relay, waiting.Mode);
            Assert.IsFalse(waiting.CanStart);
            StringAssert.Contains("Relay allocation", waiting.Problem);
            KnServerTransportPlan ready = KnTransportPlanner.PlanServer(s, true, true, m_Folder, _ => false);
            Assert.IsTrue(ready.CanStart);
            Assert.IsNull(ready.Warning);
        }

        [Test]
        public void ClientChoice_RelayThenEncryptedThenPlain()
        {
            Assert.AreEqual(KnTransportMode.Relay, KnTransportPlanner.ChooseClient(true, true));
            Assert.AreEqual(KnTransportMode.Secure, KnTransportPlanner.ChooseClient(false, true));
            Assert.AreEqual(KnTransportMode.Plain, KnTransportPlanner.ChooseClient(false, false));
        }

        // ------------------------------------------------------------------ applying the plan to a world

        [Test]
        public void ApplyServerPlan_LoadsTheSecrets_ButNeverPrintsThem()
        {
            WritePair();
            // aim the plan at the temp files regardless of the default folder
            var plan = new KnServerTransportPlan(KnTransportMode.Secure, true, null, null, Path.Combine(m_Folder, "server.crt"), Path.Combine(m_Folder, "server.key"));
            using var world = new World("kn-transport-test");
            Assert.IsTrue(KnTransport.ApplyServerPlan(world, plan, true, out string warning, out string problem), problem);
            KnWorldTransport t = KnTransport.Of(world);
            Assert.AreEqual(KnTransportMode.Secure, t.Mode);
            StringAssert.Contains(KeyBody, t.PrivateKey, "the driver gets the key");
            Assert.IsNull(warning);
            Assert.AreEqual("Secure", t.ToString(), "a log line of the transport holds no certificate or key");
            Assert.IsFalse(plan.CertificatePath.Contains(KeyBody));
        }

        [Test]
        public void ApplyServerPlan_UnreadableFiles_ReleaseRefuses_DevelopmentFallsBackToPlainWithAWarning_AndTheLineHasNoKey()
        {
            File.WriteAllText(Path.Combine(m_Folder, "server.crt"), "not a certificate");
            File.WriteAllText(Path.Combine(m_Folder, "server.key"), KeyBody);
            var plan = new KnServerTransportPlan(KnTransportMode.Secure, true, null, null, Path.Combine(m_Folder, "server.crt"), Path.Combine(m_Folder, "server.key"));
            using var world = new World("kn-transport-test-2");
            Assert.IsFalse(KnTransport.ApplyServerPlan(world, plan, true, out _, out string problem));
            StringAssert.Contains("will not start without encryption", problem);
            Assert.IsFalse(problem.Contains(KeyBody), "no secret in the message");

            Assert.IsTrue(KnTransport.ApplyServerPlan(world, plan, false, out string warning, out _));
            StringAssert.Contains("NOT encrypted", warning);
            Assert.IsFalse(warning.Contains(KeyBody));
            Assert.AreEqual(KnTransportMode.Plain, KnTransport.Of(world).Mode);
        }

        [Test]
        public void ANoPlanWorld_IsPlain_AndRelayNeedsItsData()
        {
            using var world = new World("kn-transport-test-3");
            Assert.AreEqual(KnTransportMode.Plain, KnTransport.Of(world).Mode);
            KnTransport.PendingServerRelay = null;
            var plan = new KnServerTransportPlan(KnTransportMode.Relay, true, null, null);
            Assert.IsFalse(KnTransport.ApplyServerPlan(world, plan, true, out _, out string problem));
            StringAssert.Contains("Relay", problem);
        }

        // ------------------------------------------------------------------ the certificate helpers in Core

        [Test]
        public void ServerCertificates_ResolveAndLoad()
        {
            WritePair();
            ServerCertificates.ResolvePaths("", "", Path.Combine(m_Folder, "build"), out string cert, out string key);
            Assert.AreEqual(Path.Combine(m_Folder, "certificates", "server.crt"), cert);
            Assert.AreEqual(Path.Combine(m_Folder, "certificates", "server.key"), key);
            Assert.IsTrue(ServerCertificates.TryLoad(Path.Combine(m_Folder, "server.crt"), Path.Combine(m_Folder, "server.key"), out string c, out string k, out string why), why);
            StringAssert.Contains("BEGIN CERTIFICATE", c);
            Assert.IsFalse(ServerCertificates.TryLoad(Path.Combine(m_Folder, "nope.crt"), Path.Combine(m_Folder, "server.key"), out _, out _, out why));
            Assert.IsFalse(why.Contains(KeyBody));
            StringAssert.Contains("-----BEGIN CERTIFICATE-----", ServerCertificates.AuthorityCertificate);
            Assert.AreEqual("krytical-nova-server", ServerCertificates.ServerName);
        }
    }
}
