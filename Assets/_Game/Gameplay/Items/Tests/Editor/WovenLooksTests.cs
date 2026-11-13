using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.Tests
{
    /// <summary>Build 17 item 6: a Weave is kept per weapon class and per gear line; the "weave" save steps from version 1 to 2.</summary>
    public sealed class WovenLooksTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private ItemDefinition Item(ushort id, EquipSlot slot, ushort line, WeaponClassId cls = WeaponClassId.None)
        {
            var d = ScriptableObject.CreateInstance<ItemDefinition>();
            d.itemId = id; d.equipSlot = slot; d.lineId = line; d.weaponClass = cls;
            made.Add(d);
            return d;
        }

        [Test]
        public void EachWeaponClassKeepsItsOwnWeave()
        {
            var woven = new List<WovenLook>();
            ItemDefinition mono = Item(10001, EquipSlot.Weapon, 1, WeaponClassId.Monoblade), glaive = Item(10017, EquipSlot.Weapon, 2, WeaponClassId.Glaive);
            ItemDefinition mono2 = Item(10033, EquipSlot.Weapon, 3, WeaponClassId.Monoblade);
            Assert.IsTrue(WovenLooks.TryKeyFor(mono, out byte k, out ushort key)); WovenLooks.Set(woven, k, key, 500);
            Assert.IsTrue(WovenLooks.TryKeyFor(glaive, out k, out key)); WovenLooks.Set(woven, k, key, 600);
            for (int i = 0; i < 3; i++)   // switch back and forth
            {
                Assert.AreEqual(500, WovenLooks.WeaveFor(woven, mono));
                Assert.AreEqual(600, WovenLooks.WeaveFor(woven, glaive));
            }
            Assert.AreEqual(500, WovenLooks.WeaveFor(woven, mono2), "decision 6-C a: every weapon of the class shows its Weave");
        }

        [Test]
        public void WeavingOneSetNeverWeavesAnother()
        {
            var woven = new List<WovenLook>();
            ItemDefinition setA = Item(11001, EquipSlot.Armor, 40), setB = Item(11017, EquipSlot.Armor, 41), glovesA = Item(11033, EquipSlot.Gloves, 42);
            WovenLooks.TryKeyFor(setA, out byte k, out ushort key);
            WovenLooks.Set(woven, k, key, 700);
            Assert.AreEqual(700, WovenLooks.WeaveFor(woven, setA));
            Assert.AreEqual(0, WovenLooks.WeaveFor(woven, setB), "set B shows its own look");
            Assert.AreEqual(0, WovenLooks.WeaveFor(woven, glovesA), "other slots untouched");
            Assert.IsTrue(WovenLooks.Set(woven, k, key, 0));
            Assert.AreEqual(0, WovenLooks.WeaveFor(woven, setA), "taken off");
        }

        [Test]
        public void TheOldSaveStepsUpKeepingEverythingAndChargingNothing()
        {
            var looks = new EquipmentLooks { weapon = 10017, armor = 11001, cape = 12001, pet = 30, petStage = 2, helmetHidden = true, capeHidden = true };
            string v1 = JsonUtility.ToJson(new WeaveSaveSteps.V1 { looks = looks, collectedOwned = true });
            var two = JsonUtility.FromJson<WeaveSaveSteps.V2>(WeaveSaveSteps.Migrate(v1, 1));
            Assert.IsTrue(two.collectedOwned);
            Assert.AreEqual(30, two.looks.pet);
            Assert.AreEqual(2, two.looks.petStage);
            Assert.IsTrue(two.looks.helmetHidden && two.looks.capeHidden);
            Assert.AreEqual(0, two.looks.weapon + two.looks.armor + two.looks.cape, "slot numbers are worked out by the server now");
            Assert.AreEqual(3, two.unbound.Count);
            Assert.AreEqual(0, two.woven.Count);
        }

        [Test]
        public void AnUnreadableSaveIsRefusedNeverReset()
        {
            Assert.Throws<FormatException>(() => WeaveSaveSteps.Migrate("not json", 1));
            Assert.Throws<InvalidOperationException>(() => WeaveSaveSteps.Migrate("{}", 2));
        }

        [Test]
        public void OldWeavesFindTheirPieceOrWait()
        {
            ItemDefinition glaiveLook = Item(10017, EquipSlot.Weapon, 2, WeaponClassId.Glaive);
            ItemDefinition armorLook = Item(11001, EquipSlot.Armor, 40), wornArmor = Item(11049, EquipSlot.Armor, 43);
            var lookups = new Dictionary<ushort, ItemDefinition> { [10017] = glaiveLook, [11001] = armorLook, [11049] = wornArmor };
            var unbound = new List<UnboundWeave>
            {
                new UnboundWeave { slot = EquipSlot.Weapon, item = 10017 },
                new UnboundWeave { slot = EquipSlot.Armor, item = 11001 },
                new UnboundWeave { slot = EquipSlot.Gloves, item = 9999 },   // a look the game no longer has: dropped
            };
            var woven = new List<WovenLook>();
            ItemDefinition armorWorn = null;
            Func<ushort, ItemDefinition> lookOf = id => lookups.TryGetValue(id, out ItemDefinition d) ? d : null;
            // nothing worn yet: the weapon Weave binds to its own class (a Monoblade worn then does not matter), the armour waits
            WovenLooks.Bind(unbound, woven, s => s == EquipSlot.Armor ? armorWorn : null, lookOf);
            Assert.AreEqual(10017, WovenLooks.WeaveFor(woven, Item(10050, EquipSlot.Weapon, 5, WeaponClassId.Glaive)));
            Assert.AreEqual(1, unbound.Count, "the armour Weave waits for a piece");
            armorWorn = wornArmor;
            WovenLooks.Bind(unbound, woven, s => s == EquipSlot.Armor ? armorWorn : null, lookOf);
            Assert.AreEqual(0, unbound.Count);
            Assert.AreEqual(11001, WovenLooks.WeaveFor(woven, wornArmor), "bound to the piece worn at load");
        }
    }
}
