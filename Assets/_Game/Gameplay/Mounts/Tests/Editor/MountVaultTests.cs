using KryticalNova.Mounts;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The mounted vault (owner, 13 Nov 2026, day list 15.2): a vault keeps the rider mounted on the server, the mount dissolves as
    /// the feet leave it and re-forms under the rider before the touchdown, and the rider lands back in the saddle.
    /// </summary>
    public sealed class MountVaultTests
    {
        [Test]
        public void Vault_KeepsTheRiderOn()
        {
            Assert.IsFalse(MountRules.AirborneTakesRiderOff(inJumpingMove: true, inVault: true), "a vault keeps the rider mounted");
            Assert.IsFalse(MountRules.AirborneTakesRiderOff(inJumpingMove: false, inVault: false), "on the ground nothing changes");
        }

        [Test]
        public void JumpOrWall_StillTakesTheRiderOff()
        {
            Assert.IsTrue(MountRules.AirborneTakesRiderOff(inJumpingMove: true, inVault: false), "a jump's flight or a wall takes the rider off");
        }

        [Test]
        public void SeatLift_LeavesTheSeatThenLandsInIt()
        {
            const float leave = 0.2f, land = 0.3f;
            Assert.AreEqual(1f, MountRules.VaultSeatLift(0f, leave, land), 1e-4f, "starts on the seat");
            Assert.AreEqual(0f, MountRules.VaultSeatLift(leave, leave, land), 1e-4f, "off the seat once the feet have left");
            Assert.AreEqual(0f, MountRules.VaultSeatLift(0.5f, leave, land), 1e-4f, "over the obstacle in the ground pose");
            Assert.AreEqual(0f, MountRules.VaultSeatLift(1f - land, leave, land), 1e-4f, "the settle begins as the mount re-forms");
            Assert.AreEqual(1f, MountRules.VaultSeatLift(1f, leave, land), 1e-4f, "lands in the saddle");
            Assert.AreEqual(1f, MountRules.VaultSeatLift(1.4f, leave, land), 1e-4f, "stays in the saddle after the touchdown");
        }

        [Test]
        public void SeatLift_NeverJumps()
        {
            const float leave = 0.2f, land = 0.3f;
            float last = MountRules.VaultSeatLift(0f, leave, land);
            for (int i = 1; i <= 200; i++)
            {
                float now = MountRules.VaultSeatLift(i / 200f, leave, land);
                Assert.LessOrEqual(Mathf.Abs(now - last), 0.06f, $"a smooth lift at t={i / 200f:0.000}");
                last = now;
            }
        }

        [Test]
        public void Settings_DefaultToTheFlipInTheSaddle()
        {
            var settings = ScriptableObject.CreateInstance<MountSettings>();
            try
            {
                Assert.IsTrue(settings.vaultMounted);
                Assert.AreEqual(2, settings.vaultRiderHeightClass, "the tall style: the front flip");
                Assert.Less(settings.vaultLeaveShare + settings.vaultLandShare, 1f, "the dissolve and the re-forming never overlap");
            }
            finally { Object.DestroyImmediate(settings); }
        }

        // Owner, 13 Nov 2026 (#253 v1): the mount dissolved for the flip and never came back. A finished vanish hides the
        // renderers and the dissolve only drives _Spawn, so the forming again has to show them.
        [Test]
        public void Reform_AfterAFinishedVanish_ShowsTheMountAgain()
        {
            var look = ScriptableObject.CreateInstance<MountMaterialiseSettings>();
            look.material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var mount = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bool ignored = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // Destroy of the dissolve copies is refused in edit mode; harmless here
            try
            {
                var materialiser = mount.AddComponent<MountMaterialiser>();
                var renderer = mount.GetComponent<Renderer>();
                materialiser.Vanish(look, 0f, 0.1f, false);
                typeof(MountMaterialiser).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(materialiser, null);
                Assert.IsTrue(materialiser.IsGone, "the vanish has finished");
                Assert.IsFalse(renderer.enabled, "a vanished mount is hidden");

                materialiser.Appear(look, 0f, 0.3f);
                Assert.IsFalse(materialiser.IsGone);
                Assert.IsTrue(renderer.enabled, "the re-forming mount is drawn (its dissolve shows it growing back)");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignored;
                Object.DestroyImmediate(mount);
                Object.DestroyImmediate(look.material);
                Object.DestroyImmediate(look);
            }
        }
    }
}
