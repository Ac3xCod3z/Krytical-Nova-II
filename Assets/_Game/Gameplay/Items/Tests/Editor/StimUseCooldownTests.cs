using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>Build 17 item 5.1: Stims are used any time, rapidly: each kind on its own cooldown of at most 0.5 s.</summary>
    public sealed class StimUseCooldownTests
    {
        private const ushort HpStim = 9001, FluxStim = 9002;

        [Test]
        public void OneKindOnCooldownNeverBlocksAnother()
        {
            var next = new Dictionary<ushort, float> { [HpStim] = 10.5f };
            Assert.IsTrue(InventoryRules.OnUseCooldown(next, HpStim, 10.2f));
            Assert.IsFalse(InventoryRules.OnUseCooldown(next, FluxStim, 10.2f), "a Flux Stim does not wait on an HP Stim");
            Assert.IsFalse(InventoryRules.OnUseCooldown(next, HpStim, 10.5f), "ready again when its own time is up");
            Assert.IsFalse(InventoryRules.OnUseCooldown(new Dictionary<ushort, float>(), HpStim, 0f), "never used: ready");
        }

        [Test]
        public void EveryShippedStimHasAtMostHalfASecondCooldown()
        {
            int found = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/_Game/Gameplay/Items/Data/Assets/Consumables" }))
            {
                var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.useEffect != ItemUseEffect.None || (!def.RestoresHealth && !def.RestoresChi)) continue;
                found++;
                Assert.LessOrEqual(def.useCooldown, 0.5f, def.displayName);
            }
            Assert.GreaterOrEqual(found, 9, "the nine HP / Flux / Dual Stims");
        }
    }
}
