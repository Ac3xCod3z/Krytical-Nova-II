using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using KryticalNova.EditorTools;
using KryticalNova.UI;
using KryticalNova.UI.Guide;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Guide page of Settings: that its library is whole (every topic can be read, every key and number its
    /// text names exists), that it speaks the game's own words, and that the page is skinned like the rest.
    /// </summary>
    public sealed class GuideTests
    {
        // Twelve Sky's words for things the game has its own name for (the Glossary's left column): never in player text.
        private static readonly string[] ForeignTerms =
        {
            "Reinforce", "Enchant", "Combine", "Combination", "Refine", "Guild", "Party", "Duel", "Silver", "Chi ",
            "Blacksmith", "Private Shop", "Friend list", "Warehouse", "Pill", "Elixir", "Monster", "Mob "
        };

        private static GuideLibrary Library() => AssetDatabase.LoadAssetAtPath<GuideLibrary>(HudSetup.GuideLibraryPath);

        private static IEnumerable<string> LinesOf(GuideTopic topic)
        {
            yield return topic.title;
            yield return topic.summary;
            foreach (GuideSection section in topic.sections)
            {
                if (!string.IsNullOrEmpty(section.heading)) yield return section.heading;
                foreach (string line in section.lines) yield return line;
            }
        }

        [Test]
        public void EveryTopic_CanBeRead()
        {
            GuideLibrary library = Library();
            Assert.IsNotNull(library, "no Guide library at " + HudSetup.GuideLibraryPath);
            var ids = new HashSet<string>();
            int topics = 0;
            foreach (GuideChapter chapter in library.chapters)
            {
                Assert.IsNotEmpty(chapter.title, "a chapter has no title");
                foreach (GuideTopic topic in chapter.topics)
                {
                    topics++;
                    Assert.IsTrue(Regex.IsMatch(topic.id ?? string.Empty, "^[a-z0-9-]+$"), $"'{topic.id}' is not an id (lower case, digits, dashes)");
                    Assert.IsTrue(ids.Add(topic.id), topic.id + " is used twice");
                    Assert.IsNotEmpty(topic.title, topic.id + " has no title");
                    Assert.IsNotEmpty(topic.summary, topic.id + " has no summary");
                    Assert.IsNotEmpty(topic.sections, topic.id + " has no sections");
                    foreach (GuideSection section in topic.sections)
                    {
                        Assert.IsNotEmpty(section.lines, $"{topic.id}: the section '{section.heading}' is empty");
                        if (section.kind != GuideSectionKind.Table) continue;
                        foreach (string line in section.lines)
                            StringAssert.Contains("|", line, $"{topic.id}: a table line needs 'name | value'");
                    }
                }
            }
            Assert.Greater(topics, 0, "the Guide has no topics");
        }

        [Test]
        public void EveryKeyAndNumberTheTextNames_Exists()
        {
            GuideLibrary library = Library();
            var tokens = new List<GuideText.Token>();
            foreach (GuideTopic topic in library.Topics)
                foreach (string line in LinesOf(topic))
                {
                    GuideText.Parse(line, tokens);
                    foreach (GuideText.Token token in tokens)
                    {
                        switch (token.Kind)
                        {
                            case GuideText.TokenKind.Key:
                                Assert.IsTrue(GuideText.IsKnownKey(token.Value), $"{topic.id}: no key named '{token.Value}'");
                                break;
                            case GuideText.TokenKind.Mouse:
                                Assert.IsTrue(GuideText.IsKnownMouse(token.Value), $"{topic.id}: no mouse button named '{token.Value}'");
                                break;
                            case GuideText.TokenKind.Fact:
                                Assert.IsTrue(library.TryFact(token.Value, out _), $"{topic.id}: the fact '{token.Value}' cannot be read");
                                break;
                            case GuideText.TokenKind.Text:
                                StringAssert.DoesNotContain("{", token.Value, $"{topic.id}: a token is misspelt in '{line}'");
                                break;
                        }
                    }
                }
        }

        [Test]
        public void EveryFact_ReadsFromItsSettingsAsset()
        {
            GuideLibrary library = Library();
            var names = new HashSet<string>();
            foreach (GuideFact fact in library.facts)
            {
                Assert.IsTrue(names.Add(fact.name), fact.name + " is listed twice");
                Assert.IsTrue(library.TryFact(fact.name, out string text), $"{fact.name}: '{fact.field}' cannot be read");
                Assert.IsNotEmpty(text);
            }
        }

        [Test]
        public void TheText_SpeaksTheGamesOwnWords()
        {
            foreach (GuideTopic topic in Library().Topics)
                foreach (string line in LinesOf(topic))
                    foreach (string term in ForeignTerms)
                        StringAssert.DoesNotContain(term, line, $"{topic.id} says '{term.Trim()}': use the Krytical Nova term");
        }

        [Test]
        public void ATopicWithoutAClip_IsOnTheList()
        {
            // Not a failure: the page shows the still, or an empty screen. The list is what is left to record.
            var missing = new List<string>();
            foreach (GuideTopic topic in Library().Topics)
                if (topic.clip == null) missing.Add(topic.id);
            if (missing.Count > 0) Assert.Inconclusive("No clip recorded yet for: " + string.Join(", ", missing));
        }

        [Test]
        public void TheGuidesSheet_IsSkinnedAndListedByEveryDocument()
        {
            const string sheet = "Assets/_Game/UI/Guide/Guide.uss";
            StringAssert.Contains(sheet, File.ReadAllText("Assets/_Game/UI/Editor/FactionSkinBuilder.cs"), "the Guide's sheet is not in the skin builder's list");
            foreach (string document in new[] { "Assets/_Game/UI/HUD/ProgressionHUD.uxml", "Assets/_Game/UI/Settings/MenuSettings.uxml" })
                StringAssert.Contains("Guide/Guide.uss", File.ReadAllText(document), document + " does not list the Guide's sheet");
        }

        [Test]
        public void TheGuide_IsAPageOfSettings_WithItsOwnKey()
        {
            CollectionAssert.Contains(SettingsPages.Pages, SettingsPages.GuidePageName);
            Assert.IsTrue(GuideText.IsKnownKey(KryticalNova.Core.KeyBindings.Guide), "the Guide's key is not on the Controls list");
        }
    }
}
