using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The summon hop's lift (owner, 6 Nov 2026): ground at the press, over the seat mid-hop, on the seat at the end.</summary>
    public sealed class MountRideMotionTests
    {
        [Test]
        public void MountUp_StartsOnTheGround_EndsOnTheSeat()
        {
            MountSettings settings = ScriptableObject.CreateInstance<MountSettings>();
            Assert.AreEqual(0f, MountRideMotion.Lift(settings.mountUpLift, 0f, settings.mountUpSeconds), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.Lift(settings.mountUpLift, settings.mountUpSeconds, settings.mountUpSeconds), 0.001f);
            Assert.Greater(MountRideMotion.Lift(settings.mountUpLift, 0.47f * settings.mountUpSeconds, settings.mountUpSeconds), 1f,
                "the hop clears the seat before dropping into it");
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Dismount_StartsOnTheSeat_EndsOnTheGround()
        {
            MountSettings settings = ScriptableObject.CreateInstance<MountSettings>();
            Assert.AreEqual(1f, MountRideMotion.Lift(settings.dismountLift, 0f, settings.dismountSeconds), 0.001f);
            Assert.AreEqual(0f, MountRideMotion.Lift(settings.dismountLift, settings.dismountSeconds, settings.dismountSeconds), 0.001f);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Lift_ClampsOutsideTheHop_AndSitsWithNoCurve()
        {
            AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.AreEqual(0f, MountRideMotion.Lift(curve, -1f, 1f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.Lift(curve, 5f, 1f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.Lift(null, 0.3f, 1f), 0.001f);
        }

        [Test]
        public void Materialise_IsSolidWhenTheRiderLands()
        {
            MountSettings settings = ScriptableObject.CreateInstance<MountSettings>();
            float solid = settings.materialiseDelay + settings.materialiseSeconds;
            float landing = settings.stirrupsAt * settings.mountUpSeconds;
            Assert.AreEqual(landing, solid, 0.03f, "the mount is solid as the rider lands on the seat (owner pick Q2)");
            Assert.LessOrEqual(settings.vanishDelay + settings.vanishSeconds, settings.dismountSeconds,
                "the mount is gone before the rider is free");
            Object.DestroyImmediate(settings);
        }
    }
}
