using KryticalNova.Networking;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Presentation;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// r7 (owner's retest, 6 Nov 2026): the own view's mirrors sent before its body existed were dropped, so the server sends them all
    /// again once the body exists (<see cref="MirrorReadyRpc"/>), only for the sender's own body, once per body spawn.
    /// </summary>
    public class MirrorReadyTests
    {
        [Test]
        public void TheReadyIsSentOncePerOwnBody_AndNeverForNoBody()
        {
            var watch = new OwnBodyWatch();
            object first = new object(), second = new object();
            Assert.IsFalse(watch.IsNew(null), "no body yet: nothing");
            Assert.IsTrue(watch.IsNew(first), "the body exists: ready");
            Assert.IsFalse(watch.IsNew(first), "the same body: once only");
            Assert.IsFalse(watch.IsNew(null));
            Assert.IsTrue(watch.IsNew(second), "a new body (rebuilt): ready again");
        }

        [Test]
        public void TheServerResends_OnlyForTheSendersOwnBody_OncePerBody_AndIgnoresTheRest()
        {
            GhostBodyRegistry.Clear();
            var serverBody = new GameObject("r7 server body");
            var ownBody = new GameObject("r7 own body");
            var rebuilt = new GameObject("r7 own body rebuilt");
            var strangerBody = new GameObject("r7 stranger's own body");
            try
            {
                using var tw = new KnTestWorld();
                tw.CreateWorlds(2);
                tw.Connect();
                var client = tw.Clients[0].GetExistingSystemManaged<PlayerRequestClientSystem>();
                var stranger = tw.Clients[1].GetExistingSystemManaged<PlayerRequestClientSystem>();
                var server = tw.Server.GetExistingSystemManaged<PlayerPackSyncServerSystem>();
                Assert.IsNotNull(client);
                Assert.IsNotNull(server);
                int id = KnTestWorld.NetworkIdOf(tw.Clients[0]);
                // the first player has a body on the server; the second (the stranger) has none there
                Entity serverGhost = tw.Server.EntityManager.CreateEntity();
                GhostBodyRegistry.Register(tw.Server, serverGhost, id, default, serverBody, false);
                tw.Tick(10);
                Assert.AreEqual(0, client.ReadiesSent, "no own body yet: nothing said (what arrives now is dropped)");

                Entity ownGhost = tw.Clients[0].EntityManager.CreateEntity();
                GhostBodyRegistry.Register(tw.Clients[0], ownGhost, id, default, ownBody, true);
                tw.TickUntil(() => server.ReadiesHeard > 0, 120);
                tw.Tick(10);
                Assert.AreEqual(1, client.ReadiesSent, "said once when the body exists");
                Assert.AreEqual(1, server.ReadiesHeard, "the server honours it for the sender's own body (it then sends every mirror again)");

                // the client says it again (its body rebuilt): the same server body was resent already, so it is ignored
                GhostBodyRegistry.Unregister(tw.Clients[0], ownGhost, out _);
                GhostBodyRegistry.Register(tw.Clients[0], tw.Clients[0].EntityManager.CreateEntity(), id, default, rebuilt, true);
                tw.TickUntil(() => server.ReadiesIgnored > 0, 120);
                Assert.AreEqual(2, client.ReadiesSent);
                Assert.AreEqual(1, server.ReadiesHeard, "a repeat for the same body: no second resend");
                Assert.AreEqual(1, server.ReadiesIgnored);

                // a connection with no body on this server: ignored, nobody's mirrors are touched
                GhostBodyRegistry.Register(tw.Clients[1], tw.Clients[1].EntityManager.CreateEntity(), KnTestWorld.NetworkIdOf(tw.Clients[1]), default, strangerBody, true);
                tw.TickUntil(() => server.ReadiesIgnored > 1, 120);
                Assert.AreEqual(1, stranger.ReadiesSent);
                Assert.AreEqual(1, server.ReadiesHeard, "never for another player's body");
                Assert.AreEqual(2, server.ReadiesIgnored);
            }
            finally
            {
                GhostBodyRegistry.Clear();
                foreach (var go in new[] { serverBody, ownBody, rebuilt, strangerBody }) Object.DestroyImmediate(go);
            }
        }
    }
}
