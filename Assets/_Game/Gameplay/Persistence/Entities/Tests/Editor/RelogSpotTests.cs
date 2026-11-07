using KryticalNova.Networking;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>Owner rule, 6 Nov 2026 (both games): a relog puts the player back where they stood, or on the spawn point.</summary>
    public class RelogSpotTests
    {
        static readonly System.Func<Vector3, Vector3?> OnMesh = feet => new Vector3(feet.x, 2f, feet.z);   // the mesh's ground is at y 2
        static readonly System.Func<Vector3, bool> Free = _ => false;

        [Test]
        public void ARelog_GoesBackToTheSavedSpot_OnTheGround_UnlessItIsGoneOffTheMeshOrBlocked()
        {
            Vector3 saved = new Vector3(12.3f, 2.9f, -4.5f);   // a body root, base offset 0.9
            Assert.IsTrue(PlayerSpawnService.TryReturnSpot("Zone01_StarterTown", "Zone01_StarterTown", saved, 0.9f, OnMesh, Free, out Vector3 spot));
            Assert.AreEqual(new Vector3(12.3f, 2.9f, -4.5f), spot, "the mesh point (the real ground height) plus the base offset");

            Assert.IsFalse(PlayerSpawnService.TryReturnSpot("Dungeon_Gone", "Zone01_StarterTown", saved, 0.9f, OnMesh, Free, out _),
                "another map (or an instance that no longer exists): the spawn point");
            Assert.IsFalse(PlayerSpawnService.TryReturnSpot(null, "Zone01_StarterTown", saved, 0.9f, OnMesh, Free, out _), "never saved: the spawn point");
            Assert.IsFalse(PlayerSpawnService.TryReturnSpot("Zone01_StarterTown", "Zone01_StarterTown", saved, 0.9f, _ => null, Free, out _),
                "off the NavMesh: the spawn point");
            Assert.IsFalse(PlayerSpawnService.TryReturnSpot("Zone01_StarterTown", "Zone01_StarterTown", saved, 0.9f, OnMesh, _ => true, out _),
                "something solid stands there: the spawn point");

            Vector3 asked = default;
            PlayerSpawnService.TryReturnSpot("Z", "Z", saved, 0.9f, feet => { asked = feet; return feet; }, Free, out _);
            Assert.AreEqual(2f, asked.y, 1e-4f, "the mesh is asked at the saved FEET (root minus the base offset)");
        }
    }
}
