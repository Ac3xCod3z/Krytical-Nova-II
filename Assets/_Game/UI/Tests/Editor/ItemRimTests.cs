using KryticalNova.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Tests
{
    /// <summary>
    /// The rim round an item's icon: its rarity's colour (left to the style sheets), or for a tempered piece the
    /// colour of the glow band its Temper is in, up and down the bands.
    /// </summary>
    public sealed class ItemRimTests
    {
        private GearProgressionSettings settings;
        private ItemDefinition gear, stim;
        private VisualElement cell;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<GearProgressionSettings>();
            gear = ScriptableObject.CreateInstance<ItemDefinition>();
            gear.lineId = 1;
            gear.rarity = ItemRarity.Rare;
            stim = ScriptableObject.CreateInstance<ItemDefinition>();
            stim.rarity = ItemRarity.Uncommon;
            cell = GearUIController.CreateItemVisual(new VisualElement(), true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(gear);
            Object.DestroyImmediate(stim);
        }

        private StyleColor Rim() => cell.Q(className: "item-rim").style.unityBackgroundImageTintColor;

        private Color Band(int temper, ItemRarity rarity)
        {
            Assert.IsTrue(GearProgressionRules.TryGetGlow(settings.temper, temper, out TemperGlowBand band), $"no glow band at {temper} %");
            return band.ColorFor(rarity);
        }

        [Test]
        public void The_rim_lies_under_the_frame_that_shows_what_is_happening_to_the_cell()
        {
            VisualElement rim = cell.Q(className: "item-rim"), frame = cell.Q(className: "item-frame");
            Assert.IsNotNull(rim);
            Assert.IsNotNull(frame);
            Assert.Less(cell.IndexOf(rim), cell.IndexOf(frame));
            Assert.Greater(cell.IndexOf(rim), cell.IndexOf(cell.Q(className: "item-icon")));
        }

        [Test]
        public void An_untempered_piece_keeps_its_raritys_rim()
        {
            GearUIController.SetRim(cell, gear, 0, settings);
            Assert.AreEqual(StyleKeyword.Null, Rim().keyword);
        }

        [Test]
        public void A_tempered_pieces_rim_is_its_glow_bands_colour_in_every_band()
        {
            foreach (TemperGlowBand band in settings.temper.glowBands)
            {
                GearUIController.SetRim(cell, gear, band.from, settings);
                Assert.AreEqual(band.ColorFor(gear.rarity), Rim().value, $"at {band.from} % ({band.name})");
            }
        }

        [Test]
        public void The_rim_follows_the_Temper_down_as_well_as_up()
        {
            GearUIController.SetRim(cell, gear, 65, settings);
            Color high = Rim().value;
            GearUIController.SetRim(cell, gear, 35, settings); // a fail took it back into the band below
            Assert.AreEqual(Band(35, gear.rarity), Rim().value);
            Assert.AreNotEqual(high, Rim().value);
            GearUIController.SetRim(cell, gear, 0, settings);
            Assert.AreEqual(StyleKeyword.Null, Rim().keyword, "with no Temper left it is the rarity's again");
        }

        [Test]
        public void An_Epic_shows_a_band_as_its_rarity_does()
        {
            gear.rarity = ItemRarity.Epic;
            GearUIController.SetRim(cell, gear, 120, settings);
            Assert.AreEqual(Band(120, ItemRarity.Epic), Rim().value);
        }

        [Test]
        public void What_cannot_be_tempered_and_an_empty_cell_have_no_glow_rim()
        {
            GearUIController.SetRim(cell, gear, 65, settings);
            GearUIController.SetRim(cell, stim, 65, settings);
            Assert.AreEqual(StyleKeyword.Null, Rim().keyword, "an item that is not ladder gear");
            GearUIController.SetRim(cell, gear, 65, settings);
            GearUIController.SetRim(cell, null, 65, settings);
            Assert.AreEqual(StyleKeyword.Null, Rim().keyword, "an empty cell");
        }

        // A cell is used again for whatever comes to lie in it: the last piece's glow must not stay behind.
        [Test]
        public void A_cell_shown_again_with_another_item_drops_the_last_ones_glow()
        {
            GearUIController.SetRim(cell, gear, 95, settings);
            GearUIController.SetItemVisual(cell, stim, 3);
            Assert.AreEqual(StyleKeyword.Null, Rim().keyword);
            GearUIController.SetRim(cell, gear, 95, settings);
            GearUIController.SetItemVisual(cell, null, 0);
            Assert.AreEqual(StyleKeyword.Null, Rim().keyword);
        }
    }
}
