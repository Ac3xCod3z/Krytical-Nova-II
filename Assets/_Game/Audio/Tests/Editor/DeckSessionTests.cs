using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Audio.Tests
{
    /// <summary>Build 17: logging out with the Deck playing kept the music on into the next character.</summary>
    public sealed class DeckSessionTests
    {
        private DeckLibrary library;

        [SetUp]
        public void SetUp()
        {
            DeckPlayer.EndSession();
            library = ScriptableObject.CreateInstance<DeckLibrary>();
            var pack = ScriptableObject.CreateInstance<DeckPack>();
            var track = ScriptableObject.CreateInstance<MusicTrack>();
            track.name = "test-track";
            track.clip = AudioClip.Create("test-clip", 44100, 1, 44100, false);
            pack.tracks = new[] { track };
            library.packs = new[] { pack };
        }

        [TearDown]
        public void TearDown()
        {
            DeckPlayer.EndSession();
            Object.DestroyImmediate(library);
        }

        [Test]
        public void EndSession_StopsTheDeck_AndTheMusicIsNoLongerHeld()
        {
            DeckPlayer deck = DeckPlayer.Create(library);
            Assert.IsNotNull(deck);
            deck.Play();
            Assert.IsTrue(DeckPlayer.HoldsMusic);

            DeckPlayer.EndSession();

            Assert.IsFalse(DeckPlayer.HoldsMusic);
            Assert.IsNull(DeckPlayer.Instance);
            Assert.IsTrue(deck == null, "the old character's Deck object is gone");
        }

        [Test]
        public void TheNextCharacter_GetsAFreshStoppedDeck()
        {
            DeckPlayer.Create(library).Play();
            DeckPlayer.EndSession();

            DeckPlayer next = DeckPlayer.Create(library);
            Assert.IsNotNull(next);
            Assert.IsFalse(next.IsPlaying);
            Assert.IsFalse(next.InFight);
        }

        [Test]
        public void EndSession_WithNoDeck_DoesNothing()
        {
            Assert.DoesNotThrow(DeckPlayer.EndSession);
        }
    }
}
