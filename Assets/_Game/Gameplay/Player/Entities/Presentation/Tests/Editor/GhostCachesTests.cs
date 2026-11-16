using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>Relevancy condition 3a (R3-3): a ghost-keyed client cache drops the ghosts that went and keeps the ones still there.</summary>
    public class GhostCachesTests
    {
        [Test]
        public void DropGone_RemovesOnlyDestroyedGhosts_AndANewGhostOfTheSamePlayerIsANewKey()
        {
            using var world = new World("kn-ghost-caches");
            EntityManager em = world.EntityManager;
            Entity stays = em.CreateEntity();
            Entity goes = em.CreateEntity();
            var cache = new Dictionary<Entity, uint> { [stays] = 4, [goes] = 9 };

            em.DestroyEntity(goes);
            Entity back = em.CreateEntity();   // the same player re-enters range: a new ghost entity (Entities may reuse the index, never the version)
            Assert.IsFalse(cache.ContainsKey(back), "a re-entering ghost is met for the first time, so it is seeded, never replayed");

            Assert.AreEqual(1, GhostCaches.DropGone(cache, em));
            CollectionAssert.AreEquivalent(new[] { stays }, cache.Keys);
            Assert.AreEqual(4u, cache[stays], "what was seen of a ghost still there is kept");
            Assert.AreEqual(0, GhostCaches.DropGone(cache, em));
        }
    }
}
