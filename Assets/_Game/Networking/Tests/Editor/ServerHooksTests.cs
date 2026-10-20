using KryticalNova.Networking;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Networking.Tests
{
    /// <summary>
    /// The server's own code reaches players, the chat and the saves only through <see cref="ServerHooks"/> (architecture
    /// stage 1). The game's systems fill the slots when they load; an empty one would make a server that cannot ban,
    /// announce or save, so this checks every slot is filled and that a non-player object reads as nobody.
    /// </summary>
    public sealed class ServerHooksTests
    {
        [Test]
        public void EverySlotIsFilledWhenTheGameLoads()
        {
            Assert.IsNotNull(ServerHooks.DescribePlayer, "the Player system tells the server who a player is");
            Assert.IsNotNull(ServerHooks.Announce, "Social lets the server announce");
            Assert.IsNotNull(ServerHooks.Saves, "Persistence gives the server the saves");
            Assert.IsNotNull(ServerHooks.Roster, "Persistence answers the menus' questions about characters");
        }

        [Test]
        public void AnObjectThatIsNoPlayerReadsAsNobody()
        {
            var thing = new GameObject("not a player");
            try
            {
                ServerHooks.PlayerFacts facts = ServerHooks.Describe(thing);
                Assert.IsFalse(facts.Found);
                Assert.AreEqual("-", facts.Faction);
                Assert.AreEqual(0, facts.Level);
                Assert.IsFalse(ServerHooks.Describe(null).Found);
            }
            finally
            {
                Object.DestroyImmediate(thing);
            }
        }
    }
}
