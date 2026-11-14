using KryticalNova.Core;
using KryticalNova.Mounts;
using KryticalNova.Mounts.Entities;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Mount Companion's DPS and HEALER stances (day list item 19, phase 3): which Hostile it fights (Autopilot's target logic inside
    /// its short range, also as the Burst job), the PvP fallback to FREE, the strike (owner's roll, share, buff, no threat), the heal pulse,
    /// the strike spot and the run to it, and that the settings asset's numbers are the rules' defaults.
    /// </summary>
    public sealed class CompanionFightTests
    {
        const int Owner = 7;
        static readonly CompanionFightTuning T = CompanionFightTuning.Default;

        static CompanionSeek Seek(int ownerTarget = 0, int current = 0) =>
            new CompanionSeek { OwnerGhostId = Owner, Owner = float3.zero, OwnerTarget = ownerTarget, Current = current, Range = T.Range };

        static HostileSeen H(int id, float x, int after = 0, bool alive = true) =>
            new HostileSeen { GhostId = id, Feet = new float3(x, 0f, 0f), Radius = 0.5f, TargetGhostId = after, Alive = alive };

        static int Pick(in CompanionSeek seek, params HostileSeen[] hostiles)
        {
            using var array = new NativeArray<HostileSeen>(hostiles, Allocator.Temp);
            int at = CompanionFightPick.Pick(seek, array);
            return at < 0 ? 0 : hostiles[at].GhostId;
        }

        [Test]
        public void Pick_TheOwnersTargetComesFirst()
        {
            Assert.AreEqual(20, Pick(Seek(ownerTarget: 20), H(10, 2f, after: Owner), H(20, 9f)));
        }

        [Test]
        public void Pick_ElseTheNearestHostileAfterTheOwner()
        {
            Assert.AreEqual(11, Pick(Seek(), H(10, 9f, after: Owner), H(11, 4f, after: Owner), H(12, 1f, after: 99)));
        }

        [Test]
        public void Pick_KeepsItsFightWhileThatHostileIsStillAfterTheOwner()
        {
            Assert.AreEqual(10, Pick(Seek(current: 10), H(10, 9f, after: Owner), H(11, 2f, after: Owner)), "no flipping to a nearer one");
            Assert.AreEqual(11, Pick(Seek(current: 10), H(10, 9f, after: 99), H(11, 2f, after: Owner)), "it turned on someone else: the next");
        }

        [Test]
        public void Pick_NeverStartsAFightAlone()
        {
            Assert.AreEqual(0, Pick(Seek(), H(10, 2f), H(11, 3f, after: 99)), "no owner target and nobody after the owner: none");
        }

        [Test]
        public void Pick_OnlyLivingHostilesInsideItsRangeOfTheOwner()
        {
            Assert.AreEqual(0, Pick(Seek(ownerTarget: 10), H(10, T.Range + 0.5f, after: Owner)), "too far from the owner");
            Assert.AreEqual(0, Pick(Seek(ownerTarget: 10), H(10, 2f, after: Owner, alive: false)), "dead");
            Assert.AreEqual(10, Pick(Seek(ownerTarget: 10), H(10, T.Range - 0.5f)), "just inside");
        }

        [Test]
        public void PickJob_GivesTheSameAnswerForEveryCompanion()
        {
            HostileSeen[] hostiles = { H(10, 9f, after: Owner), H(11, 4f, after: Owner), H(20, 6f) };
            CompanionSeek[] seeks = { Seek(), Seek(ownerTarget: 20), Seek(current: 10), new CompanionSeek { OwnerGhostId = 3, Range = T.Range } };
            using var h = new NativeArray<HostileSeen>(hostiles, Allocator.TempJob);
            using var s = new NativeArray<CompanionSeek>(seeks, Allocator.TempJob);
            using var picked = new NativeArray<int>(seeks.Length, Allocator.TempJob);
            new CompanionPickJob { Seeks = s, Hostiles = h, Picked = picked }.ScheduleParallel(seeks.Length, 1, default).Complete();
            for (int i = 0; i < seeks.Length; i++) Assert.AreEqual(CompanionFightPick.Pick(seeks[i], h), picked[i], "companion " + i);
            Assert.AreEqual(1, picked[0]);
            Assert.AreEqual(2, picked[1]);
            Assert.AreEqual(0, picked[2]);
            Assert.AreEqual(-1, picked[3]);
        }

        [Test]
        public void Stances_AreBuiltExceptTank_AndTheCombatOnesActAsFreeInPvp()
        {
            Assert.IsTrue(CompanionFightRules.IsBuilt(CompanionStance.Free));
            Assert.IsTrue(CompanionFightRules.IsBuilt(CompanionStance.Dps));
            Assert.IsTrue(CompanionFightRules.IsBuilt(CompanionStance.Healer));
            Assert.IsFalse(CompanionFightRules.IsBuilt(CompanionStance.Tank), "phase 4");
            Assert.AreEqual(CompanionStance.Dps, CompanionFightRules.Effective(CompanionStance.Dps, false));
            Assert.AreEqual(CompanionStance.Free, CompanionFightRules.Effective(CompanionStance.Dps, true));
            Assert.AreEqual(CompanionStance.Free, CompanionFightRules.Effective(CompanionStance.Healer, true));
            Assert.AreEqual(CompanionStance.Free, CompanionFightRules.Effective(CompanionStance.Free, true), "FREE is the only PvP stance");
        }

        [Test]
        public void Strike_IsAShareOfTheOwnersRollWithItsBuff_AndAddsNoThreat()
        {
            var roll = new DamageResult
            {
                DamageDealt = 100f, RawPhysicalDamage = 120f, MitigatedPhysicalDamage = 90f, NetElementalDamage = 10f, IsCritical = true,
                IsReflected = true, ReflectedDamage = 30f, ShieldShattered = true,
            };
            DamageResult r = CompanionFightRules.Strike(roll, T);
            float k = T.DamageShare * (1f + T.DamageBuff);
            Assert.AreEqual(100f * k, r.DamageDealt, 1e-4f);
            Assert.AreEqual(90f * k, r.MitigatedPhysicalDamage, 1e-4f);
            Assert.AreEqual(10f * k, r.NetElementalDamage, 1e-4f);
            Assert.IsTrue(r.IsCritical, "a critical roll stays critical");
            Assert.IsTrue(r.NoThreat, "never draws aggro");
            Assert.IsFalse(r.IsReflected, "a reflect never hurts the owner for the mount's strike");
            Assert.AreEqual(0f, r.ReflectedDamage);
            Assert.IsFalse(r.ShieldShattered);
            Assert.IsTrue(CompanionFightRules.Strike(new DamageResult { IsEvaded = true }, T).IsEvaded, "a miss stays a miss");
        }

        [Test]
        public void Healer_HealsTheHurtOwnerInAFight_AShareOfTheirMax()
        {
            Assert.AreEqual(1000f * T.HealShare, CompanionFightRules.PulseHeal(1000f, T), 1e-4f);
            Assert.IsTrue(CompanionFightRules.ShouldHeal(true, true, 500f, 1000f, T));
            Assert.IsFalse(CompanionFightRules.ShouldHeal(false, true, 500f, 1000f, T), "out of a fight: no (the default)");
            Assert.IsFalse(CompanionFightRules.ShouldHeal(true, true, 1000f, 1000f, T), "full");
            Assert.IsFalse(CompanionFightRules.ShouldHeal(true, false, 0f, 1000f, T), "defeated");
            CompanionFightTuning always = T;
            always.HealOutOfFight = true;
            Assert.IsTrue(CompanionFightRules.ShouldHeal(false, true, 500f, 1000f, always));
        }

        [Test]
        public void StrikeSpot_IsOnItsOwnSideInsideItsReach_AndTheRunGetsThere()
        {
            var hostile = new Vector3(10f, 0f, 0f);
            Vector3 spot = CompanionFightRules.StrikeSpot(Vector3.zero, hostile, 1f, T);
            Assert.Less(spot.x, hostile.x, "on its own side");
            Assert.IsTrue(CompanionFightRules.InReach(spot, hostile, 1f, T), "inside its reach from the spot");
            Assert.IsFalse(CompanionFightRules.InReach(Vector3.zero, hostile, 1f, T), "not from where it starts");

            var follow = new CompanionFollower();
            follow.Place(Vector3.zero, 0f);
            for (int i = 0; i < 40; i++) follow.StepToward(spot, hostile, 10f, 0.1f);
            Assert.Less(Vector3.Distance(follow.Position, spot), 0.01f, "there");
            Assert.AreEqual(90f, follow.Yaw, 1f, "faces the Hostile once there");
            Assert.AreEqual(0, follow.TrailCount, "the owner's path is forgotten while it fights");
        }

        [Test]
        public void TheSettingsAssetsNumbers_AreTheRulesDefaults()
        {
            var settings = ScriptableObject.CreateInstance<MountSettings>();
            try
            {
                CompanionFightTuning a = settings.CompanionFight, d = CompanionFightTuning.Default;
                Assert.AreEqual(d.Range, a.Range);
                Assert.AreEqual(d.LookSeconds, a.LookSeconds);
                Assert.AreEqual(d.StrikeSeconds, a.StrikeSeconds);
                Assert.AreEqual(d.Reach, a.Reach);
                Assert.AreEqual(d.DamageShare, a.DamageShare);
                Assert.AreEqual(d.DamageBuff, a.DamageBuff);
                Assert.AreEqual(d.PulseSeconds, a.PulseSeconds);
                Assert.AreEqual(d.HealShare, a.HealShare);
                Assert.AreEqual(d.HealOutOfFight, a.HealOutOfFight);
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}
