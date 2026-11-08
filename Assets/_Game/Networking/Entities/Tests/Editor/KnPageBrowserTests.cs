using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using NUnit.Framework;
using KryticalNova.Core;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// 8 Nov 2026 port of NGO's browser control page onto the entity page (KnPageBrowser): login and lockout, the session cookie, the
    /// same-origin and X-KN-Check refusals, NGO's body fields mapped onto the entity handlers, the page's files and the countdown.
    /// No sockets: <see cref="KnPageBrowser.Route"/> is called directly.
    /// </summary>
    public class KnPageBrowserTests
    {
        const string Password = "abc-def-ghi-42";
        static readonly IPAddress Home = IPAddress.Parse("192.168.1.20");

        sealed class FakeHost : KnPageBrowser.IHost
        {
            public string Folder_;
            public readonly List<string> Calls = new List<string>();
            public string Password() => KnPageBrowserTests.Password;
            public string Folder() => Folder_;
            public string StatusExtras() => "\"port\":7777,\"mapServer\":false,\"addresses\":[\"192.168.1.20\"],\"gms\":[],\"bans\":[],\"serverList\":[],\"mode\":\"direct\"";
            public string GmNameOf(ulong playerKey) => playerKey == 7 ? "Ace" : string.Empty;
            public string Backup() { Calls.Add("backup"); return "Backup: done"; }
            public string SwitchMode(string mode, float seconds) { Calls.Add("mode " + mode + " " + seconds); return "switched"; }
            public string ListAdd(string name, string address, int port) { Calls.Add("add " + name + " " + address + " " + port); return "added"; }
            public string ListRemove(string id) { Calls.Add("remove " + id); return "removed"; }
            public string ListPublish() { Calls.Add("publish"); return "published"; }
        }

        sealed class FakeDesk : KnPageDesk.IDesk
        {
            public readonly List<string> Calls = new List<string>();
            public string Save() { Calls.Add("save"); return "saved"; }
            public string Announce(string text) { Calls.Add("announce " + text); return "announced"; }
            public string Ban(string who, ulong playerKey, string length, string reason, string from) { Calls.Add($"ban who={who} key={playerKey} length={length} reason={reason}"); return "banned"; }
            public string Unban(string who) { Calls.Add("unban " + who); return "unbanned"; }
            public string BansJson() => "[]";
            public string FlagsJson(long after) => "{\"flags\":[],\"last\":0}";
            public void Record(string pressed, string answer) { Calls.Add("record " + pressed + " -> " + answer); }
        }

        sealed class FakeTools : KnPageRules.ICommands
        {
            public readonly List<string> Calls = new List<string>();
            public string Command(string line, string from) { Calls.Add("command " + line); return "ran " + line; }
            public string GmListJson() => "[]";
            public string GmAdd(string name) { Calls.Add("gmadd " + name); return "added"; }
            public string GmBind(string name, ulong playerKey) { Calls.Add("gmbind " + name + " " + playerKey); return "bound"; }
            public string GmUnbind(string name) { Calls.Add("gmunbind " + name); return "unbound"; }
            public string GmRemove(string name) { Calls.Add("gmremove " + name); return "removed"; }
        }

        FakeHost m_Host;
        FakeDesk m_Desk;
        FakeTools m_Tools;
        KnPageBrowser.State m_State;
        string m_Folder;

        [SetUp]
        public void SetUp()
        {
            m_Folder = Path.Combine(Path.GetTempPath(), "kn-page-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Folder);
            File.WriteAllText(Path.Combine(m_Folder, "index.html"), "<html>page</html>");
            File.WriteAllText(Path.Combine(m_Folder, "app.js"), "console.log(1);");
            File.WriteAllText(Path.Combine(m_Folder, "secret.txt"), "no");
            m_Host = new FakeHost { Folder_ = m_Folder };
            m_Desk = new FakeDesk();
            m_Tools = new FakeTools();
            KnPageDesk.Desk = m_Desk;
            KnPageRules.Commands = m_Tools;
            m_State = new KnPageBrowser.State { WrongPasswordDelayMs = 0 };
            KnShutdown.Current.Reset();
            KnStopRequests.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            KnPageDesk.Desk = null;
            KnPageRules.Commands = null;
            KnPageBrowser.Host = null;
            KnShutdown.Current.Reset();
            KnStopRequests.Reset();
            try { Directory.Delete(m_Folder, true); } catch (Exception) { /* temp */ }
        }

        static KnStatusSnapshot Snapshot() => new KnStatusSnapshot
        {
            Name = "N", Players = 2, MaxPlayers = 100, Map = "Zone01", Build = "1.0",
            PlayerRows = new[]
            {
                new KnPlayerRow { Key = 7, NetworkId = 1, Name = "Ace", PingMs = 30, Map = "Zone01", Level = 42, Faction = "Hegemony" },
                new KnPlayerRow { Key = 8, NetworkId = 2, Name = "Bo", PingMs = 40, Map = "Zone01" },
            },
        };

        static ControlPageRules.Request Req(string method, string path, string body = "", string cookie = null, string check = null, string origin = null, string host = "192.168.1.20:8090")
        {
            var r = new ControlPageRules.Request { Method = method, Path = path, Body = body };
            if (host != null) r.Headers["Host"] = host;
            if (cookie != null) r.Headers["Cookie"] = KnPageBrowser.CookieName + "=" + cookie;
            if (check != null) r.Headers["X-KN-Check"] = check;
            if (origin != null) r.Headers["Origin"] = origin;
            return r;
        }

        KnPageRules.Reply Route(ControlPageRules.Request r, IPAddress from = null) =>
            KnPageBrowser.Route(m_State, m_Host, r, from ?? Home, Snapshot(), new StopEndpointRules(), false);

        static string Run(KnPageRules.Reply reply)
        {
            Assert.IsNotNull(reply.Work, "the call runs on the game's thread");
            return reply.Work();
        }

        // logs in; returns the cookie token and the check
        (string Cookie, string Check) Login()
        {
            KnPageRules.Reply reply = Route(Req("POST", "/api/login", "{\"password\":\"" + Password + "\"}"));
            Assert.AreEqual(200, reply.Status);
            const string marker = KnPageBrowser.CookieName + "=";
            int at = reply.ExtraHeaders.IndexOf(marker, StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, "Set-Cookie");
            string cookie = reply.ExtraHeaders.Substring(at + marker.Length).Split(';')[0];
            StringAssert.Contains("HttpOnly", reply.ExtraHeaders);
            StringAssert.Contains("SameSite=Strict", reply.ExtraHeaders);
            string body = reply.Body;
            int c = body.IndexOf("\"check\":\"", StringComparison.Ordinal) + 9;
            return (cookie, body.Substring(c, body.IndexOf('"', c) - c));
        }

        // 8 Nov 2026: the socket writer left out Reply.ExtraHeaders, so the browser never got the login's cookie and the dashboard fell
        // back to the sign-in screen at its first call. The text that goes on the wire must carry them.
        [Test]
        public void TheHeadOnTheWire_CarriesTheLoginCookieAndTheSecurityHeaders()
        {
            KnPageRules.Reply login = Route(Req("POST", "/api/login", "{\"password\":\"" + Password + "\"}"));
            string head = KnServerPage.HeadOf(login, login.Body.Length);
            StringAssert.StartsWith("HTTP/1.1 200 OK\r\n", head);
            StringAssert.Contains("\r\nSet-Cookie: " + KnPageBrowser.CookieName + "=", head);
            StringAssert.Contains("HttpOnly", head);
            StringAssert.Contains("\r\nContent-Security-Policy: ", head);
            StringAssert.EndsWith("\r\n\r\n", head);
            Assert.AreEqual(1, head.Split(new[] { "\r\n\r\n" }, StringSplitOptions.None).Length - 1, "one blank line, at the end");
            // and a reply without extra headers still makes a whole head
            StringAssert.EndsWith("\r\n\r\n", KnServerPage.HeadOf(new KnPageRules.Reply(404, null, string.Empty, false), 0));
        }

        [Test]
        public void WrongPassword_IsRefused_AndFiveWrongLockTheAddress_EvenForTheRightOne()
        {
            for (int i = 0; i < 4; i++)
            {
                KnPageRules.Reply wrong = Route(Req("POST", "/api/login", "{\"password\":\"nope\"}"));
                Assert.AreEqual(401, wrong.Status);
                StringAssert.Contains("Wrong password", wrong.Body);
            }
            StringAssert.Contains("Locked for 5 minutes", Route(Req("POST", "/api/login", "{\"password\":\"nope\"}")).Body);
            KnPageRules.Reply locked = Route(Req("POST", "/api/login", "{\"password\":\"" + Password + "\"}"));
            Assert.AreEqual(429, locked.Status);
            StringAssert.Contains("Too many wrong passwords", locked.Body);
            // another address is not locked
            Assert.AreEqual(200, Route(Req("POST", "/api/login", "{\"password\":\"" + Password + "\"}"), IPAddress.Parse("192.168.1.99")).Status);
        }

        [Test]
        public void RightPassword_OpensASession_TheCookieReachesTheCallsAndLogoutEndsIt()
        {
            Assert.AreEqual(401, Route(Req("GET", "/api/session")).Status, "no cookie, no session");
            var (cookie, check) = Login();
            KnPageRules.Reply session = Route(Req("GET", "/api/session", cookie: cookie));
            Assert.AreEqual(200, session.Status);
            StringAssert.Contains(check, session.Body);
            Assert.AreEqual(401, Route(Req("GET", "/api/session", cookie: "forged")).Status);
            KnPageRules.Reply out_ = Route(Req("POST", "/api/logout", "{}", cookie, check));
            Assert.AreEqual(200, out_.Status);
            StringAssert.Contains("Max-Age=0", out_.ExtraHeaders);
            Assert.AreEqual(401, Route(Req("GET", "/api/session", cookie: cookie)).Status);
        }

        [Test]
        public void ALogin_FromAnotherSite_IsRefused()
        {
            KnPageRules.Reply reply = Route(Req("POST", "/api/login", "{\"password\":\"" + Password + "\"}", origin: "http://evil.example"));
            Assert.AreEqual(403, reply.Status);
        }

        [Test]
        public void AButtonWithoutTheCheck_OrFromAnotherOrigin_IsRefused_WithTheCheckItRuns()
        {
            var (cookie, check) = Login();
            Assert.AreEqual(403, Route(Req("POST", "/api/save", "{}", cookie)).Status, "no X-KN-Check");
            Assert.AreEqual(403, Route(Req("POST", "/api/save", "{}", cookie, "wrong")).Status);
            Assert.AreEqual(403, Route(Req("POST", "/api/save", "{}", cookie, check, "http://evil.example")).Status);
            Assert.IsEmpty(m_Desk.Calls);
            KnPageRules.Reply ok = Route(Req("POST", "/api/save", "{}", cookie, check, "http://192.168.1.20:8090"));
            Assert.AreEqual("saved", Run(ok));
            CollectionAssert.Contains(m_Desk.Calls, "save");
        }

        [Test]
        public void ANameThatIsNotThisMachine_OrAnAddressFromTheInternet_IsRefused()
        {
            Assert.AreEqual(403, Route(Req("GET", "/", host: "evil.example.com")).Status);
            Assert.AreEqual(403, Route(Req("GET", "/index.html"), IPAddress.Parse("8.8.8.8")).Status);
        }

        [Test]
        public void TheOwnersKeyHeader_StaysOnTheOldJsonPath_AndSlashAsksForHtmlOnlyFromABrowser()
        {
            KnPageBrowser.Host = m_Host;
            var owner = Req("GET", "/api/log");
            owner.Headers["X-KN-Key"] = "k";
            Assert.IsFalse(KnPageBrowser.Wants(owner));
            Assert.IsTrue(KnPageBrowser.Wants(Req("GET", "/api/log")));
            Assert.IsFalse(KnPageBrowser.Wants(Req("GET", "/")), "the status JSON stays at /");
            var browser = Req("GET", "/");
            browser.Headers["Accept"] = "text/html,application/xhtml+xml";
            Assert.IsTrue(KnPageBrowser.Wants(browser));
            Assert.IsTrue(KnPageBrowser.Wants(Req("GET", "/app.js")));
            Assert.IsFalse(KnPageBrowser.Wants(Req("GET", "/status")));
            Assert.IsFalse(KnPageBrowser.Wants(Req("POST", "/stop")));
            KnPageBrowser.Host = null;
            Assert.IsFalse(KnPageBrowser.Wants(Req("GET", "/api/log")), "no host, no browser page");
        }

        [Test]
        public void TheFilesOfThePage_AreServedWithTheirType_NothingOutsideTheFolder()
        {
            KnPageRules.Reply index = Route(Req("GET", "/"));
            Assert.AreEqual(200, index.Status);
            StringAssert.StartsWith("text/html", index.ContentType);
            Assert.AreEqual("<html>page</html>", System.Text.Encoding.UTF8.GetString(index.Bytes));
            StringAssert.Contains("Content-Security-Policy", index.ExtraHeaders);
            KnPageRules.Reply js = Route(Req("GET", "/app.js"));
            StringAssert.StartsWith("text/javascript", js.ContentType);
            Assert.AreEqual(404, Route(Req("GET", "/secret.txt")).Status, "unknown type");
            Assert.AreEqual(404, Route(Req("GET", "/../secret.txt")).Status);
            Assert.AreEqual(404, Route(Req("GET", "/%2e%2e/secret.txt")).Status);
            Assert.AreEqual(404, Route(Req("GET", "/..%5Capp.js")).Status);
            Assert.AreEqual(404, Route(Req("GET", "/.hidden.js")).Status);
            Assert.AreEqual(404, Route(Req("GET", "/missing.js")).Status);
            Assert.AreEqual(405, Route(Req("POST", "/app.js")).Status);
        }

        [Test]
        public void TheStatus_HasTheShapeThePageReads()
        {
            var (cookie, _) = Login();
            KnPageRules.Reply reply = Route(Req("GET", "/api/status", cookie: cookie));
            string json = Run(reply);
            Assert.IsTrue(reply.WorkIsJson);
            StringAssert.Contains("\"name\":\"N\"", json);
            StringAssert.Contains("\"maxPlayers\":100", json);
            StringAssert.Contains("\"shutdownIn\":-1", json);
            StringAssert.Contains("\"stopping\":false", json);
            StringAssert.Contains("\"id\":\"7\",\"name\":\"Ace\"", json);
            StringAssert.Contains("\"level\":42,\"faction\":\"Hegemony\"", json, "the page's list shows the real level and faction");
            StringAssert.Contains("\"gm\":\"Ace\"", json);
            StringAssert.Contains("\"ping\":40", json);
            StringAssert.Contains("\"addresses\":[\"192.168.1.20\"]", json);
            StringAssert.Contains("\"mode\":\"direct\"", json);
        }

        [Test]
        public void NgoBodyFields_AreMappedOntoTheEntityHandlers()
        {
            var (cookie, check) = Login();
            KnPageRules.Reply Post(string path, string body) => Route(Req("POST", path, body, cookie, check));

            Assert.AreEqual("announced", Run(Post("/api/announce", "{\"text\":\"hello all\"}")));
            Assert.AreEqual("ran gm.players", Run(Post("/api/command", "{\"line\":\"gm.players\"}")));
            Assert.AreEqual("added", Run(Post("/api/gm/add", "{\"name\":\"Ace\"}")));
            Assert.AreEqual("bound", Run(Post("/api/gm/bind", "{\"name\":\"Ace\",\"id\":\"8\"}")));
            Assert.AreEqual("unbound", Run(Post("/api/gm/unbind", "{\"name\":\"Ace\"}")));
            Assert.AreEqual("removed", Run(Post("/api/gm/remove", "{\"name\":\"Ace\"}")));
            // a player of the list is banned by their key; a typed name or account id by name; "text" is the length
            Assert.AreEqual("banned", Run(Post("/api/ban", "{\"id\":\"8\",\"text\":\"3d\",\"reason\":\"spam\"}")));
            Assert.AreEqual("banned", Run(Post("/api/ban", "{\"id\":\"Cato\",\"text\":\"forever\",\"reason\":\"\"}")));
            Assert.AreEqual("unbanned", Run(Post("/api/unban", "{\"id\":\"0123abcd\"}")));

            CollectionAssert.Contains(m_Desk.Calls, "announce hello all");
            CollectionAssert.Contains(m_Tools.Calls, "command gm.players");
            CollectionAssert.Contains(m_Tools.Calls, "gmadd Ace");
            CollectionAssert.Contains(m_Tools.Calls, "gmbind Ace 8");
            CollectionAssert.Contains(m_Tools.Calls, "gmunbind Ace");
            CollectionAssert.Contains(m_Tools.Calls, "gmremove Ace");
            CollectionAssert.Contains(m_Desk.Calls, "ban who= key=8 length=3d reason=spam");
            CollectionAssert.Contains(m_Desk.Calls, "ban who=Cato key=0 length=forever reason=");
            CollectionAssert.Contains(m_Desk.Calls, "unban 0123abcd");
            // the GM buttons go in the GM record like NGO's (the desk's own calls record themselves)
            Assert.IsTrue(m_Desk.Calls.Exists(c => c.StartsWith("record gm/bind", StringComparison.Ordinal)), "gm/bind recorded");
        }

        [Test]
        public void Kick_NamesThePlayerByKey_AndAnUnknownOneIsAnswered()
        {
            var (cookie, check) = Login();
            KnPageRules.Reply gone = Route(Req("POST", "/api/kick", "{\"id\":\"99\",\"reason\":\"spam\"}", cookie, check));
            Assert.IsNull(gone.Work);
            StringAssert.Contains("no longer here", gone.Body);
            KnPageRules.Reply known = Route(Req("POST", "/api/kick", "{\"id\":\"8\",\"reason\":\"spam\"}", cookie, check));
            Assert.IsNotNull(known.Work, "the refusal is queued on the game's thread");
        }

        [Test]
        public void BackupModeAndListButtons_GoToTheHost_AndAreRecorded()
        {
            var (cookie, check) = Login();
            KnPageRules.Reply Post(string path, string body) => Route(Req("POST", path, body, cookie, check));
            Assert.AreEqual("Backup: done", Run(Post("/api/backup", "{}")));
            Assert.AreEqual("switched", Run(Post("/api/mode", "{\"text\":\"relay\",\"seconds\":30}")));
            Assert.AreEqual("added", Run(Post("/api/list/add", "{\"name\":\"Home\",\"address\":\"1.2.3.4\",\"port\":7777}")));
            Assert.AreEqual("removed", Run(Post("/api/list/remove", "{\"id\":\"0\"}")));
            KnPageRules.Reply publish = Post("/api/list/publish", "{}");
            StringAssert.Contains("published", publish.Body);
            CollectionAssert.AreEqual(new[] { "backup", "mode relay 30", "add Home 1.2.3.4 7777", "remove 0", "publish" }, m_Host.Calls);
            Assert.IsTrue(m_Desk.Calls.Exists(c => c.StartsWith("record mode text=\"relay\" seconds=30", StringComparison.Ordinal)));
        }

        [Test]
        public void StopAndCancel_RunTheCountdown_NotAnInstantStop()
        {
            var (cookie, check) = Login();
            KnPageRules.Reply Post(string path, string body) => Route(Req("POST", path, body, cookie, check));
            StringAssert.Contains("Restart in 90 seconds", Run(Post("/api/stop", "{\"seconds\":90,\"reason\":\"patch\",\"restart\":true}")));
            Assert.IsTrue(KnShutdown.Current.Active);
            Assert.IsTrue(KnShutdown.Current.Restarts);
            Assert.IsFalse(KnStopRequests.Stopping, "the stop begins at zero");
            StringAssert.Contains("\"restarting\":true", Run(Route(Req("GET", "/api/status", cookie: cookie))));
            Assert.IsTrue(m_Desk.Calls.Exists(c => c.StartsWith("announce The server restarts in 90 seconds. patch", StringComparison.Ordinal)));
            Assert.AreEqual("Shutdown called off.", Run(Post("/api/cancel", "{}")));
            Assert.IsFalse(KnShutdown.Current.Active);
        }

        [Test]
        public void TheCountdown_NoticesAtTheMarks_AndFiresOnce()
        {
            var said = new List<string>();
            var count = new KnShutdown.Countdown();
            count.Schedule(0, 130, "maintenance", false, said.Add);
            Assert.AreEqual(1, said.Count, "the first notice is the schedule's own");
            Assert.IsFalse(count.Tick(5, said.Add));        // 125 s left: nothing
            Assert.IsFalse(count.Tick(10, said.Add));       // 120 s left: the 2-minute notice
            StringAssert.Contains("2 minutes", said[said.Count - 1]);
            Assert.IsFalse(count.Tick(70, said.Add));       // 60 s
            StringAssert.Contains("1 minute", said[said.Count - 1]);
            Assert.AreEqual(61, count.SecondsLeft(69));
            Assert.IsTrue(count.Tick(130, said.Add), "fires at zero");
            Assert.IsFalse(count.Tick(131, said.Add), "and only once");
            Assert.AreEqual(-1, count.SecondsLeft(131));
            Assert.AreEqual("No shutdown is counting down.", count.Cancel(said.Add));
        }
    }
}
