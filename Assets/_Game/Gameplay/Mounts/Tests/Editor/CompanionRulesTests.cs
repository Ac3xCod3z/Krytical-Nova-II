using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The Mount Companion's pure rules (day list item 19, plan evals: follow math, hunger rates, HP, knock-out, Stim).</summary>
    public sealed class CompanionRulesTests
    {
        static readonly CompanionTuning T = CompanionTuning.Default;
        const float Dt = 0.1f;

        [Test]
        public void RestSpot_IsBehindAndToTheLeft()
        {
            Vector3 spot = CompanionRules.RestSpot(Vector3.zero, 0f, T);
            Assert.AreEqual(-1.5f, spot.x, 1e-4f, "left of an owner facing +Z");
            Assert.AreEqual(-2.5f, spot.z, 1e-4f, "behind");
            Vector3 turned = CompanionRules.RestSpot(Vector3.zero, 90f, T);   // facing +X: behind is -X, left is +Z
            Assert.AreEqual(-2.5f, turned.x, 1e-4f);
            Assert.AreEqual(1.5f, turned.z, 1e-4f);
        }

        [Test]
        public void Follow_KeepsUpWithAWalkingOwner_AndNeverComesCloserThanTheGap()
        {
            var f = new CompanionFollower();
            f.Place(CompanionRules.RestSpot(Vector3.zero, 0f, T), 0f);
            Vector3 owner = Vector3.zero;
            float minGap = float.MaxValue, maxGap = 0f;
            for (int i = 0; i < 80; i++)   // 8 s at 5 m/s
            {
                owner += Vector3.forward * 5f * Dt;
                Assert.AreEqual(CompanionFollower.Result.Moved, f.Step(owner, 0f, 5f, 20f, Dt, i * Dt, T), "no warp while walking");
                float gap = Flat(f.Position, owner);
                minGap = Mathf.Min(minGap, gap);
                maxGap = Mathf.Max(maxGap, gap);
            }
            Assert.GreaterOrEqual(minGap, T.MinGap - 1e-3f, "never closer than the gap");
            Assert.Less(maxGap, T.CatchUpDistance + 3f, "keeps up");
            // the owner stops: it settles on its spot
            for (int i = 0; i < 60; i++) f.Step(owner, 0f, 0f, 20f, Dt, 8f + i * Dt, T);
            Assert.Less(Flat(f.Position, CompanionRules.RestSpot(owner, 0f, T)), 0.2f, "rests behind-left");
            Assert.AreEqual(0f, f.Speed, 1e-3f);
        }

        [Test]
        public void Follow_CatchesUpFasterWhenFarBehind()
        {
            var f = new CompanionFollower();
            Vector3 owner = Vector3.zero;
            f.Place(CompanionRules.RestSpot(owner, 0f, T) + Vector3.back * 12f, 0f);
            f.Step(owner, 0f, 0f, 20f, Dt, 0f, T);
            Assert.IsTrue(f.CatchingUp, "more than 8 m behind its spot");
            Assert.Greater(f.Speed, T.WalkSpeed * 1.2f, "25 % faster");
            for (int i = 1; i < 60; i++) f.Step(owner, 0f, 0f, 20f, Dt, i * Dt, T);
            Assert.IsFalse(f.CatchingUp, "within 3 m it stops hurrying");
        }

        [Test]
        public void Follow_WarpsWhenTooFar_AfterATeleport_AndAtMostOnceInTheCooldown()
        {
            var f = new CompanionFollower();
            Vector3 owner = Vector3.zero;
            f.Place(owner + Vector3.back * 31f, 0f);
            Assert.AreEqual(CompanionFollower.Result.Warped, f.Step(owner, 0f, 0f, 20f, Dt, 0f, T), "more than 30 m away");
            Assert.Less(Flat(f.Position, CompanionRules.RestSpot(owner, 0f, T)), 1e-3f, "formed at its spot");

            owner += Vector3.forward * 12f;   // a portal: 12 m in one step
            Assert.AreEqual(CompanionFollower.Result.Moved, f.Step(owner, 0f, 0f, 20f, Dt, 1f, T), "one warp per 2 s");
            Assert.AreEqual(CompanionFollower.Result.Warped, f.Step(owner, 0f, 0f, 20f, Dt, 2.1f, T), "the teleport warps it once the cooldown is over");
            Assert.AreEqual(2, f.Warps);
        }

        [Test]
        public void Hunger_DrainsTenTimesFasterOutThanRidden()
        {
            var ridden = new MountTuning(100_000f, 0.04f, 0.05f);
            var outside = new MountTuning(100_000f, 0.04f, 0.5f);
            double c1 = 0, c2 = 0;
            MountState r = MountRules.Drain(default, 60.0, ridden, ref c1);
            MountState o = MountRules.Drain(default, 60.0, outside, ref c2);
            Assert.AreEqual(50u, r.HungerDeficit, "0.05 % a minute ridden");
            Assert.AreEqual(500u, o.HungerDeficit, "0.5 % a minute out: a full bar lasts 200 min");
        }

        [Test]
        public void MaxHp_ScalesWithTheOwnersHpAndGrowth()
        {
            Assert.AreEqual(600f, CompanionRules.MaxHp(1000f, 0, T), 1e-3f);
            Assert.AreEqual(800f, CompanionRules.MaxHp(1000f, 50, T), 1e-3f);
            Assert.AreEqual(1000f, CompanionRules.MaxHp(1000f, 100, T), 1e-3f);
        }

        [Test]
        public void Regen_OnlyAtHome_ByTheClock_AndNotBeforeTheRestEnds()
        {
            Assert.AreEqual(300, CompanionRules.LostNow(500, 0, 600, true, T), "10 min at home x 2 % = 20 %");
            Assert.AreEqual(500, CompanionRules.LostNow(500, 0, 600, false, T), "nothing comes back while out");
            Assert.AreEqual(500, CompanionRules.LostNow(500, 1000, 600, true, T), "nothing before hpAt (a rest)");
            Assert.AreEqual(0, CompanionRules.LostNow(500, 0, 100_000, true, T));
            int lost = 500; long at = 0;
            CompanionRules.Settle(ref lost, ref at, 600, true, T);
            Assert.AreEqual(300, lost);
            Assert.AreEqual(600, at);
        }

        [Test]
        public void Hurt_KnocksOutAtZero_ThenRestsAndComesBackAt30Percent()
        {
            int lost = 0;
            Assert.IsFalse(CompanionRules.Hurt(ref lost, 300f, 600f));
            Assert.AreEqual(500, lost, "300 of 600 HP");
            Assert.IsTrue(CompanionRules.Hurt(ref lost, 400f, 600f), "past 0 HP");
            Assert.AreEqual(1000, lost);
            CompanionRules.KnockOut(10_000, T, out lost, out long hpAt, out long restUntil);
            Assert.AreEqual(10_300, restUntil, "rests 300 s");
            Assert.AreEqual(300, CompanionRules.RestLeft(restUntil, 10_000));
            Assert.AreEqual(0, CompanionRules.RestLeft(restUntil, 10_301));
            Assert.AreEqual(700, CompanionRules.LostNow(lost, hpAt, 10_300, true, T), "back at 30 %");
            Assert.AreEqual("5:00", CompanionRules.Clock(300));
        }

        [Test]
        public void Stim_Heals40Percent_NeverBelowZero()
        {
            Assert.AreEqual(300, CompanionRules.StimHealed(700, T));
            Assert.AreEqual(0, CompanionRules.StimHealed(200, T));
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
