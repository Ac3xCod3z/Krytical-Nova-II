using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>
    /// M5 seam 2: the guard of a Hostile's strike as NGO's HostileBrainLink had it (now HostileStrikeRules, used by it): a player on a wall
    /// (up > 30 degrees) is reached only within the attack range + 0.5 m + the 1.2 m eye height, in a clear line.
    /// </summary>
    public sealed class HostileStrikeRulesTests
    {
        GameObject m_Attacker, m_Target;

        [SetUp]
        public void SetUp()
        {
            m_Attacker = new GameObject("strike attacker");
            m_Target = new GameObject("strike target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Attacker);
            Object.DestroyImmediate(m_Target);
        }

        [Test]
        public void AWall_IsAnUpTiltedOver30Degrees()
        {
            m_Target.transform.rotation = Quaternion.Euler(29f, 0f, 0f);
            Assert.IsFalse(HostileStrikeRules.OnAWall(m_Target.transform));
            m_Target.transform.rotation = Quaternion.Euler(31f, 0f, 0f);
            Assert.IsTrue(HostileStrikeRules.OnAWall(m_Target.transform));
        }

        [Test]
        public void OnAWall_ReachIsTheRangePlusSlackPlusEyeHeight_InAClearLine()
        {
            const float range = 2f;
            float limit = range + HostileStrikeRules.WallReachSlack + HostileStrikeRules.EyeHeight;
            m_Attacker.transform.position = Vector3.zero;
            Vector3 eyes = Vector3.up * HostileStrikeRules.EyeHeight;
            m_Target.transform.position = eyes + Vector3.forward * (limit - 0.05f);
            Assert.IsTrue(HostileStrikeRules.ReachesOnWall(m_Attacker.transform, m_Target.transform, range), "just within");
            m_Target.transform.position = eyes + Vector3.forward * (limit + 0.05f);
            Assert.IsFalse(HostileStrikeRules.ReachesOnWall(m_Attacker.transform, m_Target.transform, range), "just beyond");

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = eyes + Vector3.forward * 1.5f;
                m_Target.transform.position = eyes + Vector3.forward * 3f;
                Physics.SyncTransforms();
                Assert.IsFalse(HostileStrikeRules.ReachesOnWall(m_Attacker.transform, m_Target.transform, range), "something in the way");
            }
            finally { Object.DestroyImmediate(wall); }
        }

        [Test]
        public void NothingLands_WithoutBothSides()
        {
            Assert.IsFalse(HostileStrikeRules.TryRoll(null, null, 0, null, m_Attacker.transform, out _));
        }
    }
}
