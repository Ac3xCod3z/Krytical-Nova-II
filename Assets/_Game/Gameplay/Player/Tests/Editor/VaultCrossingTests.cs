using KryticalNova.Core;
using KryticalNova.Player;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Tests
{
    /// <summary>
    /// What stops a vault's crossing (day list 15.2, 13 Nov 2026): static geometry always does; a character never does (a rider's own
    /// mount model, a pet, a player body or a Hostile), on a dedicated server and in a host Editor alike.
    /// </summary>
    public sealed class VaultCrossingTests
    {
        private sealed class Dummy : MonoBehaviour, IDamageable
        {
            public string EntityName => "dummy";
            public bool IsAlive => true;
            public float CurrentHealth => 1f;
            public float MaxHealth => 1f;
            public Transform Transform => transform;
            public CharacterCombatStats CombatStats => null;
            public DamageResult TakeDamage(DamageResult damage, CharacterCombatStats attacker) => damage;
        }

        private GameObject made;

        [TearDown]
        public void Clean()
        {
            if (made != null) Object.DestroyImmediate(made);
        }

        [Test]
        public void StaticGeometry_AlwaysBlocks()
        {
            made = GameObject.CreatePrimitive(PrimitiveType.Cube);
            made.name = "Roof beam";
            Assert.IsTrue(PlayerVaultMotor.BlocksCrossing(made.GetComponent<Collider>()));
        }

        [Test]
        public void NestedStaticGeometry_StillBlocks()
        {
            made = new GameObject("Building");
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.transform.SetParent(made.transform);
            Assert.IsTrue(PlayerVaultMotor.BlocksCrossing(beam.GetComponent<Collider>()), "a part of a building is still geometry");
        }

        [Test]
        public void RidersOwnMountModel_NeverBlocks()
        {
            made = new GameObject("Player (view)");
            made.AddComponent<NavMeshAgent>().enabled = false;
            var mount = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mount.name = "Mount (Brambleback)";
            mount.transform.SetParent(made.transform);
            Assert.IsFalse(PlayerVaultMotor.BlocksCrossing(mount.GetComponent<Collider>()), "the mount rides under a NavMeshAgent body");
        }

        [Test]
        public void ADamageableCharacter_NeverBlocks()
        {
            made = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            made.AddComponent<Dummy>();
            Assert.IsFalse(PlayerVaultMotor.BlocksCrossing(made.GetComponent<Collider>()));
        }

        [Test]
        public void NoCollider_DoesNotBlock()
        {
            Assert.IsFalse(PlayerVaultMotor.BlocksCrossing(null));
        }
    }
}
