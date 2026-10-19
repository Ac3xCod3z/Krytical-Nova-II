using System;
using System.IO;
using KryticalNova.Core;
using KryticalNova.EditorTools;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The copy tool (online phase 4, milestone 2) with the TEST save database only (SaveService --test, port 7802):
    /// a save folder in the old one-piece layout goes in, comes back out in parts, nothing is lost and the source is not
    /// changed; an account already in the database is left alone. Skipped where the test service is not running.
    /// </summary>
    public sealed class SaveCopyToolTests
    {
        private const string TestUrl = "http://127.0.0.1:7802";
        private static readonly string KeyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets", "save-service-key.txt");

        private string folder, outFolder, account, hero;
        private ServiceSaveStore service;

        [SetUp]
        public void Prepare()
        {
            if (!File.Exists(KeyFile)) Assert.Ignore("No save service key on this PC.");
            service = new ServiceSaveStore(TestUrl, File.ReadAllText(KeyFile).Trim(), "copy-test");
            if (!service.Reachable(out string problem)) { service.Dispose(); service = null; Assert.Ignore("The test save service is not running: " + problem); }
            account = Guid.NewGuid().ToString("N");
            hero = Guid.NewGuid().ToString("N");
            folder = Path.Combine(Path.GetTempPath(), "kn-copy-in-" + account.Substring(0, 8));
            outFolder = Path.Combine(Path.GetTempPath(), "kn-copy-out-" + account.Substring(0, 8));
            var old = new AccountSave { version = 1, accountId = account };
            old.Set("deepvault", "{\"cells\":[9]}");
            var character = new CharacterSave { characterId = hero, name = "Copytest", level = 40 };
            character.Set("inventory", "{\"pack\":[1,2,3]}");
            character.Set("vendorWeek", "{\"week\":2}");
            old.characters.Add(character);
            new FileSaveStore(folder, background: false).Write("accounts/" + account, JsonUtility.ToJson(old));
        }

        [TearDown]
        public void Clean()
        {
            if (service != null)
            {
                service.Delete("accounts/" + account);
                service.Delete("accounts/" + account + "/parts/deepvault");
                service.Delete("accounts/" + account + "/" + hero + "/inventory");
                service.Delete("accounts/" + account + "/" + hero + "/vendorweek");
                service.Flush();
                service.Dispose();
            }
            if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
            if (outFolder != null && Directory.Exists(outFolder)) Directory.Delete(outFolder, true);
        }

        [Test]
        public void SaveFilesGoInAndComeBackOutInParts()
        {
            string before = File.ReadAllText(Path.Combine(folder, "accounts", account + ".json"));
            string report = SaveCopyTool.CopyIn(folder, TestUrl, "TEST");
            StringAssert.Contains("1 account(s) with 1 character(s)", report);
            Assert.AreEqual(before, File.ReadAllText(Path.Combine(folder, "accounts", account + ".json")), "the source is only read");
            Assert.IsFalse(File.Exists(Path.Combine(folder, "accounts", account + "-v1.json")), "and nothing is added to it");

            Assert.IsTrue(service.TryRead("accounts/" + account + "/" + hero + "/inventory", out string pack));
            Assert.AreEqual("{\"pack\":[1,2,3]}", pack);

            StringAssert.Contains("Left alone", SaveCopyTool.CopyIn(folder, TestUrl, "TEST"), "a second copy overwrites nothing");

            SaveCopyTool.CopyOut(TestUrl, "TEST", outFolder);
            AccountSave back = ServerSave.ReadAccountFrom(new FileSaveStore(outFolder, background: false), account);
            Assert.AreEqual(2, AccountSave.CurrentVersion);
            Assert.AreEqual("{\"cells\":[9]}", back.Get("deepvault"));
            Assert.AreEqual("Copytest", back.Find(hero).name);
            Assert.AreEqual(40, back.Find(hero).level);
            Assert.AreEqual("{\"week\":2}", back.Find(hero).Get("vendorWeek"));
        }
    }
}
