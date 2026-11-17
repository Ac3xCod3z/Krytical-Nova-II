using System.Collections.Generic;
using KryticalNova.Crossfire;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Crossfire.Tests
{
    /// <summary>
    /// The dead of a war get up in their own yard (owner; crossfire-plan "the dead come back in their own yard"): the spread of the spots,
    /// and every spot inside the yard's box, so a death never takes a player out of the war.
    /// </summary>
    public sealed class CrossfireRespawnTests
    {
        const float Spacing = 1.2f;
        const int Spots = 40;

        [Test]
        public void TheFirst_GetsUpOnTheSpotItself_AndTheSpreadStartsAgainAfterItsSpots()
        {
            Assert.AreEqual(Vector3.zero, CrossfireRules.RespawnSpread(0, Spacing, Spots));
            Assert.AreEqual(Vector3.zero, CrossfireRules.RespawnSpread(Spots, Spacing, Spots));
            Assert.AreEqual(CrossfireRules.RespawnSpread(3, Spacing, Spots), CrossfireRules.RespawnSpread(Spots + 3, Spacing, Spots));
        }

        [Test]
        public void EverySpot_IsFlat_ApartFromTheOthers_AndWithinTheClearLanding()
        {
            var seen = new List<Vector3>();
            for (int i = 0; i < Spots; i++)
            {
                Vector3 p = CrossfireRules.RespawnSpread(i, Spacing, Spots);
                Assert.AreEqual(0f, p.y, "spot " + i + " is on the ground");
                Assert.LessOrEqual(p.magnitude, 7.6f, "spot " + i + " is inside the yard's 7 m clear landing (with a little slack)");
                foreach (Vector3 q in seen) Assert.Greater(Vector3.Distance(p, q), 0.9f, "spot " + i + " stands apart from the others");
                seen.Add(p);
            }
        }

        [Test]
        public void TheYard_PutsItsDeadOnItsRespawnSpot_OrItsLanding_AlwaysInsideTheBox()
        {
            var go = new GameObject("test yard");
            try
            {
                go.transform.position = new Vector3(139f, 6f, 0f);
                CrossfireArea yard = go.AddComponent<CrossfireArea>();
                yard.size = new Vector3(42f, 14f, 80f);
                var landing = new GameObject("landing").transform;
                landing.SetParent(go.transform, false);
                landing.position = new Vector3(134f, 0f, 0f);
                landing.rotation = Quaternion.Euler(0f, 270f, 0f);
                yard.landing = landing;

                yard.RespawnSpot(0, Spacing, Spots, out Vector3 at, out float yaw);
                Assert.AreEqual(landing.position, at, "no respawn spot: the landing");
                Assert.AreEqual(270f, yaw, 0.01f);

                var shrine = new GameObject("respawn").transform;
                shrine.SetParent(go.transform, false);
                shrine.position = new Vector3(152f, 0.9f, 0f);
                yard.respawn = shrine;
                for (int i = 0; i < Spots; i++)
                {
                    yard.RespawnSpot(i, Spacing, Spots, out at, out _);
                    Assert.IsTrue(yard.Contains(at), "respawn " + i + " stays in the yard (in the war)");
                }

                // a spot at the box's very edge: no spread rather than a place outside the war
                shrine.position = new Vector3(159.9f, 0.9f, 0f);
                for (int i = 0; i < Spots; i++)
                {
                    yard.RespawnSpot(i, Spacing, Spots, out at, out _);
                    Assert.IsTrue(yard.Contains(at), "edge respawn " + i + " stays in the yard");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void WithStands_TheDeadTakeTheShrineStandsInTurn_FacingAsTheStandDoes_NeverOnTheShrine_AlwaysInsideTheBox()
        {
            var go = new GameObject("test yard");
            try
            {
                go.transform.position = new Vector3(139f, 6f, 0f);
                CrossfireArea yard = go.AddComponent<CrossfireArea>();
                yard.size = new Vector3(42f, 14f, 80f);
                var shrine = new GameObject("shrine").transform;
                shrine.SetParent(go.transform, false);
                shrine.position = new Vector3(152f, 0.9f, 0f);
                yard.respawn = shrine;
                // The Crucible's four stands round the shrine (Front, Left, Rear, Right), facing the gate; one empty slot is skipped
                Vector3[] at4 = { new Vector3(146.5f, 0.9f, 0f), new Vector3(152f, 0.9f, 5.5f), new Vector3(157.5f, 0.9f, 0f), new Vector3(152f, 0.9f, -5.5f) };
                var stands = new Transform[5];
                for (int i = 0; i < 4; i++)
                {
                    stands[i < 2 ? i : i + 1] = new GameObject("stand " + i).transform;
                    stands[i < 2 ? i : i + 1].SetParent(go.transform, false);
                    stands[i < 2 ? i : i + 1].position = at4[i];
                    stands[i < 2 ? i : i + 1].rotation = Quaternion.Euler(0f, 270f, 0f);
                }
                yard.respawnStands = stands;

                for (int i = 0; i < 4; i++)
                {
                    yard.RespawnSpot(i, Spacing, Spots, out Vector3 first, out float yaw);
                    Assert.AreEqual(at4[i], first, "dead " + i + " gets up on stand " + i + " itself");
                    Assert.AreEqual(270f, yaw, 0.01f, "facing as the stand does");
                }
                var seen = new List<Vector3>();
                for (int i = 0; i < Spots; i++)
                {
                    yard.RespawnSpot(i, Spacing, Spots, out Vector3 p, out _);
                    Assert.IsTrue(yard.Contains(p), "respawn " + i + " stays in the yard (in the war)");
                    Assert.Greater(Vector3.Distance(p, shrine.position), 1.5f, "respawn " + i + " is never on the shrine");
                    foreach (Vector3 q in seen) Assert.Greater(Vector3.Distance(p, q), 0.9f, "respawn " + i + " stands apart from the others");
                    seen.Add(p);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
