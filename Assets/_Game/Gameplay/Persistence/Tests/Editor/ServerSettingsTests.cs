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
                "{ \"port\": 99999, \"maxPlayers\": 0, \"backupHours\": -3, \"backupsKept\": 0, \"connectionMode\": \"relay\" }");
            ServerLaunchOptions options = ServerLaunchOptions.Load(serverFolder, new[] { "Server.exe", "-maxPlayers", "lots" }, out _);
            Assert.AreEqual(7777, options.port);
            Assert.AreEqual(1, options.maxPlayers);
            Assert.AreEqual(0f, options.backupHours);
            Assert.AreEqual(1, options.backupsKept);
            Assert.AreEqual("direct", options.connectionMode, "relay is not built yet");
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
    }
}
