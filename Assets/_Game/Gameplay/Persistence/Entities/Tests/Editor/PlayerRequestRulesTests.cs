using System;
using KryticalNova.Core;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// The item uses the DOTS server takes (feel test item 5): only effects whose result it saves, so no item is spent from the real Pack
    /// into a part that is still passed through (M8 research, 2 Nov: a mount or Mount Feed was lost at the next login).
    /// </summary>
    public class PlayerRequestRulesTests
    {
        [Test]
        public void OnlyEffectsWhoseResultIsSaved_AreUsed_EveryOtherWaitsForItsPort()
        {
            var ported = new[] { ItemUseEffect.None, ItemUseEffect.FluxFlask, ItemUseEffect.FeedPet, ItemUseEffect.Serum, ItemUseEffect.Recall,
                ItemUseEffect.ExperienceBoost, ItemUseEffect.LearnSkill };   // M8 8.3.3 / 8.3.2: the xpboosts and skills parts are real
            foreach (ItemUseEffect effect in Enum.GetValues(typeof(ItemUseEffect)))
                Assert.AreEqual(Array.IndexOf(ported, effect) >= 0, PlayerRequestServerSystem.EffectPorted(effect), effect.ToString());
            Assert.IsFalse(PlayerRequestServerSystem.EffectPorted(ItemUseEffect.RegisterMount), "a mount lands in the passed-through mounts part");
            Assert.IsFalse(PlayerRequestServerSystem.EffectPorted(ItemUseEffect.FeedMount));
            Assert.IsTrue(PlayerRequestServerSystem.EffectPorted(ItemUseEffect.ExperienceBoost), "xpboosts is real now (M8 8.3.3)");
            Assert.IsTrue(PlayerRequestServerSystem.EffectPorted(ItemUseEffect.LearnSkill), "skills is real now (M8 8.3.2)");
        }
    }
}
