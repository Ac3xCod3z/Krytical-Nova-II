using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using KryticalNova.EditorTools;
using KryticalNova.Factions;
using KryticalNova.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The interface skins: which skin the menus wear (the Menu Theme setting and the pick made once a start of the
    /// game), that every faction has a whole skin asset, and that the in-game UI's skin sheets are what those assets
    /// say (so a value changed in an asset is the value on screen), without touching the colours that mean something
    /// in play.
    /// </summary>
    public sealed class UiSkinTests
    {
        private static readonly FactionId[] Factions = { FactionId.Hegemony, FactionId.Null, FactionId.Covenant };

        // ---------------------------------------------------------------- the menus' skin

        [Test]
        public void AFactionInMenuTheme_LocksTheMenusToIt_WhateverThePick()
        {
            foreach (FactionId rolled in Factions)
            {
                Assert.AreEqual(FactionId.Hegemony, MenuSkin.Resolve(MenuTheme.Hegemony, rolled));
                Assert.AreEqual(FactionId.Null, MenuSkin.Resolve(MenuTheme.Null, rolled));
                Assert.AreEqual(FactionId.Covenant, MenuSkin.Resolve(MenuTheme.Covenant, rolled));
            }
        }

        [Test]
        public void OnRotate_TheMenusWearTheRunsPick()
        {
            foreach (FactionId rolled in Factions)
                Assert.AreEqual(rolled, MenuSkin.Resolve(MenuTheme.Rotate, rolled));
        }

        [Test]
        public void ThePick_IsOneOfTheThreeFactions_EachAboutAsOften()
        {
            var random = new System.Random(12345);
            var seen = new Dictionary<FactionId, int>();
            const int Rolls = 3000;
            for (int i = 0; i < Rolls; i++)
            {
                FactionId pick = MenuSkin.Roll(random);
                Assert.AreNotEqual(FactionId.None, pick);
                seen[pick] = seen.TryGetValue(pick, out int had) ? had + 1 : 1;
            }
            Assert.AreEqual(3, seen.Count, "a faction is never picked");
            foreach (KeyValuePair<FactionId, int> each in seen)
                Assert.That(each.Value, Is.InRange(Rolls / 3 - 150, Rolls / 3 + 150), each.Key + " is picked too often or too seldom");
        }

        [Test]
        public void ThePick_IsKeptForTheRun()
        {
            FactionId first = MenuSkin.Rolled;
            for (int i = 0; i < 20; i++) Assert.AreEqual(first, MenuSkin.Rolled);
            Assert.AreNotEqual(FactionId.None, first);
        }

        // ---------------------------------------------------------------- the skin assets

        [Test]
        public void EveryFaction_HasAWholeSkin()
        {
            foreach (FactionId faction in Factions)
            {
                UiSkin skin = FactionSkinBuilder.LoadSkin(faction);
                Assert.NotNull(skin, FactionSkinBuilder.SkinPath(faction));
                Assert.AreEqual(faction, skin.faction);
                Assert.NotNull(skin.bodyFont, faction + " body font");
                Assert.NotNull(skin.titleFont, faction + " title font");
                foreach ((string part, Sprite sprite) in new[]
                         {
                             ("panel", skin.panel), ("card", skin.card), ("token", skin.token), ("item slot", skin.itemSlot),
                             ("frame", skin.frame), ("small frame", skin.frameSmall), ("bar", skin.bar), ("lit pip", skin.pipOn),
                             ("unlit pip", skin.pipOff), ("close icon", skin.closeIcon), ("arrow icon", skin.arrowIcon), ("soft backing", skin.softBacking),
                         })
                    Assert.NotNull(sprite, faction + " has no " + part);
                foreach ((string part, GameObject prefab) in new[]
                         {
                             ("title screen", skin.titleScreen), ("choice screen", skin.choiceScreen), ("list screen", skin.listScreen),
                             ("loading screen", skin.loadingScreen), ("confirmation popup", skin.confirmPopup), ("option row", skin.optionRow),
                             ("body label", skin.bodyLabel),
                         })
                    Assert.NotNull(prefab, faction + " has no " + part);
            }
        }

        [Test]
        public void NoTwoFactions_ShareAPanelOrAFont()
        {
            for (int a = 0; a < Factions.Length; a++)
                for (int b = a + 1; b < Factions.Length; b++)
                {
                    UiSkin first = FactionSkinBuilder.LoadSkin(Factions[a]), second = FactionSkinBuilder.LoadSkin(Factions[b]);
                    Assert.AreNotSame(first.panel, second.panel, Factions[a] + " and " + Factions[b] + ": panel");
                    Assert.AreNotSame(first.frame, second.frame, Factions[a] + " and " + Factions[b] + ": frame");
                    Assert.AreNotSame(first.bodyFont, second.bodyFont, Factions[a] + " and " + Factions[b] + ": body font");
                    Assert.AreNotSame(first.titleScreen, second.titleScreen, Factions[a] + " and " + Factions[b] + ": title screen");
                }
        }

        // ---------------------------------------------------------------- the sheets made from them

        [Test]
        public void TheSkinSheets_AreWhatTheAssetsSay()
        {
            foreach (FactionId faction in Factions)
            {
                string path = FactionSkinBuilder.SheetPath(faction);
                Assert.IsTrue(File.Exists(path), path);
                string expected = FactionSkinBuilder.Generate(FactionSkinBuilder.LoadSkin(faction), out _);
                Assert.AreEqual(expected, File.ReadAllText(path),
                    path + " is out of date: run Krytical Nova > UI > Build Faction Skins (a base sheet or the skin asset changed)");
            }
        }

        [Test]
        public void ASkin_OnlyAppliesUnderItsFactionsClass()
        {
            var selector = new Regex(@"^([^{}/\s][^{}]*)\{", RegexOptions.Multiline);
            foreach (FactionId faction in Factions)
            {
                string css = "." + UiSkin.ClassOf(faction) + " ";
                string text = File.ReadAllText(FactionSkinBuilder.SheetPath(faction));
                foreach (Match match in selector.Matches(text))
                    foreach (string each in match.Groups[1].Value.Split(','))
                        StringAssert.StartsWith(css, each.Trim() + " ", faction + ": '" + each.Trim() + "' would change another faction's UI");
            }
        }

        [Test]
        public void ASkin_LeavesTheColoursOfPlayAlone()
        {
            // Rarity colours, and the comic ink and paper of a speech bubble, are the same in every skin.
            var rule = new Regex(@"([^{}]+)\{([^{}]*)\}");
            foreach (FactionId faction in Factions)
            {
                string text = File.ReadAllText(FactionSkinBuilder.SheetPath(faction));
                foreach (Match match in rule.Matches(text))
                {
                    string selectors = match.Groups[1].Value;
                    if (!selectors.Contains(".rarity-") && !selectors.Contains("-bubble")) continue;
                    StringAssert.DoesNotContain("rgb", match.Groups[2].Value, faction + " recolours " + selectors.Trim());
                }
            }
        }

        [Test]
        public void TheHudDocuments_ListEverySkinSheet()
        {
            foreach (string document in new[] { "Assets/_Game/UI/HUD/ProgressionHUD.uxml", "Assets/_Game/UI/NPC/NpcInteraction.uxml" })
            {
                string text = File.ReadAllText(document);
                foreach (FactionId faction in Factions)
                    StringAssert.Contains(Path.GetFileName(FactionSkinBuilder.SheetPath(faction)), text, document + " does not list the " + faction + " skin");
            }
        }
    }
}
