using System;
using NUnit.Framework;
using Unity.NetCode;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Perf round 3, R3-2 (reader audit perf-r3-2-field-audit.md): components only the owner's prediction and the server read are sent
    /// to the owner only; the ones other players' bodies are drawn from are sent to everyone.
    /// </summary>
    public class OwnerOnlyFieldsTests
    {
        static SendToOwnerType SendTypeOf(Type t)
        {
            var a = (GhostComponentAttribute)Attribute.GetCustomAttribute(t, typeof(GhostComponentAttribute));
            return a == null ? SendToOwnerType.All : a.OwnerSendType;
        }

        [Test]
        public void OwnerOnlyComponents_AreSentToTheOwnerOnly()
        {
            Assert.AreEqual(SendToOwnerType.SendToOwner, SendTypeOf(typeof(ServerOrder)));
            Assert.AreEqual(SendToOwnerType.SendToOwner, SendTypeOf(typeof(WaterPlan)));
        }

        [Test]
        public void WhatObserversDrawFrom_IsSentToEveryone()
        {
            // PlayerPresentationFollowSystem draws other players from these (the wake: MoveState.Water)
            Assert.AreEqual(SendToOwnerType.All, SendTypeOf(typeof(MoveState)));
            Assert.AreEqual(SendToOwnerType.All, SendTypeOf(typeof(PlayerPose)));
            Assert.AreEqual(SendToOwnerType.All, SendTypeOf(typeof(MotionPlan)));
            Assert.AreEqual(SendToOwnerType.All, SendTypeOf(typeof(VaultPlan)));
            Assert.AreEqual(SendToOwnerType.All, SendTypeOf(typeof(KnockPlan)));
        }
    }
}
