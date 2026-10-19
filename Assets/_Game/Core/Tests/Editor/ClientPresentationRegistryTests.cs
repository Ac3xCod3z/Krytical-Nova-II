using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The only door from gameplay to the screen (80 runtime files go through it): a sink registered is the one
    /// gameplay reaches, an old sink going away never unplugs its replacement, and the world is blocked only where a
    /// registered UI says so.
    /// </summary>
    public sealed class ClientPresentationRegistryTests
    {
        private sealed class Messages : IGameMessageSink
        {
            public string Last;
            public void ShowMessage(string message) => Last = message;
        }

        private sealed class Block : IPointerBlockQuery
        {
            public Rect Area;
            public bool BlocksWorldPointer(Vector2 screenPosition) => Area.Contains(screenPosition);
        }

        private IGameMessageSink messagesBefore;

        [SetUp]
        public void Keep() => messagesBefore = ClientPresentationRegistry.Messages;

        [TearDown]
        public void Restore()
        {
            if (ClientPresentationRegistry.Messages != null) ClientPresentationRegistry.UnregisterMessages(ClientPresentationRegistry.Messages);
            if (messagesBefore != null) ClientPresentationRegistry.RegisterMessages(messagesBefore);
        }

        [Test]
        public void ARegisteredSinkIsTheOneGameplayReaches()
        {
            var sink = new Messages();
            ClientPresentationRegistry.RegisterMessages(sink);
            ClientPresentationRegistry.Messages?.ShowMessage("hello");
            Assert.AreEqual("hello", sink.Last);
        }

        [Test]
        public void AnOldSinkGoingAwayLeavesItsReplacementPluggedIn()
        {
            var first = new Messages();
            var second = new Messages();
            ClientPresentationRegistry.RegisterMessages(first);
            ClientPresentationRegistry.RegisterMessages(second);
            ClientPresentationRegistry.UnregisterMessages(first);
            Assert.AreSame(second, ClientPresentationRegistry.Messages, "the screen that replaced it keeps the slot");
            ClientPresentationRegistry.UnregisterMessages(second);
            Assert.IsNull(ClientPresentationRegistry.Messages, "with its own sink gone the slot is empty, as on a server");
        }

        [Test]
        public void TheWorldIsBlockedOnlyUnderARegisteredUi()
        {
            var panel = new Block { Area = new Rect(0f, 0f, 100f, 100f) };
            ClientPresentationRegistry.RegisterPointerBlock(panel);
            ClientPresentationRegistry.RegisterPointerBlock(panel);   // twice is once
            try
            {
                Assert.IsTrue(ClientPresentationRegistry.BlocksWorldPointer(new Vector2(50f, 50f)));
                Assert.IsFalse(ClientPresentationRegistry.BlocksWorldPointer(new Vector2(500f, 500f)));
            }
            finally
            {
                ClientPresentationRegistry.UnregisterPointerBlock(panel);
            }
            Assert.IsFalse(ClientPresentationRegistry.BlocksWorldPointer(new Vector2(50f, 50f)), "one unregister removes it");
        }
    }
}
