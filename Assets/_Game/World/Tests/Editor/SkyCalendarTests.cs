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
        public void Constellations_LeaveClearSkyBetweenEachOther()
        {
            // owner 14 Nov, part 3: "too close together" -> at least 45 degrees of empty sky between any two, at the real pole
            Assert.That(StarCharts.MinGapDegrees, Is.GreaterThanOrEqualTo(45f));
            float gap = StarCharts.SmallestGap(s.poleElevation, out int a, out int b);
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
            for (long j = 1; j < 40; j++)
                if (System.Math.Abs(SkyCalendar.RareStreakStart(1, 0, j, false, s) - SkyCalendar.RareStreakStart(2, 0, j, false, s)) > 0.5) differ++;
            Assert.That(differ, Is.GreaterThan(5));
        }

        /// <summary>Walks a night (not the Emberfall's) in 0.25 s steps and returns the seconds each streak started.</summary>
        System.Collections.Generic.List<float> StreakOnsets(long night, int seed, out int mostAtOnce)
        {
            var onsets = new System.Collections.Generic.List<float>();
            mostAtOnce = 0;
            int before = 0;
            for (float sec = 0; sec < 1800f; sec += 0.25f)
            {
                float t = 0.75f + sec / 3600f;
                long day = night;
                if (t >= 1f) { t -= 1f; day++; }
                var now = SkyCalendar.Evaluate(At(day, t, seed), s);
                Assert.That(now.ShowerNight, Is.False, "pick a night that is not the Emberfall's");
                mostAtOnce = Mathf.Max(mostAtOnce, now.StreakCount);
                if (now.StreakCount > before) onsets.Add(sec);
                before = now.StreakCount;
            }
            return onsets;
        }

        [Test]
        public void ShootingStars_Rare_OneEveryOneToThreeMinutes_NeverTwoAtOnce()
        {
            // owner 14 Nov, part 3: "too many shooting stars" -> about one every 1-3 minutes, never several at once outside The Emberfall
            Assert.That(s.streakSeconds, Is.LessThan(s.shootingStarGapSeconds.x));
            foreach (long night in new long[] { 9001, 9002, 12345 })
            {
                var onsets = StreakOnsets(night, 1234, out int most);
                Assert.That(most, Is.LessThanOrEqualTo(1), $"night {night}: several at once");
                Assert.That(onsets.Count, Is.InRange(8, 32), $"night {night}: {onsets.Count} in a 30-minute night");
                Assert.That(onsets[0], Is.InRange(s.shootingStarGapSeconds.x - 0.5f, s.shootingStarGapSeconds.y + 0.5f), $"night {night}: first one");
                for (int i = 1; i < onsets.Count; i++)
                    Assert.That(onsets[i] - onsets[i - 1], Is.InRange(s.shootingStarGapSeconds.x - 0.5f, s.shootingStarGapSeconds.y + 0.5f),
                                $"night {night}: gap {i}");
            }
        }

        [Test]
        public void GmForcedMeteor_ComesAtOnce_ThenRareGaps()
        {
            var c = At(123, 0.95f);
            c.Forced = SkyEventKind.Meteor;
            int most = 0, onsets = 0, before = 0;
            for (double age = 0; age < s.forcedSeconds; age += 0.25)
            {
                c.ForcedAge = age;
                var now = SkyCalendar.Evaluate(c, s);
                c.Forced = SkyEventKind.None;
                int natural = SkyCalendar.Evaluate(c, s).StreakCount;
                c.Forced = SkyEventKind.Meteor;
                int forced = now.StreakCount - natural;
                most = Mathf.Max(most, forced);
                if (forced > before) onsets++;
                if (age >= 1.0 && age < 1.0 + s.streakSeconds - 0.3) Assert.That(forced, Is.EqualTo(1), $"the first one right away ({age:0.00} s)");
                before = forced;
            }
            Assert.That(most, Is.EqualTo(1));
            Assert.That(onsets, Is.InRange(1, 3), "180 s of force at 60-180 s gaps");
        }

        [Test]
        public void Emberfall_DenseAtItsPeak_LongStreaksFromOneRadiant()
        {
            // owner 14 Nov, part 3: the references show many long glowing streaks at once, all from one point
            long night = s.showerDayOfYear + s.gameYearDays * 3L;
            var now = SkyCalendar.Evaluate(At(night + 1, 0.0f), s);   // midnight: the peak
            Assert.That(now.ShowerNight, Is.True);
            Assert.That(now.StreakCount, Is.GreaterThanOrEqualTo(8), "dense at the peak");
            Vector3 radiant = SkyCalendar.Dir(s.showerRadiant.x, s.showerRadiant.y);
            for (int i = 0; i < now.StreakCount; i++)
            {
                var st = now.Streak(i);
                Assert.That(st.Shower, Is.True);
                Assert.That(Vector3.Angle(st.Start, st.End), Is.EqualTo(s.showerStreakDegrees).Within(0.2f), "long streaks");
                Assert.That(Vector3.Angle(radiant, st.End), Is.GreaterThan(Vector3.Angle(radiant, st.Start)), "runs away from the radiant");
                Assert.That(Vector3.Dot(Vector3.Cross(st.Start, st.End), radiant), Is.EqualTo(0f).Within(1e-3f), "on a great circle through the radiant");
            }
            // early in the night (low on its curve) it is thinner than at the peak
            var early = SkyCalendar.Evaluate(At(night, 0.77f), s);
            Assert.That(early.StreakCount, Is.LessThan(now.StreakCount));
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
                if (now.EclipseDay) { days++; Assert.That(now.Eclipse, Is.EqualTo(1f)); Assert.That(now.EclipseMoonAngle, Is.EqualTo(0f)); }
                else { Assert.That(now.Eclipse, Is.EqualTo(0f)); Assert.That(now.EclipseMoon, Is.False); }
            }
            Assert.That(days, Is.EqualTo(2));
            float sr = SkyCalendar.DefaultSunDisc, mr = sr * s.eclipseMoonScale;
            Assert.That(SkyCalendar.DiscCover(sr + mr, sr, mr), Is.EqualTo(0f));
            Assert.That(SkyCalendar.DiscCover(mr - sr, sr, mr), Is.EqualTo(1f));
            Assert.That(SkyCalendar.DiscCover(sr, sr, mr), Is.InRange(0.3f, 0.95f));
        }

        [Test]
        public void EclipseMoon_TravelsOneWay_AcrossTheSun_NeverFadesOrTurnsBack()
        {
            // owner 14 Nov, part 3: a real body crossing the sky in ONE direction: seen on approach, crosses the sun, keeps going
            var c = At(400, 0.5f);
            c.Forced = SkyEventKind.Eclipse;
            float len = SkyCalendar.ForcedLength(SkyEventKind.Eclipse, s);
            float reach = s.eclipseSeconds * 0.5f + s.eclipseApproachSeconds;
            Assert.That(len, Is.GreaterThanOrEqualTo(2f * reach));
            float last = float.NegativeInfinity, lastStep = -1f, lastCover = 0f;
            bool covered = false, uncovered = false;
            for (double age = 0; age <= 2.0 * reach; age += 0.5)
            {
                c.ForcedAge = age;
                var now = SkyCalendar.Evaluate(c, s);
                Assert.That(now.EclipseMoon, Is.True, $"the moon is in the sky the whole way ({age} s)");
                float a = now.EclipseMoonAngle;
                Assert.That(a, Is.GreaterThan(last), $"never turns back ({age} s)");
                float step = a - last;
                if (lastStep > 0f && !float.IsInfinity(last))
                    Assert.That(step, Is.LessThan(lastStep * 1.6f + 1e-3f).And.GreaterThan(lastStep / 1.6f - 1e-3f), $"no jump in speed ({age} s)");
                if (!float.IsInfinity(last)) lastStep = step;
                last = a;
                // cover rises then falls with the real overlap; at the ends the moon is opposite the sun (below the horizon)
                if (now.Eclipse >= 1f) covered = true;
                if (covered && now.Eclipse < lastCover) uncovered = true;
                if (!covered) Assert.That(now.Eclipse, Is.GreaterThanOrEqualTo(lastCover - 1e-5f), $"cover only grows before totality ({age} s)");
                if (uncovered) Assert.That(now.Eclipse, Is.LessThanOrEqualTo(lastCover + 1e-5f), $"cover only shrinks after totality ({age} s)");
                lastCover = now.Eclipse;
            }
            Assert.That(covered && uncovered, Is.True);
            c.ForcedAge = 0;
            Assert.That(SkyCalendar.Evaluate(c, s).EclipseMoonAngle, Is.EqualTo(-Mathf.PI).Within(1e-3f), "rises opposite the sun");
            c.ForcedAge = 2.0 * reach;
            Assert.That(SkyCalendar.Evaluate(c, s).EclipseMoonAngle, Is.EqualTo(Mathf.PI).Within(1e-3f), "sets opposite the sun");
            c.ForcedAge = 2.0 * reach + 1.0;
            Assert.That(SkyCalendar.Evaluate(c, s).EclipseMoon, Is.False);
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
            c.Forced = SkyEventKind.Eclipse; c.ForcedAge = s.eclipseSeconds * 0.5 + s.eclipseApproachSeconds;
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
            c.Forced = SkyEventKind.Eclipse; c.ForcedAge = s.eclipseSeconds * 0.5 + s.eclipseApproachSeconds;
            Assert.That(SkyCalendar.Evaluate(c, s).Sun2, Is.EqualTo(0f));
        }

        [Test]
        public void SecondSun_UpOnlyForItsShareOfTheOrbit()
        {
            int up = 0, total = 0;
            for (long d = 0; d < s.sun2OrbitDays * 5L; d++)
                for (float t = 0.4f; t < 0.6f; t += 0.05f) { total++; if (SkyCalendar.Evaluate(At(d, t), s).Sun2 > 0f) up++; }
            Assert.That(up / (float)total, Is.InRange(s.sun2Share - 0.08f, s.sun2Share + 0.02f));
        }

        [Test]
        public void SecondSun_NeverAtNight_AtEveryNightHour_EvenForced()
        {
            // owner 14 Nov, part 3: the orange glow near the HUD at midnight was the second sun; it is never drawn at night
            for (long d = 0; d < s.sun2OrbitDays * 3L; d++)
                for (int hour = 18; hour <= 30; hour++)
                    for (int quarter = 0; quarter < 4; quarter++)
                    {
                        float t = ((hour + quarter * 0.25f) / 24f) % 1f;
                        if (hour == 30 && quarter > 0) break;
                        var c = At(d, t);
                        Assert.That(SkyCalendar.Evaluate(c, s).Sun2, Is.EqualTo(0f), $"day {d} {hour % 24}:{quarter * 15:00}");
                        c.Forced = SkyEventKind.Sun2; c.ForcedAge = s.forcedSeconds * 0.5;
                        Assert.That(SkyCalendar.Evaluate(c, s).Sun2, Is.EqualTo(0f), $"forced, day {d} {hour % 24}:{quarter * 15:00}");
                    }
            // it fades with the day: nothing at dawn, partly in soon after, full by mid-morning
            Assert.That(SkyCalendar.Sun2DayGate(0.25f, s), Is.EqualTo(0f));
            Assert.That(SkyCalendar.Sun2DayGate(0.25f + 0.5f * s.sun2DayFade * 0.5f, s), Is.InRange(0.3f, 0.7f));
            Assert.That(SkyCalendar.Sun2DayGate(0.5f, s), Is.EqualTo(1f));
            Assert.That(SkyCalendar.Sun2DayGate(0.75f, s), Is.EqualTo(0f));
            var noon = At(3, 0.5f); noon.Forced = SkyEventKind.Sun2; noon.ForcedAge = s.forcedSeconds * 0.5;
            Assert.That(SkyCalendar.Evaluate(noon, s).Sun2, Is.GreaterThan(0.9f), "a GM can still show it by day");
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

        [Test]
        public void GmForcedEclipse_RunsItsWholeLength_AndEndsAtNoCover()
        {
            s.forcedSeconds = 180f; s.eclipseSeconds = 240f; s.eclipseApproachSeconds = 120f;
            SkyNow At2(double age) { var c = At(400, 0.5f); c.Forced = SkyEventKind.Eclipse; c.ForcedAge = age; return SkyCalendar.Evaluate(c, s); }
            Assert.That(At2(240).Eclipse, Is.EqualTo(1f), "totality at the middle");
            Assert.That(At2(200).Eclipse, Is.GreaterThan(0f).And.LessThan(1f), "still a partial bite after the usual 180 s force");
            Assert.That(At2(359.5).Eclipse, Is.LessThan(0.01f), "almost gone at the last contact: the light never jumps back");
            Assert.That(At2(362).Eclipse, Is.EqualTo(0f));
            Assert.That(At2(362).EclipseMoon, Is.True, "the moon keeps going after the last contact");
            Assert.That(At2(481).EclipseMoon, Is.False);
        }

        [Test]
        public void Nebula_TwiceAsBigAsBefore()
        {
            // owner 14 Nov, part 3: the nebula 2x or more on screen (was 38 / 34 / 46 degrees)
            float[] before = { 38f, 34f, 46f };
            for (int i = 0; i < before.Length; i++) Assert.That(s.nebulas[i].degrees, Is.GreaterThanOrEqualTo(2f * before[i]), s.nebulas[i].name);
            Assert.That(s.cometTailDegrees, Is.GreaterThanOrEqualTo(44f), "comet tail about 2x (was 22)");
        }
    }
}
