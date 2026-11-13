using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// Relevancy's unwritten rule, written down (lead 4, condition 1, 12 Nov 2026): a connection owns ONE ghost, its player's body,
    /// and that ghost's place is the connection's place: for who is sent to it (<c>RelevancyFillJob</c>) and for what it is sent
    /// first (<c>GhostConnectionPositionSystem</c>). Both take the first owned ghost they find. So no other ghost prefab may have an
    /// owner until those two places know which owned ghost is the body.
    /// </summary>
    public class OneOwnedGhostLintTests
    {
        private const string GameFolder = "Assets/_Game";
        private const string PlayerGhost = "Assets/_Game/Gameplay/Player/Entities/Prefabs/PlayerGhost.prefab";

        [Test]
        public void ThePlayerGhost_IsTheOnlyGhostPrefabWithAnOwner()
        {
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, GameFolder);
            var owned = new List<string>();
            foreach (string file in Directory.EnumerateFiles(root, "*.prefab", SearchOption.AllDirectories))
                // a test's own ghost prefab (Player/Entities/Tests/Editor/Generated/PlayerGhostTest.prefab) is never spawned by the game
                if (!file.Replace('\\', '/').Contains("/Tests/") && HasOwner(file)) owned.Add(file.Substring(root.Length - GameFolder.Length).Replace('\\', '/'));

            CollectionAssert.Contains(owned, PlayerGhost, "the player ghost has an owner (its connection): relevancy finds a connection's place by it");
            string[] others = owned.Where(p => p != PlayerGhost).ToArray();
            Assert.IsEmpty(others,
                "Another ghost prefab has an owner: " + string.Join(", ", others) + ". Relevancy takes the FIRST ghost a connection owns as that " +
                "connection's place (RelevancyFillJob in Networking/Entities/Runtime/RelevancyFill.cs, and GhostConnectionPositionSystem in " +
                "GhostImportanceSystems.cs). With a second owned ghost (a pet, a mount, a Kiosk stand) a player could be sent the surroundings " +
                "of that ghost instead of their own body's. Before this prefab gets an owner: make both places pick the BODY (for example a " +
                "tag component the fill requires for 'the place'), keep the other owned ghosts always relevant to their owner as today, add " +
                "a test with two owned ghosts far apart, and then list the prefab here.");
        }

        /// <summary>A ghost prefab's GhostAuthoringComponent writes "HasOwner: 1" in its text; read line by line, so big prefabs cost little.</summary>
        static bool HasOwner(string file)
        {
            foreach (string line in File.ReadLines(file))
                if (line.StartsWith("  HasOwner: 1", System.StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
