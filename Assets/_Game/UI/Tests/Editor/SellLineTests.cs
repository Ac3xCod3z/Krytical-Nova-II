using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Build 17, item 5.4: every item tooltip says what a vendor pays (VendorRules.SellValue), hidden when no vendor buys it.</summary>
    public sealed class SellLineTests
    {
        [Test]
        public void OneItemSaysItsPrice()
        {
            Assert.AreEqual("Sells for 120 Creds", GearUIController.SellLine(120, 1));
            Assert.AreEqual("Sells for 1 Cred", GearUIController.SellLine(1, 1));
        }

        [Test]
        public void AStackSaysEachAndTheWhole()
        {
            Assert.AreEqual("Sells for 12 Creds each (240 Creds for 20)", GearUIController.SellLine(12, 20));
        }

        [Test]
        public void NothingForAnItemNoVendorBuys()
        {
            Assert.IsNull(GearUIController.SellLine(0, 1));
            Assert.IsNull(GearUIController.SellLine(0, 20));
        }
    }
}
