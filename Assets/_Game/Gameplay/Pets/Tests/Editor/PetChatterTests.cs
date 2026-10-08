using System.Collections.Generic;
using KryticalNova.Pets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// When a pet speaks and what it picks (never the same thing twice running, never too soon), and what the four
    /// pets of the project have to say: every pet reacts to every moment, with at least eight things to say.
    /// </summary>
    public sealed class PetChatterTests
    {
        private const string CatalogPath = "Assets/_Game/Gameplay/Pets/Data/Assets/PetCatalog.asset";
        private const string SettingsPath = "Assets/_Game/Gameplay/Pets/Data/Assets/PetChatterSettings.asset";
        private const int LongestLine = 38;

        private static readonly PetChatterTrigger[] Triggers =
        {
            PetChatterTrigger.OwnerDied, PetChatterTrigger.RareDrop, PetChatterTrigger.LevelUp, PetChatterTrigger.BruiserKill,
            PetChatterTrigger.CriticalHit, PetChatterTrigger.LowHealth, PetChatterTrigger.LongIdle,
        };

        private PetChatterSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<PetChatterSettings>();
            settings.quietSeconds = 8f;
            settings.repeatSeconds = new[] { 0f, 20f, 0f, 10f, 25f, 45f, 120f };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        // ------------------------------------------------------------------ picking

        [Test]
        public void TheSameThing_IsNeverPickedTwiceRunning()
        {
            for (int count = 2; count <= 10; count++)
                for (int last = 0; last < count; last++)
                    for (int step = 0; step < 200; step++)
                    {
                        int pick = PetChatterRules.Pick(count, last, step / 200f);
                        Assert.AreNotEqual(last, pick, $"{count} variants, last {last}, roll {step / 200f}");
                        Assert.IsTrue(pick >= 0 && pick < count);
                    }
        }

        [Test]
        public void EveryOtherVariant_CanBePicked_AndAboutAsOften()
        {
            const int count = 8, last = 3, rolls = 7000;
            var seen = new int[count];
            for (int i = 0; i < rolls; i++) seen[PetChatterRules.Pick(count, last, (i + 0.5f) / rolls)]++;
            Assert.AreEqual(0, seen[last]);
            for (int i = 0; i < count; i++)
                if (i != last) Assert.AreEqual(rolls / (count - 1), seen[i], 2, "variant " + i);
        }

        [Test]
        public void WithOneThingToSay_ItIsSaid_AndWithNothing_NothingIs()
        {
            Assert.AreEqual(0, PetChatterRules.Pick(1, 0, 0.7f));
            Assert.AreEqual(-1, PetChatterRules.Pick(0, -1, 0.7f));
            Assert.AreEqual(7, PetChatterRules.Pick(8, -1, 1f), "a roll of exactly 1 is still a pick");
        }

        [Test]
        public void TheMoreImportantMoment_Wins()
        {
            Assert.AreEqual(PetChatterTrigger.OwnerDied, PetChatterRules.MoreImportant(PetChatterTrigger.CriticalHit, PetChatterTrigger.OwnerDied));
            Assert.AreEqual(PetChatterTrigger.LevelUp, PetChatterRules.MoreImportant(PetChatterTrigger.LevelUp, PetChatterTrigger.LongIdle));
        }

        // ------------------------------------------------------------------ not too often

        [Test]
        public void APet_DoesNotRepeatItself_OverALongFight()
        {
            var voice = new PetChatterVoice();
            var random = new System.Random(7);
            int before = -1;
            float now = 0f;
            for (int i = 0; i < 300; i++)
            {
                now += 30f; // past every wait
                int said = voice.Speak(PetChatterTrigger.CriticalHit, 8, now, settings, (float)random.NextDouble());
                Assert.GreaterOrEqual(said, 0);
                Assert.AreNotEqual(before, said, "said the same line twice in a row at " + i);
                before = said;
            }
        }

        [Test]
        public void AfterSpeaking_APetIsQuietForAWhile()
        {
            var voice = new PetChatterVoice();
            Assert.GreaterOrEqual(voice.Speak(PetChatterTrigger.LevelUp, 8, 100f, settings, 0.3f), 0);
            Assert.AreEqual(-1, voice.Speak(PetChatterTrigger.RareDrop, 8, 100f + settings.quietSeconds - 0.1f, settings, 0.3f), "still inside the quiet");
            Assert.GreaterOrEqual(voice.Speak(PetChatterTrigger.RareDrop, 8, 100f + settings.quietSeconds, settings, 0.3f), 0);
        }

        [Test]
        public void TheSameMoment_IsNotReactedToAgainTooSoon()
        {
            var voice = new PetChatterVoice();
            float wait = settings.RepeatSeconds(PetChatterTrigger.CriticalHit);
            Assert.Greater(wait, settings.quietSeconds);
            Assert.GreaterOrEqual(voice.Speak(PetChatterTrigger.CriticalHit, 8, 50f, settings, 0.5f), 0);
            Assert.AreEqual(-1, voice.Speak(PetChatterTrigger.CriticalHit, 8, 50f + wait - 0.1f, settings, 0.5f));
            Assert.GreaterOrEqual(voice.Speak(PetChatterTrigger.CriticalHit, 8, 50f + wait, settings, 0.5f), 0);
        }

        [Test]
        public void ADeath_IsAlwaysReactedTo()
        {
            var voice = new PetChatterVoice();
            Assert.GreaterOrEqual(voice.Speak(PetChatterTrigger.CriticalHit, 8, 10f, settings, 0.5f), 0);
            Assert.GreaterOrEqual(voice.Speak(PetChatterTrigger.OwnerDied, 8, 10.5f, settings, 0.5f), 0, "a death breaks the quiet");
        }

        [Test]
        public void ANewPet_RemembersNothing()
        {
            var voice = new PetChatterVoice();
            voice.Speak(PetChatterTrigger.CriticalHit, 8, 10f, settings, 0.5f);
            voice.Forget();
            Assert.AreEqual(-1, voice.LastVariant(PetChatterTrigger.CriticalHit));
            Assert.IsTrue(voice.MaySpeak(PetChatterTrigger.CriticalHit, 10.1f, settings));
        }

        [Test]
        public void ALongerLine_StaysUpLonger_ButNotForever()
        {
            Assert.Greater(settings.BubbleSeconds("A tactical retreat is still tactical."), settings.BubbleSeconds("W."));
            Assert.AreEqual(settings.bubbleSeconds, settings.BubbleSeconds(null), 0.0001f, "an emoji has no text to read");
            Assert.LessOrEqual(settings.BubbleSeconds(new string('x', 500)), settings.longestBubbleSeconds);
        }

        // ------------------------------------------------------------------ what the project's pets say

        private static IReadOnlyList<PetDefinition> Pets()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PetCatalog>(CatalogPath);
            Assert.NotNull(catalog, CatalogPath);
            return catalog.Pets;
        }

        [Test]
        public void TheSettingsAsset_Exists_WithAWaitForEveryMoment()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PetChatterSettings>(SettingsPath);
            Assert.NotNull(asset, SettingsPath);
            Assert.AreEqual(PetChatterRules.TriggerCount, asset.repeatSeconds.Length);
            Assert.AreEqual(Triggers.Length, PetChatterRules.TriggerCount);
            Assert.Greater(asset.lowHealthRearmFraction, asset.lowHealthFraction, "the warning must be able to arm again");
        }

        [Test]
        public void EveryPet_ReactsToEveryMoment_WithAtLeastEightThingsToSay()
        {
            IReadOnlyList<PetDefinition> pets = Pets();
            Assert.Greater(pets.Count, 0);
            foreach (PetDefinition pet in pets)
            {
                Assert.NotNull(pet.chatter, pet.displayName + " has no chatter");
                foreach (PetChatterTrigger trigger in Triggers)
                    Assert.GreaterOrEqual(pet.chatter.Count(trigger), PetChatterSet.WantedVariants, $"{pet.displayName}, {trigger}");
            }
        }

        [Test]
        public void EveryPet_ReactsInItsOwnWay()
        {
            var seen = new Dictionary<string, string>();
            foreach (PetDefinition pet in Pets())
            {
                foreach (PetChatterTrigger trigger in Triggers)
                {
                    PetChatterSet.Moment moment = pet.chatter.Find(trigger);
                    var said = new List<string>();
                    if (pet.chatter.voice == PetVoice.Text) said.AddRange(moment.lines);
                    else foreach (Sprite emoji in moment.emoji) said.Add(emoji != null ? emoji.name : "(none)");
                    string all = string.Join("|", said);
                    Assert.IsFalse(seen.TryGetValue(trigger + ":" + all, out string other), $"{pet.displayName} and {other} say exactly the same at {trigger}");
                    seen[trigger + ":" + all] = pet.displayName;
                }
            }
        }

        [Test]
        public void TextLines_AreShort_AndNoneIsThereTwice()
        {
            foreach (PetDefinition pet in Pets())
            {
                if (pet.chatter.voice != PetVoice.Text) continue;
                foreach (PetChatterTrigger trigger in Triggers)
                {
                    var seen = new HashSet<string>();
                    foreach (string line in pet.chatter.Find(trigger).lines)
                    {
                        Assert.IsFalse(string.IsNullOrWhiteSpace(line), $"{pet.displayName}, {trigger}: an empty line");
                        Assert.LessOrEqual(line.Length, LongestLine, $"\"{line}\" is too long to read in a glance");
                        Assert.IsTrue(seen.Add(line), $"{pet.displayName}, {trigger}: \"{line}\" is there twice");
                    }
                }
            }
        }

        [Test]
        public void EmojiSets_HaveADrawingInEveryPlace_AndNoneIsThereTwice()
        {
            int emojiPets = 0;
            foreach (PetDefinition pet in Pets())
            {
                if (pet.chatter.voice != PetVoice.Emoji) continue;
                emojiPets++;
                foreach (PetChatterTrigger trigger in Triggers)
                {
                    var seen = new HashSet<Sprite>();
                    foreach (Sprite emoji in pet.chatter.Find(trigger).emoji)
                    {
                        Assert.NotNull(emoji, $"{pet.displayName}, {trigger}: an emoji is missing (bake the pet emoji)");
                        Assert.IsTrue(seen.Add(emoji), $"{pet.displayName}, {trigger}: {emoji.name} is there twice");
                    }
                }
            }
            Assert.Greater(emojiPets, 0, "the animal pets speak in emoji");
        }
    }
}
