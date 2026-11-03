using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using KryticalNova.Core;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// M7.4 the page's command box and GM list (owner Q3 a): the owner's only (this machine and the key), each call runs on the game's thread
    /// through the page's tools and comes back as the page's JSON; nothing runs without the key or before the tools are there.
    /// </summary>
    public class KnPageGmRoutesTests
    {
        private const string Key = "page-key";

        sealed class Tools : KnPageRules.ICommands
        {
            public readonly List<string> Calls = new List<string>();
            public string Command(string line, string from) { Calls.Add("command " + line + " | " + from); return "ran " + line; }
            public string GmListJson() { Calls.Add("list"); return "[{\"name\":\"Ace\",\"bound\":true,\"signedIn\":false}]"; }
            public string GmAdd(string name) { Calls.Add("add " + name); return name + " is a GM."; }
            public string GmBind(string name, ulong playerKey) { Calls.Add("bind " + name + " " + playerKey); return "bound"; }
            public string GmUnbind(string name) { Calls.Add("unbind " + name); return "unbound"; }
            public string GmRemove(string name) { Calls.Add("remove " + name); return "removed"; }
        }

        Tools m_Tools;

        [SetUp]
        public void SetUp()
        {
            KnPageWork.Reset();
            KnPageRules.Commands = m_Tools = new Tools();
        }

        [TearDown]
        public void TearDown()
        {
            KnPageRules.Commands = null;
            KnPageWork.Reset();
        }

        static KnPageRules.Reply Call(string method, string path, string body, bool loopback = true, string key = Key) =>
            KnPageRules.Handle(method, path, loopback, key, Key, new KnStatusSnapshot(), new StopEndpointRules(), false, null, body);

        /// <summary>As the page's thread does: queue the work, and run it here as the game's thread would.</summary>
        static KnPageRules.Reply Finish(KnPageRules.Reply reply)
        {
            Assert.IsNotNull(reply.Work, "the call has work for the game's thread");
            Task<KnPageRules.Reply> waiting = Task.Run(() => KnPageWork.Run(reply, 5000));
            for (int i = 0; i < 500 && !waiting.IsCompleted; i++)
            {
                KnPageWork.RunQueued();
                System.Threading.Thread.Sleep(2);
            }
            Assert.IsTrue(waiting.Wait(5000), "the page got its answer");
            return waiting.Result;
        }

        [Test]
        public void OnlyTheOwner_WithTheKey_OnThisMachine()
        {
            Assert.AreEqual(403, Call("POST", "/api/command", "{\"line\":\"gm.players\"}", key: "wrong").Status);
            Assert.AreEqual(403, Call("POST", "/api/command", "{\"line\":\"gm.players\"}", loopback: false).Status);
            Assert.AreEqual(403, Call("GET", "/api/gm", null, key: null).Status);
            Assert.AreEqual(0, m_Tools.Calls.Count, "nothing ran");
            KnPageRules.Commands = null;
            Assert.AreEqual(503, Call("POST", "/api/command", "{\"line\":\"gm.players\"}").Status, "no tools yet");
        }

        [Test]
        public void TheCommandBox_RunsTheLineOnTheGamesThread_AndAnswersAsJson()
        {
            KnPageRules.Reply reply = Finish(Call("POST", "/api/command", "{\"line\":\"  gm.time 0  \"}"));
            Assert.AreEqual(200, reply.Status);
            Assert.AreEqual("{\"answer\":\"ran gm.time 0\"}", reply.Body);
            Assert.AreEqual("command gm.time 0 | Control page (this machine)", m_Tools.Calls[0], "trimmed, and the GM record says who");

            KnPageRules.Reply empty = Call("POST", "/api/command", "{\"line\":\"  \"}");
            Assert.IsNull(empty.Work, "an empty line runs nothing");
            StringAssert.Contains("Type a command first.", empty.Body);
            Assert.AreEqual(405, Call("GET", "/api/command", null).Status);
            Assert.AreEqual(400, Call("POST", "/api/command", "not json {").Status);
        }

        [Test]
        public void TheGmList_AndItsButtons_GoToTheTools()
        {
            KnPageRules.Reply list = Finish(Call("GET", "/api/gm", null));
            Assert.AreEqual("[{\"name\":\"Ace\",\"bound\":true,\"signedIn\":false}]", list.Body, "the list is sent as it is");

            Assert.AreEqual("{\"answer\":\"Bea is a GM.\"}", Finish(Call("POST", "/api/gm/add", "{\"name\":\"Bea\"}")).Body);
            Finish(Call("POST", "/api/gm/bind", "{\"name\":\"Bea\",\"player\":\"42\"}"));
            Finish(Call("POST", "/api/gm/unbind", "{\"name\":\"Bea\"}"));
            Finish(Call("POST", "/api/gm/remove", "{\"name\":\"Bea\"}"));
            CollectionAssert.AreEqual(new[] { "list", "add Bea", "bind Bea 42", "unbind Bea", "remove Bea" }, m_Tools.Calls);

            KnPageRules.Reply noPlayer = Call("POST", "/api/gm/bind", "{\"name\":\"Bea\",\"player\":\"x\"}");
            Assert.IsNull(noPlayer.Work);
            StringAssert.Contains("Pick the player from the player list.", noPlayer.Body);
            Assert.AreEqual(404, Call("POST", "/api/gm/fly", "{}").Status);
        }

        [Test]
        public void AGameThreadThatDoesNotAnswer_GivesTheOwner504()
        {
            KnPageRules.Reply reply = Call("POST", "/api/command", "{\"line\":\"gm.players\"}");
            KnPageRules.Reply late = KnPageWork.Run(reply, 50);   // nothing runs the queue
            Assert.AreEqual(504, late.Status);
        }
    }
}
