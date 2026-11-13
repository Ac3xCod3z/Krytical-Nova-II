using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Progression.Tests
{
    /// <summary>
    /// Build 17, item 2.3 (owner, 13 Nov 2026): a new character starts with 5 stat points. The server body of the Player prefab reads
    /// <see cref="CharacterProgressionData.startingStatPoints"/> from this asset when it wakes (PlayerCharacterProgression.Awake) and keeps
    /// it when no save exists; the owner's view gets it through OwnerProgress. This pins the asset value and the prefab's link to it.
    /// </summary>
    public sealed class StartingPointsTests
    {
        private const string ProgressionPath = "Assets/_Game/Gameplay/Progression/Data/Assets/CharacterData/CharacterProgression.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        [Test]
        public void ANewCharacterStartsWithFiveStatPoints()
        {
            var data = AssetDatabase.LoadAssetAtPath<CharacterProgressionData>(ProgressionPath);
            Assert.IsNotNull(data, ProgressionPath);
            Assert.AreEqual(5, data.startingStatPoints);

            var player = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, PlayerPrefabPath);
            var progression = player.GetComponentInChildren<PlayerCharacterProgression>(true);
            Assert.IsNotNull(progression, "the Player prefab has no PlayerCharacterProgression");
            var linked = new SerializedObject(progression).FindProperty("progressionData").objectReferenceValue;
            Assert.AreSame(data, linked, "the Player prefab must read the CharacterProgression asset");
        }
    }
}
