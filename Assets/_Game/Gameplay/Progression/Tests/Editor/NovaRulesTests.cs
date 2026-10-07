using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The rules of Ascension and Nova Standings, read from a settings object with its default numbers.</summary>
    public sealed class NovaRulesTests
    {
        private NovaSettings settings;
        private AscensionSettings Ascension => settings.ascension;
        private StandingSettings Standings => settings.standings;

        // A roll that lands in each of the three outcomes, whatever the odds are.
        private const float RollRise = 0f, RollSlip = 0.999f;
        private float RollHold(int level)
        {
            AscensionOdds odds = NovaRules.OddsAt(Ascension, level);
            return (odds.rise + odds.hold * 0.5f) / (odds.rise + odds.hold + odds.slip);
        }

        [SetUp]
        public void SetUp() => settings = ScriptableObject.CreateInstance<NovaSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        // ---------------------------------------------------------------- Ascension

        [Test]
        public void TheNovaChangesEveryEightLevels_TwelveTimes()
        {
            Assert.AreEqual(0, NovaRules.Tier(Ascension, 0), "no Nova at +0");
            Assert.AreEqual(1, NovaRules.Tier(Ascension, 1));
            Assert.AreEqual(1, NovaRules.Tier(Ascension, 8));
            Assert.AreEqual(2, NovaRules.Tier(Ascension, 9));
            Assert.AreEqual(12, NovaRules.Tier(Ascension, 89));
            Assert.AreEqual(12, NovaRules.Tier(Ascension, 96));
            Assert.AreEqual(12, NovaRules.TierCount(Ascension));
            Assert.AreEqual(Ascension.maxLevel, 96);
        }

        [Test]
        public void TheOddsAre55_35_10ToPlus24_Then45_40_15()
        {
            foreach (int level in new[] { 0, 12, 24 })
            {
                AscensionOdds early = NovaRules.OddsAt(Ascension, level);
                Assert.AreEqual(new Vector3(55f, 35f, 10f), new Vector3(early.rise, early.hold, early.slip), $"at +{level}");
            }
            foreach (int level in new[] { 25, 60, 95 })
            {
                AscensionOdds late = NovaRules.OddsAt(Ascension, level);
                Assert.AreEqual(new Vector3(45f, 40f, 15f), new Vector3(late.rise, late.hold, late.slip), $"at +{level}");
            }
        }

        [Test]
        public void AnAttemptCostsHalfOfTwelveSkysCreds()
        {
            Assert.AreEqual(100, Ascension.renownCost);
            Assert.AreEqual(500_000, Ascension.credsCost);
            Assert.IsNull(NovaRules.AscendBlocked(Ascension, 10, 100, 500_000));
            Assert.AreEqual(NovaRules.ReasonNoRenown, NovaRules.AscendBlocked(Ascension, 10, 99, 500_000));
            Assert.AreEqual(NovaRules.ReasonNoCreds, NovaRules.AscendBlocked(Ascension, 10, 100, 499_999));
            Assert.AreEqual(NovaRules.ReasonAscensionComplete, NovaRules.AscendBlocked(Ascension, 96, 100_000, 100_000_000));
        }

        [Test]
        public void ARollGivesOneOfThreeOutcomes()
        {
            Assert.AreEqual(AscensionOutcome.Rise, NovaRules.Resolve(Ascension, 12, RollRise, out int up));
            Assert.AreEqual(13, up);
            Assert.AreEqual(AscensionOutcome.Hold, NovaRules.Resolve(Ascension, 12, RollHold(12), out int same));
            Assert.AreEqual(12, same);
            Assert.AreEqual(AscensionOutcome.Slip, NovaRules.Resolve(Ascension, 12, RollSlip, out int down));
            Assert.AreEqual(11, down);
        }

        [Test]
        public void TheEdgesOfTheOddsFallWhereTheNumbersSay()
        {
            // 55 / 35 / 10: a roll is a share of 100.
            Assert.AreEqual(AscensionOutcome.Rise, NovaRules.Resolve(Ascension, 12, 0.549f, out _));
            Assert.AreEqual(AscensionOutcome.Hold, NovaRules.Resolve(Ascension, 12, 0.551f, out _));
            Assert.AreEqual(AscensionOutcome.Hold, NovaRules.Resolve(Ascension, 12, 0.899f, out _));
            Assert.AreEqual(AscensionOutcome.Slip, NovaRules.Resolve(Ascension, 12, 0.901f, out _));
        }

        [Test]
        public void ASlipNeverTakesANovaTierOnceEarned()
        {
            for (int tier = 1; tier <= NovaRules.TierCount(Ascension); tier++)
            {
                int floor = (tier - 1) * Ascension.levelsPerTier + 1;
                Assert.AreEqual(floor, NovaRules.TierFloor(Ascension, floor));
                Assert.IsTrue(NovaRules.SlipIsHarmless(Ascension, floor), $"+{floor} is the floor of Nova {tier}");
                Assert.AreEqual(AscensionOutcome.SlipHeld, NovaRules.Resolve(Ascension, floor, RollSlip, out int held), $"at +{floor}");
                Assert.AreEqual(floor, held);

                if (floor + 1 >= Ascension.maxLevel) continue;
                Assert.IsFalse(NovaRules.SlipIsHarmless(Ascension, floor + 1));
                Assert.AreEqual(AscensionOutcome.Slip, NovaRules.Resolve(Ascension, floor + 1, RollSlip, out int slipped));
                Assert.AreEqual(floor, slipped, "a slip inside the tier stops at its floor");
            }
        }

        [Test]
        public void AscensionNeverGoesBelowZero_OrAboveTheCap()
        {
            Assert.AreEqual(AscensionOutcome.Hold, NovaRules.Resolve(Ascension, 0, RollSlip, out int floor), "nothing to lose at +0");
            Assert.AreEqual(0, floor);
            Assert.AreEqual(AscensionOutcome.Rise, NovaRules.Resolve(Ascension, 95, RollRise, out int top));
            Assert.AreEqual(96, top);
            Assert.AreEqual(AscensionOutcome.Hold, NovaRules.Resolve(Ascension, 96, RollRise, out int capped));
            Assert.AreEqual(96, capped);
        }

        [Test]
        public void WithTheRuleOff_ASlipCanCrossATier()
        {
            Ascension.slipKeepsTier = false;
            Assert.AreEqual(AscensionOutcome.Slip, NovaRules.Resolve(Ascension, 9, RollSlip, out int slipped));
            Assert.AreEqual(8, slipped);
            Assert.AreEqual(AscensionOutcome.Hold, NovaRules.Resolve(Ascension, 0, RollSlip, out _), "still never below +0");
        }

        [Test]
        public void LevelsToTheNextNova()
        {
            Assert.AreEqual(1, NovaRules.LevelsToNextTier(Ascension, 0));
            Assert.AreEqual(8, NovaRules.LevelsToNextTier(Ascension, 1));
            Assert.AreEqual(1, NovaRules.LevelsToNextTier(Ascension, 8));
            Assert.AreEqual(0, NovaRules.LevelsToNextTier(Ascension, 90), "the last Nova has none after it");
        }

        // ---------------------------------------------------------------- Nova Standings

        [Test]
        public void StandingsCostTwelveSkysList_InOrder()
        {
            int[] expected = { 800, 1700, 2500, 3400, 4200, 5100, 5900, 6800, 7600, 8500, 9300, 10000 };
            Assert.AreEqual(expected.Length, Standings.MaxLevel);
            int total = 0;
            for (int level = 0; level < expected.Length; level++)
            {
                Assert.AreEqual(expected[level], NovaRules.StandingCost(Standings, level), $"rank {level + 1}");
                total += expected[level];
            }
            Assert.AreEqual(65_800, total, "a whole track");
            Assert.AreEqual(-1, NovaRules.StandingCost(Standings, 12), "nothing after the last rank");
        }

        [Test]
        public void AStandingIsBlockedOnlyByRenownOrByBeingComplete()
        {
            Assert.IsNull(NovaRules.StandingBlocked(Standings, 0, 800));
            Assert.AreEqual(NovaRules.ReasonNoRenown, NovaRules.StandingBlocked(Standings, 0, 799));
            Assert.AreEqual(NovaRules.ReasonNoRenown, NovaRules.StandingBlocked(Standings, 5, 5_099));
            Assert.AreEqual(NovaRules.ReasonStandingComplete, NovaRules.StandingBlocked(Standings, 12, 1_000_000));
        }

        [Test]
        public void EachFactionHasItsOwnTwelveRankWords()
        {
            foreach (FactionId faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
            {
                var seen = new HashSet<string>();
                for (int level = 1; level <= Standings.MaxLevel; level++)
                {
                    string word = NovaRules.RankWord(Standings, faction, level);
                    Assert.IsFalse(string.IsNullOrEmpty(word), $"{faction} rank {level}");
                    Assert.IsTrue(seen.Add(word), $"{faction} uses '{word}' twice");
                }
            }
            Assert.AreEqual("Vanguard Thane", NovaRules.StandingName(Standings, FactionId.Covenant, StandingTrack.Vanguard, 6));
            Assert.AreEqual("Mindwell Daemon", NovaRules.StandingName(Standings, FactionId.Null, StandingTrack.Mindwell, 4));
            Assert.AreEqual("Bulwark Hegemon", NovaRules.StandingName(Standings, FactionId.Hegemony, StandingTrack.Bulwark, 12));
            Assert.IsNull(NovaRules.StandingName(Standings, FactionId.Hegemony, StandingTrack.Bulwark, 0), "no name below rank 1");
        }

        [Test]
        public void TheWornStandingIsOneTheCharacterHolds()
        {
            var state = new NovaState().WithStanding(StandingTrack.Ironblood, 3).Wearing(StandingTrack.Ironblood);
            Assert.IsTrue(state.TryGetWorn(out StandingTrack worn));
            Assert.AreEqual(StandingTrack.Ironblood, worn);
            Assert.AreEqual("Ironblood Specialist", NovaRules.WornName(Standings, FactionId.Hegemony, state));

            Assert.IsFalse(state.Wearing(StandingTrack.Vanguard).TryGetWorn(out _), "a track with no rank cannot be worn");
            Assert.IsNull(NovaRules.WornName(Standings, FactionId.Hegemony, state.WearingNone()));
        }

        // ---------------------------------------------------------------- what both give

        [Test]
        public void AscensionRaisesAllFourAttributes_AndEveryHeldTrackCounts()
        {
            var state = new NovaState { ascension = 37 }
                .WithStanding(StandingTrack.Vanguard, 6)
                .WithStanding(StandingTrack.Mindwell, 12)
                .Wearing(StandingTrack.Vanguard);
            var modifiers = new List<StatModifier>();
            NovaRules.AddModifiers(settings, state, this, modifiers);

            foreach (StatType attribute in new[] { StatType.Strength, StatType.Agility, StatType.Vitality, StatType.Spirit })
            {
                StatModifier found = modifiers.Find(m => m.Stat == attribute);
                Assert.IsNotNull(found, attribute.ToString());
                Assert.AreEqual(ModifierType.Flat, found.ModType);
                Assert.AreEqual(37f, found.Value);
            }
            StatModifier attack = modifiers.Find(m => m.Stat == StatType.AttackPower);
            Assert.AreEqual(ModifierType.PercentAdd, attack.ModType);
            Assert.AreEqual(6f, attack.Value);
            Assert.AreEqual(12f, modifiers.Find(m => m.Stat == StatType.MaxChi).Value, "the track that is not worn counts too");
            Assert.IsNull(modifiers.Find(m => m.Stat == StatType.DefensePower), "a track with no rank gives nothing");
            Assert.IsNull(modifiers.Find(m => m.Stat == StatType.MaxHP));
            Assert.AreEqual(6, modifiers.Count);
        }

        [Test]
        public void NothingHeldGivesNothing()
        {
            var modifiers = new List<StatModifier>();
            NovaRules.AddModifiers(settings, default, this, modifiers);
            Assert.AreEqual(0, modifiers.Count);
        }

        [Test]
        public void EveryTierHasALook_FromAStarWithOneRingToAFullRayedNova()
        {
            NovaLookSettings look = settings.look;
            NovaTierLook[] tiers = look.tiers;
            Assert.AreEqual(NovaRules.TierCount(Ascension), tiers.Length);
            Assert.AreEqual(1, RingsAt(look, 1), "tier 1 is a star with one ring");
            Assert.AreEqual(0, tiers[0].rays + tiers[0].motes, "and nothing else");
            Assert.AreEqual(0f, tiers[0].prism);
            NovaTierLook last = tiers[tiers.Length - 1];
            Assert.AreEqual(look.rings.Length, RingsAt(look, tiers.Length), "the last tier turns every ring there is");
            Assert.Greater(last.rays, 0);
            Assert.Greater(last.motes, 0);
            Assert.Greater(last.prism, 0f);
            for (int i = 1; i < tiers.Length; i++)
            {
                Assert.GreaterOrEqual(tiers[i].radius, tiers[i - 1].radius, $"tier {i + 1} is no smaller than the one before");
                Assert.GreaterOrEqual(RingsAt(look, i + 1), RingsAt(look, i), $"tier {i + 1} loses no ring");
                Assert.GreaterOrEqual(look.Reach(i + 1), look.Reach(i), $"tier {i + 1} reaches no less far");
            }
        }

        [Test]
        public void EveryRingFitsItsNova()
        {
            NovaLookSettings look = settings.look;
            foreach (NovaRing ring in look.rings)
            {
                Assert.That(ring.fromTier, Is.InRange(1, look.tiers.Length));
                Assert.Greater(ring.radius, 0f);
                Assert.Greater(ring.width, 0f);
                Assert.Less(ring.width, ring.radius, "a band is narrower than the ring it lies on");
                NovaTierLook first = look.tiers[ring.fromTier - 1];
                Assert.Greater(ring.radius - ring.width * 0.5f, first.core, "it turns clear of the star");
            }
        }

        [Test]
        public void ANovaIsKnownByItsRingColour_AndFloatsNoLowerAsItGrows()
        {
            NovaLookSettings look = settings.look;
            Assert.AreEqual(Color.white, look.ColourOf(0), "no Nova, no colour");
            Assert.AreEqual(look.tiers[0].outer, look.ColourOf(1));
            Assert.AreEqual(look.tiers[look.tiers.Length - 1].outer, look.ColourOf(99), "a tier past the last uses the last");
            Assert.Greater(look.CentreAboveHead(0), look.gapAboveHead, "where a first Nova will appear");
            for (int tier = 1; tier <= look.tiers.Length; tier++)
            {
                float centre = look.CentreAboveHead(tier);
                Assert.Greater(centre, look.gapAboveHead, $"tier {tier} clears the head");
                if (tier > 1) Assert.GreaterOrEqual(centre, look.CentreAboveHead(tier - 1), $"tier {tier} floats no lower");
            }
        }

        private static int RingsAt(NovaLookSettings look, int tier)
        {
            int count = 0;
            foreach (NovaRing ring in look.rings)
                if (ring.fromTier <= tier) count++;
            return count;
        }
    }
}
