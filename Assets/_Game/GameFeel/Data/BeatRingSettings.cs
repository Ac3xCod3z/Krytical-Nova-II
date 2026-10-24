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
        [Tooltip("The bottom segments. The ring keeps its own colours whatever the pet (owner, 23 Oct 2026).")]
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
        [Tooltip("Seconds a bar takes to rise most of the way to the music (an eased step, not a straight line).")]
        [Range(0.005f, 0.3f)] public float attackSeconds = 0.035f;
        [Tooltip("Seconds a bar takes to settle most of the way back down.")]
        [Range(0.02f, 1f)] public float releaseSeconds = 0.16f;
        [Tooltip("Each band is measured against its own loudest of late, so every bar moves whatever the mix: seconds that loudest takes to halve.")]
        [Range(0.5f, 10f)] public float adaptHalfLife = 2.5f;
        [Tooltip("A band quieter than this share of the music's loudest band stays down (the floor of the adaptive scale).")]
        [Range(0f, 0.5f)] public float adaptFloor = 0.12f;
        [Tooltip("How long a peak cap waits before it falls, in seconds.")]
        [Min(0f)] public float peakHold = 0.25f;
        [Tooltip("How fast a peak cap falls (share of a full bar per second).")]
        [Min(0.1f)] public float peakFall = 1.2f;
        [Tooltip("How loud the music must be for a full bar: the spectrum's power is multiplied by this.")]
        [Min(0.1f)] public float gain = 9f;
        [Tooltip("How much every bar jumps on each beat, and on the first beat of a bar.")]
        [Range(0f, 1f)] public float beatKick = 0.12f;
        [Range(0f, 1f)] public float barKick = 0.25f;
        [Tooltip("How far the whole ring pulses out on each beat, and on the first beat of a bar, as a share of its radius.")]
        [Range(0f, 0.5f)] public float beatPulse = 0.07f;
        [Range(0f, 0.5f)] public float barPulse = 0.13f;
        [Tooltip("Width of the comet that runs round the ring once a beat (radians).")]
        [Range(0.1f, 2f)] public float cometWidth = 0.45f;
        [Tooltip("How much brighter the comet makes the bars it passes.")]
        [Range(0f, 3f)] public float cometBoost = 1.1f;
        [Tooltip("Seconds of the burst after a press on the beat. A press off it shows nothing (owner, 23 Oct 2026: no need to make a miss feel bad).")]
        [Min(0.05f)] public float burstSeconds = 0.4f;
        [Tooltip("How far the ring swells in a burst, as a share of its radius.")]
        [Range(0f, 1f)] public float burstSwell = 0.35f;

        [Header("A combo step on the beat")]
        [Tooltip("Each step of the Cadence combo swells the ring this much more than the burst does (step 1, 2, 3).")]
        public Vector3 stepSwell = new Vector3(0.35f, 0.55f, 0.85f);
        [Tooltip("Seconds of the shock ring that runs out over the ground from the ring at a combo step.")]
        [Min(0.05f)] public float shockSeconds = 0.45f;
        [Tooltip("How far the shock ring runs, as a multiple of the ring's radius, at step 1 (more at steps 2 and 3).")]
        [Min(1f)] public float shockReach = 2.4f;
        [Tooltip("Width of the shock ring (metres).")]
        [Min(0.01f)] public float shockWidth = 0.09f;
        [Tooltip("Segments of the shock ring round its circle.")]
        [Range(16, 96)] public int shockSegments = 48;
        [Tooltip("A small camera kick at a combo step, larger with each step (the player's Screen Shake setting applies).")]
        [Range(0f, 1f)] public float stepKick = 0.18f;
        [Tooltip("The sound of a combo step; it rises in pitch with each step.")]
        public AudioClip stepSound;
        public UnityEngine.Audio.AudioMixerGroup stepSoundGroup;
        [Range(0f, 1f)] public float stepSoundVolume = 0.7f;
        [Range(0f, 0.5f)] public float stepPitchRise = 0.12f;

        [Header("When to press (owner, 24 Oct 2026)")]
        [Tooltip("The closing ring: shrinks onto the Beat Ring once a beat and meets it on the beat.")]
        public bool approach = true;
        [Tooltip("How far out the closing ring starts, as a multiple of the ring's radius beyond it.")]
        [Range(0.2f, 3f)] public float approachReach = 1.1f;
        [Tooltip("Width of the closing ring (metres).")]
        [Range(0.01f, 0.3f)] public float approachWidth = 0.06f;
        [Range(0f, 1f)] public float approachAlpha = 0.8f;
        public Color approachColour = new Color(0.85f, 0.95f, 1f);
        [Tooltip("The closing ring before the first beat of a bar (the count-in): wider and cyan. Never gold: gold is the window, press now.")]
        public bool countIn = true;
        public Color approachBar = new Color(0.45f, 0.9f, 1f);
        [Tooltip("The ring in the Cadence window: press now.")]
        public Color approachWindow = new Color(1f, 0.82f, 0.2f);
        [Tooltip("Seconds the closing ring stays locked on the Beat Ring in gold after a press on the beat (the catch).")]
        [Min(0.05f)] public float caughtSeconds = 0.3f;
    }
}
