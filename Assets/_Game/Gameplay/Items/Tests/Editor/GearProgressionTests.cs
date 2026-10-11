using System.Collections.Generic;
using System.Linq;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Gear progression: the state an item carries, the Tempering odds and outcomes, which material a Fusion takes,
    /// Tuning, Reforge, and what the state is worth in stats. The rules are given their rolls, so nothing here is
    /// left to chance. Uses the baked ladder for real items and a settings object with the default numbers.
    /// </summary>
    public sealed class GearProgressionTests
    {
        private const string Root = "Assets/_Game/Gameplay/Items/Data/Assets/";
        private const string HotStreakPath = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/SelfBuffSkills/Skill_GarudasPrayer.asset";

        private GearProgressionSettings settings;
        private GearDictionary dictionary;
        private ItemCatalog catalog;

        [OneTimeSetUp]
        public void Load()
        {
            dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(Root + "GearDictionary.asset");
            catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "ItemCatalog.asset");
            var table = AssetDatabase.LoadAssetAtPath<GearStatTable>(Root + "GearStatTable.asset");
            Assert.IsNotNull(dictionary, "GearDictionary.asset");
            Assert.IsNotNull(catalog, "ItemCatalog.asset");
            Assert.IsNotNull(table, "GearStatTable.asset");
            // The default numbers, not whatever the asset has been tuned to: these tests pin the rules.
            settings = ScriptableObject.CreateInstance<GearProgressionSettings>();
            settings.statTable = table;
        }

        [OneTimeTearDown]
        public void Unload() => Object.DestroyImmediate(settings);

        private ItemDefinition Gear(EquipSlot slot, ItemRarity rarity, int level, WeaponClassId weaponClass = WeaponClassId.None)
        {
            ItemDefinition item = catalog.Items.FirstOrDefault(d => d != null && d.IsLadderGear && d.equipSlot == slot && d.rarity == rarity
                && d.itemLevel == level && (weaponClass == WeaponClassId.None || d.weaponClass == weaponClass));
            Assert.IsNotNull(item, $"No {rarity} {slot} at level {level} in the catalog (bake the gear ladder).");
            return item;
        }

        private static AlloyGrade Alloy(TemperSettings temper, int step, bool prime = false) =>
            temper.alloys.First(a => a.step == step && a.prime == prime);

        // ---------------------------------------------------------------- state

        [Test]
        public void StateSurvivesPackingAndKeepsTheBitsItDoesNotOwn()
        {
            var state = new GearItemState(132, 12, 25);
            Assert.AreEqual(state, GearItemState.Unpack(state.Pack()));
            Assert.IsTrue(default(GearItemState).IsClean);
            Assert.AreEqual(0UL, default(GearItemState).Pack(), "an untouched piece is a plain item");

            ulong sockets = 0xABCDUL << 40; // bits kept for Slotting
            ulong packed = state.PackInto(sockets);
            Assert.AreEqual(state, GearItemState.Unpack(packed));
            Assert.AreEqual(sockets, packed & (0xFFFFUL << 40), "bits above the state are untouched");
            Assert.AreEqual(GearItemState.MaxTemper, new GearItemState(999, 0, 0).Temper, "clamped");
            Assert.AreEqual("+36%  FS3  T7", new GearItemState(36, 3, 7).ToString());

            // The dye is carried beside the rest, and is no work done on the piece.
            var dyed = new GearItemState(36, 3, 7, 24);
            Assert.AreEqual(dyed, GearItemState.Unpack(dyed.Pack()));
            Assert.AreEqual(24, GearItemState.Unpack(dyed.Pack()).Dye);
            Assert.AreEqual(24, dyed.WithTemper(39).WithFusion(4).WithTune(8).Dye, "work on a piece leaves its dye");
            Assert.AreEqual(36, dyed.WithDye(3).Temper, "a dye leaves the work on a piece");
            Assert.IsTrue(new GearItemState(0, 0, 0, 5).IsClean, "a dyed piece is still untouched");
            Assert.AreNotEqual(0UL, new GearItemState(0, 0, 0, 5).Pack());
            Assert.AreEqual(GearItemState.MaxDye, new GearItemState(0, 0, 0, 999).Dye, "clamped");
            ulong socketBits = 1UL << 40;
            Assert.AreEqual(socketBits, new GearItemState(1, 1, 1, 9).PackInto(socketBits) & socketBits, "the bits above are kept");
        }

        // ---------------------------------------------------------------- Tempering

        [Test]
        public void TemperChanceFollowsTheFormulaWithLuckAndItsLimits()
        {
            TemperSettings temper = settings.temper;
            Assert.AreEqual(100f, GearProgressionRules.TemperChance(temper, 0, 3, 0f, TemperProtection.None), "103 - (0 + 3)");
            Assert.AreEqual(88f, GearProgressionRules.TemperChance(temper, 0, 15, 0f, TemperProtection.None), "103 - (0 + 15)");
            Assert.AreEqual(28f, GearProgressionRules.TemperChance(temper, 60, 15, 0f, TemperProtection.None), "103 - (60 + 15)");
            Assert.AreEqual(5f, GearProgressionRules.TemperChance(temper, 110, 9, 0f, TemperProtection.None), "never below the floor");
            Assert.AreEqual(38f, GearProgressionRules.TemperChance(temper, 60, 15, 1000f, TemperProtection.None), "100 Luck is 1%");
            Assert.AreEqual(53f, GearProgressionRules.TemperChance(temper, 60, 15, 99999f, TemperProtection.None), "Luck is capped at 25%");
            Assert.AreEqual(33f, GearProgressionRules.TemperChance(temper, 60, 15, 0f, TemperProtection.Lucky), "a Lucky Failsafe adds 5%");
        }

        [Test]
        public void AFailTakesThreePercentAndOnlyBreaksFromSixty()
        {
            TemperSettings temper = settings.temper;
            AlloyGrade alloy = Alloy(temper, 6);
            Assert.AreEqual(0f, GearProgressionRules.BreakChance(temper, 59));
            Assert.AreEqual(5f, GearProgressionRules.BreakChance(temper, 60));
            Assert.AreEqual(15f, GearProgressionRules.BreakChance(temper, 89), 0.001f);
            Assert.AreEqual(30f, GearProgressionRules.BreakChance(temper, 119), 0.001f);

            // Below 60 the worst roll there is still cannot break it.
            var next = GearProgressionRules.ResolveTemper(temper, new GearItemState(30, 0, 0), alloy, 50f, TemperProtection.None,
                successRoll: 0.99f, breakRoll: 0f, out GearWorkOutcome outcome, out bool spent);
            Assert.AreEqual(GearWorkOutcome.Fail, outcome);
            Assert.AreEqual(27, next.Temper);
            Assert.IsFalse(spent);

            next = GearProgressionRules.ResolveTemper(temper, new GearItemState(2, 0, 0), alloy, 50f, TemperProtection.None, 0.99f, 0.99f, out outcome, out _);
            Assert.AreEqual(0, next.Temper, "never below zero");

            GearProgressionRules.ResolveTemper(temper, new GearItemState(90, 0, 0), alloy, 10f, TemperProtection.None, 0.99f, 0f, out outcome, out _);
            Assert.AreEqual(GearWorkOutcome.Broken, outcome, "a fail at 90% with the break roll against it");
        }

        [Test]
        public void AFailsafeIsOnlySpentWhenItSavesThePiece()
        {
            TemperSettings temper = settings.temper;
            AlloyGrade alloy = Alloy(temper, 6);
            var state = new GearItemState(90, 2, 0);

            GearProgressionRules.ResolveTemper(temper, state, alloy, 10f, TemperProtection.Failsafe, 0f, 0f, out var outcome, out bool spent);
            Assert.AreEqual(GearWorkOutcome.Success, outcome);
            Assert.IsFalse(spent, "a success leaves the Failsafe");

            var next = GearProgressionRules.ResolveTemper(temper, state, alloy, 10f, TemperProtection.Failsafe, 0.99f, 0.99f, out outcome, out spent);
            Assert.AreEqual(GearWorkOutcome.Fail, outcome);
            Assert.IsFalse(spent, "a fail that would not have broken it leaves the Failsafe");
            Assert.AreEqual(87, next.Temper);

            next = GearProgressionRules.ResolveTemper(temper, state, alloy, 10f, TemperProtection.Failsafe, 0.99f, 0f, out outcome, out spent);
            Assert.AreEqual(GearWorkOutcome.FailProtected, outcome);
            Assert.IsTrue(spent, "the fail that would have broken it uses the Failsafe up");
            Assert.AreEqual(87, next.Temper, "it still costs the 3%");
            Assert.AreEqual(2, next.Fusion, "and nothing else");

            next = GearProgressionRules.ResolveTemper(temper, state, alloy, 10f, TemperProtection.Advanced, 0.99f, 0f, out outcome, out spent);
            Assert.AreEqual(GearWorkOutcome.FailHeld, outcome, "held, with a look of its own: not the fail a plain Failsafe softens");
            Assert.IsTrue(spent);
            Assert.AreEqual(90, next.Temper, "an Advanced Failsafe: a fail costs nothing");
        }

        [Test]
        public void TemperStopsAtOneHundredAndTwenty()
        {
            TemperSettings temper = settings.temper;
            ItemDefinition weapon = Gear(EquipSlot.Weapon, ItemRarity.Epic, 100);
            AlloyGrade small = Alloy(temper, 3), big = Alloy(temper, 15), prime = Alloy(temper, 3, prime: true);
            Assert.AreEqual(120, temper.hardCap, "120% is the cap for now");
            Assert.IsFalse(GearProgressionRules.PrimeAlloyInUse(temper));

            var next = GearProgressionRules.ResolveTemper(temper, new GearItemState(110, 0, 0), big, 100f, TemperProtection.None, 0f, 0f, out _, out _);
            Assert.AreEqual(120, next.Temper, "110 + 15 stops at 120");
            Assert.IsNull(GearProgressionRules.TemperBlockedReason(settings, weapon, new GearItemState(117, 0, 0), small, TemperProtection.None));

            var atCap = new GearItemState(120, 0, 0);
            Assert.IsNotNull(GearProgressionRules.TemperBlockedReason(settings, weapon, atCap, small, TemperProtection.None), "nothing works at the cap");
            Assert.IsNotNull(GearProgressionRules.TemperBlockedReason(settings, weapon, atCap, prime, TemperProtection.Advanced), "not even Prime Alloy");
            Assert.IsNotNull(GearProgressionRules.TemperBlockedReason(settings, weapon, new GearItemState(60, 0, 0), prime, TemperProtection.None),
                "Prime Alloy has no use while the cap is 120");
        }

        /// <summary>The system for later: with the cap raised, Prime Alloy carries a piece from 120% to 150%.</summary>
        [Test]
        public void RaisingTheCapBringsPrimeAlloyBack()
        {
            var later = ScriptableObject.CreateInstance<GearProgressionSettings>();
            try
            {
                later.statTable = settings.statTable;
                later.temper.hardCap = 150;
                TemperSettings temper = later.temper;
                ItemDefinition weapon = Gear(EquipSlot.Weapon, ItemRarity.Epic, 100);
                AlloyGrade big = Alloy(temper, 15), prime = Alloy(temper, 3, prime: true);
                Assert.IsTrue(GearProgressionRules.PrimeAlloyInUse(temper));

                var next = GearProgressionRules.ResolveTemper(temper, new GearItemState(110, 0, 0), big, 100f, TemperProtection.None, 0f, 0f, out _, out _);
                Assert.AreEqual(120, next.Temper, "ordinary Alloy still stops at 120");
                Assert.IsNotNull(GearProgressionRules.TemperBlockedReason(later, weapon, new GearItemState(120, 0, 0), big, TemperProtection.None));
                Assert.IsNull(GearProgressionRules.TemperBlockedReason(later, weapon, new GearItemState(120, 0, 0), prime, TemperProtection.None));
                Assert.IsNotNull(GearProgressionRules.TemperBlockedReason(later, weapon, new GearItemState(60, 0, 0), prime, TemperProtection.None),
                    "Prime Alloy is not for pieces below 120");
                Assert.IsNotNull(GearProgressionRules.TemperBlockedReason(later, weapon, new GearItemState(133, 0, 0), prime, TemperProtection.Failsafe),
                    "from 133% only an Advanced Failsafe will do");
                Assert.IsNull(GearProgressionRules.TemperBlockedReason(later, weapon, new GearItemState(133, 0, 0), prime, TemperProtection.Advanced));
                Assert.IsNotNull(GearProgressionRules.TemperBlockedReason(later, weapon, new GearItemState(150, 0, 0), prime, TemperProtection.Advanced),
                    "150% is the end");

                next = GearProgressionRules.ResolveTemper(temper, new GearItemState(149, 0, 0), prime, 100f, TemperProtection.Advanced, 0f, 0f, out _, out _);
                Assert.AreEqual(150, next.Temper);
            }
            finally
            {
                Object.DestroyImmediate(later);
            }
        }

        [Test]
        public void GlowBandsAreGreenYellowBluePurpleOrange()
        {
            TemperSettings temper = settings.temper;
            Assert.IsFalse(GearProgressionRules.TryGetGlow(temper, 0, out _), "an untouched piece does not glow");
            string Band(int percent) => GearProgressionRules.TryGetGlow(temper, percent, out TemperGlowBand band) ? band.name : null;
            Assert.AreEqual("Green", Band(1));
            Assert.AreEqual("Green", Band(29));
            Assert.AreEqual("Yellow", Band(30));
            Assert.AreEqual("Yellow", Band(59));
            Assert.AreEqual("Blue", Band(60));
            Assert.AreEqual("Purple", Band(90));
            Assert.AreEqual("Purple", Band(119));
            Assert.AreEqual("Orange", Band(120));
            Assert.AreEqual("Orange", Band(150));
            Assert.Greater(GearProgressionRules.GlowLookOf(temper, ItemRarity.Epic).intensity,
                GearProgressionRules.GlowLookOf(temper, ItemRarity.Rare).intensity * 2f, "an Epic shows the same colour far brighter");
        }

        [Test]
        public void AnEpicAtTheCapGlowsRedWhereARareGlowsOrange()
        {
            TemperSettings temper = settings.temper;
            Assert.IsTrue(GearProgressionRules.TryGetGlow(temper, 120, ItemRarity.Rare, out Color rare, out TemperGlowLook rareLook));
            Assert.IsTrue(GearProgressionRules.TryGetGlow(temper, 120, ItemRarity.Epic, out Color epic, out TemperGlowLook epicLook));
            Assert.IsTrue(GearProgressionRules.TryGetGlow(temper, 120, out TemperGlowBand orange));
            Assert.AreEqual(orange.color, rare, "a Rare keeps the band's own orange");
            Assert.AreNotEqual(rare, epic);
            Assert.Greater(epic.r, 0.9f);
            Assert.Less(epic.g, 0.25f, "red, not orange");
            Assert.Less(epic.b, 0.25f);
            Assert.AreEqual(epic, orange.ColorFor(ItemRarity.Legendary), "and so does everything above an Epic");

            // Below the cap a rarity changes the look, never the colour.
            foreach (int percent in new[] { 1, 30, 60, 90, 119 })
            {
                Assert.IsTrue(GearProgressionRules.TryGetGlow(temper, percent, ItemRarity.Rare, out Color low, out TemperGlowLook lowLook));
                Assert.IsTrue(GearProgressionRules.TryGetGlow(temper, percent, ItemRarity.Epic, out Color lowEpic, out TemperGlowLook lowEpicLook));
                Assert.AreEqual(low, lowEpic, $"{percent}%");
                Assert.AreEqual(0f, lowLook.ember, $"{percent}%: embers are the cap's own");
                Assert.AreEqual(0f, lowEpicLook.ember, $"{percent}%");
            }

            // At the cap both burn, and they are still two looks: only the Epic has tongues, lightning and sparks.
            Assert.Greater(rareLook.ember, 0f);
            Assert.Greater(epicLook.ember, 0f);
            Assert.AreEqual(0f, rareLook.flame);
            Assert.AreEqual(0f, rareLook.lightning);
            Assert.AreEqual(0f, rareLook.twinkle);
            Assert.Greater(epicLook.flame, 0f);
            Assert.Greater(epicLook.lightning, 0f);
            Assert.IsFalse(GearProgressionRules.TryGetGlow(temper, 0, ItemRarity.Epic, out _, out _), "an untouched piece does not glow");
        }

        [Test]
        public void EveryGlowBandStartsExactlyWhereTheSettingsSay()
        {
            // The forge's rim and ring take the band's colour: one percent short of a band is still the band below.
            TemperSettings temper = settings.temper;
            TemperGlowBand last = default;
            for (int i = 0; i < temper.glowBands.Length; i++)
            {
                TemperGlowBand band = temper.glowBands[i];
                Assert.IsTrue(GearProgressionRules.TryGetGlow(temper, band.from, out TemperGlowBand at));
                Assert.AreEqual(band.color, at.color, $"{band.name} starts at {band.from}%");
                bool below = GearProgressionRules.TryGetGlow(temper, band.from - 1, out TemperGlowBand under);
                if (i == 0) Assert.IsFalse(below, "nothing glows below the first band");
                else Assert.AreEqual(last.color, under.color, $"{band.from - 1}% is still {last.name}");
                last = band;
            }
        }

        [Test]
        public void ReachingTheAnnouncedTemperIsNewsOnce()
        {
            TemperSettings temper = settings.temper;
            Assert.AreEqual(120, temper.announceFrom);
            Assert.IsTrue(GearProgressionRules.TemperIsAnnounced(temper, 119, 120));
            Assert.IsTrue(GearProgressionRules.TemperIsAnnounced(temper, 108, 123), "a big Alloy that jumps past it");
            Assert.IsFalse(GearProgressionRules.TemperIsAnnounced(temper, 105, 117), "not there yet");
            Assert.IsFalse(GearProgressionRules.TemperIsAnnounced(temper, 120, 120), "already there");
            Assert.IsFalse(GearProgressionRules.TemperIsAnnounced(temper, 120, 135), "announced when it got there, not again above");
            temper.announceFrom = 0;
            Assert.IsFalse(GearProgressionRules.TemperIsAnnounced(temper, 119, 120), "0 switches it off");
            StringAssert.Contains("Tolan", GearProgressionRules.TemperAnnouncement("Tolan", "Sunlord Monoblade", 120));
            StringAssert.Contains("Sunlord Monoblade", GearProgressionRules.TemperAnnouncement("Tolan", "Sunlord Monoblade", 120));
        }

        // ---------------------------------------------------------------- what the state is worth

        private float Total(ItemDefinition item, GearItemState state, StatType stat) =>
            GearStatResolver.Total(new ItemStack(item.itemId, 1, state.Pack()), item, stat, settings);

        [Test]
        public void TemperRaisesWhatEachSlotIsFor()
        {
            ItemDefinition weapon = Gear(EquipSlot.Weapon, ItemRarity.Epic, 100);
            Assert.AreEqual(884f, Total(weapon, default, StatType.AttackPower), "the owner's table, untouched");
            Assert.AreEqual(884f * 2.2f, Total(weapon, new GearItemState(120, 0, 0), StatType.AttackPower), 0.01f, "Attack Power x (1 + 120%)");

            ItemDefinition gloves = Gear(EquipSlot.Gloves, ItemRarity.Epic, 100);
            Assert.AreEqual(100f * 1.5f, Total(gloves, new GearItemState(50, 0, 0), StatType.HitRate), 0.01f, "Gloves: Hit Rate");
            Assert.AreEqual(94f, Total(gloves, new GearItemState(50, 0, 0), StatType.DefensePower), 0.01f, "their Defense is left alone");

            ItemDefinition boots = Gear(EquipSlot.Boots, ItemRarity.Epic, 100);
            Assert.AreEqual(35f * 1.5f, Total(boots, new GearItemState(50, 0, 0), StatType.EvasionRate), 0.01f, "Boots: Evasion");

            ItemDefinition ring = Gear(EquipSlot.Ring, ItemRarity.Rare, 45);
            Assert.AreEqual(6f, Total(ring, new GearItemState(120, 0, 0), StatType.CriticalRate), 0.001f, "Ring: Critical");

            ItemDefinition amulet = Gear(EquipSlot.Amulet, ItemRarity.Uncommon, 40);
            float baked = GearStatResolver.Total(amulet, StatType.Luck);
            Assert.Greater(baked, 0f, "Uncommon gear carries Luck");
            Assert.AreEqual(baked + 4f * 1.2f * 120f, Total(amulet, new GearItemState(120, 0, 0), StatType.Luck), 0.01f,
                "Amulet: Luck, more of it the higher the Temper");
        }

        [Test]
        public void OnlyCheapGearCarriesLuck()
        {
            Assert.Greater(GearStatResolver.Total(Gear(EquipSlot.Armor, ItemRarity.Common, 28), StatType.Luck), 0f);
            Assert.Greater(GearStatResolver.Total(Gear(EquipSlot.Armor, ItemRarity.Uncommon, 40), StatType.Luck), 0f);
            Assert.AreEqual(0f, GearStatResolver.Total(Gear(EquipSlot.Armor, ItemRarity.Rare, 45), StatType.Luck));
            Assert.AreEqual(0f, GearStatResolver.Total(Gear(EquipSlot.Armor, ItemRarity.Epic, 100), StatType.Luck));
        }

        [Test]
        public void HotStreakKeepsTheTwelveSkyTwoSlope()
        {
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(HotStreakPath);
            Assert.IsNotNull(skill, HotStreakPath);
            Assert.AreEqual("Hot Streak", skill.displayName);
            Assert.AreEqual(30, skill.maxRank);
            Assert.AreEqual(13f, skill.GetLuckBonus(1), 0.001f, "rank 1");
            Assert.AreEqual(100f, skill.GetLuckBonus(30), 0.001f, "rank 30");
        }

        [Test]
        public void FusionAndTuningAddTheirOwnBonuses()
        {
            ItemDefinition weapon = Gear(EquipSlot.Weapon, ItemRarity.Epic, 100);
            Assert.AreEqual(884f * 1.36f, Total(weapon, new GearItemState(0, 12, 0), StatType.AttackPower), 0.01f, "FS12: +36% of its Attack Power");
            Assert.AreEqual(250f * 1.24f, Total(weapon, new GearItemState(0, 12, 0), StatType.HitRate), 0.01f, "FS12: +24% of its Hit Rate");
            Assert.AreEqual(25f, Total(weapon, new GearItemState(0, 0, 25), StatType.CriticalRate), 0.001f, "Tune 25: +1% Critical a stage");
            Assert.AreEqual(25f * 5f + 5f, Total(weapon, new GearItemState(0, 0, 25), StatType.Spirit), 0.001f, "and Spirit, with the last stage's bonus");
            Assert.AreEqual(24f * 5f, Total(weapon, new GearItemState(0, 0, 24), StatType.Spirit), 0.001f, "no bonus before the last stage");

            ItemDefinition ring = Gear(EquipSlot.Ring, ItemRarity.Rare, 45);
            float baked = GearStatResolver.Total(ring, StatType.ElementalAttack);
            Assert.AreEqual(baked + 8f * 1.45f * 3f, Total(ring, new GearItemState(0, 3, 0), StatType.ElementalAttack), 0.01f, "Ring: Elemental x rarity");
        }

        // ---------------------------------------------------------------- Fusion

        [Test]
        public void AnEpicTakesTheClosestRareTierAtOrBelowItsLevel()
        {
            GearStatTable table = settings.statTable;
            int Needed(int epicLevel) => GearProgressionRules.FusionMaterialLevel(table, Gear(EquipSlot.Armor, ItemRarity.Epic, epicLevel));
            Assert.AreEqual(95, Needed(100));
            Assert.AreEqual(95, Needed(103));
            Assert.AreEqual(105, Needed(106));
            Assert.AreEqual(105, Needed(109));
            Assert.AreEqual(110, Needed(112));
            Assert.AreEqual(45, GearProgressionRules.FusionMaterialLevel(table, Gear(EquipSlot.Armor, ItemRarity.Rare, 45)), "a Rare takes its own level");
        }

        [Test]
        public void FusionAcceptsOnlyTheRightUntouchedRare()
        {
            var ready = new GearItemState(12, 0, 0); // an Epic is fused at 12% Temper, like a Rare
            ItemDefinition epic112 = Gear(EquipSlot.Armor, ItemRarity.Epic, 112), epic106 = Gear(EquipSlot.Armor, ItemRarity.Epic, 106);
            ItemDefinition rare110 = Gear(EquipSlot.Armor, ItemRarity.Rare, 110), rare105 = Gear(EquipSlot.Armor, ItemRarity.Rare, 105),
                rare95 = Gear(EquipSlot.Armor, ItemRarity.Rare, 95);
            string Blocked(ItemDefinition target, GearItemState targetState, ItemDefinition material, GearItemState materialState = default) =>
                GearProgressionRules.FusionBlockedReason(settings, target, targetState, material, materialState);

            Assert.IsNull(Blocked(epic112, ready, rare110), "Epic 112 takes Rare 110");
            Assert.IsNotNull(Blocked(epic112, ready, rare105), "and not Rare 105");
            Assert.IsNull(Blocked(epic106, ready, rare105), "Epic 106 takes Rare 105");
            Assert.IsNotNull(Blocked(epic106, ready, rare95), "not a lower tier");
            Assert.IsNotNull(Blocked(epic106, ready, rare110), "not a higher one");
            Assert.IsNotNull(Blocked(epic112, ready, epic112), "an Epic is never fed an Epic");
            Assert.IsNotNull(Blocked(epic112, new GearItemState(9, 0, 0), rare110), "an Epic needs 12% Temper");
            Assert.IsNotNull(Blocked(epic112, ready, rare110, new GearItemState(3, 0, 0)), "a tempered material is refused");
            Assert.IsNotNull(Blocked(epic112, ready, rare110, new GearItemState(0, 1, 0)), "a fused material is refused");
            Assert.IsNotNull(Blocked(epic112, ready, Gear(EquipSlot.Boots, ItemRarity.Rare, 110)), "another kind of piece is refused");
            Assert.IsNotNull(Blocked(epic112, new GearItemState(120, 12, 0), rare110), "FS12 is the end");

            ItemDefinition rare45 = Gear(EquipSlot.Armor, ItemRarity.Rare, 45);
            Assert.IsNull(Blocked(rare45, new GearItemState(12, 0, 0), rare45), "a Rare takes a Rare of its level at 12% Temper");
            Assert.IsNotNull(Blocked(rare45, new GearItemState(9, 0, 0), rare45));
            Assert.IsNotNull(Blocked(Gear(EquipSlot.Armor, ItemRarity.Uncommon, 40), ready, rare45), "Uncommon gear is not fused");

            ItemDefinition blade = Gear(EquipSlot.Weapon, ItemRarity.Rare, 45, WeaponClassId.Monoblade);
            Assert.IsNotNull(Blocked(blade, new GearItemState(12, 0, 0), Gear(EquipSlot.Weapon, ItemRarity.Rare, 45, WeaponClassId.Glaive)),
                "a weapon takes a weapon of its own class");
        }

        [Test]
        public void ASuccessfulFusionCostsTwelveTemperAndTheOddsFallAfterStageThree()
        {
            FusionSettings fusion = settings.fusion;
            GearItemState next = GearProgressionRules.ResolveFusion(fusion, new GearItemState(132, 4, 7));
            Assert.AreEqual(new GearItemState(120, 5, 7), next);
            Assert.AreEqual(0, GearProgressionRules.ResolveFusion(fusion, new GearItemState(5, 0, 0)).Temper, "never below zero");

            Assert.AreEqual(100f, GearProgressionRules.FusionChance(fusion, 3, 0f));
            Assert.AreEqual(93f, GearProgressionRules.FusionChance(fusion, 4, 0f));
            Assert.AreEqual(37f, GearProgressionRules.FusionChance(fusion, 12, 0f));
            Assert.AreEqual(100_000, GearProgressionRules.FusionCost(fusion, 1));
            Assert.AreEqual(650_000, GearProgressionRules.FusionCost(fusion, 12));
            Assert.IsTrue(fusion.failConsumesMaterial, "a failed Fusion destroys the material");
        }

        // ---------------------------------------------------------------- Tuning and Reforge

        [Test]
        public void TuningDropsAStageOnAFailOnlyFromThirteen()
        {
            TuningSettings tuning = settings.tuning;
            Assert.AreEqual(100f, GearProgressionRules.TuneChance(tuning, 10));
            Assert.AreEqual(95f, GearProgressionRules.TuneChance(tuning, 11));
            Assert.AreEqual(25f, GearProgressionRules.TuneChance(tuning, 25));
            Assert.AreEqual(8, GearProgressionRules.ResolveTune(tuning, new GearItemState(0, 0, 7), true).Tune);
            Assert.AreEqual(12, GearProgressionRules.ResolveTune(tuning, new GearItemState(0, 0, 12), false).Tune, "stages 1-12 never drop");
            Assert.AreEqual(12, GearProgressionRules.ResolveTune(tuning, new GearItemState(0, 0, 13), false).Tune, "13 and up drop one");

            Assert.IsTrue(GearProgressionRules.TryGetBracket(tuning, 1, out TuningBracket first));
            Assert.AreEqual(GearMaterialIds.TuningChip, first.chipItemId);
            Assert.AreEqual(10, first.renown);
            Assert.IsTrue(GearProgressionRules.TryGetBracket(tuning, 21, out TuningBracket last));
            Assert.AreEqual(2, last.chips);
            Assert.AreEqual(GearMaterialIds.VioletChip, last.chipItemId);

            Assert.IsNotNull(GearProgressionRules.TuneBlockedReason(settings, Gear(EquipSlot.Armor, ItemRarity.Uncommon, 40), default));
            Assert.IsNull(GearProgressionRules.TuneBlockedReason(settings, Gear(EquipSlot.Armor, ItemRarity.Rare, 45), default));
            Assert.IsNotNull(GearProgressionRules.TuneBlockedReason(settings, Gear(EquipSlot.Armor, ItemRarity.Rare, 45), new GearItemState(0, 0, 25)));
        }

        [Test]
        public void ReforgeTakesAnUntouchedRareAndOffersOnlyWearableRaresOfItsLevel()
        {
            ItemDefinition rare = Gear(EquipSlot.Armor, ItemRarity.Rare, 45);
            Assert.IsNull(GearProgressionRules.ReforgeBlockedReason(settings, rare, default));
            Assert.IsNotNull(GearProgressionRules.ReforgeBlockedReason(settings, rare, new GearItemState(3, 0, 0)));
            Assert.IsNotNull(GearProgressionRules.ReforgeBlockedReason(settings, Gear(EquipSlot.Armor, ItemRarity.Epic, 100), default));
            Assert.AreEqual(1_000_000, GearProgressionRules.ReforgeCost(settings.reforge, 45));
            Assert.AreEqual(2_200_000, GearProgressionRules.ReforgeCost(settings.reforge, 105));
            Assert.AreEqual(2_300_000, GearProgressionRules.ReforgeCost(settings.reforge, 110));

            Assert.IsTrue(dictionary.TryGet(rare.itemId, out GearDictionary.Entry entry));
            FactionId faction = FactionId.None;
            foreach (FactionId candidate in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
                if (FactionRules.Includes(entry.factions, candidate)) { faction = candidate; break; }
            var results = new List<ushort>();
            GearProgressionRules.CollectReforgeResults(dictionary, rare, faction, entry.bodies, results);
            Assert.IsNotEmpty(results);
            CollectionAssert.DoesNotContain(results, rare.itemId, "never the same piece back");
            foreach (ushort id in results)
            {
                Assert.IsTrue(dictionary.TryGet(id, out GearDictionary.Entry result));
                Assert.AreEqual(ItemRarity.Rare, result.rarity);
                Assert.AreEqual(45, result.itemLevel);
                Assert.IsTrue(dictionary.CanUse(id, faction, entry.bodies, out _), $"{result.name} must be wearable by the same character");
            }
        }
    }
}
