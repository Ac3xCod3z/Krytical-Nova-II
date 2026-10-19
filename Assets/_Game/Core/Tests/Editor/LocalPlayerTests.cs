using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The one way every system finds the player this machine controls (architecture stage 3): a component is found
    /// once and kept, a new local player starts afresh, and a player that is gone reads as none.
    /// </summary>
    public sealed class LocalPlayerTests
    {
        private GameObject before;

        [SetUp]
        public void Keep() => before = LocalPlayer.Object;

        [TearDown]
        public void Restore() => LocalPlayer.Set(before);

        [Test]
        public void AComponentIsFoundOnceAndKept()
        {
            var player = new GameObject("player", typeof(BoxCollider));
            try
            {
                LocalPlayer.Set(player);
                BoxCollider first = LocalPlayer.Get<BoxCollider>();
                Assert.IsNotNull(first);
                Assert.AreSame(first, LocalPlayer.Get<BoxCollider>());
                Assert.IsNull(LocalPlayer.Get<SphereCollider>(), "a component the player lacks is null");
                Assert.IsTrue(LocalPlayer.TryGetPosition(out _));
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void ANewLocalPlayerStartsAfreshAndIsAnnounced()
        {
            var one = new GameObject("one", typeof(BoxCollider));
            var two = new GameObject("two", typeof(BoxCollider));
            int told = 0;
            void Count() => told++;
            LocalPlayer.Changed += Count;
            try
            {
                LocalPlayer.Set(one);
                BoxCollider first = LocalPlayer.Get<BoxCollider>();
                LocalPlayer.Set(two);
                Assert.AreSame(two.GetComponent<BoxCollider>(), LocalPlayer.Get<BoxCollider>(), "not the old player's");
                Assert.AreNotSame(first, LocalPlayer.Get<BoxCollider>());
                LocalPlayer.Set(two);
                Assert.AreEqual(2, told, "setting the same player again says nothing");
            }
            finally
            {
                LocalPlayer.Changed -= Count;
                Object.DestroyImmediate(one);
                Object.DestroyImmediate(two);
            }
        }

        [Test]
        public void APlayerThatIsGoneReadsAsNone()
        {
            var player = new GameObject("player", typeof(BoxCollider));
            LocalPlayer.Set(player);
            Assert.IsNotNull(LocalPlayer.Get<BoxCollider>());
            Object.DestroyImmediate(player);
            Assert.IsNull(LocalPlayer.Get<BoxCollider>());
            Assert.IsFalse(LocalPlayer.TryGetPosition(out _));
        }
    }
}
