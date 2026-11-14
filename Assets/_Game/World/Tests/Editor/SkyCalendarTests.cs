using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Item 24 (A Unique Sky): the sky calendar is pure, repeatable, spaced and as rare as the owner picked (24.1 A).</summary>
    public sealed class SkyCalendarTests
    {
        SkyEventSettings s;

        [SetUp] public void SetUp() => s = ScriptableObject.CreateInstance<SkyEventSettings>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(s);

        static SkyClock At(long day, float t, int seed = 1234) =>
            new SkyClock { Seed = seed, Day = day, Time01 = t, DayLengthSeconds = 3600f, MonthDays = 8f, ForcedAge = -1 };

        [Test]
        public void Constellations_LeaveClearSkyBetweenEachOther([Values(25f, 35f, 50f)] float pole)
        {
            float gap = StarCharts.SmallestGap(pole, out int a, out int b);
            Assert.That(gap, Is.GreaterThanOrEqualTo(StarCharts.MinGapDegrees),
                $"{StarCharts.All[a].Name} and {StarCharts.All[b].Name} are only {gap:0.0} deg apart");
            Assert.That(StarCharts.All.Length, Is.EqualTo(8));
        }

        [Test]
        public void NightN_HasConstellationN_OverheadAtMidnight()
        {
            for (long night = 100; night < 116; night++)
            {
                var now = SkyCalendar.Evaluate(At(night + 1, 0f), s);
                int expected = (int)(night % 8);
                Assert.That(now.OverheadConstellation, Is.EqualTo(expected), $"night {night}");
                var c = StarCharts.All[expected];
                Vector3 centre = SkyCalendar.FromDome(now.StarBasisX, now.StarBasisY, now.StarBasisZ, c.RightAscension, s.poleElevation + c.DeclinationOffset);
                Assert.That(Vector3.Angle(centre, Vector3.up), Is.LessThan(Mathf.Abs(c.DeclinationOffset) + 1f), $"night {night}");
            }
        }

        [Test]
        public void SameInput_SameSky_OtherSeed_OtherShootingStars()
        {
            var a = SkyCalendar.Evaluate(At(5000, 0.9f), s);
            var b = SkyCalendar.Evaluate(At(5000, 0.9f), s);
            Assert.That(a.DomeAngle, Is.EqualTo(b.DomeAngle));
            Assert.That(a.StreakCount, Is.EqualTo(b.StreakCount));
            int differ = 0;
            for (long n = 0; n < 40; n++) if (SkyCalendar.ShootingStars(1, n, s) != SkyCalendar.ShootingStars(2, n, s)) differ++;
            Assert.That(differ, Is.GreaterThan(5));
        }

        [Test]
        public void ShootingStars_AFewANight()
        {
            int total = 0;
            for (long n = 0; n < 200; n++)
            {
                int k = SkyCalendar.ShootingStars(77, n, s);
                Assert.That(k, Is.InRange(0, s.shootingStarsPerNight * 2));
                total += k;
            }
            Assert.That(total / 200f, Is.InRange(2f, 6f));
        }

        [Test]
        public void ShootingStar_ShowsAtItsHashedMoment()
        {
            // walk one night in 0.25 s steps: at least one streak is seen when the night has any
            long night = 9001;
            if (SkyCalendar.ShootingStars(1234, night, s) == 0) night++;
            int seen = 0;
            for (float sec = 0; sec < 1800f; sec += 0.25f)
            {
                float t = 0.75f + sec / 3600f;
                long day = night;
                if (t >= 1f) { t -= 1f; day++; }
                if (SkyCalendar.Evaluate(At(day, t), s).StreakCount > 0) seen++;
            }
            Assert.That(seen, Is.GreaterThan(0));
        }

        [Test]
        public void Shower_OnlyOnItsDayOfTheYear()
        {
            int nights = 0;
            for (long d = 0; d < s.gameYearDays * 2L; d++)
                if (SkyCalendar.Evaluate(At(d, 0.9f), s).ShowerNight) nights++;
            Assert.That(nights, Is.EqualTo(2));
        }

        [Test]
        public void Eclipse_EveryTwoGameYears_TotalAtItsPeak()
        {
            int days = 0;
            for (long d = 0; d < s.gameYearDays * 4L; d++)
            {
                var now = SkyCalendar.Evaluate(At(d, s.eclipsePeak01), s);
                if (now.EclipseDay) { days++; Assert.That(now.Eclipse, Is.EqualTo(1f)); }
                else Assert.That(now.Eclipse, Is.EqualTo(0f));
            }
            Assert.That(days, Is.EqualTo(2));
            Assert.That(SkyCalendar.EclipseCover(s.eclipseSeconds, s.eclipseSeconds, s.eclipseTotality), Is.EqualTo(0f));
        }

        [Test]
        public void Comet_SeveralNightsEachReturn()
        {
            int nights = 0;
            for (long d = 0; d < s.cometPeriodDays * 3L; d++)
                if (SkyCalendar.Evaluate(At(d, 0.95f), s).Comet > 0f) nights++;
            Assert.That(nights, Is.EqualTo(3 * s.cometNights));
        }

        [Test]
        public void Nebula_ThreeNightsInEachMoonCycle()
        {
            int nights = 0;
            for (long d = 800; d < 800 + 8 * 4; d++)
                if (SkyCalendar.Evaluate(At(d, 0.0f + 0.99f), s).Nebula > 0f) nights++;
            Assert.That(nights, Is.EqualTo(4 * s.nebulaNights));
        }

        [Test]
        public void GmForce_ShowsForItsLength_ThenEnds()
        {
            var c = At(123, 0.5f);   // midday: no comet would be up by itself
            c.Forced = SkyEventKind.Comet; c.ForcedAge = s.forcedSeconds * 0.5;
            Assert.That(SkyCalendar.Evaluate(c, s).Comet, Is.GreaterThan(0.5f));
            c.ForcedAge = s.forcedSeconds + 1;
            Assert.That(SkyCalendar.Evaluate(c, s).Comet, Is.EqualTo(0f));
            c.Forced = SkyEventKind.Eclipse; c.ForcedAge = s.eclipseSeconds * 0.5;
            Assert.That(SkyCalendar.Evaluate(c, s).Eclipse, Is.EqualTo(1f));
        }

        [Test]
        public void SecondSun_NeverUpDuringAnEclipse()
        {
            for (long d = 0; d < s.gameYearDays * 4L; d++)
            {
                var now = SkyCalendar.Evaluate(At(d, s.eclipsePeak01), s);
                if (now.EclipseDay) Assert.That(now.Sun2, Is.EqualTo(0f), $"day {d}");
            }
            var c = At(55, 0.42f);
            c.Forced = SkyEventKind.Eclipse; c.ForcedAge = s.eclipseSeconds * 0.5;
            Assert.That(SkyCalendar.Evaluate(c, s).Sun2, Is.EqualTo(0f));
        }

        [Test]
        public void SecondSun_UpOnlyForItsShareOfTheOrbit()
        {
            int up = 0, total = 0;
            for (long d = 0; d < s.sun2OrbitDays * 5L; d++)
                for (float t = 0f; t < 1f; t += 0.25f) { total++; if (SkyCalendar.Evaluate(At(d, t), s).Sun2 > 0f) up++; }
            Assert.That(up / (float)total, Is.InRange(s.sun2Share - 0.08f, s.sun2Share + 0.02f));
        }

        // ---- owner 14 Nov: all three nebulas, one picked at random each nebula night, the same for everyone

        [Test]
        public void NebulaNights_UseAllThreeLooks()
        {
            var seen = new int[s.nebulas.Length];
            int nights = 0;
            for (long night = 0; night < 2400; night++)
            {
                var now = SkyCalendar.Evaluate(At(night + 1, 0f), s);   // midnight of that night
                if (now.Nebula <= 0f) continue;
                nights++;
                Assert.That(now.NebulaLook, Is.InRange(0, s.nebulas.Length - 1), $"night {night}");
                seen[now.NebulaLook]++;
            }
            Assert.That(nights, Is.GreaterThan(600), "a nebula on 3 of every 8 nights");
            for (int i = 0; i < seen.Length; i++)
                Assert.That(seen[i] / (float)nights, Is.InRange(0.22f, 0.45f), $"{s.nebulas[i].name} shown on {seen[i]} of {nights} nebula nights");
        }

        [Test]
        public void SameNight_SameNebula_AllNightLong()
        {
            for (long night = 500; night < 600; night++)
            {
                var evening = SkyCalendar.Evaluate(At(night, 0.92f), s);       // 22:00
                var midnight = SkyCalendar.Evaluate(At(night + 1, 0f), s);
                var lateNight = SkyCalendar.Evaluate(At(night + 1, 0.08f), s);  // 02:00, still that night
                if (midnight.Nebula <= 0f) continue;
                Assert.That(evening.NebulaLook, Is.EqualTo(midnight.NebulaLook), $"night {night}");
                Assert.That(lateNight.NebulaLook, Is.EqualTo(midnight.NebulaLook), $"night {night}");
                Assert.That(SkyCalendar.Evaluate(At(night + 1, 0f), s).NebulaLook, Is.EqualTo(midnight.NebulaLook), "repeatable");
            }
        }

        [Test]
        public void NebulaPick_FollowsTheWeights()
        {
            s.nebulas[1].weight = 0f;
            for (long night = 0; night < 500; night++)
                Assert.That(SkyCalendar.PickNebula(1234, night, s), Is.Not.EqualTo(1), $"night {night}: a weight of 0 is never picked");
            foreach (var look in s.nebulas) look.weight = 0f;
            Assert.That(SkyCalendar.PickNebula(1234, 7, s), Is.EqualTo(-1));
        }
    }
}
