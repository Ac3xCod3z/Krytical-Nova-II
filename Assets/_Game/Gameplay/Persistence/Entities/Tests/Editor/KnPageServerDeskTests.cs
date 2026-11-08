using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// 8 Nov 2026 port of the rest of NGO's control page (ServerControlPage:319-416) onto the entity netcode's page: the owner's only, each
    /// press on the game's thread and in the GM record; a player picked from the list is banned by their proven account and sent away
    /// through the kick, and the join check's list (ServerBans) holds it; unban ends it; the log is NGO's last lines by id.
    /// </summary>
    public class KnPageServerDeskTests
    {
        const string Key = "page-key";
        const string Account = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        ISaveStore m_Store;
        Func<string, string, int> m_Announce;

        sealed class Desk : KnPageDesk.IDesk
        {
            public readonly List<string> Calls = new List<string>();
            public string Save() { Calls.Add("save"); return "Everyone is saved."; }
            public string Announce(string text) { Calls.Add("announce " + text); return "Announced to 2 player(s)."; }
            public string Ban(string who, ulong playerKey, string length, string reason, string from) { Calls.Add($"ban {who}|{playerKey}|{length}|{reason}|{from}"); return "banned"; }
            public string Unban(string who) { Calls.Add("unban " + who); return "unbanned"; }
            public string BansJson() { Calls.Add("bans"); return "[]"; }
            public string FlagsJson(long after) { Calls.Add("flags " + after); return "{\"flags\":[]}"; }
            public void Record(string pressed, string answer) => Calls.Add("record " + pressed + " -> " + answer);
        }

        [SetUp]
        public void SetUp()
        {
            m_Store = ServerSave.Store;
            ServerSave.Store = new MemorySaveStore();
            m_Announce = ServerHooks.Announce;
            PlayerKeys.Reset();
            KnKickRequests.Reset();
            KnPageDesk.PageLog.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            KnPageDesk.Desk = null;
            ServerHooks.Announce = m_Announce;
            ServerSave.Store = m_Store;
            PlayerKeys.Reset();
            KnKickRequests.Reset();
            KnPageDesk.PageLog.Reset();
        }

        static KnPageRules.Reply Call(string method, string path, string body = null, string query = null, bool loopback = true, string key = Key) =>
            KnPageRules.Handle(method, path, loopback, key, Key, new KnStatusSnapshot(), new StopEndpointRules(), false, query, body);

        [Test]
        public void TheButtons_AreTheOwnersOnly_RunOnTheGamesThread_AndGoInTheGmRecord()
        {
            var desk = new Desk();
            Assert.AreEqual(503, Call("POST", KnPageDesk.SavePath, "{}").Status, "no desk yet");
            KnPageDesk.Desk = desk;
            Assert.AreEqual(403, Call("POST", KnPageDesk.SavePath, "{}", key: "wrong").Status);
            Assert.AreEqual(403, Call("POST", KnPageDesk.BanPath, "{\"player\":\"7\"}", loopback: false).Status);
            Assert.AreEqual(403, Call("GET", KnPageDesk.LogPath, key: null).Status, "the log too");
            Assert.AreEqual(405, Call("GET", KnPageDesk.SavePath).Status);
            CollectionAssert.IsEmpty(desk.Calls, "nothing ran");

            StringAssert.Contains("Type the announcement first.", Call("POST", KnPageDesk.AnnouncePath, "{\"text\":\"  \"}").Body);
            StringAssert.Contains("Pick the player from the player list", Call("POST", KnPageDesk.BanPath, "{}").Body);
            StringAssert.Contains("Name the ban", Call("POST", KnPageDesk.UnbanPath, "{}").Body);
            CollectionAssert.IsEmpty(desk.Calls, "refused before the game's thread");

            KnPageRules.Reply save = Call("POST", KnPageDesk.SavePath, "{}");
            Assert.IsNotNull(save.Work, "for the game's thread");
            Assert.AreEqual("Everyone is saved.", save.Work());
            Assert.AreEqual("Announced to 2 player(s).", Call("POST", KnPageDesk.AnnouncePath, "{\"text\":\" Restart at noon \"}").Work());
            Assert.AreEqual("banned", Call("POST", KnPageDesk.BanPath, "{\"player\":\"7\",\"length\":\"3d\",\"reason\":\"cheating\"}").Work());
            Assert.AreEqual("unbanned", Call("POST", KnPageDesk.UnbanPath, "{\"name\":\"Talwyn\"}").Work());
            KnPageRules.Reply bans = Call("GET", KnPageDesk.BansPath);
            Assert.IsTrue(bans.WorkIsJson);
            Assert.AreEqual("[]", bans.Work());
            Assert.AreEqual("{\"flags\":[]}", Call("GET", KnPageDesk.FlagsPath, query: "after=12").Work());
            CollectionAssert.AreEqual(new[]
            {
                "save", "record save -> Everyone is saved.",
                "announce Restart at noon", "record announce Restart at noon -> Announced to 2 player(s).",
                "ban |7|3d|cheating|Control page (this machine)", "record ban player 7 3d cheating -> banned",
                "unban Talwyn", "record unban Talwyn -> unbanned",
                "bans", "flags 12",
            }, desk.Calls, "each press runs once and is recorded with its answer; the lists are not presses");
        }

        [Test]
        public void TheLog_IsNgosLastLines_ByIdAfter()
        {
            KnPageDesk.PageLog.Add("[Server] started");
            KnPageDesk.PageLog.Add("broke", error: true);
            string all = Call("GET", KnPageDesk.LogPath).Body;
            StringAssert.Contains("\"text\":\"[Server] started\"", all);
            StringAssert.Contains("\"text\":\"(error) broke\"", all, "errors marked as NGO's");
            StringAssert.EndsWith("\"last\":2}", all);
            Assert.AreEqual("{\"lines\":[],\"last\":2}", Call("GET", KnPageDesk.LogPath, query: "after=2").Body, "nothing new");
            for (int i = 0; i < KnPageDesk.PageLog.Kept + 20; i++) KnPageDesk.PageLog.Add("line " + i);
            StringAssert.DoesNotContain("[Server] started", Call("GET", KnPageDesk.LogPath).Body, "NGO keeps the last 500");
        }

        [Test]
        public void ABanOfAPlayerFromTheList_IsOnTheirProvenAccount_SendsThemAway_AndUnbanEndsIt()
        {
            var desk = new KnPageServerDesk();
            Assert.AreEqual("That player is no longer here.", desk.Ban(string.Empty, 999, "3d", "cheating", KnPageDesk.From));
            PlayerKey key = PlayerKeys.Mint(7);
            StringAssert.Contains("has no proven account to ban", desk.Ban(string.Empty, key.Value, "3d", "cheating", KnPageDesk.From));
            StringAssert.DoesNotContain("banned", desk.Ban(string.Empty, key.Value, "3y", "cheating", KnPageDesk.From), "a nonsense length is refused (NGO's words)");
            Assert.AreEqual(0, KnKickRequests.Pending, "nobody sent away yet");

            ServerAccounts.Remember(key, Account, string.Empty);
            try
            {
                string said = desk.Ban(string.Empty, key.Value, "3d", "cheating", KnPageDesk.From);
                StringAssert.StartsWith("Account " + Account + " is banned", said);
                StringAssert.EndsWith("and was sent away.", said);
                Assert.IsTrue(ServerBans.IsBanned(Account, DateTime.UtcNow, out BanEntry ban), "on the list the join check reads");
                Assert.AreEqual("cheating", ban.reason);
                Assert.AreEqual(KnPageDesk.From, ban.by);
                Assert.AreEqual(1, KnKickRequests.Pending, "sent away through the kick");
                StringAssert.Contains("\"account\":\"" + Account + "\"", desk.BansJson());

                StringAssert.Contains("is no longer banned", desk.Unban(Account));
                Assert.IsFalse(ServerBans.IsBanned(Account, DateTime.UtcNow, out _));
                Assert.AreEqual("[]", desk.BansJson());
            }
            finally { ServerAccounts.Forget(key); }

            // by name or account id when they are not on (NGO BanPlayer's other half, done here: NGO's needs NGO listening)
            string offline = desk.Ban(Account, 0, "forever", "spam", KnPageDesk.From);
            StringAssert.StartsWith("Account " + Account + " is banned", offline);
            StringAssert.EndsWith("they will be turned away when they try to join.", offline);
            Assert.IsTrue(ServerBans.IsBanned(Account, DateTime.UtcNow, out _));
            Assert.AreEqual(1, KnKickRequests.Pending, "nobody else sent away");
            StringAssert.Contains("Nobody on or saved here is called", desk.Ban("Nobody", 0, "3d", "", KnPageDesk.From));
            StringAssert.Contains("is no longer banned", desk.Unban(Account));

            ServerHooks.Announce = (text, from) => text == "Restart" && from == "Server" ? 3 : -1;
            Assert.AreEqual("Announced to 3 player(s).", desk.Announce("Restart"));
            desk.Record("save", "Everyone is saved.");
            Assert.AreEqual("Control page (this machine): save -> Everyone is saved.", desk.Recorded[desk.Recorded.Count - 1]);
        }
    }
}
