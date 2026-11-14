using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Townsfolk.Tests
{
    /// <summary>The town-life mind's pure rules (plan evals: the scorer, day plans, caps).</summary>
    public sealed class TownLifeRulesTests
    {
        static readonly float4 Starts = new float4(5f, 8f, 18f, 22f);

        [Test]
        public void PeriodsFollowTheHours()
        {
            Assert.AreEqual((byte)TownPeriod.Night, TownLifeRules.PeriodOf(3f, Starts));
            Assert.AreEqual((byte)TownPeriod.Dawn, TownLifeRules.PeriodOf(6f, Starts));
            Assert.AreEqual((byte)TownPeriod.Day, TownLifeRules.PeriodOf(12f, Starts));
            Assert.AreEqual((byte)TownPeriod.Dusk, TownLifeRules.PeriodOf(20f, Starts));
            Assert.AreEqual((byte)TownPeriod.Night, TownLifeRules.PeriodOf(23f, Starts));
            // the seed's jitter moves the edge: at 07:50 with half an hour of jitter it is still dawn, with minus half an hour it is day
            Assert.AreEqual((byte)TownPeriod.Dawn, TownLifeRules.PeriodOf(7.8f, Starts, 0.5f));
            Assert.AreEqual((byte)TownPeriod.Day, TownLifeRules.PeriodOf(7.8f, Starts, -0.5f));
        }

        [Test]
        public void HoursWrapPastMidnight()
        {
            Assert.IsTrue(TownLifeRules.InHours(23f, 22f, 6f));
            Assert.IsTrue(TownLifeRules.InHours(3f, 22f, 6f));
            Assert.IsFalse(TownLifeRules.InHours(12f, 22f, 6f));
            Assert.IsTrue(TownLifeRules.InHours(12f, 8f, 20f));
            Assert.IsFalse(TownLifeRules.InHours(20f, 8f, 20f));
            Assert.IsTrue(TownLifeRules.InHours(17f, 0f, 0f), "from == to is always open");
        }

        [Test]
        public void WantedByDayNightAndInBetween()
        {
            var stroller = new float4(0.4f, 0.8f, 0.6f, 0.1f);
            Assert.AreEqual(8, TownLifeRules.Wanted(8, 2, stroller, (byte)TownPeriod.Day));
            Assert.AreEqual(2, TownLifeRules.Wanted(8, 2, stroller, (byte)TownPeriod.Night));
            Assert.AreEqual(4, TownLifeRules.Wanted(8, 2, stroller, (byte)TownPeriod.Dawn));   // 8 x 0.4 / 0.8
            Assert.AreEqual(6, TownLifeRules.Wanted(8, 2, stroller, (byte)TownPeriod.Dusk));   // 8 x 0.6 / 0.8
        }

        [Test]
        public void CapsByPeriodRainAndCeiling()
        {
            Assert.AreEqual(48, TownLifeRules.Cap(48, 20, 60, (byte)TownPeriod.Day, false, 0.6f));
            Assert.AreEqual(20, TownLifeRules.Cap(48, 20, 60, (byte)TownPeriod.Night, false, 0.6f));
            Assert.AreEqual(34, TownLifeRules.Cap(48, 20, 60, (byte)TownPeriod.Dusk, false, 0.6f));
            Assert.AreEqual(29, TownLifeRules.Cap(48, 20, 60, (byte)TownPeriod.Day, true, 0.6f));
            Assert.AreEqual(60, TownLifeRules.Cap(90, 20, 60, (byte)TownPeriod.Day, false, 0.6f), "never past the hard cap");
        }

        static TownLifeRules.SpotInputs Liked(float like = 1f) => new TownLifeRules.SpotInputs
        {
            Like = like, Open = true, Allowed = true, Distance = 10f, PreferredDistance = 40f, FreeSlots = 2, SlotCount = 2, CrowdingDislike = 1f,
        };

        [Test]
        public void ScorerNeverPicksFullShutRecentOrUnliked()
        {
            Assert.Greater(TownLifeRules.SpotScore(Liked()), 0f);
            var full = Liked(); full.FreeSlots = 0;
            var shut = Liked(); shut.Open = false;
            var recent = Liked(); recent.Recent = true;
            var unliked = Liked(0f);
            var notMine = Liked(); notMine.Allowed = false;
            Assert.AreEqual(0f, TownLifeRules.SpotScore(full));
            Assert.AreEqual(0f, TownLifeRules.SpotScore(shut));
            Assert.AreEqual(0f, TownLifeRules.SpotScore(recent));
            Assert.AreEqual(0f, TownLifeRules.SpotScore(unliked));
            Assert.AreEqual(0f, TownLifeRules.SpotScore(notMine));
        }

        [Test]
        public void ScorerPrefersLikedNearShelteredInRainAndFriends()
        {
            Assert.Greater(TownLifeRules.SpotScore(Liked(1f)), TownLifeRules.SpotScore(Liked(0.4f)));
            var far = Liked(); far.Distance = 120f;
            Assert.Greater(TownLifeRules.SpotScore(Liked()), TownLifeRules.SpotScore(far));
            var wet = Liked(); wet.Raining = true; wet.Rain = (byte)RainBehaviour.Shelter;
            var dry = wet; dry.Sheltered = true;
            Assert.Greater(TownLifeRules.SpotScore(dry), TownLifeRules.SpotScore(wet) * 5f);
            var friends = Liked(); friends.Friends = 2; friends.FriendBonus = 0.5f;
            Assert.Greater(TownLifeRules.SpotScore(friends), TownLifeRules.SpotScore(Liked()));
            var crowdedLoner = Liked(); crowdedLoner.FreeSlots = 1; crowdedLoner.CrowdingDislike = 2f;
            var crowded = crowdedLoner; crowded.CrowdingDislike = 1f;
            Assert.Less(TownLifeRules.SpotScore(crowdedLoner), TownLifeRules.SpotScore(crowded), "crowding counts double for a Loner");
            var darkOwl = Liked(); darkOwl.Night = true; darkOwl.NeedsLight = true;
            var litOwl = darkOwl; litOwl.Lit = true;
            Assert.Greater(TownLifeRules.SpotScore(litOwl), TownLifeRules.SpotScore(darkOwl));
        }

        [Test]
        public void PickIsWeightedAmongTheBest()
        {
            using var scores = new NativeArray<float>(new[] { 0.6f, 0.3f, 0.1f, 0f }, Allocator.Temp);
            Assert.AreEqual(0, TownLifeRules.PickWeighted(scores, 3, 0.0f));
            Assert.AreEqual(0, TownLifeRules.PickWeighted(scores, 3, 0.59f));
            Assert.AreEqual(1, TownLifeRules.PickWeighted(scores, 3, 0.61f));
            Assert.AreEqual(2, TownLifeRules.PickWeighted(scores, 3, 0.95f));
            using var none = new NativeArray<float>(new[] { 0f, 0f }, Allocator.Temp);
            Assert.AreEqual(-1, TownLifeRules.PickWeighted(none, 2, 0.5f));
        }

        [Test]
        public void ShiftsAndRainSendHome()
        {
            Assert.IsTrue(TownLifeRules.StaysOut(true, 10f, 6f, 14f, false, (byte)RainBehaviour.Shelter));
            Assert.IsFalse(TownLifeRules.StaysOut(true, 15f, 6f, 14f, false, (byte)RainBehaviour.Shelter));
            Assert.IsTrue(TownLifeRules.StaysOut(true, 2f, 18f, 6f, false, (byte)RainBehaviour.Ignore), "a night shift over midnight");
            Assert.IsFalse(TownLifeRules.StaysOut(false, 12f, 0f, 0f, true, (byte)RainBehaviour.GoHome));
            Assert.IsFalse(TownLifeRules.StaysOut(false, 12f, 0f, 0f, true, (byte)RainBehaviour.PackUp));
            Assert.IsTrue(TownLifeRules.StaysOut(false, 12f, 0f, 0f, true, (byte)RainBehaviour.Shelter));
        }

        [Test]
        public void ChatCircleSlotsFaceTheMiddle()
        {
            for (int k = 0; k < 4; k++)
            {
                TownLifeRules.SlotPlace(float3.zero, 0f, (byte)TownSpotKind.ChatCircle, k, 4, 1.2f, out float3 p, out float yaw);
                Assert.AreEqual(1.2f, math.length(p.xz), 1e-4f);
                float2 facing = new float2(math.sin(yaw), math.cos(yaw));
                Assert.AreEqual(-1f, math.dot(facing, math.normalize(p.xz)), 1e-4f, "slot " + k + " faces the middle");
            }
            TownLifeRules.SlotPlace(float3.zero, 0f, (byte)TownSpotKind.Bench, 0, 2, 0.8f, out float3 a, out _);
            TownLifeRules.SlotPlace(float3.zero, 0f, (byte)TownSpotKind.Bench, 1, 2, 0.8f, out float3 b, out _);
            Assert.AreEqual(0.8f, math.distance(a, b), 1e-4f, "a bench's slots sit side by side");
        }

        [Test]
        public void ActivitiesOfSpots()
        {
            Assert.AreEqual((byte)TownActivity.Chat, TownLifeRules.ActivityAt((byte)TownSpotKind.ChatCircle));
            Assert.AreEqual((byte)TownActivity.Shop, TownLifeRules.ActivityAt((byte)TownSpotKind.VendorStall));
            Assert.AreEqual((byte)TownActivity.Guard, TownLifeRules.ActivityAt((byte)TownSpotKind.GuardPost));
            Assert.IsFalse(TownLifeRules.Claimable((byte)TownSpotKind.Walk));
            Assert.IsFalse(TownLifeRules.Claimable((byte)TownSpotKind.HomeDoor));
        }
    }
}
