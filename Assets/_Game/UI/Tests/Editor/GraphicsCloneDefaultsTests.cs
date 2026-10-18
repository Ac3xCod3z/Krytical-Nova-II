using KryticalNova.Networking;
using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Player 2 clone's own graphics (owner, 18 Oct 2026): it starts below High, and the main Editor is never
    /// touched by the rule.
    /// </summary>
    public sealed class GraphicsCloneDefaultsTests
    {
        [Test]
        public void ClonePresetIsBelowHigh()
        {
            Assert.That(GraphicsPreferences.ClonePreset, Is.LessThan(GraphicsPreset.High));
            Assert.That(GraphicsPreferences.CloneFrameCap, Is.GreaterThan(0));
        }

        // First launch by hardware (owner, 18 Oct 2026): Low reaches laptops and integrated graphics, the mid tier keeps High.
        [TestCase("Intel(R) Iris(R) Xe Graphics", 128, 8, GraphicsPreset.Low)]
        [TestCase("Intel(R) UHD Graphics 620", 1024, 8, GraphicsPreset.Low)]
        [TestCase("AMD Radeon(TM) Graphics", 512, 12, GraphicsPreset.Low)]
        [TestCase("AMD Custom GPU 0405", 1024, 8, GraphicsPreset.Low)]
        [TestCase("NVIDIA GeForce RTX 3060", 12288, 4, GraphicsPreset.Low)]
        [TestCase("NVIDIA GeForce GTX 1060 6GB", 6144, 8, GraphicsPreset.Medium)]
        [TestCase("NVIDIA GeForce GTX 1650", 4096, 8, GraphicsPreset.Medium)]
        [TestCase("Radeon RX 580 Series", 8192, 8, GraphicsPreset.Medium)]
        [TestCase("NVIDIA GeForce RTX 3050 Laptop GPU", 4096, 12, GraphicsPreset.Medium)]
        [TestCase("NVIDIA GeForce RTX 3060", 12288, 12, GraphicsPreset.High)]
        [TestCase("AMD Radeon RX 6700 XT", 12272, 12, GraphicsPreset.High)]
        [TestCase("Intel(R) Arc(TM) A770 Graphics", 16224, 16, GraphicsPreset.High)]
        [TestCase("NVIDIA GeForce RTX 5070 Ti", 16303, 16, GraphicsPreset.High)]
        public void FirstLaunchPresetFollowsTheHardware(string card, int videoMb, int threads, GraphicsPreset expected)
        {
            Assert.That(GraphicsPreferences.PresetForHardware(card, videoMb, threads), Is.EqualTo(expected));
        }

        [Test]
        public void MainEditorKeepsItsOwnOptions()
        {
            if (NetworkConnectionManager.EditorCloneProfile() != null) Assert.Ignore("Runs in the main Editor only.");

            GraphicsPreset before = GraphicsPreferences.Preset;
            int capBefore = GraphicsPreferences.FrameCap;
            Assert.That(GraphicsPreferences.ApplyCloneDefaults(), Is.False);
            Assert.That(GraphicsPreferences.Preset, Is.EqualTo(before));
            Assert.That(GraphicsPreferences.FrameCap, Is.EqualTo(capBefore));
        }
    }
}
