using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using KryticalNova.EditorTools;
using KryticalNova.Factions;
using KryticalNova.UI;
using KryticalNova.UI.Menus;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The character creator in every faction's skin: each skin has a creator of its own packs (its own screen, or
    /// every part one is assembled from), and whichever it is, the creator scene hands each skin's controller every
    /// part it works with.
    /// </summary>
    public sealed class CreatorScreenTests
    {
        private static readonly FactionId[] Factions = FactionSkinBuilder.Factions;

        [Test]
        public void EverySkin_HasACreatorOfItsOwnPack()
        {
            foreach (FactionId faction in Factions)
            {
                UiSkin skin = FactionSkinBuilder.LoadSkin(faction);
                Assert.NotNull(skin, FactionSkinBuilder.SkinPath(faction));
                Assert.AreEqual(FactionSkinBuilder.CreatorTabCount, skin.creatorTabIcons.Length, faction + " tab pictures");
                foreach (Sprite picture in skin.creatorTabIcons) Assert.NotNull(picture, faction + " has a tab without a picture");
                if (skin.creatorScreen != null) continue;

                foreach ((string part, GameObject prefab) in new[]
                         {
                             ("tab strip", skin.tabStrip), ("framed panel", skin.framedPanel), ("icon button", skin.iconButton),
                             ("option row", skin.optionRow), ("slider row", skin.sliderRow), ("text field", skin.textField),
                             ("section heading", skin.sectionHeader), ("headline label", skin.headerLabel), ("body label", skin.bodyLabel),
                             ("start button", skin.startButton), ("footer", skin.footer),
                         })
                    Assert.NotNull(prefab, $"{faction} has no creator screen and no {part} to assemble one from");
                Assert.AreEqual(FactionSkinBuilder.TraitCount, skin.traitIcons.Length, faction + " trait pictures");
                foreach (Sprite picture in skin.traitIcons) Assert.NotNull(picture, faction + " has a trait without a picture");
            }
        }

        [Test]
        public void NoTwoFactions_ShareACreator()
        {
            for (int a = 0; a < Factions.Length; a++)
                for (int b = a + 1; b < Factions.Length; b++)
                {
                    UiSkin one = FactionSkinBuilder.LoadSkin(Factions[a]), other = FactionSkinBuilder.LoadSkin(Factions[b]);
                    string pair = $"{Factions[a]} and {Factions[b]}";
                    if (one.creatorScreen != null || other.creatorScreen != null) Assert.AreNotSame(one.creatorScreen, other.creatorScreen, pair + " wear one creator screen");
                    Assert.AreNotSame(one.tabStrip, other.tabStrip, pair + " share a tab strip");
                    Assert.AreNotSame(one.framedPanel, other.framedPanel, pair + " share a framed panel");
                    Assert.AreNotSame(one.iconButton, other.iconButton, pair + " share an icon button");
                }
        }

        // What the controller works with, and how many of each it is handed.
        private static readonly string[] Parts =
        {
            "screen", "database", "showroom", "portraits", "hotkeys", "originSection", "optionsSection", "colourSection", "finaliseSection",
            "classes", "gear", "items", "nameInput", "nameSuggestion", "nameMessage", "finaliseButton",
        };

        private static readonly (string list, int count)[] Lists =
        {
            ("tabButtons", FactionSkinBuilder.CreatorTabCount), ("originTiles", 4), ("originPortraits", 4), ("optionRows", 9), ("sliderRows", 5), ("swatchGroups", 3),
        };

        [Test]
        public void TheCreatorScene_HandsEverySkinsControllerItsParts()
        {
            string scene = File.ReadAllText(FrontEndBuilder.ScenePath(FrontEnd.CharacterCreatorScene)).Replace("\r\n", "\n");
            var controllers = new List<string>();
            foreach (string component in scene.Split(new[] { "\n--- !u!" }, System.StringSplitOptions.None))
                if (component.Contains("\n  tabButtons:") && component.Contains("\n  swatchGroups:")) controllers.Add(component);
            Assert.AreEqual(Factions.Length, controllers.Count, "one creator controller per faction skin");

            foreach (string controller in controllers)
            {
                foreach (string part in Parts)
                    StringAssert.IsMatch($@"\n  {part}: \{{fileID: -?[1-9]", controller, $"a creator controller has no '{part}'");
                foreach ((string list, int count) in Lists)
                {
                    Match found = Regex.Match(controller, $@"\n  {list}:\n((?:  - \{{fileID: -?\d+[^\n]*\n)+)");
                    Assert.IsTrue(found.Success, $"a creator controller has no '{list}'");
                    string[] entries = found.Groups[1].Value.TrimEnd('\n').Split('\n');
                    Assert.AreEqual(count, entries.Length, $"'{list}' of a creator controller");
                    foreach (string entry in entries) StringAssert.DoesNotContain("fileID: 0", entry, $"'{list}' of a creator controller has an empty place");
                }
            }
        }
    }
}
