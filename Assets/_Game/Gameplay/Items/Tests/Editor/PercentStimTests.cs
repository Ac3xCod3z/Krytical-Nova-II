using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Build 17 item 5.2: the % Stims restore a percent of max HP / Flux, beside (never instead of) the flat amount.</summary>
    public sealed class PercentStimTests
    {
        private static ItemDefinition Make(float hp, float flux, float hpPct, float fluxPct)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.restoreHealth = hp; def.restoreChi = flux; def.restoreHealthPercent = hpPct; def.restoreChiPercent = fluxPct;
            return def;
        }

        [TestCase(30f, 5000f, 1500f)]
        [TestCase(60f, 5000f, 3000f)]
        [TestCase(90f, 5000f, 4500f)]
        public void APercentHpStimRestoresThatShareOfMaxHp(float pct, float max, float expected)
        {
            ItemDefinition def = Make(0f, 0f, pct, 0f);
            Assert.IsTrue(def.IsConsumable);
            Assert.IsTrue(def.RestoresHealth);
            Assert.IsFalse(def.RestoresChi);
            Assert.AreEqual(expected, def.HealthRestoredAt(max), 1e-3f);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void AMixedStimRestoresTheSameShareOfBoth()
        {
            ItemDefinition def = Make(0f, 0f, 60f, 60f);
            Assert.AreEqual(600f, def.HealthRestoredAt(1000f), 1e-3f);
            Assert.AreEqual(240f, def.ChiRestoredAt(400f), 1e-3f);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void AFlatStimIsUnchanged()
        {
            ItemDefinition def = Make(300f, 0f, 0f, 0f);
            Assert.AreEqual(300f, def.HealthRestoredAt(99999f), 1e-3f);
            Assert.AreEqual(0f, def.ChiRestoredAt(500f), 1e-3f);
            Assert.IsFalse(def.RestoresChi);
            Object.DestroyImmediate(def);
        }
    }
}
