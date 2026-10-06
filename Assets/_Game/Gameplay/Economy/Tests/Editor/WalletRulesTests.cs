using KryticalNova.Economy;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class WalletRulesTests
    {
        private const long Cap = 1_000;

        [Test]
        public void Add_StopsAtTheCap_AndSaysHowMuchWentIn()
        {
            long balance = 900;
            Assert.AreEqual(100, WalletRules.Add(ref balance, 250, Cap));
            Assert.AreEqual(Cap, balance);
            Assert.AreEqual(0, WalletRules.Add(ref balance, 1, Cap));
            Assert.AreEqual(0, WalletRules.Add(ref balance, -50, Cap), "a negative amount is never an earn");
            Assert.AreEqual(Cap, balance);
        }

        [Test]
        public void Add_NeverOverflows()
        {
            long balance = long.MaxValue - 5;
            Assert.AreEqual(5, WalletRules.Add(ref balance, long.MaxValue, long.MaxValue));
            Assert.AreEqual(long.MaxValue, balance);
        }

        [Test]
        public void Spend_IsAllOrNothing()
        {
            long balance = 300;
            Assert.IsFalse(WalletRules.TrySpend(ref balance, 301));
            Assert.AreEqual(300, balance);
            Assert.IsFalse(WalletRules.TrySpend(ref balance, -1));
            Assert.IsTrue(WalletRules.TrySpend(ref balance, 300));
            Assert.AreEqual(0, balance);
        }

        [Test]
        public void Transfer_NeedsTheCredsAndTheRoom()
        {
            long from = 500, to = 800;
            Assert.IsFalse(WalletRules.TryTransfer(ref from, ref to, 600, Cap), "the payer does not have it");
            Assert.IsFalse(WalletRules.TryTransfer(ref from, ref to, 300, Cap), "it does not fit under the receiver's cap");
            Assert.AreEqual(500, from);
            Assert.AreEqual(800, to);
            Assert.IsTrue(WalletRules.TryTransfer(ref from, ref to, 200, Cap));
            Assert.AreEqual(300, from);
            Assert.AreEqual(Cap, to);
        }

        [Test]
        public void Creds_AreShownWithThousandsSeparators()
        {
            Assert.AreEqual("999,999,999,999", WalletRules.Format(999_999_999_999));
            Assert.AreEqual("1 Cred", WalletRules.FormatCreds(1));
            Assert.AreEqual("20,000,000 Creds", WalletRules.FormatCreds(20_000_000));
        }

        // ---------------------------------------------------------------- the settings

        private EconomySettings settings;

        [SetUp]
        public void SetUp() => settings = ScriptableObject.CreateInstance<EconomySettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void TheDefaults_MakeSenseTogether()
        {
            Assert.LessOrEqual(settings.tradeCredsPerTrade, settings.tradeCredsPerDay);
            Assert.LessOrEqual(settings.tradeCredsPerDay, settings.walletCap);
            for (int i = 1; i < settings.kioskPriceCapByRarity.Length; i++)
                Assert.GreaterOrEqual(settings.kioskPriceCapByRarity[i], settings.kioskPriceCapByRarity[i - 1]);
            for (int i = 1; i < settings.incomeTarget.Length; i++)
            {
                Assert.Greater(settings.incomeTarget[i].level, settings.incomeTarget[i - 1].level);
                Assert.GreaterOrEqual(settings.incomeTarget[i].credsPerHour, settings.incomeTarget[i - 1].credsPerHour);
            }
            Assert.IsTrue(settings.kioskTiers[0].IsFree, "everyone can open a Kiosk for the first length");
            Assert.AreEqual(3, settings.kioskTiers[0].days);
            Assert.AreEqual(14, settings.kioskTiers[1].days);
            Assert.AreEqual(30, settings.kioskTiers[2].days);
            Assert.IsFalse(settings.kioskTiers[1].IsFree);
            Assert.IsFalse(settings.kioskTiers[2].IsFree);
        }

        [Test]
        public void Income_IsReadBetweenThePoints()
        {
            Assert.AreEqual(3_000, settings.CredsPerHour(1));
            Assert.AreEqual(3_000, settings.CredsPerHour(-4), "below the first point is the first point");
            Assert.AreEqual(2_000_000, settings.CredsPerHour(112));
            Assert.AreEqual(2_000_000, settings.CredsPerHour(500));
            Assert.AreEqual(57_500, settings.CredsPerHour(30), "half way between level 20 (25k) and 40 (90k)");
            Assert.AreEqual(10.0, settings.HoursToEarn(20_000_000, 112), 0.0001, "the daily trade cap is ten hours at the level cap");
        }

        [Test]
        public void KioskPrices_AreCappedByRarity_StackablesPerUnit_AndPetsByGrowth()
        {
            Assert.AreEqual(50_000, settings.KioskPriceCap(ItemRarity.Common, false));
            Assert.AreEqual(20_000_000, settings.KioskPriceCap(ItemRarity.Epic, false));
            Assert.AreEqual(10_000, settings.KioskPriceCap(ItemRarity.Epic, true), "anything that stacks is capped per unit");

            Assert.AreEqual(50_000, settings.KioskPriceCap(ItemRarity.Common, false, 0f), "a new pet is worth what its rarity allows");
            Assert.AreEqual(20_000_000, settings.KioskPriceCap(ItemRarity.Common, false, 1f), "a fully grown one may cost the top price");
            Assert.AreEqual(10_025_000, settings.KioskPriceCap(ItemRarity.Common, false, 0.5f));
        }

        [Test]
        public void TheSalesTax_LeavesTheGame()
        {
            long seller = settings.AfterTax(1_000_000, out long tax);
            Assert.AreEqual(50_000, tax);
            Assert.AreEqual(950_000, seller);
            Assert.AreEqual(0, settings.AfterTax(0, out tax));
            Assert.AreEqual(0, tax);
            Assert.AreEqual(19, settings.AfterTax(19, out tax), "the tax is rounded down, in the seller's favour");
        }
    }
}
