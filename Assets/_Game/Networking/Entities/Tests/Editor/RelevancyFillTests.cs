using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// Wave 4 relevancy, the fill's rule without a world (<see cref="RelevancyFill.Build"/>): who is sent to whom at the owner's 150 m,
    /// the leave margin, the own ghost, the always-relevant pairs, and the lead's four edge cases (no body yet, flat distance, a
    /// teleport, a ghost destroyed between fills).
    /// </summary>
    public class RelevancyFillTests
    {
        /// <summary>Ghosts, connections and the set sent so far, as the fill system hands them to the rule.</summary>
        sealed class Scene : IDisposable
        {
            public NativeList<RelevancyGhost> Ghosts = new NativeList<RelevancyGhost>(16, Allocator.Persistent);
            public NativeList<int> Viewers = new NativeList<int>(4, Allocator.Persistent);
            public NativeList<RelevancyAlways> Always = new NativeList<RelevancyAlways>(4, Allocator.Persistent);
            public NativeParallelHashMap<RelevantGhostForConnection, int> Set = new NativeParallelHashMap<RelevantGhostForConnection, int>(4, Allocator.Persistent);
            public NativeList<RelevantGhostForConnection> Pairs = new NativeList<RelevantGhostForConnection>(16, Allocator.Persistent);
            public NativeList<RelevantGhostForConnection> Became = new NativeList<RelevantGhostForConnection>(16, Allocator.Persistent);
            public RelevancyConfig Config = new RelevancyConfig { PlayerRadius = 150f, ObjectRadius = 150f, LeaveMargin = 16f, FillIntervalTicks = 6 };
            int m_NextGhost = 100;

            /// <summary>An in-game connection with no body.</summary>
            public void Connection(int networkId) => Viewers.Add(networkId);

            /// <summary>A connection with its player's ghost at (x, z); the ghost id.</summary>
            public int Player(int networkId, float x, float z)
            {
                Viewers.Add(networkId);
                return Body(networkId, x, z);
            }

            /// <summary>A ghost owned by a connection that is already there.</summary>
            public int Body(int networkId, float x, float z)
            {
                Ghosts.Add(new RelevancyGhost { GhostId = ++m_NextGhost, Owner = networkId, At = new float2(x, z) });
                return m_NextGhost;
            }

            /// <summary>A ghost nobody owns (a Hostile, a Strike Dummy).</summary>
            public int Object(float x, float z)
            {
                Ghosts.Add(new RelevancyGhost { GhostId = ++m_NextGhost, Owner = 0, At = new float2(x, z) });
                return m_NextGhost;
            }

            public void Move(int ghostId, float x, float z)
            {
                for (int i = 0; i < Ghosts.Length; i++)
                    if (Ghosts[i].GhostId == ghostId) { RelevancyGhost g = Ghosts[i]; g.At = new float2(x, z); Ghosts[i] = g; return; }
                Assert.Fail("no ghost " + ghostId);
            }

            public void Destroy(int ghostId)
            {
                for (int i = 0; i < Ghosts.Length; i++)
                    if (Ghosts[i].GhostId == ghostId) { Ghosts.RemoveAtSwapBack(i); return; }
                Assert.Fail("no ghost " + ghostId);
            }

            /// <summary>One fill: the rule, then the set as the send system will read it. The most ghosts any connection is sent.</summary>
            public int Fill()
            {
                int most = RelevancyFill.Build(Ghosts.AsArray(), Viewers.AsArray(), Always.AsArray(), Set, Config, Pairs, Became);
                RelevancyFill.Write(Set, Pairs);
                return most;
            }

            public bool Sees(int networkId, int ghostId) => Set.ContainsKey(new RelevantGhostForConnection(networkId, ghostId));

            public bool BecameNow(int networkId, int ghostId) => Became.AsArray().Contains(new RelevantGhostForConnection(networkId, ghostId));

            public int CountFor(int networkId)
            {
                int n = 0;
                for (int i = 0; i < Pairs.Length; i++) if (Pairs[i].Connection == networkId) n++;
                return n;
            }

            public void Dispose()
            {
                Ghosts.Dispose(); Viewers.Dispose(); Always.Dispose(); Set.Dispose(); Pairs.Dispose(); Became.Dispose();
            }
        }

        // ------------------------------------------------------------------ the rule's two small decisions

        [Test]
        public void TheRule_InsideAlways_InTheMarginOnlyWhenSentBefore_PastItNever()
        {
            Assert.IsTrue(RelevancyRules.Sees(140f * 140f, 150f, 16f, sentBefore: false));
            Assert.IsTrue(RelevancyRules.Sees(150f * 150f, 150f, 16f, sentBefore: false), "the radius itself is inside");
            Assert.IsFalse(RelevancyRules.Sees(160f * 160f, 150f, 16f, sentBefore: false), "the margin does not let anybody in");
            Assert.IsTrue(RelevancyRules.Sees(160f * 160f, 150f, 16f, sentBefore: true), "the margin keeps what was sent");
            Assert.IsFalse(RelevancyRules.Sees(167f * 167f, 150f, 16f, sentBefore: true), "past radius + margin it goes");
        }

        [Test]
        public void TheGrid_PutsNegativePlacesInTheirOwnCells_AndDropsTheHeight()
        {
            Assert.AreEqual(new int2(0, 0), RelevancyRules.Cell(new float2(0.5f, 165.9f), 166f));
            Assert.AreEqual(new int2(-1, 1), RelevancyRules.Cell(new float2(-0.5f, 166f), 166f), "floor, not a cut towards zero");
            Assert.AreEqual(new float2(3f, 5f), RelevancyRules.Flat(new float3(3f, 900f, 5f)));
        }

        // ------------------------------------------------------------------ enter and leave

        [Test]
        public void APlayer_IsSent_At140Metres_NotAt170()
        {
            using var s = new Scene();
            int a = s.Player(1, 0f, 0f);
            int near = s.Player(2, 140f, 0f);
            int far = s.Player(3, 0f, 170f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, near));
            Assert.IsFalse(s.Sees(1, far));
            Assert.IsTrue(s.Sees(2, a), "seeing goes both ways at one radius");
            Assert.IsFalse(s.Sees(3, a));
            Assert.IsTrue(s.BecameNow(1, near), "new in the set: said once");
        }

        [Test]
        public void Leaving_NeedsTheMargin_AndComingBackNeedsTheRadius()
        {
            using var s = new Scene();
            s.Player(1, 0f, 0f);
            int b = s.Player(2, 140f, 0f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, b));

            s.Move(b, 160f, 0f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, b), "160 m: still sent, it was sent before");
            Assert.IsFalse(s.BecameNow(1, b), "kept, not new");

            s.Move(b, 167f, 0f);
            s.Fill();
            Assert.IsFalse(s.Sees(1, b), "167 m: past 150 + 16, gone");

            s.Move(b, 155f, 0f);
            s.Fill();
            Assert.IsFalse(s.Sees(1, b), "155 m: not back until inside 150 m");

            s.Move(b, 149f, 0f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, b));
            Assert.IsTrue(s.BecameNow(1, b), "back in: new again");
        }

        [Test]
        public void Hostiles_UseTheirOwnRadius()
        {
            using var s = new Scene();
            s.Player(1, 0f, 0f);
            int hostileNear = s.Object(140f, 0f);
            int hostileFar = s.Object(0f, -170f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, hostileNear), "150 m for monsters too (the owner's one number)");
            Assert.IsFalse(s.Sees(1, hostileFar));

            // the asset can bring monsters down to NGO's 75 m later without new code
            s.Config.ObjectRadius = 75f;
            s.Config.LeaveMargin = 0f;
            int player = s.Player(2, 100f, 0f);
            int hostileMid = s.Object(-100f, 0f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, player), "players stay at 150 m");
            Assert.IsFalse(s.Sees(1, hostileMid), "a monster at 100 m is past 75 m");
            Assert.IsFalse(s.Sees(1, hostileNear));
        }

        [Test]
        public void AGhostOnTheNextCell_IsFound_AcrossTheBorderAndTheOrigin()
        {
            using var s = new Scene();
            // cells are 166 m: these pairs are close but in different cells, also across x = 0 and z = 0
            int a = s.Player(1, 165f, 165f);
            int b = s.Player(2, 167f, 167f);
            int c = s.Player(3, -1f, -1f);
            int d = s.Player(4, 1f, 1f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, b) && s.Sees(2, a));
            Assert.IsTrue(s.Sees(3, d) && s.Sees(4, c));
        }

        // ------------------------------------------------------------------ always relevant

        [Test]
        public void TheOwnGhost_IsAlwaysSent_TenKilometresFromEveryone()
        {
            using var s = new Scene();
            int a = s.Player(1, 10_000f, -10_000f);
            int b = s.Player(2, 0f, 0f);
            Assert.AreEqual(1, s.Fill(), "each is sent one ghost: its own");
            Assert.IsTrue(s.Sees(1, a));
            Assert.IsTrue(s.Sees(2, b));
            Assert.IsFalse(s.Sees(1, b));
            Assert.AreEqual(1, s.CountFor(1), "the own ghost is in once, not twice");
        }

        [Test]
        public void ACalloutOpponent_IsSentAt400Metres_AndGoesWhenTheCalloutEnds()
        {
            using var s = new Scene();
            int a = s.Player(1, 0f, 0f);
            int b = s.Player(2, 400f, 0f);
            s.Always.Add(new RelevancyAlways { NetworkId = 1, GhostId = b });
            s.Always.Add(new RelevancyAlways { NetworkId = 2, GhostId = a });
            s.Fill();
            Assert.IsTrue(s.Sees(1, b) && s.Sees(2, a), "the two of a Callout always see each other");

            s.Always.Clear();
            s.Fill();
            Assert.IsFalse(s.Sees(1, b), "the Callout is over: 400 m is out of range again");
            Assert.IsFalse(s.Sees(2, a));
        }

        [Test]
        public void AnAlwaysRelevantGhostThatIsAlsoNear_IsInOnce()
        {
            using var s = new Scene();
            s.Player(1, 0f, 0f);
            int b = s.Player(2, 20f, 0f);
            s.Always.Add(new RelevancyAlways { NetworkId = 1, GhostId = b });
            s.Fill();
            Assert.AreEqual(2, s.CountFor(1), "its own and the opponent, each once");
        }

        // ------------------------------------------------------------------ the lead's four edge cases (Change 4)

        [Test]
        public void EdgeA_AConnectionWithNoBody_GetsNothing_ThenItsOwnGhostWithTheBody()
        {
            using var s = new Scene();
            int other = s.Player(1, 0f, 0f);
            s.Connection(2);   // signed in, at character select or travelling: no ghost of its own
            s.Fill();
            Assert.AreEqual(0, s.CountFor(2), "no place in the world: nothing is sent, nothing throws");

            int body = s.Body(2, 30f, 0f);
            s.Fill();
            Assert.IsTrue(s.Sees(2, body), "the own ghost comes with the body");
            Assert.IsTrue(s.Sees(2, other), "and its surroundings with it");
            Assert.IsTrue(s.Sees(1, body));
        }

        [Test]
        public void EdgeB_DistanceIsFlat_ARoofOrCliffTopNeverHides()
        {
            // the fill system hands the rule x and z only (RelevancyRules.Flat): 100 m away and 500 m up is 100 m away
            using var s = new Scene();
            s.Player(1, 0f, 0f);
            float2 onTheCliff = RelevancyRules.Flat(new float3(100f, 500f, 0f));
            int b = s.Player(2, onTheCliff.x, onTheCliff.y);
            s.Fill();
            Assert.IsTrue(s.Sees(1, b));
        }

        [Test]
        public void EdgeC_ATeleport_GivesTheNewSurroundingsAtTheNextFill_AndTheOwnGhostNeverDropsOut()
        {
            using var s = new Scene();
            int a = s.Player(1, 0f, 0f);
            int oldNeighbour = s.Player(2, 50f, 0f);
            int newNeighbour = s.Player(3, 5_000f, 5_050f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, oldNeighbour));

            s.Move(a, 5_000f, 5_000f);   // Recall, a respawn, gm.tp
            s.Fill();
            Assert.IsTrue(s.Sees(1, a), "the own ghost is by owner, not by distance");
            Assert.IsTrue(s.Sees(1, newNeighbour));
            Assert.IsFalse(s.Sees(1, oldNeighbour), "the margin cannot keep what is kilometres away");
            Assert.IsTrue(s.Sees(3, a) && !s.Sees(2, a), "and the others see it arrive and go");
        }

        [Test]
        public void EdgeD_AGhostDestroyedBetweenFills_LeavesNoPairBehind()
        {
            using var s = new Scene();
            s.Player(1, 0f, 0f);
            int hostile = s.Object(10f, 0f);
            s.Fill();
            Assert.IsTrue(s.Sees(1, hostile));

            s.Destroy(hostile);
            s.Fill();
            Assert.IsFalse(s.Sees(1, hostile), "the set holds exactly the new pairs: a re-used ghost id finds no stale entry");
            Assert.AreEqual(1, s.Set.Count(), "only the own ghost is left");
        }

        // ------------------------------------------------------------------ the two lists on a connection

        [Test]
        public void BecameRelevant_NamesOnlyWhatIsNew()
        {
            using var s = new Scene();
            int a = s.Player(1, 0f, 0f);
            int b = s.Player(2, 10f, 0f);
            s.Fill();
            Assert.AreEqual(4, s.Became.Length, "the first fill: everything is new (two own ghosts, two neighbours)");
            s.Fill();
            Assert.AreEqual(0, s.Became.Length, "nothing moved: nothing is new");
            int c = s.Player(3, 0f, 10f);
            s.Fill();
            Assert.AreEqual(5, s.Became.Length, "the newcomer for both, and its own three");
            Assert.IsTrue(s.BecameNow(1, c) && s.BecameNow(2, c) && s.BecameNow(3, a) && s.BecameNow(3, b) && s.BecameNow(3, c));
        }

        [Test]
        public void TheSetsRoom_GrowsByDoubling_AndHoldsExactlyThePairs()
        {
            using var s = new Scene();
            for (int i = 1; i <= 12; i++) s.Player(i, i, 0f);   // 12 players in one spot: 144 pairs into a set made for 4
            s.Fill();
            Assert.AreEqual(144, s.Set.Count());
            int room = s.Set.Capacity;
            Assert.GreaterOrEqual(room, 144);
            s.Fill();
            Assert.AreEqual(room, s.Set.Capacity, "the same pairs again: the room stays");
        }
    }
}
