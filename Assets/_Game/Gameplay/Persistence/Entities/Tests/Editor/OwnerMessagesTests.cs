using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// M7/M8 system 0: a line the server says to one player reaches that player's message area, and no other player's (NGO's owner-only
    /// message RPCs never run on a server body of the entity netcode).
    /// </summary>
    public class OwnerMessagesTests
    {
        sealed class Lines : IGameMessageSink
        {
            public readonly List<string> Said = new List<string>();
            public void ShowMessage(string message) => Said.Add(message);
        }

        [Test]
        public void AServerLine_ReachesItsPlayer_AndNoOtherPlayer()
        {
            var lines = new Lines();
            ClientPresentationRegistry.RegisterMessages(lines);
            try
            {
                using var tw = new KnTestWorld();
                tw.CreateWorlds(2);
                tw.Connect();
                int first = KnTestWorld.NetworkIdOf(tw.Clients[0]);
                var firstClient = tw.Clients[0].GetExistingSystemManaged<PlayerRequestClientSystem>();
                var secondClient = tw.Clients[1].GetExistingSystemManaged<PlayerRequestClientSystem>();
                Assert.IsNotNull(firstClient, "the client world has the request system");

                Assert.IsTrue(EntityOwnerMessages.Send(tw.Server, first, "Your Flask is empty. Refill it at an Apothecary."));
                tw.TickUntil(() => firstClient.Tells > 0, 120);
                tw.Tick(10);

                Assert.AreEqual(1, firstClient.Tells, "the player it was said to");
                Assert.AreEqual(0, secondClient.Tells, "nobody else");
                CollectionAssert.AreEqual(new[] { "Your Flask is empty. Refill it at an Apothecary." }, lines.Said);
                Assert.IsFalse(EntityOwnerMessages.Send(tw.Server, 9999, "nobody"), "no such player: nothing is sent");
                Assert.IsFalse(OwnerMessages.TryTell(null, "x"), "not a server body: the caller shows it itself");
            }
            finally { ClientPresentationRegistry.UnregisterMessages(lines); }
        }
    }
}
