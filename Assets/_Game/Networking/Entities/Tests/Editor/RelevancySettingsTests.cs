using NUnit.Framework;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>Wave 4 relevancy: the settings asset is in Resources and holds the owner's numbers (never a silent default).</summary>
    public class RelevancySettingsTests
    {
        [Test]
        public void TheAsset_LoadsFromResources_WithTheOwners150Metres()
        {
            RelevancySettings s = RelevancySettings.Load();
            Assert.IsNotNull(s, "Resources/" + RelevancySettings.ResourcePath + " is missing");
            Assert.AreEqual(150f, s.playerRadius, "the owner's radius for players (12 Nov 2026)");
            Assert.AreEqual(150f, s.objectRadius, "the owner's radius for monsters (12 Nov 2026)");
            Assert.Greater(s.leaveMargin, 0f, "without a margin a body on the edge flickers");
            Assert.GreaterOrEqual(s.fillIntervalTicks, 1);
            Assert.Greater(s.importancePlayer, s.importanceHostile, "players before Hostiles");
            Assert.Greater(s.importanceHostile, s.importanceDummy, "Hostiles before Strike Dummies");
        }

        [Test]
        public void TheAsset_ShipsSwitchedOff_UntilTheTwoBotRunPasses()
        {
            // The lead's term (12 Nov 2026): relevancy ships OFF until the two-bot run and the "told once" test pass.
            // When they have, set enabled in the asset and turn this test round.
            Assert.IsFalse(RelevancySettings.Load().enabled);
        }
    }
}
