using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Networking.Entities.Tests;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// Build 17 #211 (Concord 040), the Sky Lift's proof without Play (owner via Nova, 13 Nov 2026: Play here ran out of memory twice).
    /// The ride: the Marshal's button asks through the client's own request (<see cref="SkyLiftLink.Requester"/>), and the server runs a
    /// GM teleport for that player only, up to the arrival marker or down to the return marker; a connection not in the game is refused.
    /// The landing: both markers are in Zone01 where the GM teleport finds walkable ground (the NavMesh snap <c>gm.tp</c> uses, 6 m)
    /// right under them, on a floor, so the player stands where they are put and does not fall.
    /// </summary>
    public class SkyLiftTests
    {
        const string ScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        const float TeleportSnap = 6f;   // GmCommands.NavSampleRadius

        readonly List<(ulong id, string line)> m_Ran = new List<(ulong, string)>();
        Func<ulong, string, string> m_OldRunner;

        [SetUp]
        public void SetUp()
        {
            m_Ran.Clear();
            m_OldRunner = PlayerGm.CommandRunner;
            PlayerGm.CommandRunner = (id, line) => { m_Ran.Add((id, line)); return "Teleported."; };
        }

        [TearDown]
        public void TearDown() => PlayerGm.CommandRunner = m_OldRunner;

        static ConnectionPlayerKey ServerKeyOf(KnTestWorld tw, World client, out Entity connection)
        {
            int networkId = KnTestWorld.NetworkIdOf(client);
            var em = tw.Server.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<ConnectionPlayerKey>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            using var keys = query.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            for (int i = 0; i < ids.Length; i++)
                if (ids[i].Value == networkId) { connection = entities[i]; return keys[i]; }
            Assert.Fail("the server has no key for that client");
            connection = Entity.Null;
            return default;
        }

        [Test]
        public void TheButton_AsksTheServer_WhichTeleportsOnlyThatPlayer_UpAndBackDown()
        {
            Assume.That(SkyLiftLink.Enabled, "this build has the Sky Lift switched off (KN_SKY_LIFT_OFF)");
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            tw.Connect();
            tw.Tick(5);
            var server = tw.Server.GetExistingSystemManaged<SkyLiftServerSystem>();
            Assert.IsNotNull(server, "the server world has the Sky Lift");
            Assert.IsNotNull(SkyLiftLink.Requester, "a joined client gives the Marshal's button its request");
            // the button belongs to the client made last (one game per machine); find which one that is by its request
            ConnectionPlayerKey keyA = ServerKeyOf(tw, tw.Clients[0], out _), keyB = ServerKeyOf(tw, tw.Clients[1], out _);
            RpcBudget.Forget(keyA.PlayerKey);
            RpcBudget.Forget(keyB.PlayerKey);

            SkyLiftLink.Requester(false);
            tw.TickUntil(() => m_Ran.Count > 0, 120);
            Assert.AreEqual(1, m_Ran.Count, "one ride up");
            Assert.AreEqual("gm.tp " + SkyLiftLink.ArrivalMarker, m_Ran[0].line, "up goes to the arrival marker");
            ulong rider = m_Ran[0].id;
            Assert.IsTrue(rider == keyA.Serial || rider == keyB.Serial, "for a joined player");
            Assert.AreNotEqual(keyA.Serial, keyB.Serial);

            SkyLiftLink.Requester(true);
            tw.TickUntil(() => m_Ran.Count > 1, 120);
            Assert.AreEqual(2, m_Ran.Count, "one ride down");
            Assert.AreEqual(("gm.tp " + SkyLiftLink.ReturnMarker), m_Ran[1].line, "down goes to the return marker");
            Assert.AreEqual(rider, m_Ran[1].id, "the same player, never the other one");
            Assert.AreEqual(2, server.Rides);
        }

        [Test]
        public void AConnectionNotInTheGame_IsRefused()
        {
            Assume.That(SkyLiftLink.Enabled, "this build has the Sky Lift switched off (KN_SKY_LIFT_OFF)");
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            tw.Connect();
            tw.Tick(5);
            ConnectionPlayerKey key = ServerKeyOf(tw, tw.Clients[0], out Entity connection);
            RpcBudget.Forget(key.PlayerKey);
            tw.Server.EntityManager.RemoveComponent<NetworkStreamInGame>(connection);
            SkyLiftLink.Requester(false);
            tw.Tick(30);
            Assert.AreEqual(0, m_Ran.Count, "no teleport for a connection that has not entered the game");
        }

        [TestCase(SkyLiftLink.ArrivalMarker, true)]
        [TestCase(SkyLiftLink.ReturnMarker, false)]
        public void EachMarker_StandsOnWalkableGround_TheTeleportSnapsTo(string markerName, bool upOnConcord)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject marker = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Transform found = Find(root.transform, markerName);
                    if (found != null) { marker = found.gameObject; break; }
                }
                Assert.IsNotNull(marker, $"Zone01 has '{markerName}'");
                Assert.AreEqual(0, marker.transform.childCount, "a bare marker (gm.tp uses its own position, not a renderer centre)");
                Vector3 at = marker.transform.position;
                Assert.IsTrue(NavMesh.SamplePosition(at, out NavMeshHit hit, TeleportSnap, NavMesh.AllAreas), $"walkable ground within {TeleportSnap} m of {at}");
                Vector2 flat = new Vector2(hit.position.x - at.x, hit.position.z - at.z);
                Assert.Less(flat.magnitude, 1f, $"the player lands at the marker (snapped {flat.magnitude:0.00} m sideways)");
                Assert.Less(Mathf.Abs(hit.position.y - at.y), 1f, $"at its height ({hit.position.y:0.00} vs {at.y:0.00})");
                Assert.IsTrue(Physics.Raycast(hit.position + Vector3.up, Vector3.down, out RaycastHit floor, 2f, ~0, QueryTriggerInteraction.Ignore),
                    "a floor under the landing spot, so the player does not fall");
                Assert.Less(Mathf.Abs(floor.point.y - hit.position.y), 0.6f, "the walkable ground is the floor's top");
                Assert.Greater(Vector3.Dot(floor.normal, Vector3.up), 0.85f, "level enough to stand on");
                if (upOnConcord) Assert.IsTrue(HasParentNamed(marker.transform, "Concord"), "the arrival is on the Concord 040 platform");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform found = Find(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static bool HasParentNamed(Transform t, string part)
        {
            for (Transform p = t.parent; p != null; p = p.parent)
                if (p.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
