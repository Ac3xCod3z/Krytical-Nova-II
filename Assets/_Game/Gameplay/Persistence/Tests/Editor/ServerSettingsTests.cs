using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Networking;
using KryticalNova.Persistence;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The dedicated server's settings file and its backups (online phase 1, chunk 2).</summary>
    public sealed class ServerSettingsTests
    {
        private string root;
        private string serverFolder;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "kn_server_tests_" + Guid.NewGuid().ToString("N"));
            serverFolder = Path.Combine(root, "KryticalNovaServer");
            Directory.CreateDirectory(serverFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void TheFirstStartWritesTheDefaultsAndKeepsSavesOutsideTheServerFolder()
        {
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new string[0], out _);
            Assert.IsTrue(File.Exists(Path.Combine(serverFolder, ServerLaunchOptions.FileName)), "the settings file is written for the owner to edit");
            Assert.AreEqual(7777, options.port);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(root, ServerLaunchOptions.DefaultSaveFolderName)), options.SaveFolderPath,
                "a new build replaces the server folder, so the saves sit beside it");
            Assert.AreEqual(Path.GetFullPath(Path.Combine(root, ServerLaunchOptions.DefaultSaveFolderName + "-Backups")), options.BackupFolderPath);
        }

        [Test]
        public void TheFileIsReadAndTheCommandLineWins()
        {
            File.WriteAllText(Path.Combine(serverFolder, ServerLaunchOptions.FileName),
                "{ \"serverName\": \"Brothers\", \"port\": 7790, \"maxPlayers\": 12, \"saveFolder\": \"Saves\" }");
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new[] { "Server.exe", "-port", "7795", "-name", "Night shift" }, out string report);
            Assert.AreEqual(7795, options.port, "the command line wins");
            Assert.AreEqual("Night shift", options.serverName);
            Assert.AreEqual(12, options.maxPlayers, "what the command line leaves alone comes from the file");
            Assert.AreEqual(Path.GetFullPath(Path.Combine(serverFolder, "Saves")), options.SaveFolderPath, "a relative folder is from the server's folder");
            Assert.AreEqual(10, options.backupsKept, "what neither says keeps its default");
            StringAssert.Contains("Night shift", report);
        }

        [Test]
        public void ABrokenFileIsLeftAloneAndTheDefaultsAreUsed()
        {
            string file = Path.Combine(serverFolder, ServerLaunchOptions.FileName);
            File.WriteAllText(file, "{ this is not json");
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new string[0], out string report);
            Assert.AreEqual(7777, options.port);
            Assert.AreEqual("{ this is not json", File.ReadAllText(file), "the owner's file is never overwritten");
            StringAssert.Contains("could not read", report);
        }

        [Test]
        public void ValuesOutOfRangeAreMadeSafe()
        {
            File.WriteAllText(Path.Combine(serverFolder, ServerLaunchOptions.FileName),
                "{ \"port\": 99999, \"maxPlayers\": 0, \"backupHours\": -3, \"backupsKept\": 0, \"connectionMode\": \" Smoke-Signals \" }");
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new[] { "Server.exe", "-maxPlayers", "lots" }, out _);
            Assert.AreEqual(7777, options.port);
            Assert.AreEqual(1, options.maxPlayers);
            Assert.AreEqual(0f, options.backupHours);
            Assert.AreEqual(1, options.backupsKept);
            Assert.AreEqual("direct", options.connectionMode, "only direct and relay exist (online step 5)");
        }

        [Test]
        public void APublicAddressIsKeptAndALocalOneIsNeverHandedOut()
        {
            File.WriteAllText(Path.Combine(serverFolder, ServerLaunchOptions.FileName), "{ \"publicAddress\": \" 203.0.113.10 \" }");
            Assert.AreEqual("203.0.113.10", ServerLaunchOptions.Load(serverFolder, new string[0], out _).publicAddress, "a rented machine's address, trimmed");
            Assert.AreEqual("198.51.100.7", ServerLaunchOptions.Load(serverFolder, new[] { "Server.exe", "-publicAddress", "198.51.100.7" }, out _).publicAddress,
                "the command line wins");
            foreach (string local in new[] { "127.0.0.1", "10.0.0.5", "172.20.1.1", "192.168.1.20", "169.254.3.3", "100.100.1.1", "0.0.0.0", "239.1.1.1", "::1", "nova.example", "" })
            {
                ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new[] { "Server.exe", "-publicAddress", local }, out string report);
                Assert.AreEqual(string.Empty, options.publicAddress, $"{local} is never handed to players (Linux plan item 2)");
                if (local.Length > 0) StringAssert.Contains("not a public IPv4", report);
            }
        }

        [Test]
        public void BackupsAreNamedByTimeAndOnlyTheNewestAreKept()
        {
            Assert.AreEqual("save_2026-10-16_14-05-09_start.zip", ServerBackups.FileNameFor(new DateTime(2026, 10, 16, 14, 5, 9), "start"));
            Assert.AreEqual("save_2026-10-16_14-05-09_beforeupdate.zip", ServerBackups.FileNameFor(new DateTime(2026, 10, 16, 14, 5, 9), "before update!"));
            var names = new List<string>
            {
                "save_2026-10-16_10-00-00_timed.zip", "save_2026-10-15_10-00-00_start.zip", "save_2026-10-16_16-00-00_stop.zip",
            };
            CollectionAssert.AreEqual(new[] { "save_2026-10-15_10-00-00_start.zip" }, ServerBackups.ToDelete(names, 2));
            Assert.IsEmpty(ServerBackups.ToDelete(names, 5));
        }

        [Test]
        public void ABackupZipsTheSaveFolderAndPrunes()
        {
            string save = Path.Combine(root, "Save");
            string backups = Path.Combine(root, "Save-Backups");
            Directory.CreateDirectory(Path.Combine(save, "accounts"));
            File.WriteAllText(Path.Combine(save, "accounts", "a.json"), "{}");
            Directory.CreateDirectory(backups);
            File.WriteAllText(Path.Combine(backups, "save_2000-01-01_00-00-00_old.zip"), "old");

            string line = ServerBackups.MakeNow(save, backups, 1, "test");
            StringAssert.StartsWith("[Backup] save_", line);
            string[] left = Directory.GetFiles(backups);
            Assert.AreEqual(1, left.Length, "only the newest is kept");
            StringAssert.EndsWith("_test.zip", left[0]);
        }

        // ------------------------------------------------------------------ map servers (online phase 5)

        [Test]
        public void TheListedServerKnowsTransitRowsServerAndAnOlderFileGainsIt()
        {
            string file = Path.Combine(serverFolder, ServerLaunchOptions.FileName);
            File.WriteAllText(file, "{ \"serverName\": \"Nova\", \"callLimits\": {}, \"ledgerDays\": 90 }");
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new string[0], out _);
            Assert.IsFalse(options.IsMapServer, "without -role the server is the listed one");
            Assert.AreEqual(1, options.mapServers.Length);
            Assert.AreEqual("MeridianSprawl_Z1_TransitRow", options.mapServers[0].map);
            Assert.AreEqual(7779, options.mapServers[0].port, "the owner forwards 7779 and 7780 for it");
            Assert.AreEqual(8092, options.mapServers[0].pagePort);
            StringAssert.Contains("\"mapServers\"", File.ReadAllText(file), "the owner sees the map servers in the file");
            StringAssert.Contains("\"Nova\"", File.ReadAllText(file), "and keeps what was there");
        }

        [Test]
        public void AMapServerIsNamedOnTheCommandLineAlwaysDirectAndKeepsItsOwnRecords()
        {
            File.WriteAllText(Path.Combine(serverFolder, ServerLaunchOptions.FileName),
                "{ \"connectionMode\": \"relay\", \"saveService\": \"http://127.0.0.1:7802\" }");
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder,
                new[] { "Server.exe", "-role", "map", "-map", "MeridianSprawl_Z1_TransitRow", "-port", "7792", "-pageport", "8093" }, out string report);
            Assert.IsTrue(options.IsMapServer);
            Assert.AreEqual(ServerLaunchOptions.Direct, options.connectionMode, "a map server is reached directly, never through the list's Relay code");
            Assert.AreEqual(7793, options.StatusPort);
            Assert.AreEqual(Path.Combine(options.SaveFolderPath, "MapServers", "MeridianSprawl_Z1_TransitRow"), options.RecordFolderPath);
            Assert.IsNull(options.MapServerProblem());
            StringAssert.Contains("map server", report);
            StringAssert.Contains("\"relay\"", File.ReadAllText(Path.Combine(serverFolder, ServerLaunchOptions.FileName)),
                "the shared file keeps the listed server's way in");
        }

        [Test]
        public void AMapServerWithoutTheSaveServiceDoesNotRun()
        {
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new[] { "Server.exe", "-role", "map" }, out _);
            StringAssert.Contains("save service", options.MapServerProblem(), "two servers cannot share a save folder");
            ServerLaunchOptions listed = ServerLaunchOptions.Load(serverFolder, new string[0], out _);
            Assert.IsNull(listed.MapServerProblem(), "the listed server may still save to files");
            Assert.AreEqual(listed.SaveFolderPath, listed.RecordFolderPath);
        }
    }
}
