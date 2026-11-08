using Kinemation.MotionWarping.Runtime.Core;
using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The leap off a mount (owner, 7-8 Nov 2026): the warp made in code must play and stop (null UnityEvents locked the
    /// ride), and the body must stay in the player's own space - it flew ~11 m / ~63 m when a point was given in the
    /// wrong space, and fell 6.6 m behind a running player while the warp held it on world points.
    /// </summary>
    public sealed class MountLeapWarpTests
    {
        private const string JumpWarpPath = "Assets/_Game/Gameplay/Player/Data/Assets/Warp_Player_DoubleJump.asset";

        [Test]
        public void ALeapWarpMadeInCode_PlaysAndStops_WithoutThrowing()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MotionWarpingAsset>(JumpWarpPath);
            if (asset == null) Assert.Inconclusive("the jump's warp asset is not at " + JumpWarpPath);
            var player = new GameObject("Player");
            var body = new GameObject("Body");
            try
            {
                body.transform.SetParent(player.transform, false);
                MotionWarping warp = MountLeapWarp.Create(player.transform, body.transform, out MountLeapSpace space);
                Assert.IsNotNull(warp);
                Assert.IsNotNull(space, "the warp works in the player's own space");
                Assert.IsNotNull(warp.onWarpStarted, "the events the vendor component invokes are there");
                Assert.IsNotNull(warp.onWarpEnded);
                space.Begin();
                Assert.DoesNotThrow(() => warp.Play(asset, new[]
                {
                    MountLeapWarp.At(space.SpacePoint(Vector3.up * 2f + Vector3.forward), Quaternion.identity),
                    MountLeapWarp.At(space.SpacePoint(Vector3.zero), Quaternion.identity),
                }));
                Assert.IsTrue(warp.IsActive(), "it is warping");
                Assert.DoesNotThrow(() => warp.Stop());
                Assert.IsFalse(warp.IsActive(), "and it ends");
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        /// <summary>
        /// Wherever the root is (far from the origin, turned round) and however far it runs on during the leap, the body
        /// is where the stand-in is in the leap space - relative to the root - never left behind on a world point.
        /// </summary>
        [Test]
        public void TheBody_FollowsTheStandIn_RelativeToTheRoot_FarFromTheOrigin_TurnedRound_AndRunning()
        {
            var player = new GameObject("Player");
            var body = new GameObject("Body");
            try
            {
                player.transform.SetPositionAndRotation(new Vector3(30f, 2f, -60f), Quaternion.Euler(0f, 180f, 0f));
                body.transform.SetParent(player.transform, false);
                body.transform.localPosition = new Vector3(0.01f, -0.5f, -0.37f); // on the seat
                MountLeapWarp.Create(player.transform, body.transform, out MountLeapSpace space);
                space.Begin();

                // The warp lifts the stand-in 1 m over the seat; meanwhile the player runs 40 m on.
                Vector3 apex = space.SpacePoint(body.transform.position + Vector3.up);
                space.Stand.position = apex;
                player.transform.position += player.transform.forward * 40f;
                space.Follow();
                AssertNear(new Vector3(0.01f, 0.5f, -0.37f), body.transform.localPosition, "over the seat, carried on with the root");

                // It lands on the rider's rest under the root.
                Vector3 restLocal = new Vector3(0.01f, -1f, -0.06f);
                space.Stand.position = space.SpacePoint(player.transform.TransformPoint(restLocal));
                space.Follow();
                AssertNear(restLocal, body.transform.localPosition, "on the rest");
                Assert.Less(Quaternion.Angle(body.transform.localRotation, Quaternion.identity), 0.01f, "facing as the root");
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        /// <summary>Owner retest 5: getting up out of the saddle never dips the body below the seat, and lifts it first.</summary>
        [Test]
        public void GettingUp_NeverDipsBelowTheSeat_AndLiftsFirst()
        {
            var player = new GameObject("Player");
            var body = new GameObject("Body");
            try
            {
                body.transform.SetParent(player.transform, false);
                body.transform.localPosition = new Vector3(0f, -0.4f, 0f); // on the seat
                MountLeapWarp.Create(player.transform, body.transform, out MountLeapSpace space);
                space.Begin();
                space.Floor = -0.4f;
                space.Lift = 0.12f;
                space.Stand.position += Vector3.down * 0.2f; // the jump's crouch dips the stand-in 20 cm
                space.Follow();
                Assert.AreEqual(-0.28f, body.transform.localPosition.y, 1e-3f, "held at the seat, lifted on top");
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string what)
        {
            Assert.Less(Vector3.Distance(expected, actual), 0.001f, what + ": expected " + expected + ", got " + actual);
        }
    }
}
