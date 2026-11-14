using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// A Unique Sky (owner's NIGHT list item 24, picks 14 Nov 2026: 1A glint stars, 2A stars only, no constellation lines,
    /// and all three nebulas 3A Soul Tide, 3B Ember Rose, 3C Veil Ribbon (owner's changes, 14 Nov), 4B amber second sun, 5A pearl corona eclipse, 6A green-teal aurora; spread-out constellations).
    /// Every number of the sky's events lives here; <see cref="SkyCalendar"/> turns (game day, world seed) into what is in
    /// the sky, so every client sees the same event at the same moment and the server only sends the seed, the day and a GM force.
    /// The default asset is <c>Resources/KN/SkyEvents</c> beside the presenter (made by Krytical Nova ▸ World ▸ Sky ▸ Bake Star Map).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Sky Event Settings")]
    public sealed class SkyEventSettings : ScriptableObject
    {
        public const string ResourcePath = "KN/SkyEvents";

        static SkyEventSettings loaded;
        static bool loadTried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { loaded = null; loadTried = false; }

        /// <summary>The sky's numbers and baked art (<c>Resources/KN/SkyEvents</c>); null until the star map is baked. Data only, so the
        /// server's sky commands and the client's presenter read the same asset.</summary>
        public static SkyEventSettings Default
        {
            get
            {
                if (!loadTried || (loaded == null && !Application.isPlaying)) { loaded = Resources.Load<SkyEventSettings>(ResourcePath); loadTried = true; }
                return loaded;
            }
        }

        [Header("Star map (baked by Krytical Nova ▸ World ▸ Sky ▸ Bake Star Map)")]
        [Tooltip("Lat-long star map: shaped stars, the constellations' stars and the galaxy band (owner 14 Nov: stars only, no constellation lines).")]
        public Texture2D starMap;
        [Tooltip("The overlay dome's shader (World/Sky/Shaders/SkyEvents.shader). Held here so a build always includes it.")]
        public Shader overlayShader;
        [Tooltip("Star map brightness.")]
        [Range(0f, 4f)] public float starBrightness = 1.6f;

        [Header("Star dome turn")]
        [Tooltip("Height of the dome's pole above the north horizon in degrees: stars wheel around it.")]
        [Range(0f, 90f)] public float poleElevation = 35f;
        [Tooltip("Compass heading of the pole (0 = +Z).")]
        [Range(-180f, 180f)] public float poleAzimuth = 0f;
        [Tooltip("Whole extra turns of the dome per game day on top of the monthly turn (0 = the stars only drift 360 / month degrees a day, about 22 over one night).")]
        [Range(0, 2)] public int extraTurnsPerDay = 0;
        [Tooltip("The dome turns 360 / this many degrees each game day, so a different constellation rides overhead each night of the moon cycle. 0 = the day cycle's lunar cycle.")]
        [Min(0f)] public float monthDays = 0f;

        [Header("Calendar")]
        [Tooltip("Game days in a game year: item 21's year is 4 seasons x 2 real weeks = 56 real days x 24 one-hour game days.")]
        [Min(8)] public int gameYearDays = 1344;
        [Tooltip("Game day of the year (0-based) of the meteor shower, The Emberfall.")]
        [Min(0)] public int showerDayOfYear = 700;
        [Tooltip("Game days between two returns of the comet, The Pale Wanderer (2160 = every 90 real days).")]
        [Min(16)] public int cometPeriodDays = 2160;
        [Tooltip("Game nights the comet stays in the sky each return.")]
        [Min(1)] public int cometNights = 6;
        [Tooltip("Game years between two total eclipses (owner 13 Nov: every 2 game years).")]
        [Min(1)] public int eclipseEveryYears = 2;
        [Tooltip("Game day of the eclipse year (0-based) the eclipse falls on.")]
        [Min(0)] public int eclipseDayOfYear = 300;

        [Header("Shooting stars (rare: owner 14 Nov, part 3)")]
        [Tooltip("Real seconds between two shooting stars, least and most (owner: about one every 1-3 minutes, never several at once outside The Emberfall). A hash picks each gap, so every client agrees.")]
        public Vector2 shootingStarGapSeconds = new Vector2(60f, 180f);
        [Tooltip("Real seconds one streak lasts (its head flies for the first 80 %, the trail fades out smoothly after).")]
        [Range(0.2f, 4f)] public float streakSeconds = 1.6f;
        [Tooltip("Half-width of a streak's trail in degrees (owner's Unity check, 14 Nov: 0.06 was too thin to see).")]
        [Range(0.02f, 1f)] public float streakWidthDegrees = 0.15f;
        [Tooltip("Size of a streak's glowing head in degrees.")]
        [Range(0.02f, 1.5f)] public float streakHeadDegrees = 0.3f;
        [Tooltip("Length of a streak in degrees of sky.")]
        [Range(2f, 60f)] public float streakDegrees = 21f;
        [Tooltip("Share of the streak's path the glowing trail covers behind the head (owner, part 3: longer trails).")]
        [Range(0.1f, 1f)] public float streakTrail = 0.7f;
        [Tooltip("Colour of a lone shooting star (HDR, linear: above 1 blooms).")]
        [ColorUsage(false, true)] public Color streakColor = new Color(1.6f, 1.54f, 1.4f);

        [Header("Meteor shower: The Emberfall (once a game year)")]
        [Tooltip("Streaks per real minute at the peak of the shower night.")]
        [Range(1f, 600f)] public float showerStreaksPerMinute = 300f;
        [Tooltip("Direction the shower radiates from: compass heading and elevation in degrees.")]
        public Vector2 showerRadiant = new Vector2(70f, 55f);
        [Tooltip("Real seconds one Emberfall streak lasts (longer than a lone shooting star: long glowing tails, owner's references 14 Nov).")]
        [Range(0.5f, 6f)] public float showerStreakSeconds = 2.4f;
        [Tooltip("Length of an Emberfall streak in degrees of sky.")]
        [Range(5f, 80f)] public float showerStreakDegrees = 36f;
        [Tooltip("Nearest and farthest a streak starts from the radiant, in degrees (far from it, the streaks run nearly parallel).")]
        public Vector2 showerSpread = new Vector2(10f, 70f);
        [Tooltip("Half-width of an Emberfall trail in degrees (wider than a lone streak, so they glow).")]
        [Range(0.02f, 1.5f)] public float showerWidthDegrees = 0.22f;
        [Tooltip("Size of an Emberfall head in degrees.")]
        [Range(0.02f, 2f)] public float showerHeadDegrees = 0.45f;
        [Tooltip("Hot white-blue head colour (HDR, linear: above 1 blooms).")]
        [ColorUsage(false, true)] public Color showerHeadColor = new Color(2.6f, 3.0f, 3.6f);
        [Tooltip("Glowing blue tail colour (HDR, linear: above 1 blooms).")]
        [ColorUsage(false, true)] public Color showerTrailColor = new Color(0.55f, 1.0f, 2.4f);

        [Header("Nebulas (3 nights in each moon cycle; each nebula night picks one of the looks, owner 14 Nov)")]
        [Tooltip("First night of the moon cycle (0-based) the nebula shows.")]
        [Min(0)] public int nebulaFirstNight = 3;
        [Tooltip("Nights in a row it shows.")]
        [Min(1)] public int nebulaNights = 3;
        [Tooltip("Its own path: compass heading where it rises and where it sets, and its highest elevation, in degrees.")]
        public Vector3 nebulaPath = new Vector3(110f, 250f, 62f);
        [Tooltip("The three nebulas (owner 14 Nov: all three, picked at random each nebula night from the world seed and the night, so every client agrees). Textures baked by Bake Star Map.")]
        public NebulaLook[] nebulas =
        {
            new NebulaLook { name = "Soul Tide", degrees = 84f, brightness = 1f, weight = 1f },
            new NebulaLook { name = "Ember Rose", degrees = 76f, brightness = 1f, weight = 1f },
            new NebulaLook { name = "Veil Ribbon", degrees = 100f, brightness = 1f, weight = 1f },
        };

        [Header("Comet: The Pale Wanderer")]
        [Tooltip("Where the comet's head sits: compass heading and elevation in degrees (it creeps a little each night).")]
        public Vector2 cometHead = new Vector2(240f, 38f);
        [Range(2f, 120f)] public float cometTailDegrees = 44f;
        [Range(0f, 3f)] public float cometBrightness = 1f;

        [Header("Second sun (pick 4B: amber, gives no light)")]
        public Color sun2Color = new Color(1f, 0.71f, 0.28f);
        [Tooltip("Disc radius in degrees.")]
        [Range(0.2f, 5f)] public float sun2Degrees = 1.1f;
        [Tooltip("Brightness of its art on the sky (colour only: it lights nothing).")]
        [Range(0f, 4f)] public float sun2Brightness = 1.2f;
        [Tooltip("Its own path: compass heading of its rise and its tilt from the zenith, in degrees.")]
        public Vector2 sun2Path = new Vector2(40f, 55f);
        [Tooltip("Game days of the planet's orbit cycle that decides whether the second sun is up at all.")]
        [Min(2)] public int sun2OrbitDays = 20;
        [Tooltip("Share of the orbit cycle the second sun is in the sky (0-1).")]
        [Range(0f, 1f)] public float sun2Share = 0.6f;
        [Tooltip("Game days its own day lasts relative to ours (rises a little later each day).")]
        [Range(0.5f, 2f)] public float sun2DayLength = 1.12f;
        [Tooltip("Share of the daytime (06:00-18:00) the second sun takes to fade in after dawn and out before dusk. It is never drawn at night (owner 14 Nov, part 3).")]
        [Range(0.02f, 0.3f)] public float sun2DayFade = 0.08f;

        [Header("Eclipse (pick 5A: pearl corona)")]
        [Tooltip("Time of day (0-1) the eclipse is at its fullest.")]
        [Range(0.3f, 0.7f)] public float eclipsePeak01 = 0.5f;
        [Tooltip("Real seconds from the moon's first bite of the sun to its last.")]
        [Range(30f, 900f)] public float eclipseSeconds = 240f;
        [Tooltip("Real seconds the moon is seen travelling toward the sun before the first bite, and away from it after the last (owner 14 Nov, part 3: a real body crossing the sky in one direction, never fading). It rises from the point opposite the sun, below the horizon, and sets there again.")]
        [Range(10f, 600f)] public float eclipseApproachSeconds = 120f;
        [Tooltip("The moon's radius against the sun's disc. Above 1 it hides the whole sun for a while (totality): the share of the bite-to-bite time in totality is (scale-1)/(scale+1).")]
        [Range(1.02f, 3f)] public float eclipseMoonScale = 1.5f;
        [Tooltip("The way the moon travels across the sun, in degrees: 0 left to right, 90 bottom to top.")]
        [Range(-180f, 180f)] public float eclipsePathDegrees = 20f;
        [Tooltip("How far the world dims toward the day cycle's night light at totality (owner 14 Nov: close to night, not pitch black). 0 = no change, 1 = the night values exactly. Eased in and out with the cover and restored exactly after.")]
        [Range(0f, 1f)] public float eclipseDarkness = 0.85f;
        [Tooltip("Deep-blue sky wash behind the moon disc and corona at totality (overlay only).")]
        public Color eclipseSkyColor = new Color(0.04f, 0.06f, 0.2f);
        public Color coronaColor = new Color(0.93f, 0.95f, 1f);
        [Tooltip("The sun's visible disc radius as a share of the custom sun art's half-size (the art has room around its disc). The eclipse moon is this times eclipseMoonScale.")]
        [Range(0.2f, 1f)] public float eclipseMoonSize = 0.5f;

        [Header("Night moon (owner 14 Nov, part 3: bigger and closer)")]
        [Tooltip("Scales the night moon's art on screen (1 = the day cycle's own size). Applied at run time on top of the day cycle's value; the custom sky's files are not touched and the custom sun never reads it.")]
        [Range(0.5f, 3f)] public float moonScale = 1.6f;

        [Header("Aurora (pick 6A: green-teal; winter and snowy zones only)")]
        public Color auroraLow = new Color(0.24f, 1f, 0.66f);
        public Color auroraHigh = new Color(0.25f, 0.78f, 1f);
        [Range(0f, 3f)] public float auroraBrightness = 1f;
        [Tooltip("Share of winter nights with an aurora.")]
        [Range(0f, 1f)] public float auroraNightShare = 0.5f;

        [Header("Daytime migrations")]
        [Tooltip("Game days between two migrations crossing the sky.")]
        [Min(2)] public int migrationEveryDays = 9;
        [Tooltip("Real seconds a flock takes to cross.")]
        [Range(20f, 600f)] public float migrationSeconds = 150f;

        [Header("World Effects (Settings, Graphics): Low keeps the sky simpler, High is the authored look")]
        [Tooltip("Most streaks drawn at once at World Effects Low, Medium, High, Ultra (the shader has SkyNow.MaxStreaks = 16 slots; only The Emberfall uses more than one).")]
        public int[] streaksByWorldEffects = { 6, 10, 16, 16 };
        [Tooltip("Whether the aurora shows at World Effects Low (the performance plan: off on Low).")]
        public bool auroraOnLow = false;

        /// <summary>Shooting stars drawn at once for a World Effects tier (0 Low .. 3 Ultra).</summary>
        public int StreaksFor(int tier)
        {
            if (streaksByWorldEffects == null || streaksByWorldEffects.Length == 0) return SkyNow.MaxStreaks;
            return Mathf.Clamp(streaksByWorldEffects[Mathf.Clamp(tier, 0, streaksByWorldEffects.Length - 1)], 0, SkyNow.MaxStreaks);
        }

        [Header("GM force")]
        [Tooltip("Real seconds a GM-forced event (sky.comet, sky.shower...) lasts.")]
        [Range(10f, 1800f)] public float forcedSeconds = 180f;
    }

    /// <summary>One nebula's look: its baked picture, size, brightness and how often a nebula night picks it.</summary>
    [System.Serializable]
    public sealed class NebulaLook
    {
        public string name;
        [Tooltip("Its baked picture (Bake Star Map writes T_Nebula_<name>.png).")]
        public Texture2D texture;
        [Tooltip("Angular size in degrees.")]
        [Range(5f, 140f)] public float degrees = 84f;
        [Range(0f, 3f)] public float brightness = 1f;
        [Tooltip("How often a nebula night picks it, against the others' weights (0 = never).")]
        [Min(0f)] public float weight = 1f;
    }
}
