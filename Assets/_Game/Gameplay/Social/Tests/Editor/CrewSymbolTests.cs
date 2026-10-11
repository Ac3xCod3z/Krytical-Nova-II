using KryticalNova.Factions;
using KryticalNova.Social;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A Crew's symbol (owner, 11 Oct 2026): a white picture from its faction's own interface pack, which the Leader
    /// gives one of the Crew colours. Every faction has the same number to choose from.
    /// </summary>
    public sealed class CrewSymbolTests
    {
        private const string SettingsPath = "Assets/_Game/Gameplay/Social/Data/Assets/CrewSettings.asset";

        private static CrewSettings Settings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CrewSettings>(SettingsPath);
            Assert.NotNull(settings, SettingsPath);
            return settings;
        }

        [Test]
        public void EveryFactionHasAPictureForEverySymbol()
        {
            CrewSettings settings = Settings();
            Assert.NotNull(settings.symbols, "Crew Settings has its symbol library (UI > Build Crew Symbols)");
            foreach (FactionId faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
                for (int i = 0; i < settings.symbolCount; i++)
                    Assert.NotNull(settings.symbols.Get(faction, i), $"{faction} symbol {i}");
        }

        [Test]
        public void ASymbolsColourIsOneOfTheCrewColours_AndWhiteWhenThereIsNone()
        {
            CrewSettings settings = Settings();
            Assert.GreaterOrEqual(settings.symbolColours.Length, 8, "enough colours to tell Crews apart");
            Assert.AreEqual(Color.white, settings.symbolColours[0], "a new Crew's symbol is white until its Leader picks");
            Assert.AreEqual(settings.symbolColours[3], settings.SymbolColour(3));
            Assert.AreEqual(Color.white, settings.SymbolColour(-1));
            Assert.AreEqual(Color.white, settings.SymbolColour(settings.symbolColours.Length));
        }

        [Test]
        public void TheSymbolAndItsColourAreBothOnTheTagEveryoneSees()
        {
            var one = new CrewTag { symbol = 2, colour = 1 };
            var other = new CrewTag { symbol = 2, colour = 4 };
            Assert.IsFalse(one.Equals(other), "a new colour is a change of the tag, so it reaches every client");
        }
    }
}
