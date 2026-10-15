using KryticalNova.Social;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Following a Squad member: when the follower walks, when it stands, and everything that ends it.</summary>
    public sealed class FollowRulesTests
    {
        private const float Keep = 3.5f, Resume = 5f, Max = 60f;
        private const float Reach = 0.8f, MaxAge = 15f;

        private static FollowFacts Following(float distance, bool moving = false) => new FollowFacts
        {
            targetPresent = true, sameSquad = true, followerAlive = true, distance = distance, moving = moving,
        };

        private static FollowStep Step(FollowFacts facts, out FollowEnd end) => FollowRules.Evaluate(facts, Keep, Resume, Max, out end);

        [Test]
        public void TheFollowerWalksWhenTheMemberIsAway_AndStandsWhenClose()
        {
            Assert.AreEqual(FollowStep.Move, Step(Following(10f), out FollowEnd end));
            Assert.AreEqual(FollowEnd.None, end);
            Assert.AreEqual(FollowStep.Hold, Step(Following(2f, moving: true), out _));
            Assert.AreEqual(FollowStep.Hold, Step(Following(Keep, moving: true), out _));
        }

        [Test]
        public void BetweenTheTwoDistancesTheFollowerKeepsDoingWhatItDid()
        {
            Assert.AreEqual(FollowStep.Hold, Step(Following(4f, moving: false), out _), "a small step by the member does not set it off");
            Assert.AreEqual(FollowStep.Move, Step(Following(4f, moving: true), out _), "on its way, it closes to the keep distance");
            Assert.AreEqual(FollowStep.Move, Step(Following(Resume, moving: false), out _));
        }

        [Test]
        public void AnOrderOfTheFollowersOwnEndsIt()
        {
            FollowFacts facts = Following(10f, moving: true);
            facts.followerOrdered = true;
            Assert.AreEqual(FollowStep.End, Step(facts, out FollowEnd end));
            Assert.AreEqual(FollowEnd.Ordered, end);
        }

        [Test]
        public void AFightEndsIt()
        {
            FollowFacts facts = Following(1f);
            facts.followerInCombat = true;
            Assert.AreEqual(FollowStep.End, Step(facts, out FollowEnd end));
            Assert.AreEqual(FollowEnd.Combat, end);
        }

        [Test]
        public void TheMemberLeavingTheSquadEndsIt()
        {
            FollowFacts facts = Following(5f);
            facts.sameSquad = false;
            Assert.AreEqual(FollowStep.End, Step(facts, out FollowEnd end));
            Assert.AreEqual(FollowEnd.LeftSquad, end);
        }

        [Test]
        public void TheMemberLeavingTheMapEndsIt()
        {
            FollowFacts facts = Following(5f);
            facts.targetPresent = false;
            facts.sameSquad = false; // unknowable once they are gone: "gone" is still the reason given
            Assert.AreEqual(FollowStep.End, Step(facts, out FollowEnd end));
            Assert.AreEqual(FollowEnd.Gone, end);
        }

        [Test]
        public void TheMemberGettingTooFarAheadEndsIt()
        {
            Assert.AreEqual(FollowStep.Move, Step(Following(Max, moving: true), out _));
            Assert.AreEqual(FollowStep.End, Step(Following(Max + 0.1f, moving: true), out FollowEnd end));
            Assert.AreEqual(FollowEnd.TooFar, end);
        }

        [Test]
        public void ADefeatedFollowerStops()
        {
            FollowFacts facts = Following(10f, moving: true);
            facts.followerAlive = false;
            Assert.AreEqual(FollowStep.End, Step(facts, out FollowEnd end));
            Assert.AreEqual(FollowEnd.Defeated, end);
        }

        [Test]
        public void OnlyASquadMateInReachCanBeFollowed()
        {
            Assert.IsNull(FollowRules.StartRefusal(true, false, true, true, 10f, Max, "Arinis"));
            Assert.IsNotNull(FollowRules.StartRefusal(false, false, false, true, 0f, Max, "Arinis"), "not here");
            Assert.IsNotNull(FollowRules.StartRefusal(true, true, false, true, 0f, Max, "Tolan"), "oneself");
            Assert.IsNotNull(FollowRules.StartRefusal(true, false, false, true, 10f, Max, "Arinis"), "not a Squad mate");
            Assert.IsNotNull(FollowRules.StartRefusal(true, false, true, false, 10f, Max, "Arinis"), "defeated");
            Assert.IsNotNull(FollowRules.StartRefusal(true, false, true, true, Max + 1f, Max, "Arinis"), "too far");
        }

        [Test]
        public void EveryEndingHasALineForThePlayer()
        {
            foreach (FollowEnd end in System.Enum.GetValues(typeof(FollowEnd)))
            {
                string line = FollowRules.Describe(end, "Arinis");
                if (end == FollowEnd.None) Assert.IsNull(line);
                else StringAssert.Contains("Arinis", line);
            }
            StringAssert.EndsWith("Learn Jump first.", FollowRules.Describe(FollowEnd.JumpRefused, "Arinis", "Learn Jump first."));
        }

        [Test]
        public void TheFollowerStandsExactlyTheKeepDistanceBehind()
        {
            var member = new UnityEngine.Vector3(10f, 0f, 0f);
            UnityEngine.Vector3 stand = FollowRules.StandPoint(new UnityEngine.Vector3(0f, 0f, 0f), member, Keep);
            Assert.AreEqual(Keep, UnityEngine.Vector3.Distance(stand, member), 0.001f);
            Assert.Less(stand.x, member.x, "on the follower's side");
            var close = new UnityEngine.Vector3(8f, 0f, 0f);
            Assert.AreEqual(close, FollowRules.StandPoint(close, member, Keep), "already inside the keep distance: stays put");
        }

        private static HopFacts Hop(float age = 1f, float toTakeoff = 5f, float toLanding = 12f, bool ready = true, bool jumping = false) =>
            new HopFacts { age = age, distanceToTakeoff = toTakeoff, distanceToLanding = toLanding, jumpReady = ready, jumping = jumping };

        [Test]
        public void AJumpOfTheMembersIsWalkedToThenCopied()
        {
            Assert.AreEqual(HopStep.WalkToTakeoff, FollowRules.NextHop(Hop(), Reach, MaxAge));
            Assert.AreEqual(HopStep.Jump, FollowRules.NextHop(Hop(toTakeoff: 0.5f), Reach, MaxAge));
            Assert.AreEqual(HopStep.None, FollowRules.NextHop(Hop(toTakeoff: 0.5f, ready: false), Reach, MaxAge), "waits for its own landing to settle");
            Assert.AreEqual(HopStep.None, FollowRules.NextHop(Hop(toTakeoff: 0.5f, jumping: true), Reach, MaxAge), "in the air");
        }

        [Test]
        public void AJumpTooOldOrAlreadyCrossedIsForgotten()
        {
            Assert.AreEqual(HopStep.Drop, FollowRules.NextHop(Hop(age: MaxAge + 1f), Reach, MaxAge));
            Assert.AreEqual(HopStep.Drop, FollowRules.NextHop(Hop(toLanding: 0.3f), Reach, MaxAge));
        }

        [Test]
        public void OnlyTheNewestJumpsAreKept()
        {
            var hops = new System.Collections.Generic.List<FollowHop>();
            for (int i = 0; i < 6; i++) FollowRules.Enqueue(hops, new FollowHop { at = i }, 4);
            Assert.AreEqual(4, hops.Count);
            Assert.AreEqual(2f, hops[0].at, "the oldest gave way");
            Assert.AreEqual(5f, hops[3].at);
        }
    }
}
