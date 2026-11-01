using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Tests;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>
    /// Plan 52, "is this body mine", with two clients in the Editor test world: a server and two clients in one process, a body per
    /// ghost tagged the way <c>ClientBodyFactory</c> tags it (ClientView, local only for the client's own ghost). On each client only its
    /// own player's body is mine; NGO's old answer (<c>!IsSpawned || IsOwner</c>, true for every never-spawned body) said yes for all four,
    /// so another player's run kicked my view and their death played my death view.
    /// </summary>
    public class BodyIsMineWorldTests
    {
        /// <summary>A part on the body that asks the question the way the game's components do (run kick, death view, messages, pets).</summary>
        sealed class MineProbe : MonoBehaviour
        {
            public bool Mine => BodyRoleTag.IsMine(this, true);   // true = what NGO's !IsSpawned gave every DOTS body
        }

        sealed class TaggedBodyFactory : IPlayerBodyFactory
        {
            public readonly List<GameObject> Made = new List<GameObject>();

            public GameObject Create(in PlayerBodyRequest request)
            {
                var go = new GameObject("TestBody " + request.NetworkId);
                go.SetActive(false);
                go.AddComponent<BodyRoleTag>().Assign(NetRoleKind.ClientView, PlayerKey.None, 0UL, request.IsLocal);
                var visuals = new GameObject("Visuals");
                visuals.transform.SetParent(go.transform);
                visuals.AddComponent<MineProbe>();   // on a child, as the jump presentation sits on 'Visuals'
                go.transform.position = request.Position;
                go.SetActive(true);
                Made.Add(go);
                return go;
            }

            public void Destroy(GameObject body)
            {
                if (body != null) Object.DestroyImmediate(body);
            }
        }

        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();
        [TearDown] public void TearDown() { GhostBodyRegistry.Clear(); LocalPlayer.Set(null); }

        [TestCase(0)]
        [TestCase(100)]
        public void OnEachClient_OnlyItsOwnPlayersBody_IsMine(int delayMs)
        {
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
            var factory = new TaggedBodyFactory();
            using var rig = PlayerRig.Create(2, delayMs);
            PlayerPresentationSetup.Install(rig.Clients[0], factory, null, registerLocalPlayer: true);
            // both clients tag their own body local (the game has one client per process; this test reads only the tags, not LocalPlayer)
            PlayerPresentationSetup.Install(rig.Clients[1], factory, null, registerLocalPlayer: true);
            rig.Tick(6);
            Assert.AreEqual(4, factory.Made.Count, "2 clients x 2 ghosts");
            for (int viewer = 0; viewer < 2; viewer++)
                for (int owner = 0; owner < 2; owner++)
                {
                    Assert.IsTrue(GhostBodyRegistry.TryGetByGhost(rig.Clients[viewer], rig.GhostIn(viewer, owner), out GhostBodyEntry entry),
                        $"client {viewer} has a body for client {owner}'s ghost");
                    var probe = entry.Body.GetComponentInChildren<MineProbe>();
                    Assert.AreEqual(viewer == owner, probe.Mine,
                        viewer == owner ? $"client {viewer}: my own body is mine" : $"client {viewer}: client {owner}'s body is never mine");
                }
            GhostBodyRegistry.Clear();
        }
    }
}
