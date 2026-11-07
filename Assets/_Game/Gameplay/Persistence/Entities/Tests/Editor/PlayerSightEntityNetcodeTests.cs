using KryticalNova.Core;
using KryticalNova.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Owner's hand test, 7 Nov 2026: no Callout on the entity netcode, neither in the player menu nor in the selected-player strip. Both asked
    /// NGO's IsSpawned, which no body of the entity netcode ever is. Another player's view counts as in sight; the own view and an offline
    /// object keep their answers.
    /// </summary>
    public sealed class PlayerSightEntityNetcodeTests
    {
        static NetworkObject Body(NetRoleKind kind, bool local, ulong connection)
        {
            var go = new GameObject("Body");
            NetworkObject net = go.AddComponent<NetworkObject>();
            if (kind != NetRoleKind.Offline) go.AddComponent<BodyRoleTag>().Assign(kind, new PlayerKey(connection), connection, local);
            return net;
        }

        [Test]
        public void AnotherPlayersView_IsInSight_TheOwnViewAndAnOfflineObjectAreNot()
        {
            NetworkObject other = Body(NetRoleKind.ClientView, false, 2), own = Body(NetRoleKind.ClientView, true, 1), offline = Body(NetRoleKind.Offline, false, 0);
            try
            {
                Assert.IsTrue(new PlayerMenuTarget("Player2", other).InSight, "another player's view: in sight (Callout, invite and follow by object)");
                Assert.IsFalse(new PlayerMenuTarget("Tester", own).InSight, "the own view is never a menu target in sight");
                Assert.IsFalse(new PlayerMenuTarget("Someone", offline).InSight, "an offline object not spawned keeps NGO's answer");
                Assert.IsFalse(new PlayerMenuTarget("Nobody").InSight, "a name only");
            }
            finally
            {
                Object.DestroyImmediate(other.gameObject);
                Object.DestroyImmediate(own.gameObject);
                Object.DestroyImmediate(offline.gameObject);
            }
        }
    }
}
