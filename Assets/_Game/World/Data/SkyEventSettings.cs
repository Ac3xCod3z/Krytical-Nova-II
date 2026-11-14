using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// A Unique Sky (owner's NIGHT list item 24, picks 14 Nov 2026: 1A glint stars, 2B faint constellation lines at deep night,
    /// 3A Soul Tide nebula, 4B amber second sun, 5A pearl corona eclipse, 6A green-teal aurora; spread-out constellations).
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
        [Tooltip("Lat-long star map: RGB = shaped stars + galaxy band, A = the constellation lines (shown only at deep night).")]
        public Texture2D starMap;
        [Tooltip("The Soul Tide nebula, drawn around its own moving direction.")]
        public Texture2D nebulaTexture;
        [Tooltip("The overlay dome's shader (World/Sky/Shaders/SkyEvents.shader). Held here so a build always includes it.")]
        public Shader overlayShader;
        [Tooltip("Star map brightness.")]
        [Range(0f, 4f)] public float starBrightness = 1.6f;
        [Tooltip("How bright the constellation lines get at the darkest hour of a clear night (pick 2B: faint).")]
        [Range(0f, 1f)] public float constellationLineStrength = 0.22f;
        [Tooltip("Star visibility above which the constellation lines start to fade in (1 = only the very darkest hours).")]
        [Range(0f, 1f)] public float constellationLineFrom = 0.85f;

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

        [Header("Shooting stars (a few a night)")]
        [Tooltip("Shooting stars each night (a hash picks the exact number around this).")]
        [Range(0, 20)] public int shootingStarsPerNight = 4;
        [Tooltip("Real seconds one streak lasts.")]
        [Range(0.2f, 4f)] public float streakSeconds = 0.9f;
        [Tooltip("Length of a streak in degrees of sky.")]
        [Range(2f, 40f)] public float streakDegrees = 14f;

        [Header("Meteor shower: The Emberfall (once a game year)")]
        [Tooltip("Streaks per real minute at the peak of the shower night.")]
        [Range(1f, 120f)] public float showerStreaksPerMinute = 30f;
        [Tooltip("Direction the shower radiates from: compass heading and elevation in degrees.")]
        public Vector2 showerRadiant = new Vector2(70f, 55f);

        [Header("Nebula (Soul Tide, 3 nights in each moon cycle)")]
        [Tooltip("First night of the moon cycle (0-based) the nebula shows.")]
        [Min(0)] public int nebulaFirstNight = 3;
        [Tooltip("Nights in a row it shows.")]
        [Min(1)] public int nebulaNights = 3;
        [Tooltip("Angular size of the nebula in degrees.")]
        [Range(5f, 90f)] public float nebulaDegrees = 38f;
        [Tooltip("Its own path: compass heading where it rises and where it sets, and its highest elevation, in degrees.")]
        public Vector3 nebulaPath = new Vector3(110f, 250f, 62f);
        [Range(0f, 3f)] public float nebulaBrightness = 1f;

        [Header("Comet: The Pale Wanderer")]
        [Tooltip("Where the comet's head sits: compass heading and elevation in degrees (it creeps a little each night).")]
        public Vector2 cometHead = new Vector2(240f, 38f);
        [Range(2f, 60f)] public float cometTailDegrees = 22f;
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

        [Header("Eclipse (pick 5A: pearl corona)")]
        [Tooltip("Time of day (0-1) the eclipse is at its fullest.")]
        [Range(0.3f, 0.7f)] public float eclipsePeak01 = 0.5f;
        [Tooltip("Real seconds from first bite to the end.")]
        [Range(30f, 900f)] public float eclipseSeconds = 240f;
        [Tooltip("Share of the eclipse spent in totality.")]
        [Range(0f, 0.8f)] public float eclipseTotality = 0.35f;
        [Tooltip("How far the world dims toward the day cycle's night light at totality (owner 14 Nov: close to night, not pitch black). 0 = no change, 1 = the night values exactly. Eased in and out with the cover and restored exactly after.")]
        [Range(0f, 1f)] public float eclipseDarkness = 0.85f;
        [Tooltip("Deep-blue sky wash behind the moon disc and corona at totality (overlay only).")]
        public Color eclipseSkyColor = new Color(0.04f, 0.06f, 0.2f);
        public Color coronaColor = new Color(0.93f, 0.95f, 1f);
        [Tooltip("The moon disc's radius as a share of the custom sun art's half-size (the art has room around its disc).")]
        [Range(0.2f, 1f)] public float eclipseMoonSize = 0.5f;

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

        [Header("GM force")]
        [Tooltip("Real seconds a GM-forced event (sky.comet, sky.shower...) lasts.")]
        [Range(10f, 1800f)] public float forcedSeconds = 180f;
    }
}
