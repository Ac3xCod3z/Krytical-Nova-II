using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// The entity netcode's world is shown only when the player's body is made, the camera is on it and the frames are steady, or by
    /// the cap on a slow PC (Master, 31 Oct); the join's door (<see cref="JoinFlow"/>) waits on it.
    /// </summary>
    public class WorldEntryDoorTests
    {
        [TearDown]
        public void TearDown()
        {
            JoinFlow.HoldUntilWorldReady(null);
            JoinFlow.Joined();
        }

        /// <summary>Ticks <paramref name="door"/> from <paramref name="from"/> for <paramref name="seconds"/> at <paramref name="frameMs"/> a frame.</summary>
        static float Run(WorldEntryDoor door, float from, float seconds, float frameMs, bool body = true, bool camera = true)
        {
            float t = from;
            while (t < from + seconds && !door.IsOpen)
            {
                t += frameMs * 0.001f;
                door.Tick(t, frameMs, body, camera);
            }
            return t;
        }

        [Test]
        public void TheDoor_OpensAfterOneSecondOfSteadyFrames_WithTheBodyAndTheCamera()
        {
            var door = new WorldEntryDoor();
            Run(door, 0f, 5f, 16f, body: false);
            Assert.IsFalse(door.IsOpen, "no body yet: shut, however smooth the frames");
            Run(door, 5f, 5f, 16f, camera: false);
            Assert.IsFalse(door.IsOpen, "the camera not on the body yet: shut");
            float opened = Run(door, 10f, 5f, 16f);
            Assert.IsTrue(door.IsOpen);
            Assert.AreEqual("steady frames", door.OpenedBy);
            Assert.AreEqual(1f, opened - 10f, 0.05f, "one second of steady frames after both were ready");
        }

        [Test]
        public void ALongFrame_StartsTheSteadySecondAgain()
        {
            var door = new WorldEntryDoor();
            float t = Run(door, 0f, 0.8f, 16f);
            door.Tick(t + 0.78f, 779f, true, true);   // the warm-up hitch of GUI run clicktick-1
            Assert.IsFalse(door.IsOpen);
            float opened = Run(door, t + 0.78f, 3f, 16f);
            Assert.IsTrue(door.IsOpen);
            Assert.AreEqual(1f, opened - (t + 0.78f), 0.05f, "a full steady second after the hitch");
        }

        [Test]
        public void ASlowPc_IsLetInByTheCap_NeverKeptOut()
        {
            var door = new WorldEntryDoor();
            Run(door, 0f, 30f, 80f);   // every frame over the steady limit
            Assert.IsTrue(door.IsOpen, "the cap opens it");
            Assert.AreEqual("the cap", door.OpenedBy);
            Assert.AreEqual(WorldEntryDoor.CapSeconds, door.WaitedSeconds, 0.081f, "the cap counts from the body and the camera being ready (within a frame)");
            Assert.AreEqual(80f, door.WorstFrameMs, 0.01f);
        }

        [Test]
        public void TheJoin_WaitsForAHoldingWorld_AndANgoJoinIsUnchanged()
        {
            JoinFlow.Joined();
            Assert.IsFalse(JoinFlow.Joining, "no join and no hold: the door is open (the NGO path without a world hold)");
            var door = new WorldEntryDoor();
            JoinFlow.HoldUntilWorldReady(() => door.IsOpen);
            Assert.IsTrue(JoinFlow.Joining, "the world is not ready: the loading screen waits");
            Run(door, 0f, 2f, 16f);
            Assert.IsTrue(JoinFlow.Joining == false, "ready: the door opens");
            Assert.IsFalse(JoinFlow.HoldingForWorld, "the hold is let go once it said yes");
        }
    }
}
