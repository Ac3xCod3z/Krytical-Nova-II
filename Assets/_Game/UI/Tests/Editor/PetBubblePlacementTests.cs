using System.Collections.Generic;
using KryticalNova.UI;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Where a pet's bubble goes: to the left of the pet when it can, shifted or on the other side when something
    /// that must stay clear is in the way, always on the screen, and never on the pet.
    /// </summary>
    public sealed class PetBubblePlacementTests
    {
        private const float Gap = 8f, Margin = 6f;
        private static readonly Rect Screen = new Rect(0f, 0f, 1920f, 1080f);
        private static readonly Vector2 Size = new Vector2(220f, 60f);
        private static readonly Rect Pet = new Rect(900f, 400f, 80f, 70f);

        private static Rect Place(Rect pet, IReadOnlyList<Rect> blocked, out bool tailOnRight, out bool clear) =>
            PetBubblePlacement.Place(pet, Size, blocked, Screen, Gap, Margin, out tailOnRight, out clear);

        private static bool Overlaps(Rect a, Rect b) => PetBubblePlacement.Covered(a, b, 0f) > 0f;

        [Test]
        public void WithNothingInTheWay_ItSitsToTheLeftOfThePet_ItsTailTowardIt()
        {
            Rect bubble = Place(Pet, new List<Rect>(), out bool tailOnRight, out bool clear);
            Assert.IsTrue(clear);
            Assert.IsTrue(tailOnRight, "the pet is on the bubble's right");
            Assert.AreEqual(Pet.xMin - Gap, bubble.xMax, 0.01f);
            Assert.AreEqual(Pet.center.y, bubble.center.y, 0.01f);
            Assert.AreEqual(Size.x, bubble.width, 0.01f);
        }

        [Test]
        public void ThePlayerOnItsLeft_PushesItUp_NotOverThePlayer()
        {
            // The player stands just left of and below the pet: the bubble's first place lies across the head.
            var player = new Rect(700f, 380f, 120f, 300f);
            Rect bubble = Place(Pet, new List<Rect> { player }, out bool tailOnRight, out bool clear);
            Assert.IsTrue(clear);
            Assert.IsFalse(Overlaps(bubble, player));
            Assert.IsTrue(tailOnRight, "still on the left: it was shifted, not flipped");
            Assert.Less(bubble.center.y, Pet.center.y, "shifted up");
        }

        [Test]
        public void WithTheWholeLeftTaken_ItFlipsToTheOtherSide()
        {
            var wall = new Rect(0f, 0f, Pet.xMin - 2f, 1080f); // a window covering everything left of the pet
            Rect bubble = Place(Pet, new List<Rect> { wall }, out bool tailOnRight, out bool clear);
            Assert.IsTrue(clear);
            Assert.IsFalse(tailOnRight, "the pet is on the bubble's left now");
            Assert.AreEqual(Pet.xMax + Gap, bubble.xMin, 0.01f);
            Assert.IsFalse(Overlaps(bubble, wall));
        }

        [Test]
        public void AtTheLeftEdgeOfTheScreen_ItFlips_RatherThanSitOnThePet()
        {
            var pet = new Rect(40f, 400f, 80f, 70f);
            Rect bubble = Place(pet, new List<Rect>(), out bool tailOnRight, out bool clear);
            Assert.IsTrue(clear);
            Assert.IsFalse(tailOnRight);
            Assert.IsFalse(Overlaps(bubble, pet));
            Assert.GreaterOrEqual(bubble.xMin, Screen.xMin);
        }

        [Test]
        public void ItKeepsItsMargin_FromWhatItMustNotCover()
        {
            var namecard = new Rect(Pet.xMin - Gap - Size.x - 30f, Pet.center.y - Size.y * 0.5f - 4f, 40f, 4f); // just touching the first place's margin
            Rect bubble = Place(Pet, new List<Rect> { namecard }, out _, out bool clear);
            Assert.IsTrue(clear);
            Assert.AreEqual(0f, PetBubblePlacement.Covered(bubble, namecard, Margin));
        }

        [Test]
        public void ItNeverLeavesTheScreen_NorSitsOnThePet_WhereverThePetIs()
        {
            var blocked = new List<Rect> { new Rect(860f, 500f, 200f, 320f), new Rect(820f, 330f, 260f, 44f), new Rect(700f, 980f, 520f, 90f) };
            for (float x = -40f; x <= 1900f; x += 97f)
            {
                for (float y = -30f; y <= 1050f; y += 83f)
                {
                    var pet = new Rect(x, y, 80f, 70f);
                    Rect bubble = Place(pet, blocked, out _, out bool clear);
                    Assert.GreaterOrEqual(bubble.xMin, Screen.xMin - 0.01f, pet.ToString());
                    Assert.LessOrEqual(bubble.xMax, Screen.xMax + 0.01f, pet.ToString());
                    Assert.GreaterOrEqual(bubble.yMin, Screen.yMin - 0.01f, pet.ToString());
                    Assert.LessOrEqual(bubble.yMax, Screen.yMax + 0.01f, pet.ToString());
                    if (!clear) continue;
                    Assert.IsFalse(Overlaps(bubble, pet), "on the pet: " + pet);
                    foreach (Rect each in blocked) Assert.AreEqual(0f, PetBubblePlacement.Covered(bubble, each, Margin), "covers " + each + " for a pet at " + pet);
                }
            }
        }

        [Test]
        public void WithNowhereClear_ItTakesThePlaceThatCoversLeast()
        {
            // Everything is covered: whatever it does it lies on something. It must still come back with a place.
            var everything = new List<Rect> { Screen };
            Rect bubble = Place(Pet, everything, out _, out bool clear);
            Assert.IsFalse(clear);
            Assert.AreEqual(Size.x, bubble.width, 0.01f);
            Assert.AreEqual(Size.y, bubble.height, 0.01f);
        }

        [Test]
        public void ARectangleOffTheScreen_IsMovedOnToIt_NotResized()
        {
            Rect moved = PetBubblePlacement.Clamp(new Rect(-50f, 1100f, 200f, 60f), Screen);
            Assert.AreEqual(new Rect(0f, 1020f, 200f, 60f), moved);
        }
    }
}
