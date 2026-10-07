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

    /// <summary>How the Nova of one tier is drawn (the Nova shader's inputs). Tier 1 is a thin ring; the last is a full rayed Nova.</summary>
    [Serializable]
    public struct NovaTierLook
    {
        [ColorUsage(false, true)] public Color inner;
        [ColorUsage(false, true)] public Color outer;
        [Tooltip("Width of the outer ring across (metres).")]
        [Min(0.1f)] public float ringDiameter;
        [Tooltip("Thickness of the rings, as a part of the ring's radius.")]
        [Range(0.005f, 0.2f)] public float ringWidth;
        [Tooltip("How much light fills the disc inside the ring.")]
        [Range(0f, 1f)] public float fill;
        [Range(0, 3)] public int innerRings;
        [Range(0, 24)] public int spokes;
        [Tooltip("Marks in the band of glyphs that turns inside the ring. 0 = no band.")]
        [Range(0, 48)] public int glyphs;
        [Range(0, 32)] public int rays;
        [Tooltip("How far the rays reach past the ring, as a part of the ring's radius.")]
        [Range(0f, 1f)] public float rayLength;
        [Tooltip("Colour runs round the Nova through the whole spectrum.")]
        [Range(0f, 1f)] public float prism;
        [Min(0f)] public float brightness;
    }

    /// <summary>Where the Nova sits and how each tier looks.</summary>
    [Serializable]
    public sealed class NovaLookSettings
    {
        [Tooltip("The Nova material (shader KryticalNova/Nova). Each character gets its own copy.")]
        public Material material;
        [Tooltip("Gap between the top of the head and the bottom of the Nova's ring (metres).")]
        public float gapAboveHead = 0.06f;
        [Tooltip("How far behind the head the Nova hangs (metres).")]
        public float behindHead = 0.05f;
        [Tooltip("0 = upright like a halo behind the head; 90 = lying flat above it.")]
        [Range(0f, 90f)] public float tilt;
        [Tooltip("On: an upright Nova turns to face the camera, so it always reads as a disc.")]
        public bool faceCamera = true;
        [Tooltip("Turns of the glyph band and spokes per minute.")]
        public float spinPerMinute = 4f;
        public NovaTierLook[] tiers = DefaultTiers();

        private static NovaTierLook Tier(Color inner, Color outer, float diameter, float width, float fill, int rings, int spokes,
            int glyphs, int rays, float rayLength, float prism, float brightness) => new NovaTierLook
        {
            inner = inner, outer = outer, ringDiameter = diameter, ringWidth = width, fill = fill, innerRings = rings,
            spokes = spokes, glyphs = glyphs, rays = rays, rayLength = rayLength, prism = prism, brightness = brightness,
        };

        private static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);

        /// <summary>Silver, cyan, green, gold, ember, crimson, violet, then every colour at once.</summary>
        public static NovaTierLook[] DefaultTiers() => new[]
        {
            // Each tier adds one thing to the one before. The light inside stays low so the later tiers keep their detail.
            Tier(Rgb(0.80f, 0.86f, 0.95f), Rgb(0.62f, 0.70f, 0.85f), 0.40f, 0.035f, 0.00f, 0, 0, 0, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(0.78f, 0.92f, 1.00f), Rgb(0.45f, 0.75f, 0.95f), 0.42f, 0.045f, 0.04f, 1, 0, 0, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(0.55f, 0.95f, 1.00f), Rgb(0.15f, 0.70f, 1.00f), 0.44f, 0.045f, 0.06f, 2, 0, 0, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(0.45f, 1.00f, 0.85f), Rgb(0.10f, 0.85f, 0.70f), 0.46f, 0.045f, 0.08f, 3, 0, 0, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(0.55f, 1.00f, 0.45f), Rgb(0.20f, 0.85f, 0.25f), 0.48f, 0.050f, 0.08f, 3, 4, 0, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(0.90f, 1.00f, 0.40f), Rgb(0.55f, 0.85f, 0.15f), 0.50f, 0.050f, 0.08f, 3, 8, 0, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(1.00f, 0.88f, 0.35f), Rgb(1.00f, 0.66f, 0.10f), 0.52f, 0.050f, 0.08f, 3, 8, 16, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(1.00f, 0.70f, 0.28f), Rgb(1.00f, 0.42f, 0.08f), 0.54f, 0.055f, 0.08f, 3, 12, 20, 0, 0.00f, 0f, 1.5f),
            Tier(Rgb(1.00f, 0.50f, 0.25f), Rgb(1.00f, 0.22f, 0.10f), 0.56f, 0.055f, 0.08f, 3, 12, 20, 8, 0.35f, 0f, 1.6f),
            Tier(Rgb(1.00f, 0.35f, 0.35f), Rgb(0.95f, 0.08f, 0.20f), 0.58f, 0.060f, 0.08f, 3, 12, 24, 12, 0.45f, 0f, 1.6f),
            Tier(Rgb(0.85f, 0.50f, 1.00f), Rgb(0.55f, 0.20f, 1.00f), 0.60f, 0.060f, 0.08f, 3, 12, 24, 16, 0.55f, 0f, 1.7f),
            Tier(Rgb(1.00f, 0.95f, 0.85f), Rgb(1.00f, 0.80f, 0.45f), 0.62f, 0.065f, 0.10f, 3, 12, 24, 24, 0.70f, 1f, 1.8f),
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
