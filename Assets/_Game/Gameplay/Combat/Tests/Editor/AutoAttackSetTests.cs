using System.Collections.Generic;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Combat.Tests
{
    /// <summary>
    /// Each weapon class holds its own auto-attack set (owner, 4 Nov 2026): every class has exactly one complete set on the Player prefab,
    /// and until a class gets its own animations its set is the combo the game shipped with (the prefab's own clips and numbers).
    /// </summary>
    public sealed class AutoAttackSetTests
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        [Test]
        public void EveryClass_HasOneCompleteComboSet_OnThePlayer()
        {
            var melee = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<PlayerMeleeCombat>(true);
            Assert.IsNotNull(melee);
            var sets = new SerializedObject(melee).FindProperty("classAutoAttacks");
            var seen = new HashSet<WeaponClassId>();
            for (int i = 0; i < sets.arraySize; i++)
            {
                var set = sets.GetArrayElementAtIndex(i).objectReferenceValue as AutoAttackSet;
                Assert.IsNotNull(set, "slot " + i + " is empty");
                Assert.IsTrue(set.IsComplete, set.name + " is missing a clip");
                Assert.IsTrue(seen.Add(set.weaponClass), set.weaponClass + " has two sets");
            }
            for (var c = WeaponClassId.Monoblade; c <= WeaponClassId.Warbow; c++)
                Assert.IsTrue(seen.Contains(c), c + " has no auto-attack set");
        }

        [Test]
        public void TheSets_HoldTheShippedCombo_UntilTheirOwnAnimationsComeIn()
        {
            var melee = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<PlayerMeleeCombat>(true);
            var so = new SerializedObject(melee);
            var sets = so.FindProperty("classAutoAttacks");
            for (int i = 0; i < sets.arraySize; i++)
            {
                var set = (AutoAttackSet)sets.GetArrayElementAtIndex(i).objectReferenceValue;
                for (int step = 1; step <= 3; step++)
                {
                    var strike = set.StrikeOf(step);
                    // a class whose own clip has been slotted in no longer has to match
                    if (strike.clip != so.FindProperty("combo" + step + "Clip").objectReferenceValue) continue;
                    Assert.AreEqual(so.FindProperty("strike" + step + "Speed").floatValue, strike.speed, 1e-4f, set.name + " strike " + step);
                    Assert.AreEqual(so.FindProperty("swing" + step + "Start").floatValue, strike.swingStart, 1e-4f, set.name + " strike " + step);
                    Assert.AreEqual(so.FindProperty("swing" + step + "End").floatValue, strike.swingEnd, 1e-4f, set.name + " strike " + step);
                    Assert.AreEqual(so.FindProperty("slash" + step + "VfxTime").floatValue, strike.slashVfxTime, 1e-4f, set.name + " strike " + step);
                }
                Assert.AreEqual(so.FindProperty("postComboDelay").floatValue, set.postComboDelay, 1e-4f, set.name);
                Assert.AreEqual(so.FindProperty("meleeStrikingRange").floatValue, set.attackRange, 1e-4f, set.name + ": every class strikes in melee today");
                Assert.AreEqual(so.FindProperty("serverMaxValidationRange").floatValue, set.serverCheckRange, 1e-4f, set.name);
            }
        }
    }
}
