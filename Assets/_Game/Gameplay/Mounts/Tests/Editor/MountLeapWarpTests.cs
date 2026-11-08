using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Owner, 7 Nov 2026 (retest 3): the leap's warp component, made in code, threw on Play (null UnityEvents), kept
    /// "running" and threw on every Stop: the body flew off, the roll never came and the ride locked.
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
            var mark = new GameObject("Landing");
            try
            {
                body.transform.SetParent(player.transform, false);
                mark.transform.SetParent(player.transform, false);
                MotionWarping warp = MountLeapWarp.Create(player.transform, body.transform);
                Assert.IsNotNull(warp);
                Assert.IsNotNull(warp.onWarpStarted, "the events the vendor component invokes are there");
                Assert.IsNotNull(warp.onWarpEnded);
                Assert.DoesNotThrow(() => warp.Play(asset, new[]
                {
                    new WarpPoint { position = Vector3.up * 2f + Vector3.forward, rotation = Quaternion.identity },
                    MountLeapWarp.LandingPoint(mark.transform),
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
        /// Owner, 7 Nov 2026 (leap proof run): the body flew ~11 m (standing) / ~63 m (running) and snapped back - the
        /// landing point was given in world space where the vendor reads it in the mark's own space. The landing must
        /// be the mark itself wherever the player stands and however it faces, and follow it as the player runs.
        /// </summary>
        [Test]
        public void TheLandingPoint_IsTheMark_FarFromTheOrigin_TurnedRound_AndRunning()
        {
            var player = new GameObject("Player");
            var mark = new GameObject("Landing");
            try
            {
                player.transform.SetPositionAndRotation(new Vector3(30f, 2f, -60f), Quaternion.Euler(0f, 180f, 0f));
                mark.transform.SetParent(player.transform, false);
                mark.transform.localPosition = new Vector3(0.01f, -1f, -0.06f);
                mark.transform.localRotation = Quaternion.Euler(0f, 5f, 0f);

                WarpPoint landing = MountLeapWarp.LandingPoint(mark.transform);
                AssertNear(mark.transform.position, landing.GetPosition(), "lands on the mark");
                Assert.Less(Quaternion.Angle(mark.transform.rotation, landing.GetRotation()), 0.01f, "faces as the mark");

                player.transform.position += new Vector3(0f, 0f, -40f); // ran on
                AssertNear(mark.transform.position, landing.GetPosition(), "and follows it");
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
