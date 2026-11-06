using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>M5.3a: the Hostiles mind's pure rules against the game's own data and the M5.0 table (parity rows and the owner's Q changes).</summary>
    public sealed class HostileRulesTests
    {
        const string CurvePath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileStatCurve.asset";
        const string SettingsPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileBrainSettings.asset";

        static HostileStatCurve Curve()
        {
            var curve = AssetDatabase.LoadAssetAtPath<HostileStatCurve>(CurvePath);
            Assert.IsNotNull(curve, CurvePath);
            return curve;
        }

        [Test]
        public void AggroRadius_MatchesCurve_RoundedUpAsEmeraldTakesIt()
        {
            // H01 parity: Emerald's DetectionRadius = CeilToInt(stats.AggroRadius) (HostileBrainLink.cs:109)
            HostileStatCurve curve = Curve();
            float3 c = new float3(curve.aggroRadius.x, curve.aggroRadius.y, curve.aggroRadius.z);
            for (int level = 1; level <= 145; level++)
                Assert.AreEqual(Mathf.Max(1, Mathf.CeilToInt(curve.Evaluate(level).AggroRadius)), HostileRules.AggroRadius(c, level), 1e-4f, "level " + level);
            Assert.AreEqual(9f, HostileRules.AggroRadius(c, 1), "every roster species (L1-22) scans 9 m");
            Assert.AreEqual(9f, HostileRules.AggroRadius(c, 22));
        }

        [Test]
        public void Speeds_MatchCurve()
        {
            HostileStatCurve curve = Curve();
            for (int level = 1; level <= 145; level++)
            {
                HostileStats s = curve.Evaluate(level);
                Assert.AreEqual(s.WalkSpeed, HostileRules.WalkSpeed(curve.walkSpeed), 1e-5f);
                Assert.AreEqual(s.RunSpeed, HostileRules.RunSpeed(new float2(curve.runSpeed.x, curve.runSpeed.y), level), 1e-4f, "level " + level);
            }
        }

        [Test]
        public void Settings_AreTheOwnersAnswers()
        {
            var settings = AssetDatabase.LoadAssetAtPath<HostileBrainSettings>(SettingsPath);
            Assert.IsNotNull(settings, SettingsPath);
            HostileBrainConfig c = HostileBrainConfig.From(settings, 30);
            Assert.AreEqual(3u, HostileRules.NoticeTicks(-1f, c.NoticeSecondsDefault, 30), "Q8: notice 0.1 s = 3 ticks");
            Assert.AreEqual(1.10f, c.ThreatSwitchMelee, 1e-5f, "Q5");
            Assert.AreEqual(1.30f, c.ThreatSwitchRanged, 1e-5f, "Q5");
            Assert.AreEqual(150u, c.GiveUpTicks, "H14 parity: Emerald gives up after 5 s");
        }

        [Test]
        public void Reach_Height_Facing_AndCommit()
        {
            float3 at = float3.zero;
            Assert.IsTrue(HostileRules.InReach(at, new float3(0f, 0f, 2.29f), 2f, 0.3f, 3f), "range + slack");
            Assert.IsFalse(HostileRules.InReach(at, new float3(0f, 0f, 2.31f), 2f, 0.3f, 3f));
            Assert.IsFalse(HostileRules.InReach(at, new float3(0f, 3.01f, 1f), 2f, 0.3f, 3f), "H07: above what it reaches");
            Assert.IsTrue(HostileRules.Facing(0f, at, new float3(1f, 0f, 1f), 60f), "45 degrees off");
            Assert.IsFalse(HostileRules.Facing(0f, at, new float3(1f, 0f, -0.1f), 60f), "behind the shoulder");
            Assert.AreEqual(100u + 7u, HostileRules.CommitTick(100, 12, 0.6f), "0.6 of a 12-tick windup");
        }

        [Test]
        public void ThreatSwitch_110InReach_130OutOfReach()
        {
            Assert.IsFalse(HostileRules.ShouldSwitch(100f, 110f, true, 1.1f, 1.3f));
            Assert.IsTrue(HostileRules.ShouldSwitch(100f, 110.5f, true, 1.1f, 1.3f));
            Assert.IsFalse(HostileRules.ShouldSwitch(100f, 129f, false, 1.1f, 1.3f));
            Assert.IsTrue(HostileRules.ShouldSwitch(100f, 131f, false, 1.1f, 1.3f));
        }

        [Test]
        public void Leash_IsZoneRadiusPlus15FromTheCentre()
        {
            Assert.IsFalse(HostileRules.BeyondLeash(new float3(34.9f, 0f, 0f), float2.zero, 20f, 15f));
            Assert.IsTrue(HostileRules.BeyondLeash(new float3(35.1f, 0f, 0f), float2.zero, 20f, 15f));
        }

        static HostileRules.ModeResult Next(byte mode, byte behaviour = HostileBehaviourKind.AttacksOnSight, bool saw = false, bool noticed = false,
            bool damaged = false, bool valid = true, bool other = false, bool leash = false, bool gaveUp = false, bool home = false) =>
            HostileRules.NextMode(new HostileRules.ModeInputs
            {
                Mode = mode, Behaviour = behaviour, SawPlayer = saw, NoticeDone = noticed, Damaged = damaged, TargetValid = valid,
                HasOtherThreat = other, BeyondLeash = leash, GaveUp = gaveUp, AtHome = home,
            });

        [Test]
        public void StateTable()
        {
            Assert.AreEqual(HostileMode.Idle, Next(HostileMode.Idle, HostileBehaviourKind.FightsBack, saw: true).Mode, "H02: FightsBack never by sight");
            Assert.AreEqual(HostileMode.Alert, Next(HostileMode.Patrol, saw: true).Mode, "AttacksOnSight notices first");
            Assert.AreEqual(HostileMode.Alert, Next(HostileMode.Alert).Mode);
            Assert.AreEqual(HostileMode.Engage, Next(HostileMode.Alert, noticed: true).Mode, "Q8: after the notice time");
            Assert.AreEqual(HostileMode.Engage, Next(HostileMode.Idle, HostileBehaviourKind.FightsBack, damaged: true).Mode, "a hit engages the same tick, no Alert");
            Assert.AreEqual(HostileMode.Engage, Next(HostileMode.Return, damaged: true).Mode, "hit while going home: turns");

            HostileRules.ModeResult leash = Next(HostileMode.Engage, leash: true, damaged: true);
            Assert.AreEqual(HostileMode.Return, leash.Mode, "H14: past the leash it goes home, even when hit");
            Assert.IsTrue(leash.HealOnHome, "and heals there");
            HostileRules.ModeResult gaveUp = Next(HostileMode.Engage, gaveUp: true);
            Assert.AreEqual(HostileMode.Return, gaveUp.Mode, "H14: Emerald's give-up");
            Assert.IsFalse(gaveUp.HealOnHome, "without a heal");

            Assert.AreEqual(HostileMode.Engage, Next(HostileMode.Engage, valid: false, other: true).Mode, "the next threat");
            Assert.AreEqual(HostileMode.Return, Next(HostileMode.Engage, valid: false).Mode, "nobody left");
            Assert.AreEqual(HostileMode.Idle, Next(HostileMode.Return, home: true).Mode);
            Assert.AreEqual(HostileMode.Dead, HostileRules.NextMode(new HostileRules.ModeInputs { Mode = HostileMode.Engage, Dead = true }).Mode);
        }
    }
}
