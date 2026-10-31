using System.Collections.Generic;
using NUnit.Framework;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>Plain-C# parts of the Entities networking foundation: chunking, settings, defaults. No world needed.</summary>
    public class KnPureTests
    {
        [Test]
        public void Chunks_SplitAndJoin_RoundTrip()
        {
            string payload = KnTestQueries.Pattern(1100);
            var chunks = new List<string>();
            Assert.IsTrue(KnApprovalChunks.TrySplit(payload, 8, chunks));
            Assert.AreEqual(3, chunks.Count);
            Assert.IsTrue(chunks.TrueForAll(c => c.Length <= KnApprovalChunks.MaxChunkBytes));
            Assert.AreEqual(payload, KnApprovalChunks.Join(chunks.ToArray()));
        }

        [Test]
        public void Chunks_RefuseEmpty_NonAscii_AndTooLong()
        {
            var chunks = new List<string>();
            Assert.IsFalse(KnApprovalChunks.TrySplit("", 8, chunks));
            Assert.IsFalse(KnApprovalChunks.TrySplit("token-é", 8, chunks));
            Assert.IsFalse(KnApprovalChunks.TrySplit(KnTestQueries.Pattern(KnApprovalChunks.MaxChunkBytes * 3 + 1), 3, chunks));
            Assert.IsTrue(KnApprovalChunks.TrySplit(KnTestQueries.Pattern(KnApprovalChunks.MaxChunkBytes * 3), 3, chunks));
        }

        [Test]
        public void DefaultLimits_LetAUnityTokenThrough_AndStayUnderTheHandshakeTimeout()
        {
            var limits = KnApprovalLimits.Default;
            Assert.GreaterOrEqual(limits.MaxChunks * KnApprovalChunks.MaxChunkBytes, 1500); // a token is 1.2 to 1.4 KB
            Assert.LessOrEqual(limits.MaxChunks * KnApprovalChunks.MaxChunkBytes, limits.MaxTotalBytes);
            Assert.Less(limits.MaxSeconds * 1000f, KnNetDefaults.HandshakeApprovalTimeoutMs);
        }

        [Test]
        public void NetDefaults_AreThirtyThirty_WithARaisedHandshakeTimeout()
        {
            var rate = KnNetDefaults.TickRate();
            Assert.AreEqual(30, rate.SimulationTickRate);
            Assert.AreEqual(30, rate.NetworkTickRate);
            Assert.Greater(rate.HandshakeApprovalTimeoutMS, 5000u);
            Assert.AreEqual(1, rate.MaxSimulationStepBatchSize, "every tick runs on its own: the walk and the jump are per-tick plans");
            Assert.AreEqual(KnNetDefaults.MaxTicksPerFrame, rate.MaxSimulationStepsPerFrame);
        }

        [Test]
        public void ServerSettings_FileThenCommandLine_CommandLineWins()
        {
            var s = KnServerSettings.Parse("{\"port\": 7800, \"pagePort\": 8100, \"maxPlayers\": 50, \"map\": \"Zone02\"}",
                new[] { "KryticalNovaServer.exe", "-batchmode", "-port", "7797", "-role", "map" });
            Assert.AreEqual(7797, s.port);
            Assert.AreEqual(8100, s.PagePort);
            Assert.AreEqual(50, s.maxPlayers);
            Assert.AreEqual("Zone02", s.map);
            Assert.AreEqual("map", s.role);
        }

        [Test]
        public void ServerSettings_NoFile_UsesDefaults_AndBadValuesAreClamped()
        {
            var d = KnServerSettings.Parse(null, new string[0]);
            Assert.AreEqual(KnServerSettings.DefaultPort, d.port);
            Assert.AreEqual("main", d.role);
            var bad = KnServerSettings.Parse("{\"port\": 99999, \"maxPlayers\": 0}", new[] { "-pageport", "notanumber" });
            Assert.AreEqual(KnServerSettings.DefaultPort, bad.port);
            Assert.AreEqual(1, bad.maxPlayers);
            Assert.AreEqual(8090, bad.PagePort);
        }
    }
}
