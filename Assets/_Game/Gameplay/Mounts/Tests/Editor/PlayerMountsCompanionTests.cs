using System.Reflection;
using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Mount Companion on <see cref="PlayerMounts"/> where the game is decided (day list item 19, plan evals: refusals, riding and out
    /// exclusive, another box while out, knock-out and rest by the clock, the Mount Stim, saves old and new). The real catalog and settings.
    /// </summary>
    public sealed class PlayerMountsCompanionTests
    {
        const string CatalogPath = "Assets/_Game/Gameplay/Mounts/Data/Assets/MountCatalog.asset";
        GameObject go;
        PlayerMounts mounts;
        MountCatalog catalog;
        long clock;

        [SetUp]
        public void SetUp()
        {
            catalog = AssetDatabase.LoadAssetAtPath<MountCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, CatalogPath);
            clock = 1_000_000;
            PlayerMounts.UtcNow = () => clock;
            go = new GameObject("CompanionTest");
            mounts = go.AddComponent<PlayerMounts>();
            typeof(PlayerMounts).GetField("catalog", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mounts, catalog);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerMounts.UtcNow = () => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Object.DestroyImmediate(go);
        }

        MountDefinition Built()
        {
            for (int box = 1; box <= MountRules.Slots; box++)
                if (catalog.Get(box) is { IsBuilt: true } m) return m;
            Assert.Fail("no built mount in the catalog");
            return null;
        }

        // the 0.4 s press guard runs on Time.unscaledTime, which does not move in EditMode
        void Press()
        {
            typeof(PlayerMounts).GetField("nextRidePress", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mounts, 0f);
            mounts.ServerToggleCompanion();
        }

        [Test]
        public void NoMount_IsRefused()
        {
            StringAssert.Contains("no mount", mounts.CompanionBlockedReason());
            Press();
            Assert.IsFalse(mounts.CompanionOut);
        }

        [Test]
        public void CallsOut_AndSendsHome()
        {
            MountDefinition m = Built();
            mounts.ServerRegister(m);
            Assert.IsNull(mounts.CompanionBlockedReason());
            Press();
            Assert.IsTrue(mounts.CompanionOut);
            Assert.AreEqual(m.mountId, ((KryticalNova.Core.IBodyMountCompanion)mounts).CompanionBox);
            Press();
            Assert.IsFalse(mounts.CompanionOut);
            Assert.AreEqual(0, ((KryticalNova.Core.IBodyMountCompanion)mounts).CompanionBox);
        }

        [Test]
        public void Starving_IsRefused()
        {
            mounts.ServerRegister(Built());
            mounts.ServerSetHunger(0f);
            StringAssert.Contains("starving", mounts.CompanionBlockedReason());
        }

        [Test]
        public void RidingAndOut_AreOneOrTheOther()
        {
            mounts.ServerRegister(Built());
            Press();
            Assert.IsTrue(mounts.CompanionOut);
            typeof(PlayerMounts).GetField("nextRidePress", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mounts, 0f);
            mounts.ServerToggleRide();
            Assert.IsFalse(mounts.CompanionOut, "the Ride key sends it home");
            Assert.AreEqual(RidePhase.GettingOn, mounts.Phase, "and the mount-up plays");
        }

        [Test]
        public void AnotherBoxWhileOut_SendsItHomeFirst()
        {
            MountDefinition m = Built();
            mounts.ServerRegister(m);
            MountDefinition other = null;
            for (int box = 1; box <= MountRules.Slots; box++) if (box != m.mountId && catalog.Get(box) != null) other = catalog.Get(box);
            Assume.That(other, Is.Not.Null, "a second mount in the catalog");
            mounts.ServerRegister(other);
            Press();
            mounts.ServerSelect(other.mountId);
            Assert.IsFalse(mounts.CompanionOut);
            Assert.AreEqual(other.mountId, mounts.SelectedId);
        }

        [Test]
        public void KnockOut_Rests300s_ThenComesBackAt30Percent()
        {
            MountDefinition m = Built();
            mounts.ServerRegister(m);
            Press();
            mounts.HurtCompanion(mounts.CompanionMaxHp * 0.4999f);
            Assert.IsTrue(mounts.CompanionOut);
            Assert.AreEqual(500, mounts.CompanionHpOf(m.mountId));
            mounts.HurtCompanion(mounts.CompanionMaxHp);
            Assert.IsFalse(mounts.CompanionOut, "knocked out: it went home");
            Assert.AreEqual(300, mounts.RestLeftOf(m.mountId));
            StringAssert.Contains("resting", mounts.CompanionBlockedReason());
            Assert.IsNull(mounts.RideBlockedReason(), "a resting mount can still be ridden (owner pick 3 a)");
            clock += 301;
            Assert.IsNull(mounts.CompanionBlockedReason());
            Assert.AreEqual(300, mounts.CompanionHpOf(m.mountId), "back at 30 %, regen from the end of the rest");
            clock += 600;
            Assert.AreEqual(500, mounts.CompanionHpOf(m.mountId), "10 min at home: +20 %");
        }

        [Test]
        public void MountStim_HealsOnlyTheMount_RefusedWhenFullOrResting()
        {
            MountDefinition m = Built();
            mounts.ServerRegister(m);
            StringAssert.Contains("not hurt", mounts.StimBlockedReason());
            Press();
            mounts.HurtCompanion(mounts.CompanionMaxHp * 0.6999f);
            Assert.AreEqual(300, mounts.CompanionHpOf(m.mountId));
            Assert.IsNull(mounts.StimBlockedReason());
            mounts.ServerStim();
            Assert.AreEqual(700, mounts.CompanionHpOf(m.mountId), "+40 % of its max");
            mounts.ServerSetCompanionHp(0f);
            Assert.IsFalse(mounts.CompanionOut);
            StringAssert.Contains("resting", mounts.StimBlockedReason());
        }

        [Test]
        public void Saves_KeepTheRestAcrossARelog_AndAnOldSaveLoadsUnchanged()
        {
            MountDefinition m = Built();
            mounts.ServerRegister(m);
            Press();
            mounts.ServerSetCompanionHp(0f);
            string json = mounts.CaptureSave();

            var go2 = new GameObject("CompanionTest2");
            try
            {
                var again = go2.AddComponent<PlayerMounts>();
                typeof(PlayerMounts).GetField("catalog", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(again, catalog);
                clock += 100;
                again.RestoreSave(json);
                Assert.IsFalse(again.CompanionOut, "nobody comes back with it out");
                Assert.AreEqual(200, again.RestLeftOf(m.mountId), "a relog does not skip the rest");
                Assert.AreEqual(CompanionStance.Free, again.Stance);

                // written before item 19: no hpLost, hpAt, restUntil or stance
                string old = "{\"version\":1,\"selected\":" + m.mountId + ",\"mounts\":[{\"id\":" + m.mountId + ",\"xp\":12345,\"hunger\":2500}]}";
                again.RestoreSave(old);
                Assert.AreEqual(12345u, again.StateOf(m.mountId).XpUnits, "EXP kept");
                Assert.AreEqual(2500u, again.StateOf(m.mountId).HungerDeficit, "hunger kept");
                Assert.AreEqual(1000, again.CompanionHpOf(m.mountId), "full HP");
                Assert.AreEqual(0, again.RestLeftOf(m.mountId), "not resting");
                Assert.AreEqual(CompanionStance.Free, again.Stance);
            }
            finally { Object.DestroyImmediate(go2); }
        }
    }
}
