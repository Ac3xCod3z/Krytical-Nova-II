using System.Collections.Generic;
using KryticalNova.Player;
using KryticalNova.UI;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Settings window's working parts that need no scene: the ledger behind APPLY and REVERT, and what the
    /// camera options do to the rig's numbers. Nothing here reads or writes the player's stored options.
    /// </summary>
    public sealed class SettingsTests
    {
        // ---------------------------------------------------------------- upscalers

        [Test]
        public void TheFirstTwoRtxGenerations_KeepTheOlderDlssModel()
        {
            Assert.IsTrue(GraphicsSettingsApplier.OlderRtx("NVIDIA GeForce RTX 2080 SUPER"));
            Assert.IsTrue(GraphicsSettingsApplier.OlderRtx("NVIDIA GeForce RTX 3060"));
            Assert.IsFalse(GraphicsSettingsApplier.OlderRtx("NVIDIA GeForce RTX 4060 Ti"));
            Assert.IsFalse(GraphicsSettingsApplier.OlderRtx("NVIDIA GeForce RTX 5070 Ti"));
            Assert.IsFalse(GraphicsSettingsApplier.OlderRtx(null));
        }

        // ---------------------------------------------------------------- APPLY and REVERT

        [Test]
        public void Revert_PutsBackWhatWasThereAtTheLastMark_AndShowsIt()
        {
            float volume = 0.8f;
            bool shake = true;
            int shown = 0;
            var ledger = new SettingsLedger();
            ledger.Add(() => volume, value => volume = value, () => shown++);
            ledger.Add(() => shake, value => shake = value, () => shown++);
            Assert.IsFalse(ledger.HasChanges);

            volume = 0.2f;
            shake = false;
            Assert.IsTrue(ledger.HasChanges);
            ledger.Revert();

            Assert.AreEqual(0.8f, volume);
            Assert.IsTrue(shake);
            Assert.IsFalse(ledger.HasChanges);
            Assert.AreEqual(2, shown, "each row is asked to show its value again");
        }

        [Test]
        public void Apply_KeepsTheChange_SoRevertHasNothingToPutBack()
        {
            float volume = 0.8f;
            var ledger = new SettingsLedger();
            ledger.Add(() => volume, value => volume = value, null);
            volume = 0.2f;
            ledger.Mark();
            Assert.IsFalse(ledger.HasChanges);
            ledger.Revert();
            Assert.AreEqual(0.2f, volume);
        }

        [Test]
        public void Revert_SetsRightASettingThatAnotherOneMoved()
        {
            // A quality preset writes the details under it: putting the preset back first would overwrite the
            // detail the player had set by hand.
            int preset = 3;        // 3 = "custom"
            float scale = 0.7f;
            var ledger = new SettingsLedger();
            ledger.Add(() => scale, value => scale = value, null);
            ledger.Add(() => preset, value => { preset = value; if (value < 3) scale = 1f; }, null);

            preset = 1;
            scale = 1f;
            ledger.Revert();

            Assert.AreEqual(3, preset);
            Assert.AreEqual(0.7f, scale);
        }

        [Test]
        public void EveryPageOfTheWindow_HasItsTabAndItsPage()
        {
            var tree = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/_Game/UI/Settings/SettingsWindow.uxml");
            Assert.IsNotNull(tree);
            UnityEngine.UIElements.VisualElement root = tree.Instantiate();
            var seen = new HashSet<string>();
            foreach (string page in SettingsPages.Pages)
            {
                Assert.IsTrue(seen.Add(page), page + " is listed twice");
                Assert.IsNotNull(UnityEngine.UIElements.UQueryExtensions.Q(root, "settings-tab-" + page), "no tab for " + page);
                Assert.IsNotNull(UnityEngine.UIElements.UQueryExtensions.Q(root, "settings-page-" + page), "no page for " + page);
            }
        }

        // ---------------------------------------------------------------- the camera

        [Test]
        public void TheCameraTurnsAsFastAsThePlayerAsks()
        {
            var mouse = new Vector2(10f, 4f);
            Vector2 shipped = CameraPreferences.Turn(mouse, 0.2f, false, 1f, false);
            Vector2 doubled = CameraPreferences.Turn(mouse, 0.2f, false, 2f, false);
            Assert.AreEqual(2f, shipped.x, 1e-5f, "at 100% the rig's own speed");
            Assert.AreEqual(shipped.x * 2f, doubled.x, 1e-5f);
            Assert.AreEqual(shipped.y * 2f, doubled.y, 1e-5f);
        }

        [Test]
        public void InvertingTheCamera_TurnsUpAndDownRound_AndNothingElse()
        {
            var mouse = new Vector2(10f, 4f);
            Vector2 plain = CameraPreferences.Turn(mouse, 1f, false, 1f, false);
            Vector2 inverted = CameraPreferences.Turn(mouse, 1f, false, 1f, true);
            Assert.AreEqual(-4f, plain.y, 1e-5f, "as shipped, the mouse up tilts the camera down");
            Assert.AreEqual(4f, inverted.y, 1e-5f);
            Assert.AreEqual(plain.x, inverted.x, 1e-5f);
            // A rig that is already inverted and a player who inverts it come back to the plain way.
            Assert.AreEqual(plain.y, CameraPreferences.Turn(mouse, 1f, true, 1f, true).y, 1e-5f);
        }

        [Test]
        public void TheFurthestCameraDistance_StaysBetweenTheRigsNearestAndFurthest()
        {
            Assert.AreEqual(24f, CameraPreferences.FurthestDistance(3f, 24f, 1f), 1e-5f, "all of it as shipped");
            Assert.AreEqual(12f, CameraPreferences.FurthestDistance(3f, 24f, 0.5f), 1e-5f);
            Assert.AreEqual(24f, CameraPreferences.FurthestDistance(3f, 24f, 5f), 1e-5f, "never further than the game allows");
            Assert.AreEqual(24f * CameraPreferences.MinFurthest, CameraPreferences.FurthestDistance(3f, 24f, 0f), 1e-5f);
            Assert.AreEqual(3f, CameraPreferences.FurthestDistance(3f, 4f, CameraPreferences.MinFurthest), 1e-5f, "never nearer than the nearest");
        }
    }
}
