using System.Collections;
using KryticalNova.Hostiles;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace KryticalNova.Persistence.Entities.Tests.Play
{
    /// <summary>
    /// M5/M6 Hostiles on the entity netcode with the game's real species prefabs (implementation plan s0: B70-B89). B70 is seam 3:
    /// a client view of a Hostile.
    /// </summary>
    public class HostileSmokeTests
    {
        const string SpeciesPath = "Assets/_Game/Gameplay/Hostiles/Prefabs/Hostile_Bonewalker.prefab";

        [UnityTest]
        public IEnumerator B70_AHostileView_NeverDecidesOrWakes_AndShowsWhatItsGhostSays()
        {
            // NGO's client copy of a Hostile (HostileNet.OnNetworkSpawn's client branch, AttackClientRpc, OnNetLevelChanged), driven by a ghost
            LogAssert.ignoreFailingMessages = true;   // a species body off the NavMesh may complain; this test reads HostileNet only
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SpeciesPath);
            Assert.IsNotNull(prefab, SpeciesPath);
            GameObject body = Object.Instantiate(prefab, new Vector3(0f, -500f, 0f), Quaternion.identity);
            try
            {
                var net = body.GetComponent<HostileNet>();
                net.BindView(12);                      // before its Start, as the view factory will
                for (int i = 0; i < 3; i++) yield return null;

                Assert.IsTrue(net.IsView);
                Assert.AreEqual(12, net.Level, "the ghost's level");
                CollectionAssert.Contains(HostileNet.Active, net, "nameplates, minimap and Autopilot see it");
                Assert.AreEqual(default(HostileStats), net.Stats, "never woken: no numbers decided here");
                net.SetLevel(30);
                Assert.AreEqual(12, net.Level, "a view never decides its level");

                int levels = 0;
                net.LevelChanged += () => levels++;
                net.ShowLevelFromGhost(14);
                Assert.AreEqual(14, net.Level);
                Assert.AreEqual(1, levels);

                int started = -1;
                bool heavySeen = false;
                net.AttackStarted += (index, windup, heavy) => { started = index; heavySeen = heavy; };
                const int third = 2;
                net.ShowAttackFromGhost(third);
                Assert.AreEqual(third, started, "the ghost's attack starts here");
                Assert.AreEqual(net.Definition.attacks.Length > third && net.Definition.attacks[third].heavy, heavySeen, "heavy as the species says");

                net.ShowStateFromGhost(new HostileAnimState { Dead = true, DeathIndex = 1 });
                net.ShowStateFromGhost(default);   // back from the pool: stands up
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Object.Destroy(body);
            }
            yield return null;
        }
    }
}
