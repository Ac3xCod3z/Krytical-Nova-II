using KryticalNova.CharacterCreation;
using KryticalNova.Items;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// M8 8.1.2b what other players see a player wear, in the look text: the loadout, the looks and the Temper come back exactly; anything
    /// malformed is refused (the body keeps what it wears); a look text from before (two lines) still reads, without gear.
    /// </summary>
    public class GearTextTests
    {
        [Test]
        public void TheGearLine_ComesBackExactly_AndAMalformedOneIsRefused()
        {
            var loadout = new EquipmentLoadout();
            loadout[EquipSlot.Weapon] = 10001;
            loadout[EquipSlot.Armor] = 20002;
            loadout[EquipSlot.Pet] = 30003;
            var looks = new EquipmentLooks { armor = 4004, armorDye = 12, capeDye = 7, pet = 5005, petStage = 3, helmetHidden = true };
            var temper = new EquipmentTemper();
            temper[EquipSlot.Weapon] = 15;
            temper[EquipSlot.Cape] = 200;

            string text = GearText.Encode(loadout, looks, temper);
            Assert.IsTrue(GearText.TryDecode(text, out EquipmentLoadout l, out EquipmentLooks k, out EquipmentTemper t), text);
            Assert.AreEqual(loadout, l);
            Assert.AreEqual(looks, k);
            Assert.AreEqual(temper, t);

            Assert.IsFalse(GearText.TryDecode("g1|1,2|3|4", out _, out _, out _), "wrong counts");
            Assert.IsFalse(GearText.TryDecode(text.Replace("g1|", "g9|"), out _, out _, out _), "unknown version");
            Assert.IsFalse(GearText.TryDecode(text.Replace("10001", "99999"), out _, out _, out _), "an item id out of range");
            Assert.IsFalse(GearText.TryDecode(text.Replace(",1,0", ",2,0"), out _, out _, out _), "a flag that is not 0 or 1");
        }

        [Test]
        public void TheLookText_CarriesTheGear_AndAnOldLookStillReads()
        {
            CharacterRecipe recipe = default;
            recipe.parts = new ushort[0];
            recipe.colors = new ColorOverride[0];
            string old = RecipeText.EncodeLook("Ace Vex", recipe);
            string gear = GearText.Encode(new EquipmentLoadout(), new EquipmentLooks(), new EquipmentTemper());
            string withGear = RecipeText.EncodeLook("Ace Vex", recipe, gear);

            RecipeText.TryDecodeLook(withGear, out string name, out _, out string got);   // the recipe's own range check is RecipeText's tests
            Assert.AreEqual("Ace Vex", name);
            Assert.AreEqual(gear, got, "the gear line");
            Assert.AreEqual(RecipeText.TryDecodeLook(old, out _, out _), RecipeText.TryDecodeLook(withGear, out _, out _),
                "the recipe reads the same with or without the gear line");
            RecipeText.TryDecodeLook(old, out _, out _, out string none);
            Assert.IsNull(none, "a two-line look has no gear");
            Assert.AreEqual(old, RecipeText.EncodeLook("Ace Vex", recipe, null), "no gear: the text is as before");
        }
    }
}
