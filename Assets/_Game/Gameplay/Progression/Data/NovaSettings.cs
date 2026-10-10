using System;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>The four Nova Standings tracks. Saved and sent by number: append only.</summary>
    public enum StandingTrack : byte
    {
        Vanguard = 0,
        Bulwark = 1,
        Ironblood = 2,
        Mindwell = 3,
    }

    /// <summary>The chances of one Ascension attempt, across one stretch of Ascension levels.</summary>
    [Serializable]
    public struct AscensionOdds
    {
        [Tooltip("Applies while the Ascension level before the attempt is at or below this.")]
        public int upTo;
        [Tooltip("Percent chance to gain a level, to stay, and to lose a level. The three add up to 100.")]
        [Range(0f, 100f)] public float rise, hold, slip;

        public AscensionOdds(int upTo, float rise, float hold, float slip)
        {
            this.upTo = upTo; this.rise = rise; this.hold = hold; this.slip = slip;
        }
    }

    /// <summary>Ascension: Renown and Creds spent on a chance to gain a level. Every level raises all four attributes.</summary>
    [Serializable]
    public sealed class AscensionSettings
    {
        [Min(1)] public int maxLevel = 96;
        [Tooltip("Levels per Nova tier: a new Nova at +1, +9, +17 ... with 8.")]
        [Min(1)] public int levelsPerTier = 8;
        [Min(0)] public int renownCost = 100;
        [Tooltip("Twelve Sky 2 charges 1,000,000 Silver; this is half.")]
        [Min(0)] public long credsCost = 500000;
        [Tooltip("Strength, Agility, Vitality and Spirit each gain this per Ascension level.")]
        [Min(0)] public int attributesPerLevel = 1;
        [Tooltip("On: a slip never takes the character out of a Nova tier it has reached (at +1, +9, +17 ... it is a plain fail).")]
        public bool slipKeepsTier = true;
        [Tooltip("In order of 'upTo'. A level above the last entry uses the last entry.")]
        public AscensionOdds[] odds =
        {
            new AscensionOdds(24, 55f, 35f, 10f),
            new AscensionOdds(96, 45f, 40f, 15f),
        };
    }

    /// <summary>One Standings track: its name and the stat each of its levels raises.</summary>
    [Serializable]
    public struct StandingTrackInfo
    {
        public StandingTrack track;
        public string name;
        public StatType stat;
        [TextArea(1, 2)] public string blurb;

        public StandingTrackInfo(StandingTrack track, string name, StatType stat, string blurb)
        {
            this.track = track; this.name = name; this.stat = stat; this.blurb = blurb;
        }
    }

    /// <summary>What a faction calls each Standing level, lowest first.</summary>
    [Serializable]
    public struct FactionRankWords
    {
        public FactionId faction;
        public string[] words;

        public FactionRankWords(FactionId faction, params string[] words)
        {
            this.faction = faction; this.words = words;
        }
    }

    /// <summary>Nova Standings: Renown spent on levels of four tracks, each level a lasting percent of one stat.</summary>
    [Serializable]
    public sealed class StandingSettings
    {
        [Tooltip("Renown for level 1, 2, 3 ... of a track. Its length is the number of levels. (Twelve Sky 2's list.)")]
        public int[] renownCosts = { 800, 1700, 2500, 3400, 4200, 5100, 5900, 6800, 7600, 8500, 9300, 10000 };
        [Tooltip("Percent of the track's stat each level adds. Every track a character owns counts, worn or not.")]
        [Min(0f)] public float percentPerLevel = 1f;
        public StandingTrackInfo[] tracks =
        {
            new StandingTrackInfo(StandingTrack.Vanguard, "Vanguard", StatType.AttackPower, "First into the fight. Raises Attack Power."),
            new StandingTrackInfo(StandingTrack.Bulwark, "Bulwark", StatType.DefensePower, "The line that holds. Raises Defense."),
            new StandingTrackInfo(StandingTrack.Ironblood, "Ironblood", StatType.MaxHP, "Still standing when the rest are down. Raises Max HP."),
            new StandingTrackInfo(StandingTrack.Mindwell, "Mindwell", StatType.MaxChi, "A deeper reserve to draw on. Raises Max Flux."),
        };
        public FactionRankWords[] rankWords =
        {
            new FactionRankWords(FactionId.Hegemony, "Associate", "Operative", "Specialist", "Agent", "Auditor", "Captain",
                "Controller", "Prefect", "Executor", "Consul", "Regent", "Hegemon"),
            new FactionRankWords(FactionId.Null, "Unit", "Routine", "Process", "Daemon", "Kernel", "Cipher",
                "Override", "Vector", "Architect", "Apex", "Zenith", "Singularity"),
            new FactionRankWords(FactionId.Covenant, "Kinsworn", "Oathsworn", "Hearthguard", "Shieldbearer", "Runebearer", "Thane",
                "Bannerlord", "Stormborn", "High Thane", "Elderblood", "Dawnbringer", "Mythborn"),
        };

        public int MaxLevel => renownCosts != null ? renownCosts.Length : 0;
    }

    /// <summary>What a ring of the Nova is made of.</summary>
    public enum NovaRingPattern : byte
    {
        /// <summary>An unbroken band of light.</summary>
        Plain = 0,
        /// <summary>Cut into dashes.</summary>
        Dashed = 1,
        /// <summary>A row of glyphs, like an inscription.</summary>
        Glyphs = 2,
    }

    /// <summary>
    /// One ring of the Nova: a band turning about the star on its own tilted axis. A character's Nova shows every
    /// ring whose <see cref="fromTier"/> it has reached, so a higher tier is the lower one with more turning in it.
    /// </summary>
    [Serializable]
    public struct NovaRing
    {
        [Tooltip("The first Nova tier that shows this ring.")]
        [Range(1, 12)] public int fromTier;
        [Tooltip("How far out it turns, as a share of the Nova's radius.")]
        [Range(0.2f, 1.4f)] public float radius;
        [Tooltip("How wide the band is, as a share of the Nova's radius.")]
        [Range(0.01f, 0.4f)] public float width;
        [Tooltip("How it leans, in degrees, before it turns: about the X axis, then about Z. (0, 0) lies flat like a halo.")]
        public Vector2 tilt;
        [Tooltip("Degrees a second it turns about its own axis. Negative turns the other way.")]
        public float spin;
        [Tooltip("Degrees a second its axis itself swings round the vertical, so the tilt is never seen from one side only.")]
        public float tumble;
        [Tooltip("On: the band stands on its edge, like a hoop with writing round it. Off: it lies in its own plane, like a planet's ring.")]
        public bool upright;
        public NovaRingPattern pattern;
        [Tooltip("Dashes or glyphs round the ring.")]
        [Range(0, 64)] public int marks;
        [Tooltip("Turns a second of a bright point running round the ring. 0 = none.")]
        public float comet;
        [Range(0f, 2f)] public float brightness;

        public NovaRing(int fromTier, float radius, float width, float tiltX, float tiltZ, float spin, float tumble,
            NovaRingPattern pattern = NovaRingPattern.Plain, int marks = 0, float comet = 0f, bool upright = false, float brightness = 1f)
        {
            this.fromTier = fromTier; this.radius = radius; this.width = width; tilt = new Vector2(tiltX, tiltZ);
            this.spin = spin; this.tumble = tumble; this.pattern = pattern; this.marks = marks; this.comet = comet;
            this.upright = upright; this.brightness = brightness;
        }
    }

    /// <summary>What changes from one Nova tier to the next: its colours, its size, and how much burns round the star.</summary>
    [Serializable]
    public struct NovaTierLook
    {
        [Tooltip("The star itself, and the hot middle of everything.")]
        [ColorUsage(false, true)] public Color inner;
        [Tooltip("The rings, the glow and the rays.")]
        [ColorUsage(false, true)] public Color outer;
        [Tooltip("Radius of the Nova (metres): where a ring of radius 1 turns.")]
        [Range(0.05f, 0.6f)] public float radius;
        [Tooltip("Radius of the star, as a share of the Nova's radius.")]
        [Range(0.05f, 0.6f)] public float core;
        [Tooltip("How strongly the star glows into the air round it.")]
        [Range(0f, 1.5f)] public float corona;
        [Tooltip("Rays of light from the star. 0 = none.")]
        [Range(0, 16)] public int rays;
        [Tooltip("How far the rays reach, as a share of the Nova's radius.")]
        [Range(0f, 2f)] public float rayLength;
        [Tooltip("Sparks in orbit round the star.")]
        [Range(0, 40)] public int motes;
        [Tooltip("Colour runs through the whole spectrum round the rings.")]
        [Range(0f, 1f)] public float prism;
        [Min(0f)] public float brightness;
    }

    /// <summary>Where the Nova sits, what it is made of, and how each tier looks.</summary>
    [Serializable]
    public sealed class NovaLookSettings
    {
        [Tooltip("Shader KryticalNova/NovaCore: the star.")]
        public Material coreMaterial;
        [Tooltip("Shader KryticalNova/NovaRing: every ring.")]
        public Material ringMaterial;
        [Tooltip("Shader KryticalNova/Nova: the glow and rays round the star, and the dark patch behind it.")]
        public Material coronaMaterial;
        [Tooltip("Shader KryticalNova/NovaMotes: the sparks in orbit.")]
        public Material motesMaterial;
        [Tooltip("Shader KryticalNova/NovaMask: the disc behind a Nova that hides the characters, pets and effects behind it (and leaves the world behind them). None = everything shows through.")]
        public Shader maskShader;
        [Tooltip("How much of the Nova's reach the hiding disc covers. 1 = out to the furthest ring.")]
        [Range(0.3f, 1.2f)] public float maskSize = 1f;
        [Tooltip("The share of the disc's radius, at its rim, over which what it hides comes back into view in dots. " +
                 "0 = the disc ends on a line, which shows as a circle cut out of whatever stands behind the Nova.")]
        [Range(0f, 0.9f)] public float maskFeather = 0.45f;
        [Tooltip("Gap between the top of the head and the lowest the Nova reaches (metres).")]
        public float gapAboveHead = 0.08f;
        [Tooltip("How dark the patch of night behind the Nova is. It is what lets the rings read against a bright sky.")]
        [Range(0f, 1f)] public float backdrop = 0.4f;
        [Tooltip("Every ring turns this much faster or slower.")]
        [Min(0f)] public float spinScale = 1f;
        [Tooltip("The colour of the burst and the banner when a Nova Standing rank is bought.")]
        [ColorUsage(false, true)] public Color standingColour = new Color(1f, 0.8f, 0.34f, 1f);
        public NovaRing[] rings = DefaultRings();
        public NovaTierLook[] tiers = DefaultTiers();

        /// <summary>The largest share of the Nova's radius any ring shown at a tier reaches.</summary>
        public float Reach(int tier)
        {
            float reach = 1f;
            if (rings == null) return reach;
            foreach (NovaRing ring in rings)
                if (ring.fromTier <= tier) reach = Mathf.Max(reach, ring.radius + ring.width * 0.5f);
            return reach;
        }

        /// <summary>The colour a tier is known by: its rings'. White with no Nova.</summary>
        public Color ColourOf(int tier) =>
            tiers == null || tiers.Length == 0 || tier <= 0 ? Color.white : tiers[Mathf.Min(tier, tiers.Length) - 1].outer;

        /// <summary>How far above the crown of the head the middle of a tier's Nova floats (metres).</summary>
        public float CentreAboveHead(int tier)
        {
            float gap = Mathf.Max(0f, gapAboveHead);
            if (tiers == null || tiers.Length == 0 || tier <= 0) return gap + 0.15f; // where the first one will appear
            return gap + tiers[Mathf.Min(tier, tiers.Length) - 1].radius * Reach(tier);
        }

        /// <summary>
        /// A halo first, then a gyroscope: one flat ring at tier I, two leaning rings by III, a dashed orbit at V
        /// and VI, the inscribed hoop at VII, an inner fast ring at VIII, running lights from IX, and a last wide
        /// orbit at XII.
        /// </summary>
        public static NovaRing[] DefaultRings() => new[]
        {
            new NovaRing(1, 1.00f, 0.060f, 12f, 0f, 40f, 14f),
            new NovaRing(2, 0.84f, 0.050f, 62f, 20f, -55f, 22f),
            new NovaRing(3, 0.68f, 0.045f, -58f, 64f, 70f, -18f),
            new NovaRing(5, 1.14f, 0.040f, 8f, -6f, -24f, 9f, NovaRingPattern.Dashed, 24),
            new NovaRing(6, 0.76f, 0.035f, 34f, -48f, 90f, 26f, NovaRingPattern.Dashed, 16),
            new NovaRing(7, 0.93f, 0.150f, 0f, 0f, 20f, 0f, NovaRingPattern.Glyphs, 22, 0f, true, 0.9f),
            new NovaRing(8, 0.50f, 0.040f, 84f, 30f, 150f, -40f),
            new NovaRing(9, 1.00f, 0.020f, 12f, 0f, 40f, 14f, NovaRingPattern.Plain, 0, 0.35f, false, 1.4f),
            new NovaRing(10, 0.84f, 0.018f, 62f, 20f, -55f, 22f, NovaRingPattern.Plain, 0, -0.5f, false, 1.4f),
            new NovaRing(12, 1.28f, 0.050f, -10f, 14f, 30f, -12f, NovaRingPattern.Dashed, 36, 0.25f),
        };

        private static NovaTierLook Tier(Color inner, Color outer, float radius, float core, float corona, int rays, float rayLength,
            int motes, float prism, float brightness) => new NovaTierLook
        {
            inner = inner, outer = outer, radius = radius, core = core, corona = corona, rays = rays, rayLength = rayLength,
            motes = motes, prism = prism, brightness = brightness,
        };

        private static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);

        /// <summary>Silver, cyan, green, gold, ember, crimson, violet, then every colour at once.</summary>
        public static NovaTierLook[] DefaultTiers() => new[]
        {
            Tier(Rgb(0.90f, 0.94f, 1.00f), Rgb(0.62f, 0.70f, 0.85f), 0.125f, 0.20f, 0.30f, 0, 0.0f, 0, 0f, 1.5f),
            Tier(Rgb(0.85f, 0.95f, 1.00f), Rgb(0.45f, 0.75f, 0.95f), 0.132f, 0.21f, 0.35f, 0, 0.0f, 0, 0f, 1.5f),
            Tier(Rgb(0.70f, 0.97f, 1.00f), Rgb(0.15f, 0.70f, 1.00f), 0.139f, 0.22f, 0.40f, 0, 0.0f, 0, 0f, 1.6f),
            Tier(Rgb(0.65f, 1.00f, 0.90f), Rgb(0.10f, 0.85f, 0.70f), 0.146f, 0.23f, 0.60f, 0, 0.0f, 6, 0f, 1.6f),
            Tier(Rgb(0.75f, 1.00f, 0.60f), Rgb(0.20f, 0.85f, 0.25f), 0.152f, 0.24f, 0.65f, 0, 0.0f, 8, 0f, 1.7f),
            Tier(Rgb(0.95f, 1.00f, 0.55f), Rgb(0.55f, 0.85f, 0.15f), 0.158f, 0.25f, 0.70f, 0, 0.0f, 10, 0f, 1.7f),
            Tier(Rgb(1.00f, 0.92f, 0.55f), Rgb(1.00f, 0.66f, 0.10f), 0.164f, 0.26f, 0.75f, 0, 0.0f, 12, 0f, 1.8f),
            Tier(Rgb(1.00f, 0.80f, 0.45f), Rgb(1.00f, 0.42f, 0.08f), 0.170f, 0.27f, 0.80f, 0, 0.0f, 14, 0f, 1.8f),
            Tier(Rgb(1.00f, 0.68f, 0.45f), Rgb(1.00f, 0.22f, 0.10f), 0.176f, 0.28f, 0.65f, 4, 1.0f, 18, 0f, 1.9f),
            Tier(Rgb(1.00f, 0.60f, 0.55f), Rgb(0.95f, 0.08f, 0.20f), 0.182f, 0.29f, 0.70f, 6, 1.2f, 22, 0f, 1.9f),
            Tier(Rgb(0.92f, 0.70f, 1.00f), Rgb(0.55f, 0.20f, 1.00f), 0.188f, 0.30f, 0.75f, 8, 1.4f, 28, 0f, 1.9f),
            Tier(Rgb(1.00f, 0.93f, 0.80f), Rgb(1.00f, 0.80f, 0.45f), 0.195f, 0.28f, 0.55f, 10, 1.5f, 36, 1f, 1.45f),
        };
    }

    /// <summary>
    /// Every number of the two Renown ladders at the Herald: Ascension (the Nova) and Nova Standings (the titles),
    /// and how the Nova looks. The rules that read it are <see cref="NovaRules"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Progression/Nova Settings", fileName = "NovaSettings")]
    public sealed class NovaSettings : ScriptableObject
    {
        public AscensionSettings ascension = new AscensionSettings();
        public StandingSettings standings = new StandingSettings();
        public NovaLookSettings look = new NovaLookSettings();
    }
}
