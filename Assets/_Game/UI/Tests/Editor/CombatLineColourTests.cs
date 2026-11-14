using KryticalNova.Core;
using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Build 17, item 4.2: your own hits and hits on you get their own class in the chat window.</summary>
    public sealed class CombatLineColourTests
    {
        [Test]
        public void EachSideOfAHitHasItsClassAndOtherLinesNone()
        {
            Assert.AreEqual("chat-line--combat-out", ChatWindowController.CombatSideClass(ChatCombatSide.Out));
            Assert.AreEqual("chat-line--combat-in", ChatWindowController.CombatSideClass(ChatCombatSide.In));
            Assert.IsNull(ChatWindowController.CombatSideClass(ChatCombatSide.None));
        }
    }
}
