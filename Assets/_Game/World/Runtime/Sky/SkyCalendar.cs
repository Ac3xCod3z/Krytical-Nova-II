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
    }

    /// <summary>One streak in the sky: from <see cref="Start"/> to <see cref="End"/>, <see cref="Progress"/> 0-1 through its life.</summary>
    public struct SkyStreak
    {
        public Vector3 Start, End;
        public float Progress, Brightness;
    }

    /// <summary>Everything in the sky at one moment (all amounts 0-1; directions are world directions, y up).</summary>
    public struct SkyNow
    {
        public const int MaxStreaks = 4;

        /// <summary>The dome's turn in degrees: the right ascension on the meridian.</summary>
        public float DomeAngle;
        public Vector3 StarBasisX, StarBasisY, StarBasisZ;
        public int OverheadConstellation;

        public float Nebula;
        public Vector3 NebulaDir;

        public int StreakCount;
        public SkyStreak S0, S1, S2, S3;
        public bool ShowerNight;

        public float Comet;
        public Vector3 CometHead;

        public float Sun2;
        public Vector3 Sun2Dir;

        /// <summary>How much of the sun the moon covers (1 = totality).</summary>
        public float Eclipse;
        public bool EclipseDay;

        /// <summary>This night has an aurora where the season allows one (the presenter applies the winter / snowy-zone gate).</summary>
        public float Aurora;

        /// <summary>A flock crossing the sky: progress 0-1, or -1 when none.</summary>
        public float Migration;
        public float MigrationHeading;

        public SkyStreak Streak(int i) => i switch { 0 => S0, 1 => S1, 2 => S2, _ => S3 };

        public void AddStreak(in SkyStreak s)
        {
            if (StreakCount >= MaxStreaks) return;
            switch (StreakCount) { case 0: S0 = s; break; case 1: S1 = s; break; case 2: S2 = s; break; default: S3 = s; break; }
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
        const int SaltStreaks = 0x5A17, SaltCount = 0x0C47, SaltComet = 0xC0E7, SaltAurora = 0xA080, SaltMigration = 0x3161, SaltShower = 0x5402;

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
            float len = s != null ? s.forcedSeconds : 180f;
            if (c.ForcedAge > len) return false;
            age01 = (float)(c.ForcedAge / len);
            return true;
        }

        /// <summary>Everything in the sky now.</summary>
        public static SkyNow Evaluate(in SkyClock c, SkyEventSettings s)
        {
            var now = new SkyNow { Migration = -1f };
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
                now.NebulaDir = Dir(Mathf.LerpAngle(s.nebulaPath.x, s.nebulaPath.y, p), s.nebulaPath.z * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.15f, 0.85f, p)));
            }

            // ---- shooting stars: a few a night, at hashed moments
            double secondsIntoNight = np * nightSeconds;
            if (isNight)
            {
                int count = ShootingStars(c.Seed, night, s);
                for (int i = 0; i < count && now.StreakCount < SkyNow.MaxStreaks; i++)
                {
                    long key = night * 64 + i;
                    double at = (0.05 + 0.9 * Hash01(c.Seed ^ SaltStreaks, key)) * nightSeconds;
                    double age = secondsIntoNight - at;
                    if (age >= 0 && age < s.streakSeconds) now.AddStreak(RandomStreak(c.Seed ^ SaltStreaks, key, (float)(age / s.streakSeconds), s));
                }
            }
            if (Forced(c, SkyEventKind.Meteor, s, out _))
            {
                // forced: one streak every 4 s for the force's whole length
                double age = c.ForcedAge % 4.0;
                if (age < s.streakSeconds) now.AddStreak(RandomStreak(c.Seed ^ SaltStreaks, -1 - (long)(c.ForcedAge / 4.0), (float)(age / s.streakSeconds), s));
            }

            // ---- The Emberfall: once a game year, many streaks from one point
            now.ShowerNight = dayOfYear == s.showerDayOfYear;
            bool showerForced = Forced(c, SkyEventKind.Shower, s, out float shAge);
            if ((now.ShowerNight && isNight) || showerForced)
            {
                double t = showerForced ? c.ForcedAge : secondsIntoNight;
                float peak = showerForced ? Fade(shAge, 0.1f) : Mathf.Sin(Mathf.PI * Mathf.Clamp01(np));
                double slot = 60.0 / Math.Max(1.0, s.showerStreaksPerMinute * Math.Max(0.15, peak));
                long k = (long)Math.Floor(t / slot);
                Vector3 radiant = Dir(s.showerRadiant.x, s.showerRadiant.y);
                for (long j = k; j > k - 6 && now.StreakCount < SkyNow.MaxStreaks; j--)
                {
                    long key = (showerForced ? -100000 : night * 4096) + j;
                    double at = j * slot + Hash01(c.Seed ^ SaltShower, key) * slot;
                    double age = t - at;
                    if (age >= 0 && age < s.streakSeconds) now.AddStreak(RadiantStreak(c.Seed ^ SaltShower, key, radiant, (float)(age / s.streakSeconds), s));
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
            now.Sun2 = sunUp;

            // ---- total eclipse: every few game years, around midday
            long dayYear = (long)Math.Floor(c.Day / (double)s.gameYearDays);
            int dayInYear = (int)Mod(c.Day, s.gameYearDays);
            now.EclipseDay = Mod(dayYear, s.eclipseEveryYears) == 0 && dayInYear == s.eclipseDayOfYear;
            double eclipseAge = double.NaN;
            if (Forced(c, SkyEventKind.Eclipse, s, out _)) eclipseAge = c.ForcedAge - s.eclipseSeconds * 0.5;
            else if (now.EclipseDay) eclipseAge = (c.Time01 - s.eclipsePeak01) * dayLen;
            if (!double.IsNaN(eclipseAge)) now.Eclipse = EclipseCover((float)eclipseAge, s.eclipseSeconds, s.eclipseTotality);
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

        /// <summary>Shooting stars on a night: around the settings' number, hashed so every client agrees.</summary>
        public static int ShootingStars(int seed, long night, SkyEventSettings s) =>
            Mathf.FloorToInt(s.shootingStarsPerNight * (0.5f + Hash01(seed ^ SaltCount, night)));

        /// <summary>Cover 0-1 at <paramref name="age"/> seconds from the peak: smooth bite, flat totality, smooth release.</summary>
        public static float EclipseCover(float age, float seconds, float totality)
        {
            float half = seconds * 0.5f, flat = half * totality;
            float a = Mathf.Abs(age);
            if (a >= half) return 0f;
            if (a <= flat) return 1f;
            float x = 1f - (a - flat) / Mathf.Max(0.001f, half - flat);
            return x * x * (3f - 2f * x);
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

        static SkyStreak RadiantStreak(int seed, long key, Vector3 radiant, float progress, SkyEventSettings s)
        {
            float spin = 360f * Hash01(seed, key * 3 + 1);
            float from = Mathf.Lerp(12f, 50f, Hash01(seed, key * 3 + 2));
            Vector3 side = Quaternion.AngleAxis(spin, radiant) * Vector3.Cross(radiant, Vector3.up).normalized;
            Vector3 axis = Vector3.Cross(radiant, side).normalized;
            Vector3 a = Quaternion.AngleAxis(from, axis) * radiant;
            Vector3 b = Quaternion.AngleAxis(from + s.streakDegrees * 0.8f, axis) * radiant;
            return new SkyStreak { Start = a, End = b, Progress = progress, Brightness = 0.6f + 0.4f * Hash01(seed, key * 3 + 3) };
        }
    }
}
