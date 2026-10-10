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
    }
}
