using KryticalNova.Crossfire;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Crossfire.Tests
{
    /// <summary>Crossfire's Muster Gates, areas and the send-back list (owner's DAY list item 17).</summary>
    public sealed class CrossfireGateTests
    {
        static CrossfireSpot At(int x) => new CrossfireSpot { Mm = new Vector3Int(x, 0, 0), YawCdeg = 9000 };

        [Test]
        public void APlayerWhoStepsIntoAYard_WhileItTakesPlayers_IsSentBackWhereTheyCameFrom()
        {
            var e = new CrossfireEntries<int>();
            e.Observe(1, At(-5000), inArea: false, inYard: false, joinOpen: true);
            e.Observe(1, At(1000), inArea: true, inYard: true, joinOpen: true);
            Assert.AreEqual(1, e.Count);
            Assert.IsTrue(e.TryEntry(1, out CrossfireSpot from));
            Assert.AreEqual(-5000, from.Mm.x, "the last place outside the war area");
            var back = e.TakeSendBack(_ => true);
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(-5000, back[0].to.Mm.x);
            Assert.AreEqual(0, e.Count, "cleared for the next war");
        }

        [Test]
        public void TheYardsTakeNobody_OutsideTheJoinWindow_AndWhoLeftIsNotSentBack()
        {
            var e = new CrossfireEntries<int>();
            e.Observe(1, At(-5000), false, false, false);
            e.Observe(1, At(1000), true, true, joinOpen: false);
            Assert.AreEqual(0, e.Count, "too early: not joined");
            e.Observe(2, At(-3000), false, false, true);
            e.Observe(2, At(1000), true, true, true);
            Assert.AreEqual(0, e.TakeSendBack(who => who != 2).Count, "already out of the war area: left where they are");
            e.Observe(3, At(-3000), false, false, true);
            e.Observe(3, At(1000), true, true, true);
            e.ForgetWhere(who => who == 3);
            Assert.AreEqual(0, e.Count, "logged out: forgotten");
        }

        [Test]
        public void SomeoneWhoWasNeverSeenOutside_HasNoPlaceToGoBackTo()
        {
            var e = new CrossfireEntries<int>();
            e.Observe(1, At(1000), true, true, true);   // came into the game inside the yard
            Assert.AreEqual(0, e.Count);
        }

        [Test]
        public void AnArea_HoldsWhatIsInsideItsBox()
        {
            var go = new GameObject("yard");
            try
            {
                CrossfireArea yard = go.AddComponent<CrossfireArea>();
                CrossfireArea.Track(yard);   // OnEnable does not run in EditMode
                yard.size = new Vector3(50, 10, 40);
                go.transform.position = new Vector3(100, 0, 0);
                Assert.IsTrue(yard.Contains(new Vector3(120, 2, 19)));
                Assert.IsFalse(yard.Contains(new Vector3(126, 0, 0)));
                Assert.IsTrue(CrossfireArea.AnyContains(new Vector3(100, 0, 0), yardsOnly: true));
                yard.kind = CrossfireArea.AreaKind.Field;
                Assert.IsFalse(CrossfireArea.AnyContains(new Vector3(100, 0, 0), yardsOnly: true), "a field is no yard");
                Assert.IsTrue(CrossfireArea.AnyContains(new Vector3(100, 0, 0), yardsOnly: false));
            }
            finally
            {
                CrossfireArea.Untrack(go.GetComponent<CrossfireArea>());
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AGate_BlocksTheWalkWhileShut_AndShowsItsLook()
        {
            var go = new GameObject("gate");
            var shut = new GameObject("shut");
            var open = new GameObject("open");
            try
            {
                MusterGate gate = go.AddComponent<MusterGate>();
                gate.blocker = go.AddComponent<NavMeshObstacle>();
                gate.shutLook = shut;
                gate.openLook = open;
                gate.Apply(false);
                Assert.IsTrue(gate.blocker.enabled && gate.blocker.carving, "shut: a carved obstacle");
                Assert.IsTrue(shut.activeSelf);
                Assert.IsFalse(open.activeSelf);
                gate.Apply(true);
                Assert.IsFalse(gate.blocker.enabled, "open: the walk goes through");
                Assert.IsFalse(shut.activeSelf);
                Assert.IsTrue(open.activeSelf);
                Assert.IsTrue(gate.IsOpen);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(shut);
                Object.DestroyImmediate(open);
            }
        }

        [Test]
        public void TheGates_FollowTheServersClockWhereThereIsOne()
        {
            CrossfireState saved = CrossfireView.Server, savedClient = CrossfireView.Client;
            bool had = CrossfireView.HasServer;
            try
            {
                CrossfireView.HasServer = true;
                CrossfireView.Server = new CrossfireState { Known = true, Now = new CrossfireNow { Phase = CrossfirePhase.War } };
                CrossfireView.Client = default;
                Assert.IsTrue(CrossfireView.ForGates.GatesOpen);
                CrossfireView.HasServer = false;
                Assert.IsFalse(CrossfireView.ForGates.GatesOpen, "a client that has heard nothing keeps them shut");
            }
            finally
            {
                CrossfireView.Server = saved;
                CrossfireView.Client = savedClient;
                CrossfireView.HasServer = had;
            }
        }
    }
}
