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
