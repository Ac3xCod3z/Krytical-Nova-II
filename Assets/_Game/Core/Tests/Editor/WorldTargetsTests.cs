using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// "Does something else move this body's root?" (WorldTargets.SyncsTransform): yes for a body the entity netcode builds and places
    /// every frame, so effects such as the hit reaction keep off its root (owner's DOTS retries 3-4, 30-31 Oct 2026: the body never turned).
    /// </summary>
    public sealed class WorldTargetsTests
    {
        static GameObject Body(NetRoleKind? kind)
        {
            var body = new GameObject("body");
            if (kind.HasValue) body.AddComponent<BodyRoleTag>().Assign(kind.Value, PlayerKey.None, 0UL, true);
            return body;
        }

        [Test]
        public void ABodyOfTheEntityNetcode_IsMovedBySomethingElse()
        {
            var view = Body(NetRoleKind.ClientView);
            var server = Body(NetRoleKind.ServerBody);
            var offline = Body(NetRoleKind.Offline);
            var plain = Body(null);
            try
            {
                Assert.IsTrue(WorldTargets.SyncsTransform(view), "a client view is placed by the ghost follow");
                Assert.IsTrue(WorldTargets.SyncsTransform(server), "a server body is placed by the server body sync");
                Assert.IsFalse(WorldTargets.SyncsTransform(offline), "an unfilled tag says nothing");
                Assert.IsFalse(WorldTargets.SyncsTransform(plain), "a plain object with no session is its own");
                Assert.IsFalse(WorldTargets.SyncsTransform(null));
            }
            finally
            {
                Object.DestroyImmediate(view); Object.DestroyImmediate(server); Object.DestroyImmediate(offline); Object.DestroyImmediate(plain);
            }
        }
    }
}
