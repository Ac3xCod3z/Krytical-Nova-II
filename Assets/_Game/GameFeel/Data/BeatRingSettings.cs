using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The Beat Ring (owner, 22 Oct 2026: "we have no way to communicate the beat to the player"): a small equaliser
    /// of segmented bars round the player's own pet, shown only in a fight. The bars dance to the fight music, a bright
    /// comet runs once round the ring a beat and reaches the front on the beat, the whole ring flares in the Cadence
    /// window, and a skill pressed on the beat sends a burst out of it. Every number of it is here.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Game Feel/Beat Ring Settings", fileName = "BeatRingSettings")]
    public sealed class BeatRingSettings : ScriptableObject
    {
        [Header("Shape")]
        [Tooltip("Bars round the ring.")]
        [Range(8, 48)] public int columns = 24;
        [Tooltip("Segments stacked in a bar (the equaliser's blocks).")]
        [Range(3, 16)] public int segments = 8;
        [Tooltip("Ring radius as a share of the pet's width.")]
        [Min(0.1f)] public float radiusScale = 0.6f;
        [Tooltip("The ring is never smaller or larger than this (metres).")]
        public Vector2 radiusLimits = new Vector2(0.35f, 1.0f);
        [Tooltip("Height of a full bar (metres), as a share of the ring's radius.")]
        [Min(0.05f)] public float barHeight = 0.42f;
        [Tooltip("Width of a bar as a share of the gap between two bars.")]
        [Range(0.1f, 1f)] public float barWidth = 0.42f;
        [Tooltip("Gap between two segments as a share of a segment.")]
        [Range(0f, 0.8f)] public float segmentGap = 0.3f;
        [Tooltip("How high the ring stands, as a share of the pet's height (0 = its feet, 0.5 = its middle).")]
        [Range(0f, 1f)] public float standAt = 0.15f;

        [Header("Colour")]
        [Tooltip("The bottom segments. The pet's own glow colour is used when it has one.")]
        public Color low = new Color(0.25f, 0.85f, 1f);
        [Tooltip("The middle segments.")]
        public Color mid = new Color(1f, 0.72f, 0.18f);
        [Tooltip("The top segments and the falling peak caps.")]
        public Color high = new Color(1f, 0.22f, 0.16f);
        [Tooltip("How see-through the lit segments are (so the ring never hides the pet or the fight).")]
        [Range(0f, 1f)] public float alpha = 0.5f;
        [Tooltip("How much of a segment that is not lit still shows, as a share of a lit one.")]
        [Range(0f, 0.5f)] public float ghost = 0.05f;
        [Tooltip("What the ring flares to in the Cadence window: press now.")]
        public Color windowFlare = new Color(1f, 0.95f, 0.7f);
        [Range(0f, 1f)] public float windowFlareStrength = 0.35f;
        [Tooltip("The burst of a skill pressed on the beat, per faction.")]
        public Color burstHegemony = new Color(0.35f, 0.9f, 1f);
        public Color burstNull = new Color(0.72f, 0.45f, 1f);
        public Color burstCovenant = new Color(0.45f, 1f, 0.6f);

        [Header("Motion")]
        [Tooltip("How fast a bar rises to the music (per second, as a share of a full bar).")]
        [Min(0.1f)] public float rise = 14f;
        [Tooltip("How fast a bar falls back.")]
        [Min(0.1f)] public float fall = 3.5f;
        [Tooltip("How long a peak cap waits before it falls, in seconds.")]
        [Min(0f)] public float peakHold = 0.25f;
        [Tooltip("How fast a peak cap falls (share of a full bar per second).")]
        [Min(0.1f)] public float peakFall = 1.2f;
        [Tooltip("How loud the music must be for a full bar: the spectrum's power is multiplied by this.")]
        [Min(0.1f)] public float gain = 9f;
        [Tooltip("How much every bar jumps on each beat, and on the first beat of a bar.")]
        [Range(0f, 1f)] public float beatKick = 0.18f;
        [Range(0f, 1f)] public float barKick = 0.35f;
        [Tooltip("Width of the comet that runs round the ring once a beat (radians).")]
        [Range(0.1f, 2f)] public float cometWidth = 0.45f;
        [Tooltip("How much brighter the comet makes the bars it passes.")]
        [Range(0f, 3f)] public float cometBoost = 1.1f;
        [Tooltip("Seconds of the burst after a press on the beat, and of the grey flicker after one off it.")]
        [Min(0.05f)] public float burstSeconds = 0.4f;
        [Min(0.05f)] public float offBeatSeconds = 0.3f;
        [Tooltip("How far the ring swells in a burst, as a share of its radius.")]
        [Range(0f, 1f)] public float burstSwell = 0.35f;
    }
}
