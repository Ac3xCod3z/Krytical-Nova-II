using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// Wave 4 relevancy, the switch and the wiring: the <c>relevancy</c> key of server-settings.json (absent / false / true) wins over
    /// the settings asset; off leaves the server world as it was; on, the fill system is the one writer of the package's set.
    /// </summary>
    public class RelevancySwitchTests
    {
        /// <summary>A filler's reason no system of the game uses (1 is the Callout's).</summary>
        const byte TestReason = 200;

        static RelevancySettings Settings(bool enabled)
        {
            var s = ScriptableObject.CreateInstance<RelevancySettings>();
            s.enabled = enabled;
            return s;
        }

        // ------------------------------------------------------------------ the key (pure)

        [Test]
        public void TheKey_Absent_False_True()
        {
            Assert.IsNull(KnServerSettings.Parse("{\"port\": 7797}", new[] { "exe" }).Relevancy, "absent: the asset decides");
            Assert.IsNull(KnServerSettings.Parse(null, new[] { "exe" }).Relevancy, "no file: the asset decides");
            Assert.AreEqual(false, KnServerSettings.Parse("{\"port\": 7797, \"relevancy\": false}", new[] { "exe" }).Relevancy);
            Assert.AreEqual(true, KnServerSettings.Parse("{\"relevancy\":true,\"port\": 7797}", new[] { "exe" }).Relevancy);
            Assert.AreEqual(false, KnServerSettings.Parse("{\"relevancy\" : \"False\"}", new[] { "exe" }).Relevancy, "quoted and any case counts");
            Assert.AreEqual(7797, KnServerSettings.Parse("{\"port\": 7797, \"relevancy\": true}", new[] { "exe" }).port, "the other keys read as before");
        }

        [Test]
        public void TheKey_WithAnythingElse_IsIgnored()
        {
            Assert.IsNull(KnServerSettings.ReadSwitch("{\"relevancy\": 150}", "relevancy"));
            Assert.IsNull(KnServerSettings.ReadSwitch("{\"relevancyRadius\": true}", "relevancy"), "another key that only begins the same");
        }

        [Test]
        public void TheServersKey_WinsOverTheAsset()
        {
            Assert.IsFalse(RelevancySetup.IsOn(assetEnabled: false, serverSwitch: null));
            Assert.IsTrue(RelevancySetup.IsOn(assetEnabled: true, serverSwitch: null));
            Assert.IsFalse(RelevancySetup.IsOn(assetEnabled: true, serverSwitch: false), "the rollback without a build");
            Assert.IsTrue(RelevancySetup.IsOn(assetEnabled: false, serverSwitch: true), "the soak's 'after' run and the owner's pop-in check");
        }

        // ------------------------------------------------------------------ on a server world

        [Test]
        public void Off_ByTheKey_LeavesTheWorldAsItWas_AndSaysSo()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            Assert.IsFalse(RelevancySetup.Install(tw.Server, Settings(true), serverSwitch: false));
            tw.Connect(goInGame: true);
            tw.Tick(8);
            Assert.AreEqual(0, KnTestWorld.Count<RelevancyConfig>(tw.Server));
            Assert.AreEqual(0, KnTestWorld.Count<GhostImportance>(tw.Server));
            Assert.AreEqual(GhostRelevancyMode.Disabled, KnTestWorld.Singleton<GhostRelevancy>(tw.Server).GhostRelevancyMode, "every ghost to everyone, as before");
            Assert.AreEqual(0, KnTestWorld.Count<BecameRelevantGhost>(tw.Server), "no connection carries the lists");
            StringAssert.Contains("[Relevancy] off (" + KnServerSettings.FileName + ")", string.Join("\n", tw.Logs));
        }

        [Test]
        public void On_ByTheKey_TheFillIsTheWriter_AndAnAlwaysRelevantGhostIsInTheSetAtOnce()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            Assert.IsTrue(RelevancySetup.Install(tw.Server, Settings(false), serverSwitch: true));
            StringAssert.Contains("[Relevancy] on (" + KnServerSettings.FileName + "): players 150 m, monsters 150 m", string.Join("\n", tw.Logs));
            tw.Connect(goInGame: true);
            tw.Tick(8);

            EntityManager em = tw.Server.EntityManager;
            GhostRelevancy relevancy = KnTestWorld.Singleton<GhostRelevancy>(tw.Server);
            Assert.AreEqual(GhostRelevancyMode.SetIsRelevant, relevancy.GhostRelevancyMode);
            Assert.AreEqual(0, relevancy.GhostRelevancySet.Count(), "two connections with no body: nothing is sent to them");

            using EntityQuery lists = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadWrite<AlwaysRelevantGhost>(), ComponentType.ReadOnly<BecameRelevantGhost>());
            Assert.AreEqual(2, lists.CalculateEntityCount(), "every in-game connection carries the two lists");

            // a filler names a ghost: the fill takes it up in that tick, not after the interval. The reason is the test's own:
            // reason 1 is the Callout's, and its server system (alive in this world) takes a reason-1 entry off again in the
            // same tick, before the fill, when no Callout runs.
            using var connections = lists.ToEntityArray(Allocator.Temp);
            using var ids = lists.ToComponentDataArray<NetworkId>(Allocator.Temp);
            AlwaysRelevantGhosts.SetOnly(em.GetBuffer<AlwaysRelevantGhost>(connections[0]), TestReason, 77);
            tw.Tick(1);
            Assert.IsTrue(relevancy.GhostRelevancySet.ContainsKey(new RelevantGhostForConnection(ids[0].Value, 77)));
            Assert.IsFalse(relevancy.GhostRelevancySet.ContainsKey(new RelevantGhostForConnection(ids[1].Value, 77)), "only for the connection that named it");
            Assert.AreEqual(1, em.GetBuffer<BecameRelevantGhost>(connections[0], true).Length, "said once, for one tick");
            tw.Tick(1);
            Assert.AreEqual(0, em.GetBuffer<BecameRelevantGhost>(connections[0], true).Length);

            AlwaysRelevantGhosts.SetOnly(em.GetBuffer<AlwaysRelevantGhost>(connections[0]), TestReason, 0);
            tw.Tick(1);
            Assert.AreEqual(0, relevancy.GhostRelevancySet.Count(), "taken off: gone from the set in that tick");

            foreach (string line in tw.Interesting) StringAssert.DoesNotContain("Exception", line);
        }

        [Test]
        public void TheAlwaysRelevantList_KeepsOneEntryAReason_AndLeavesTheOthersAlone()
        {
            using var world = new World("relevancy-list-test");
            Entity e = world.EntityManager.CreateEntity();
            DynamicBuffer<AlwaysRelevantGhost> list = world.EntityManager.AddBuffer<AlwaysRelevantGhost>(e);
            Assert.IsTrue(AlwaysRelevantGhosts.IsOnly(list, 1, 0), "empty: nothing to do for 'none'");

            AlwaysRelevantGhosts.SetOnly(list, 2, 500);   // another filler's entry
            AlwaysRelevantGhosts.SetOnly(list, 1, 41);
            Assert.IsTrue(AlwaysRelevantGhosts.IsOnly(list, 1, 41));
            Assert.IsFalse(AlwaysRelevantGhosts.IsOnly(list, 1, 42));

            AlwaysRelevantGhosts.SetOnly(list, 1, 42);   // a new opponent
            Assert.AreEqual(2, list.Length);
            Assert.IsTrue(AlwaysRelevantGhosts.IsOnly(list, 1, 42) && AlwaysRelevantGhosts.IsOnly(list, 2, 500));

            AlwaysRelevantGhosts.SetOnly(list, 1, 0);   // the Callout is over
            Assert.AreEqual(1, list.Length);
            Assert.AreEqual(500, list[0].GhostId, "the other filler's entry stays");
        }
    }
}
