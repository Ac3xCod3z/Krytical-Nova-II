using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Progression.Tests
{
    public sealed class MovementSettingsTests
    {
        private MovementSettings settings;

        [SetUp]
        public void SetUp() => settings = ScriptableObject.CreateInstance<MovementSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        // The fastest Run rank is 29 m/s; it reaches its destination at the arrival speed factor of that.
        [TestCase(5f, 60f)]
        [TestCase(29f, 60f)]
        [TestCase(29f, 30f)]
        [TestCase(29f, 20f)]
        // The fastest Run rank with the Move Speed stat at its limit (Boots and the ranged Stance).
        [TestCase(43.5f, 60f)]
        [TestCase(43.5f, 20f)]
        public void A_body_arrives_however_far_it_goes_in_a_frame(float fullSpeed, float framesPerSecond)
        {
            float frame = 1f / framesPerSecond;
            // Every phase a frame can fall in on the way to the point.
            for (float start = 10f; start < 11f; start += 0.013f)
            {
                float remaining = start;
                bool arrived = false;
                for (int i = 0; i < 2000 && !arrived; i++)
                {
                    float speed = fullSpeed * settings.ArrivalSpeedFactor(remaining, fullSpeed);
                    if (settings.ArrivesThisFrame(remaining, speed, frame)) { arrived = true; break; }
                    remaining -= speed * frame;
                    Assert.Greater(remaining, -0.0001f, $"{fullSpeed} m/s at {framesPerSecond} fps: stepped over the point from {start} m");
                }
                Assert.IsTrue(arrived, $"{fullSpeed} m/s at {framesPerSecond} fps from {start} m");
            }
        }

        [Test]
        public void A_body_far_from_its_destination_has_not_arrived()
        {
            Assert.IsFalse(settings.ArrivesThisFrame(5f, 29f, 1f / 30f));
            Assert.IsTrue(settings.ArrivesThisFrame(settings.stoppingDistance, 0f, 0f), "standing inside the stopping distance");
        }

        [Test]
        public void A_move_has_stalled_only_when_it_gained_nothing_for_the_whole_time()
        {
            Assert.IsFalse(settings.HasStalled(0f, settings.stallSeconds * 0.5f), "not watched for long enough yet");
            Assert.IsTrue(settings.HasStalled(0f, settings.stallSeconds), "held on the spot for the whole time");
            Assert.IsFalse(settings.HasStalled(settings.stallDistance, settings.stallSeconds), "it gained the distance");
        }

        // Walking is the slowest a move goes: in the time a stall takes to be called, it gains far more than a stall allows.
        [Test]
        public void A_walk_is_never_taken_for_a_stall()
        {
            Assert.Greater(settings.walkSpeed * settings.arrivalMinSpeedFactor * settings.stallSeconds, settings.stallDistance * 10f);
        }

        [Test]
        public void TheMoveSpeedStat_MultipliesSpeed_WithinItsLimits()
        {
            Assert.AreEqual(1f, settings.StatSpeedMultiplier(100f), 1e-5f, "100 % is normal speed");
            Assert.AreEqual(1.2f, settings.StatSpeedMultiplier(120f), 1e-5f, "the ranged Stance at full rank");
            Assert.AreEqual(settings.maxStatSpeedMultiplier, settings.StatSpeedMultiplier(400f), 1e-5f);
            Assert.AreEqual(settings.minStatSpeedMultiplier, settings.StatSpeedMultiplier(0f), 1e-5f);
        }
    }
}
