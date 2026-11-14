using System.Reflection;
using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The companion's stances grow with the mount's growth like a pet's stats (owner, 14 Nov 2026), from the real MountSettings asset: DPS
    /// about 40 % -> 70 % of the owner's strike, HEALER 1.5 % -> 3 % of max HP every 2 s, TANK 40 % -> 70 % less damage and x1 -> x2 HP, each at
    /// 0, 50 and 100 %; the window's tooltip shows the numbers of the growth; and the ride stats (the two main stats up to +50, the riding
    /// speeds) are exactly what they were at every growth, whatever the stance.
    /// </summary>
    public sealed class CompanionStanceGrowthTests
    {
        const string SettingsPath = "Assets/_Game/Gameplay/Mounts/Data/Assets/MountSettings.asset";
        const string CatalogPath = "Assets/_Game/Gameplay/Mounts/Data/Assets/MountCatalog.asset";

        static CompanionFightTuning Fight()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MountSettings>(SettingsPath);
            Assert.IsNotNull(settings, SettingsPath);
            return settings.CompanionFight;
        }

        [TestCase(0, 0.4025f)]
        [TestCase(50, 0.55200005f)]
        [TestCase(100, 0.7015f)]
        public void Dps_StrikeShareGrows_About40To70Percent(int growth, float strike)
        {
            Assert.AreEqual(strike, CompanionFightRules.StrikeScale(Fight().AtGrowth(growth)), 1e-4f);
        }

        [TestCase(0, 15f)]
        [TestCase(50, 22.5f)]
        [TestCase(100, 30f)]
        public void Healer_PulseGrows_1Point5To3PercentOfMaxHp(int growth, float healOf1000)
        {
            CompanionFightTuning t = Fight().AtGrowth(growth);
            Assert.AreEqual(healOf1000, CompanionFightRules.PulseHeal(1000f, t), 1e-3f);
            Assert.AreEqual(2f, t.PulseSeconds, "every 2 s at any growth");
        }

        [TestCase(0, 0.4f, 1f, 0.6f)]
        [TestCase(50, 0.55f, 1.5f, 0.3f)]
        [TestCase(100, 0.7f, 2f, 0.15f)]
        public void Tank_DefenceAndHpGrow_40To70PercentLess_X1ToX2Hp(int growth, float buff, float hpTimes, float costOfABlow)
        {
            CompanionFightTuning t = Fight().AtGrowth(growth);
            Assert.AreEqual(buff, t.DefenceBuff, 1e-4f);
            Assert.AreEqual(hpTimes, t.TankHpMultiplier, 1e-4f);
            Assert.AreEqual(costOfABlow, CompanionFightRules.TakenScale(CompanionStance.Tank, t), 1e-4f, "share of its plain max HP a blow costs");
            Assert.AreEqual(1f, CompanionFightRules.TakenScale(CompanionStance.Free, t), "only TANK (a TANK in PvP acts as FREE)");
        }

        [Test]
        public void Tooltips_ShowTheNumbersOfTheGrowth()
        {
            CompanionFightTuning full = Fight().AtGrowth(100), none = Fight().AtGrowth(0);
            StringAssert.Contains("70 % less damage", CompanionFightRules.StanceTip(CompanionStance.Tank, full, 100));
            StringAssert.Contains("x2 HP", CompanionFightRules.StanceTip(CompanionStance.Tank, full, 100));
            StringAssert.Contains("40 % less damage", CompanionFightRules.StanceTip(CompanionStance.Tank, none, 0));
            StringAssert.Contains("about 70 %", CompanionFightRules.StanceTip(CompanionStance.Dps, full, 100));
            StringAssert.Contains("about 40 %", CompanionFightRules.StanceTip(CompanionStance.Dps, none, 0));
            StringAssert.Contains("3 % of your max HP", CompanionFightRules.StanceTip(CompanionStance.Healer, full, 100));
            StringAssert.Contains("1.5 % of your max HP", CompanionFightRules.StanceTip(CompanionStance.Healer, none, 0));
        }

        [Test]
        public void RideStats_AreWhatTheyWere_AtEveryGrowth_WhateverTheStance()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MountSettings>(SettingsPath);
            var catalog = AssetDatabase.LoadAssetAtPath<MountCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, CatalogPath);
            MountDefinition mount = null;
            for (int box = 1; box <= MountRules.Slots && mount == null; box++)
                if (catalog.Get(box) is { IsBuilt: true } m) mount = m;
            Assume.That(mount, Is.Not.Null, "a built mount");

            var go = new GameObject("RideStatsTest");
            long clock = 1_000_000;
            PlayerMounts.UtcNow = () => clock;
            try
            {
                var mounts = go.AddComponent<PlayerMounts>();
                typeof(PlayerMounts).GetField("catalog", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mounts, catalog);
                mounts.ServerRegister(mount);
                mounts.ServerSelect(mount.mountId);
                foreach (int growth in new[] { 0, 50, 100 })
                {
                    mounts.ServerSetGrowth(growth);
                    Assert.AreEqual(growth, mounts.GrowthOf(mount.mountId));
                    // the owner's ride numbers before stances grew (owner: "stay the way they work now")
                    float first = mount.firstMax * growth / 100f, second = mount.secondMax * growth / 100f;
                    float walk = settings.walkSpeed.x + (settings.walkSpeed.y - settings.walkSpeed.x) * growth / 100f;
                    float run = settings.runSpeed.x + (settings.runSpeed.y - settings.runSpeed.x) * growth / 100f;
                    foreach (CompanionStance stance in new[] { CompanionStance.Free, CompanionStance.Tank, CompanionStance.Dps, CompanionStance.Healer })
                    {
                        mounts.ServerSetStance((int)stance);
                        Assert.AreEqual(first, MountRules.StatBonus(mount.firstMax, mounts.GrowthOf(mount.mountId), true), 1e-4f, $"first stat at {growth} % ({stance})");
                        Assert.AreEqual(second, MountRules.StatBonus(mount.secondMax, mounts.GrowthOf(mount.mountId), true), 1e-4f, $"second stat at {growth} % ({stance})");
                        Assert.AreEqual(walk, MountRules.Speed(settings.walkSpeed.x, settings.walkSpeed.y, mounts.GrowthOf(mount.mountId), true), 1e-4f);
                        Assert.AreEqual(run, MountRules.Speed(settings.runSpeed.x, settings.runSpeed.y, mounts.GrowthOf(mount.mountId), true), 1e-4f);
                    }
                }
                Assert.AreEqual(mount.firstMax, MountRules.StatBonus(mount.firstMax, 100, true), "the main stat bonus tops out at its max (+50)");
            }
            finally
            {
                PlayerMounts.UtcNow = () => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Object.DestroyImmediate(go);
            }
        }
    }
}
