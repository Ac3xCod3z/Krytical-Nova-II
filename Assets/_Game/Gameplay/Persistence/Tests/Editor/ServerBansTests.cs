using System;
using System.IO;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Bans and the server's records (online phase 2, milestone 4): a ban is on the account, for a time or for good, kept
    /// in the world/bans document, read back after a restart, ended early by account or character; a banned player reads
    /// why and for how long; the records are one line each in their own file.
    /// </summary>
    public sealed class ServerBansTests
    {
        private const string Bob = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string Cy = "cccccccccccccccccccccccccccccccc";
        private static readonly DateTime Now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        private ISaveStore previousStore;
        private MemorySaveStore memory;

        [SetUp]
        public void UseMemory()
        {
            previousStore = ServerSave.Store;
            memory = new MemorySaveStore();
            ServerSave.Store = memory;
        }

        [TearDown]
        public void Restore() => ServerSave.Store = previousStore;

        [TestCase("", null)]
        [TestCase("forever", null)]
        [TestCase("Permanent", null)]
        [TestCase("0", null)]
        [TestCase("30m", 30d)]
        [TestCase("12h", 720d)]
        [TestCase("3d", 4320d)]
        [TestCase("2", 2880d)]
        [TestCase("2w", 20160d)]
        [TestCase("1.5h", 90d)]
        public void LengthsRead(string text, double? minutes)
        {
            Assert.IsTrue(ServerBans.TryParseLength(text, out TimeSpan? length, out string error), error);
            Assert.AreEqual(minutes, length?.TotalMinutes);
        }

        [TestCase("soon")]
        [TestCase("-3d")]
        [TestCase("3y")]
        [TestCase("d")]
        public void NonsenseIsNoLength(string text)
        {
            Assert.IsFalse(ServerBans.TryParseLength(text, out _, out string error));
            StringAssert.Contains("not a ban length", error);
        }

        [Test]
        public void ALengthHasACeiling()
        {
            Assert.IsTrue(ServerBans.TryParseLength("99999w", out TimeSpan? length, out _));
            Assert.AreEqual(3650d, length.Value.TotalDays);
        }

        [Test]
        public void ABanKeepsTheAccountOutUntilItRunsOut()
        {
            ServerBans.Ban(Bob, "Bob", "GM Ace", "spamming", TimeSpan.FromDays(3), Now);
            Assert.IsTrue(ServerBans.IsBanned(Bob, Now.AddDays(2), out BanEntry ban));
            Assert.AreEqual("spamming", ban.reason);
            Assert.AreEqual("GM Ace", ban.by);
            Assert.IsFalse(ServerBans.IsBanned(Cy, Now, out _), "another account");
            Assert.IsFalse(ServerBans.IsBanned(Bob, Now.AddDays(3), out _), "ran out");
            Assert.AreEqual(0, ServerBans.All(Now.AddDays(4)).Count, "a ban that ran out is dropped");
        }

        [Test]
        public void ABanForGoodNeverRunsOut()
        {
            ServerBans.Ban(Bob, "Bob", "Host", "", null, Now);
            Assert.IsTrue(ServerBans.IsBanned(Bob, Now.AddYears(50), out BanEntry ban));
            Assert.IsTrue(ban.IsForever);
        }

        [Test]
        public void BansOutliveARestart()
        {
            ServerBans.Ban(Bob, "Bob", "Host", "cheating", TimeSpan.FromDays(1), Now);
            Assert.IsTrue(memory.TryRead(ServerSave.WorldPrefix + ServerBans.Document, out string json));
            StringAssert.Contains(Bob, json);

            var restarted = new MemorySaveStore();
            restarted.Write(ServerSave.WorldPrefix + ServerBans.Document, json);
            ServerSave.Store = restarted; // forgets the loaded documents, as a new server would
            Assert.IsTrue(ServerBans.IsBanned(Bob, Now, out BanEntry ban));
            Assert.AreEqual("cheating", ban.reason);
        }

        [Test]
        public void BanningAgainReplacesTheBan()
        {
            ServerBans.Ban(Bob, "Bob", "Host", "first", TimeSpan.FromHours(1), Now);
            ServerBans.Ban(Bob, "Bob", "Host", "second", null, Now);
            Assert.AreEqual(1, ServerBans.All(Now).Count);
            Assert.IsTrue(ServerBans.IsBanned(Bob, Now.AddDays(10), out BanEntry ban));
            Assert.AreEqual("second", ban.reason);
        }

        [Test]
        public void UnbanByAccountOrCharacter()
        {
            ServerBans.Ban(Bob, "Bob", "Host", "", null, Now);
            ServerBans.Ban(Cy, "Cy", "Host", "", null, Now);
            Assert.IsFalse(ServerBans.Unban("Nobody", Now, out _));
            Assert.IsTrue(ServerBans.Unban("bob", Now, out BanEntry ended));
            Assert.AreEqual(Bob, ended.account);
            Assert.IsTrue(ServerBans.Unban(Cy.ToUpperInvariant(), Now, out _));
            Assert.AreEqual(0, ServerBans.All(Now).Count);
        }

        [Test]
        public void TheBannedPlayerReadsWhyAndForHowLong()
        {
            BanEntry timed = ServerBans.Ban(Bob, "Bob", "Host", "spamming", TimeSpan.FromHours(50), Now);
            string text = ServerBans.Refusal(timed, Now);
            StringAssert.StartsWith("This account is banned from this server for 2 days 2 hours more (until ", text);
            StringAssert.EndsWith("server time): spamming", text);

            BanEntry forGood = ServerBans.Ban(Cy, "Cy", "Host", "", null, Now);
            Assert.AreEqual("This account is banned from this server.", ServerBans.Refusal(forGood, Now));
        }

        [Test]
        public void LengthsReadForPeople()
        {
            Assert.AreEqual("for good", ServerBans.Describe(null));
            Assert.AreEqual("30 minutes", ServerBans.Describe(TimeSpan.FromMinutes(30)));
            Assert.AreEqual("1 hour", ServerBans.Describe(TimeSpan.FromHours(1)));
            Assert.AreEqual("12 hours", ServerBans.Describe(TimeSpan.FromHours(12)));
            Assert.AreEqual("3 days", ServerBans.Describe(TimeSpan.FromDays(3)));
            Assert.AreEqual("1 day 1 hour", ServerBans.Describe(TimeSpan.FromHours(25)));
        }

        [Test]
        public void TheListingNamesEveryBan()
        {
            Assert.AreEqual("Nobody is banned.", ServerBans.Listing(Now));
            ServerBans.Ban(Bob, "Bob", "GM Ace", "spamming", TimeSpan.FromDays(3), Now);
            string listing = ServerBans.Listing(Now);
            StringAssert.Contains("1 banned:", listing);
            StringAssert.Contains("Bob  account " + Bob + "  3 days left  by GM Ace: spamming", listing);
        }

        [Test]
        public void ASavedCharacterIsFoundByName()
        {
            WriteAccount(Bob, "Bob");
            WriteAccount(Cy, "Cy", "Second");
            CollectionAssert.AreEqual(new[] { Bob }, ServerSave.AccountsWithCharacter("BOB"));
            CollectionAssert.AreEqual(new[] { Cy }, ServerSave.AccountsWithCharacter("second"));
            Assert.AreEqual(0, ServerSave.AccountsWithCharacter("Nobody").Count);
        }

        [Test]
        public void TheRecordsAreOneLineEachInTheirOwnFile()
        {
            string folder = Path.Combine(Path.GetTempPath(), "kn-records-" + Guid.NewGuid().ToString("N"));
            try
            {
                var time = new DateTime(2026, 10, 18, 9, 5, 0);
                ServerRecords.WriteFile(folder, RecordKind.Gm, "Server window: gm.ban Bob 3d \"spam\nmore\"", time);
                ServerRecords.WriteFile(folder, RecordKind.Economy, "Trade: one", time);
                ServerRecords.WriteFile(folder, RecordKind.Economy, "Trade: two", time);
                ServerRecords.WriteFile(folder, RecordKind.Access, "Joined: Bob", time);
                string[] gm = File.ReadAllLines(Path.Combine(folder, "gm.log"));
                Assert.AreEqual(1, gm.Length, "a line break inside a reason stays on the line");
                Assert.AreEqual("2026-10-18 09:05:00  Server window: gm.ban Bob 3d \"spam more\"", gm[0]);
                Assert.AreEqual(2, File.ReadAllLines(Path.Combine(folder, "economy.log")).Length);
                Assert.AreEqual(1, File.ReadAllLines(Path.Combine(folder, "access.log")).Length);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void ALongAnswerIsCutForTheRecord()
        {
            Assert.AreEqual("Done.", ServerRecords.Short(null));
            Assert.AreEqual("First line …", ServerRecords.Short("First line\nsecond line"));
            Assert.AreEqual(201, ServerRecords.Short(new string('x', 500)).Length);
        }

        [Test]
        public void NothingIsRecordedWithoutASession()
        {
            // Playing alone (no session) keeps no records and writes nothing.
            Assert.IsFalse(ServerRecords.Keeping);
            ServerRecords.Write(RecordKind.Gm, "gm.heal");
        }

        private void WriteAccount(string id, params string[] characters)
        {
            var account = new AccountSave { accountId = id };
            foreach (string name in characters) account.characters.Add(new CharacterSave { characterId = Guid.NewGuid().ToString("N"), name = name });
            memory.Write(ServerSave.AccountsPrefix + id, JsonUtility.ToJson(account));
        }
    }
}
