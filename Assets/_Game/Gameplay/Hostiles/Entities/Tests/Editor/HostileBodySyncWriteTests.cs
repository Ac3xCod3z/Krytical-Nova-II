using KryticalNova.Player.Entities;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 8: <see cref="HostileBodySyncServerSystem"/> writes a mob's CombatTarget only when it changed.
    /// <see cref="HostileBodySyncServerSystem.Refresh"/> builds exactly the value the old code wrote every tick, and says "changed" only
    /// when that value differs from what the ghost holds.
    /// </summary>
    public sealed class HostileBodySyncWriteTests
    {
        // The old per-tick build, verbatim.
        static CombatTarget Old(CombatTarget target, float3 position, bool hasEntity, float hp, float maxHp, int r, int h)
        {
            target.FeetMm = HostileBodyLinkServerSystem.FeetMm(position);
            target.HealthMilli = hasEntity ? Mathf.RoundToInt(hp * 1000f) : 0;
            target.MaxHealthMilli = hasEntity ? Mathf.RoundToInt(maxHp * 1000f) : 0;
            target.RadiusMm = r;
            target.HeightMm = h;
            return target;
        }

        static CombatTarget Linked(float3 at) => new CombatTarget { Kind = CombatTargetKinds.Hostile, FeetMm = HostileBodyLinkServerSystem.FeetMm(at) };

        [Test]
        public void AMovingAndAStandingMob_HoldTheSameValues_AsTheOldEveryTickWrite()
        {
            float3 standAt = new float3(12.3456f, 1.2f, -7.89f);
            CombatTarget standOld = Linked(standAt), standNew = standOld;
            CombatTarget moveOld = Linked(float3.zero), moveNew = moveOld;
            int standWrites = 0, moveWrites = 0;
            for (int tick = 0; tick < 30; tick++)
            {
                float3 moveAt = new float3(tick * 0.137f, 0f, tick * -0.051f);
                float hp = 500f - tick * 3.3f;   // the moving mob is being hit
                standOld = Old(standOld, standAt, true, 420f, 420f, 700, 1800);
                if (HostileBodySyncServerSystem.Refresh(ref standNew, standAt, true, 420f, 420f, 700, 1800)) standWrites++;
                moveOld = Old(moveOld, moveAt, true, hp, 500f, 900, 2200);
                if (HostileBodySyncServerSystem.Refresh(ref moveNew, moveAt, true, hp, 500f, 900, 2200)) moveWrites++;
                Assert.AreEqual(standOld, standNew, $"standing, tick {tick}");
                Assert.AreEqual(moveOld, moveNew, $"moving, tick {tick}");
            }
            Assert.AreEqual(1, standWrites, "the standing mob is written once (its first HP and size), then never again");
            Assert.AreEqual(30, moveWrites, "the moving, hit mob is written every tick, as before");
        }

        [Test]
        public void ABodyWithNoCombatEntity_ReadsZeroHp_AsBefore()
        {
            CombatTarget t = Linked(float3.zero);
            t.HealthMilli = 5000; t.MaxHealthMilli = 5000;
            Assert.IsTrue(HostileBodySyncServerSystem.Refresh(ref t, float3.zero, false, 99f, 99f, 500, 2000));
            Assert.AreEqual(0, t.HealthMilli);
            Assert.AreEqual(0, t.MaxHealthMilli);
            Assert.AreEqual(CombatTargetKinds.Hostile, t.Kind, "the kind set at link time is kept");
        }

        [Test]
        public void HundredStandingMobs_Over30Ticks_AreWrittenOnlyOnTheirFirstTick()
        {
            const int mobs = 100, ticks = 30;
            var targets = new CombatTarget[mobs];
            for (int m = 0; m < mobs; m++) targets[m] = Linked(new float3(m, 0f, -m));
            int writes = 0;
            for (int tick = 0; tick < ticks; tick++)
                for (int m = 0; m < mobs; m++)
                    if (HostileBodySyncServerSystem.Refresh(ref targets[m], new float3(m, 0f, -m), true, 300f, 300f, 600, 1900)) writes++;
            Debug.Log($"[perf chunk 8] CombatTarget writes, {mobs} standing mobs over {ticks} ticks: old {mobs * ticks}, new {writes} " +
                $"(the first tick fills HP and size; every later tick 0)");
            Assert.AreEqual(mobs, writes);
        }
    }
}
