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
        public void TheAsset_ShipsSwitchedOn_AfterTheFourConditions()
        {
            // The lead's term (12 Nov 2026): relevancy shipped OFF until the one-owner lint, the world tests, the client audit (R3-3a)
            // and the two-bot run had passed; the Master switched it on with the owner's 150 m (15 Nov 2026, perf round 3 R3-3b).
            // The live server can still turn it off without a build: server-settings.json "relevancy": false.
            Assert.IsTrue(RelevancySettings.Load().enabled);
        }
    }
}
