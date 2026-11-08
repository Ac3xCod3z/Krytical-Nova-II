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
                    new WarpPoint { transform = mark.transform, position = mark.transform.position, rotation = Quaternion.identity },
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
    }
}
