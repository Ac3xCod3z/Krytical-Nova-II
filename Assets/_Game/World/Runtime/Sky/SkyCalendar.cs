using System;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>The sky events a GM can force (sent with the world's state, so every client shows the same one).</summary>
    public enum SkyEventKind : byte
    {
        None = 0, Meteor = 1, Shower = 2, Comet = 3, Nebula = 4, Sun2 = 5, Eclipse = 6, Aurora = 7, Migration = 8,
    }

    /// <summary>What the calendar needs: the world's seed, the calendar day and time, and any GM force.</summary>
    public struct SkyClock
    {
        public int Seed;
        /// <summary>The in-game calendar day (<see cref="WorldEnvironmentState.CalendarDay"/>).</summary>
        public long Day;
        /// <summary>Time of day 0-1 (0 midnight).</summary>
        public float Time01;
        /// <summary>Real seconds in a game day.</summary>
        public float DayLengthSeconds;
        /// <summary>Game days in a moon cycle (the dome turns once per cycle).</summary>
        public float MonthDays;
        public SkyEventKind Forced;
        /// <summary>Real seconds since the GM forced it (negative or NaN = none).</summary>
        public double ForcedAge;
        /// <summary>Angular radius of the sun's visible disc in radians, for the eclipse's cover (0 = <see cref="SkyCalendar.DefaultSunDisc"/>).</summary>
        public float SunDiscRadians;
    }

    /// <summary>One streak in the sky: from <see cref="Start"/> to <see cref="End"/>, <see cref="Progress"/> 0-1 through its life.</summary>
    public struct SkyStreak
    {
        public Vector3 Start, End;
        public float Progress, Brightness;
        /// <summary>An Emberfall streak (long, hot white-blue head, glowing blue tail) rather than a lone shooting star.</summary>
        public bool Shower;
    }

    /// <summary>Everything in the sky at one moment (all amounts 0-1; directions are world directions, y up).</summary>
    public struct SkyNow
    {
        /// <summary>Streak slots (owner 14 Nov, part 3: The Emberfall dense at its peak; a lone shooting star uses one).</summary>
        public const int MaxStreaks = 24;

        /// <summary>The dome's turn in degrees: the right ascension on the meridian.</summary>
        public float DomeAngle;
        public Vector3 StarBasisX, StarBasisY, StarBasisZ;
        public int OverheadConstellation;

        public float Nebula;
        public Vector3 NebulaDir;
        /// <summary>Which of <see cref="SkyEventSettings.nebulas"/> tonight's nebula is (-1 none).</summary>
        public int NebulaLook;

        public int StreakCount;
        public SkyStreak S0, S1, S2, S3, S4, S5, S6, S7, S8, S9, S10, S11, S12, S13, S14, S15, S16, S17, S18, S19, S20, S21, S22, S23;
        public bool ShowerNight;

        public float Comet;
        public Vector3 CometHead;

        public float Sun2;
        public Vector3 Sun2Dir;

        /// <summary>How much of the sun's disc the moon covers (1 = totality), from the two discs' real overlap.</summary>
        public float Eclipse;
        public bool EclipseDay;
        /// <summary>The eclipse moon is in the sky (from its rise opposite the sun, across the sun, to its set opposite it again).</summary>
        public bool EclipseMoon;
        /// <summary>Where the eclipse moon is: radians from the sun along its path (negative before the peak, positive after; only ever grows).</summary>
        public float EclipseMoonAngle;
        /// <summary>Angular radii (radians) of the sun's disc and the eclipse moon.</summary>
        public float EclipseSunRadius, EclipseMoonRadius;

        /// <summary>This night has an aurora where the season allows one (the presenter applies the winter / snowy-zone gate).</summary>
        public float Aurora;

        /// <summary>A flock crossing the sky: progress 0-1, or -1 when none.</summary>
        public float Migration;
        public float MigrationHeading;

        public SkyStreak Streak(int i) => i switch { 0 => S0, 1 => S1, 2 => S2, 3 => S3, 4 => S4, 5 => S5, 6 => S6, 7 => S7, 8 => S8, 9 => S9, 10 => S10, 11 => S11, 12 => S12, 13 => S13, 14 => S14, 15 => S15, 16 => S16, 17 => S17, 18 => S18, 19 => S19, 20 => S20, 21 => S21, 22 => S22, _ => S23 };

        public void AddStreak(in SkyStreak s)
        {
            if (StreakCount >= MaxStreaks) return;
            switch (StreakCount) { case 0: S0 = s; break; case 1: S1 = s; break; case 2: S2 = s; break; case 3: S3 = s; break; case 4: S4 = s; break; case 5: S5 = s; break; case 6: S6 = s; break; case 7: S7 = s; break; case 8: S8 = s; break; case 9: S9 = s; break; case 10: S10 = s; break; case 11: S11 = s; break; case 12: S12 = s; break; case 13: S13 = s; break; case 14: S14 = s; break; case 15: S15 = s; break; case 16: S16 = s; break; case 17: S17 = s; break; case 18: S18 = s; break; case 19: S19 = s; break; case 20: S20 = s; break; case 21: S21 = s; break; case 22: S22 = s; break; default: S23 = s; break; }
            StreakCount++;
        }
    }

    /// <summary>
    /// The sky's calendar (owner's NIGHT list item 24): a pure function of (world seed, calendar day, time of day, GM force) and
    /// <see cref="SkyEventSettings"/>, so every client works out the same events at the same moment and the server sends nothing new
    /// but the force. No rendering here: the presenter turns <see cref="SkyNow"/> into shader globals (black-sky rule).
    /// Tested by <c>SkyCalendarTests</c>.
    /// </summary>
    public static class SkyCalendar
    {
        const int SaltStreaks = 0x5A17, SaltCount = 0x0C47, SaltComet = 0xC0E7, SaltAurora = 0xA080, SaltMigration = 0x3161, SaltShower = 0x5402, SaltNebula = 0x4EB1;

        public static float Hash01(int seed, long n) => WorldEnvironmentNetwork.Hash01(seed, n);

        static long Mod(long a, long m) => ((a % m) + m) % m;

        /// <summary>The night a moment belongs to: the evening's calendar day (after midnight it is still yesterday's night).</summary>
        public static long NightOf(long day, float time01) => time01 < 0.5f ? day - 1 : day;

        /// <summary>How far through its night a moment is: 0 at 18:00, 0.5 at midnight, 1 at 06:00 (outside 0-1 by day).</summary>
        public static float NightProgress(float time01)
        {
            float t = time01 < 0.5f ? time01 + 1f : time01;
            return (t - 0.75f) / 0.5f;
        }

        /// <summary>A world direction from a compass heading and an elevation in degrees.</summary>
        public static Vector3 Dir(float headingDeg, float elevationDeg)
        {
            float h = headingDeg * Mathf.Deg2Rad, e = elevationDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(h) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(h) * Mathf.Cos(e));
        }

        /// <summary>
        /// The dome's frame turned to <paramref name="domeAngle"/>: for a view direction d, right ascension = atan2(d·Y, d·X) and
        /// declination = asin(d·Z). A star at the meridian's right ascension and declination = pole elevation is at the zenith.
        /// </summary>
        public static void StarBasis(float poleElevation, float poleAzimuth, float domeAngle, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            z = Dir(poleAzimuth, poleElevation);
            Vector3 meridian = (Vector3.up - z * Vector3.Dot(Vector3.up, z));
            meridian = meridian.sqrMagnitude < 1e-6f ? Vector3.forward : meridian.normalized;
            Vector3 east = Vector3.Cross(z, meridian);
            float m = domeAngle * Mathf.Deg2Rad, c = Mathf.Cos(m), s = Mathf.Sin(m);
            x = c * meridian - s * east;
            y = s * meridian + c * east;
        }

        /// <summary>The world direction of a dome point (ra, dec in degrees) in a turned frame.</summary>
        public static Vector3 FromDome(Vector3 x, Vector3 y, Vector3 z, float raDeg, float decDeg)
        {
            Vector3 d = StarCharts.DomeDir(raDeg, decDeg);
            return x * d.x + y * d.y + z * d.z;
        }

        /// <summary>The dome's turn: one turn per moon cycle (plus whole extra turns a day), so night N has constellation N overhead at midnight.</summary>
        public static float DomeAngle(in SkyClock c, SkyEventSettings s)
        {
            float month = s != null && s.monthDays > 0f ? s.monthDays : Mathf.Max(1f, c.MonthDays);
            int extra = s != null ? s.extraTurnsPerDay : 0;
            double days = c.Day + c.Time01 - 1.0;
            double turns = days / month + days * extra;
            return (float)((turns - Math.Floor(turns)) * 360.0);
        }

        static float Fade(float x, float edge) => Mathf.Clamp01(x / edge) * Mathf.Clamp01((1f - x) / edge);

        static bool Forced(in SkyClock c, SkyEventKind kind, SkyEventSettings s, out float age01)
        {
            age01 = 0f;
            if (c.Forced != kind || double.IsNaN(c.ForcedAge) || c.ForcedAge < 0) return false;
            float len = ForcedLength(kind, s);
            if (c.ForcedAge > len) return false;
            age01 = (float)(c.ForcedAge / len);
            return true;
        }

        /// <summary>
        /// Which nebula a night shows (owner 14 Nov: all three, at random): a weighted pick hashed from the world seed and the night, so
        /// the server and every client agree with no network data. -1 when no look has a weight.
        /// </summary>
        public static int PickNebula(int seed, long night, SkyEventSettings s)
        {
            var looks = s != null ? s.nebulas : null;
            if (looks == null || looks.Length == 0) return -1;
            float total = 0f;
            for (int i = 0; i < looks.Length; i++) if (looks[i] != null) total += Mathf.Max(0f, looks[i].weight);
            if (total <= 0f) return -1;
            float r = Hash01(seed ^ SaltNebula, night) * total;
            int last = -1;
            for (int i = 0; i < looks.Length; i++)
            {
                if (looks[i] == null || looks[i].weight <= 0f) continue;
                last = i;
                r -= looks[i].weight;
                if (r < 0f) return i;
            }
            return last;
        }

        /// <summary>How long a GM force lasts: <see cref="SkyEventSettings.forcedSeconds"/>, but an eclipse always runs from first bite to
        /// the end (<see cref="SkyEventSettings.eclipseSeconds"/>), so the world's light never jumps back mid-way.</summary>
        public static float ForcedLength(SkyEventKind kind, SkyEventSettings s)
        {
            float len = s != null ? s.forcedSeconds : 180f;
            if (kind == SkyEventKind.Eclipse && s != null) len = Mathf.Max(len, s.eclipseSeconds + 2f * s.eclipseApproachSeconds);
            return len;
        }

        /// <summary>Everything in the sky now.</summary>
        public static SkyNow Evaluate(in SkyClock c, SkyEventSettings s)
        {
            var now = new SkyNow { Migration = -1f, NebulaLook = -1 };
            if (s == null) return now;

            float poleEl = s.poleElevation;
            now.DomeAngle = DomeAngle(c, s);
            StarBasis(poleEl, s.poleAzimuth, now.DomeAngle, out now.StarBasisX, out now.StarBasisY, out now.StarBasisZ);
            now.OverheadConstellation = StarCharts.NearestTo(now.DomeAngle);

            float dayLen = Mathf.Max(10f, c.DayLengthSeconds);
            float nightSeconds = dayLen * 0.5f;
            long night = NightOf(c.Day, c.Time01);
            float np = NightProgress(c.Time01);
            bool isNight = np > 0f && np < 1f;
            float nightFade = isNight ? Fade(np, 0.08f) : 0f;
            long year = (long)Math.Floor(night / (double)s.gameYearDays);
            int dayOfYear = (int)Mod(night, s.gameYearDays);
            int month = Mathf.Max(1, Mathf.RoundToInt(s.monthDays > 0f ? s.monthDays : Mathf.Max(1f, c.MonthDays)));

            // ---- nebula: its own path, a few nights in each moon cycle
            int cycleNight = (int)Mod(night, month);
            bool nebulaNight = cycleNight >= s.nebulaFirstNight && cycleNight < s.nebulaFirstNight + s.nebulaNights;
            float nebulaProgress = np;
            if (Forced(c, SkyEventKind.Nebula, s, out float nAge)) { nebulaNight = true; nebulaProgress = 0.5f; }
            if (nebulaNight && (isNight || c.Forced == SkyEventKind.Nebula))
            {
                float p = Mathf.Clamp01(nebulaProgress);
                now.Nebula = c.Forced == SkyEventKind.Nebula ? Fade(nAge, 0.1f) : nightFade;
                now.NebulaLook = PickNebula(c.Seed, night, s);
                if (now.NebulaLook < 0) now.Nebula = 0f;
                now.NebulaDir = Dir(Mathf.LerpAngle(s.nebulaPath.x, s.nebulaPath.y, p), s.nebulaPath.z * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.15f, 0.85f, p)));
            }

            // ---- shooting stars (owner 14 Nov, part 3: RARE, about one every 1-3 minutes, never several at once)
            double secondsIntoNight = np * nightSeconds;
            if (isNight) RareStreaks(ref now, c.Seed ^ SaltStreaks, night * 4096, secondsIntoNight, false, s);
            // forced (sky.meteor): the first one a second after the GM asks, then the same rare gaps for the force's length
            if (Forced(c, SkyEventKind.Meteor, s, out _) && c.ForcedAge >= 1.0)
                RareStreaks(ref now, c.Seed ^ SaltStreaks, -(1L << 40), c.ForcedAge - 1.0, true, s);

            // ---- The Emberfall: once a game year, many long glowing streaks falling from one point (owner's references, 14 Nov)
            now.ShowerNight = dayOfYear == s.showerDayOfYear;
            bool showerForced = Forced(c, SkyEventKind.Shower, s, out float shAge);
            if ((now.ShowerNight && isNight) || showerForced)
            {
                double t = showerForced ? c.ForcedAge : secondsIntoNight;
                // dense at the peak: a fixed beat at the peak rate, each beat lit with the chance the night's curve gives (stable frame to frame)
                float peak = showerForced ? Fade(shAge, 0.1f) : Mathf.Sin(Mathf.PI * Mathf.Clamp01(np));
                double slot = 60.0 / Math.Max(1.0, s.showerStreaksPerMinute);
                float life = Mathf.Max(0.1f, s.showerStreakSeconds);
                long k = (long)Math.Floor(t / slot);
                int back = Mathf.Min(256, Mathf.CeilToInt((float)(life / slot)) + 1);
                Vector3 radiant = Dir(s.showerRadiant.x, s.showerRadiant.y);
                for (long j = k; j > k - back && now.StreakCount < SkyNow.MaxStreaks; j--)
                {
                    long key = (showerForced ? -(1L << 41) : night * 65536) + j;
                    if (Hash01(c.Seed ^ SaltShower, key * 7 + 5) >= Mathf.Max(0.05f, peak)) continue;
                    double at = j * slot + Hash01(c.Seed ^ SaltShower, key) * slot;
                    double age = t - at;
                    if (age >= 0 && age < life) now.AddStreak(RadiantStreak(c.Seed ^ SaltShower, key, radiant, (float)(age / life), s));
                }
            }

            // ---- The Pale Wanderer: every few real months, several nights
            long cycle = (long)Math.Floor(night / (double)s.cometPeriodDays);
            int into = (int)Mod(night, s.cometPeriodDays);
            int start = (int)(Hash01(c.Seed ^ SaltComet, cycle) * Math.Max(1, s.cometPeriodDays - s.cometNights));
            bool cometNight = into >= start && into < start + s.cometNights;
            if (Forced(c, SkyEventKind.Comet, s, out float cAge)) { now.Comet = Fade(cAge, 0.1f); now.CometHead = Dir(s.cometHead.x, s.cometHead.y); }
            else if (cometNight && isNight)
            {
                now.Comet = nightFade;
                now.CometHead = Dir(s.cometHead.x + (into - start) * 2.5f, s.cometHead.y);
            }

            // ---- second sun: its own path, up only for part of the orbit cycle
            double orbit = (c.Day + c.Time01) / s.sun2OrbitDays;
            float orbit01 = (float)(orbit - Math.Floor(orbit));
            float sunUp = orbit01 < s.sun2Share ? Fade(orbit01 / Mathf.Max(0.01f, s.sun2Share), 0.06f) : 0f;
            bool sun2Forced = Forced(c, SkyEventKind.Sun2, s, out float s2Age);
            if (sun2Forced) sunUp = Mathf.Max(sunUp, Fade(s2Age, 0.1f));
            double own = (c.Day + c.Time01) / s.sun2DayLength;
            float own01 = (float)(own - Math.Floor(own));
            if (sun2Forced) own01 = 0.42f;                      // force: put it well up in the sky
            float theta = (own01 - 0.25f) * Mathf.PI * 2f;
            Vector3 east = Dir(s.sun2Path.x, 0f);
            Vector3 high = Quaternion.AngleAxis(s.sun2Path.y, east) * Vector3.up;
            now.Sun2Dir = (east * Mathf.Cos(theta) + high * Mathf.Sin(theta)).normalized;
            // owner 14 Nov, part 3: NEVER at night; it shows only while our sun is up and fades in after dawn, out before dusk
            now.Sun2 = sunUp * Sun2DayGate(c.Time01, s);

            // ---- total eclipse: every few game years, around midday
            long dayYear = (long)Math.Floor(c.Day / (double)s.gameYearDays);
            int dayInYear = (int)Mod(c.Day, s.gameYearDays);
            now.EclipseDay = Mod(dayYear, s.eclipseEveryYears) == 0 && dayInYear == s.eclipseDayOfYear;
            // the moon is a real body (owner 14 Nov, part 3): it rises opposite the sun, travels one way along a great circle through the
            // sun, crosses it at the peak and sets opposite it again; the cover is the two discs' real overlap, nothing fades
            float sr = c.SunDiscRadians > 0f ? c.SunDiscRadians : DefaultSunDisc;
            now.EclipseSunRadius = sr;
            now.EclipseMoonRadius = sr * Mathf.Max(1.02f, s.eclipseMoonScale);
            float reach = s.eclipseSeconds * 0.5f + s.eclipseApproachSeconds;
            double eclipseAge = double.NaN;
            if (Forced(c, SkyEventKind.Eclipse, s, out _)) eclipseAge = c.ForcedAge - reach;
            else if (now.EclipseDay) eclipseAge = (c.Time01 - s.eclipsePeak01) * dayLen;
            if (!double.IsNaN(eclipseAge) && Math.Abs(eclipseAge) <= reach)
            {
                now.EclipseMoon = true;
                now.EclipseMoonAngle = EclipseMoonAngle((float)eclipseAge, s, sr, now.EclipseMoonRadius);
                now.Eclipse = DiscCover(Mathf.Abs(now.EclipseMoonAngle), sr, now.EclipseMoonRadius);
            }
            // owner (14 Nov): the second sun is never in the sky during a solar eclipse (the whole eclipse day, forced or natural)
            if (now.EclipseDay || now.Eclipse > 0f) now.Sun2 = 0f;

            // ---- aurora: some winter nights (the presenter applies the season / zone gate)
            if (Forced(c, SkyEventKind.Aurora, s, out float aAge)) now.Aurora = Fade(aAge, 0.1f);
            else if (isNight && Hash01(c.Seed ^ SaltAurora, night) < s.auroraNightShare) now.Aurora = nightFade;

            // ---- daytime migrations
            if (Forced(c, SkyEventKind.Migration, s, out float mAge)) { now.Migration = mAge; now.MigrationHeading = 360f * Hash01(c.Seed ^ SaltMigration, -7); }
            else if (Hash01(c.Seed ^ SaltMigration, c.Day) < 1f / s.migrationEveryDays)
            {
                float startT = 0.33f + 0.3f * Hash01(c.Seed ^ SaltMigration, c.Day * 3 + 1);
                float p = (c.Time01 - startT) * dayLen / s.migrationSeconds;
                if (p >= 0f && p < 1f) { now.Migration = p; now.MigrationHeading = 360f * Hash01(c.Seed ^ SaltMigration, c.Day * 3 + 2); }
            }
            return now;
        }

        /// <summary>Angular radius of the sun's disc (radians) when the clock does not give one: the day cycle's sun art (tan 0.05) times the default share 0.5.</summary>
        public const float DefaultSunDisc = 0.025f;

        /// <summary>
        /// How much the second sun shows for the time of day: 0 all night (18:00-06:00), fading in over <see cref="SkyEventSettings.sun2DayFade"/>
        /// of the day after dawn and out before dusk (owner 14 Nov, part 3: never drawn at night).
        /// </summary>
        public static float Sun2DayGate(float time01, SkyEventSettings s)
        {
            float dayP = (time01 - 0.25f) / 0.5f;
            return Fade(dayP, Mathf.Max(0.01f, s != null ? s.sun2DayFade : 0.08f));
        }

        /// <summary>
        /// Rare streaks (owner 14 Nov, part 3): one per beat of (least + most gap) / 2 seconds, placed in its beat by a hash so the gap between
        /// two is always between <see cref="SkyEventSettings.shootingStarGapSeconds"/> x and y. <paramref name="t"/> is seconds since the beats began.
        /// </summary>
        static void RareStreaks(ref SkyNow now, int seed, long keyBase, double t, bool firstAtZero, SkyEventSettings s)
        {
            if (t < 0) return;
            long k = (long)Math.Floor(t / RareBeat(s));
            for (long j = k; j >= k - 1 && j >= 0; j--)
            {
                if (j == 0 && !firstAtZero) continue;   // a night's first one comes a beat after dusk
                double age = t - RareStreakStart(seed, keyBase, j, firstAtZero, s);
                if (age >= 0 && age < s.streakSeconds) now.AddStreak(RandomStreak(seed, keyBase + j, (float)(age / s.streakSeconds), s));
            }
        }

        static double RareBeat(SkyEventSettings s)
        {
            float gMin = Mathf.Max(1f, s.shootingStarGapSeconds.x), gMax = Mathf.Max(gMin, s.shootingStarGapSeconds.y);
            return (gMin + gMax) * 0.5;
        }

        /// <summary>When rare streak <paramref name="j"/> starts, in seconds since the beats began (also for tests).</summary>
        public static double RareStreakStart(int seed, long keyBase, long j, bool firstAtZero, SkyEventSettings s)
        {
            float gMin = Mathf.Max(1f, s.shootingStarGapSeconds.x), gMax = Mathf.Max(gMin, s.shootingStarGapSeconds.y);
            if (j == 0 && firstAtZero) return 0.0;
            return j * RareBeat(s) + (gMax - gMin) * 0.5f * Hash01(seed, keyBase + j);
        }

        /// <summary>
        /// The eclipse moon's place on its path: radians from the sun's centre, negative before the peak and positive after, and it only ever
        /// grows (never turns back). Across the bite (first to last contact, <see cref="SkyEventSettings.eclipseSeconds"/>) it moves at one steady
        /// speed; beyond, it speeds up smoothly (no jump in speed) to reach the point opposite the sun (pi, below the horizon) after
        /// <see cref="SkyEventSettings.eclipseApproachSeconds"/>.
        /// </summary>
        public static float EclipseMoonAngle(float age, SkyEventSettings s, float sunRadius, float moonRadius)
        {
            float half = Mathf.Max(1f, s.eclipseSeconds * 0.5f), approach = Mathf.Max(1f, s.eclipseApproachSeconds);
            float contact = sunRadius + moonRadius, v = contact / half;
            float a = Mathf.Abs(age), g;
            if (a <= half) g = v * a;
            else
            {
                float x = Mathf.Min(a - half, approach);
                float k = Mathf.Max(0f, (Mathf.PI - contact - v * approach) / (approach * approach * approach));
                g = contact + v * x + k * x * x * x;
            }
            return age < 0f ? -g : g;
        }

        /// <summary>Share of the sun's disc (radius <paramref name="r1"/>) the moon's (radius <paramref name="r2"/>) hides at centre distance <paramref name="d"/>.</summary>
        public static float DiscCover(float d, float r1, float r2)
        {
            if (d >= r1 + r2) return 0f;
            if (d <= r2 - r1) return 1f;
            if (d <= r1 - r2) return (r2 * r2) / (r1 * r1);
            float a1 = Mathf.Acos(Mathf.Clamp((d * d + r1 * r1 - r2 * r2) / (2f * d * r1), -1f, 1f));
            float a2 = Mathf.Acos(Mathf.Clamp((d * d + r2 * r2 - r1 * r1) / (2f * d * r2), -1f, 1f));
            float tri = 0.5f * Mathf.Sqrt(Mathf.Max(0f, (-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2)));
            float area = r1 * r1 * a1 + r2 * r2 * a2 - tri;
            return Mathf.Clamp01(area / (Mathf.PI * r1 * r1));
        }

        static SkyStreak RandomStreak(int seed, long key, float progress, SkyEventSettings s)
        {
            float head = 360f * Hash01(seed, key * 5 + 1);
            float elev = Mathf.Lerp(28f, 72f, Hash01(seed, key * 5 + 2));
            float travel = 360f * Hash01(seed, key * 5 + 3);
            Vector3 a = Dir(head, elev);
            Vector3 tangent = Vector3.Cross(a, Vector3.up).normalized;
            tangent = Quaternion.AngleAxis(travel, a) * tangent;
            Vector3 axis = Vector3.Cross(a, tangent);
            Vector3 b = Quaternion.AngleAxis(s.streakDegrees, axis) * a;
            if (b.y < 0.05f) { (a, b) = (b, a); }
            return new SkyStreak { Start = a, End = b, Progress = progress, Brightness = 0.7f + 0.3f * Hash01(seed, key * 5 + 4) };
        }

        /// <summary>
        /// One Emberfall streak: it starts <see cref="SkyEventSettings.showerSpread"/> from the radiant and runs straight away from it, on the
        /// part of the sky below the radiant (falling toward the horizon, like the owner's references), so the streaks in view run nearly parallel.
        /// </summary>
        static SkyStreak RadiantStreak(int seed, long key, Vector3 radiant, float progress, SkyEventSettings s)
        {
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, radiant);
            down = down.sqrMagnitude < 1e-6f ? Vector3.ProjectOnPlane(Vector3.forward, radiant).normalized : down.normalized;
            float spin = (Hash01(seed, key * 3 + 1) - 0.5f) * 150f;
            Vector3 way = Quaternion.AngleAxis(spin, radiant) * down;
            float from = Mathf.Lerp(s.showerSpread.x, s.showerSpread.y, Hash01(seed, key * 3 + 2)) * Mathf.Deg2Rad;
            float to = from + s.showerStreakDegrees * Mathf.Deg2Rad;
            Vector3 a = radiant * Mathf.Cos(from) + way * Mathf.Sin(from);
            Vector3 b = radiant * Mathf.Cos(to) + way * Mathf.Sin(to);
            return new SkyStreak { Start = a, End = b, Progress = progress, Brightness = 0.6f + 0.4f * Hash01(seed, key * 3 + 3), Shower = true };
        }
    }
}
