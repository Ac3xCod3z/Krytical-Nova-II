using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using KryticalNova.UI;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The cards over players and hostiles are placed after the camera has moved this frame (owner's DOTS retry 4, 30 Oct 2026:
    /// projected with the camera of the frame before, a card over a body walking across the screen shook).
    /// </summary>
    public class NamecardOrderTests
    {
        static int BrainOrder()
        {
            foreach (string guid in AssetDatabase.FindAssets("CinemachineBrain t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() != null && script.GetClass().Name == "CinemachineBrain")
                    return MonoImporter.GetExecutionOrder(script);
            }
            Assert.Fail("no CinemachineBrain script found");
            return 0;
        }

        static int OrderOf(System.Type type)
        {
            var attribute = type.GetCustomAttribute<DefaultExecutionOrder>();
            return attribute != null ? attribute.order : 0;
        }

        [Test]
        public void TheCards_ArePlacedAfterTheCameraBrain()
        {
            int brain = BrainOrder();
            Assert.Greater(OrderOf(typeof(PlayerNamecardController)), brain, "player namecards after the camera");
            Assert.Greater(OrderOf(typeof(HostileNameplateController)), brain, "hostile plates after the camera");
            Assert.Greater(OrderOf(typeof(CombatFeedbackManager)), brain, "damage numbers after the camera");
            Assert.Greater(OrderOf(typeof(PetBubbleController)), brain, "pet bubbles after the camera");
            Assert.Greater(OrderOf(typeof(LootHudController)), brain, "loot labels after the camera");
            Assert.Greater(OrderOf(typeof(NpcInteractionUI)), brain, "NPC prompts after the camera");
        }
    }
}
