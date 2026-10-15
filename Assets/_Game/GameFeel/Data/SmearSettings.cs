using System;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>One kind of smear: how long it lasts, how far it reaches and how it dies away.</summary>
    [Serializable]
    public struct SmearProfile
    {
        [Tooltip("How long the smear lasts, in seconds, from the moment the motion starts.")]
        [Min(0.02f)] public float seconds;
        [Tooltip("The body is stretched back to where it was this long ago, in seconds: longer = a longer smear at the same speed.")]
        [Min(0.01f)] public float trailSeconds;
        [Tooltip("How much of the stretch shows over the smear's life (across: 0 = its start, 1 = its end). Strong at the start, gone by the end.")]
        public AnimationCurve shape;
        [Tooltip("How far the trailing side is pulled back, as a share of the way to where the body was (the smear package's strength).")]
        [Range(0f, 2f)] public float strength;
        [Tooltip("The smear is never longer than this, in metres, however fast the body goes.")]
        [Min(0f)] public float longest;
        [Tooltip("Below this speed (metres a second) nothing is smeared: walking (5), a shuffle, a small correction of position. The smear is whole from a third above it.")]
        [Min(0f)] public float slowest;
        [Tooltip("How much of the stretch stays once the smear's life is over, for as long as the motion goes on (a run until it stops, a jump until it lands): 0 = none, the smear is the take-off alone; 0.35 = a lighter smear the whole way (owner, 15 Oct 2026).")]
        [Range(0f, 1f)] public float sustain;

        // The smear is whole from this many times the slowest speed it shows at.
        private const float WholeFrom = 1.33f;

        /// <summary>
        /// How far back the body is stretched: toward where it was (<paramref name="back"/>, a place it left
        /// <paramref name="span"/> seconds ago), never further than the longest smear, as strong as the smear's shape
        /// says at this point of its <paramref name="life"/> (0 = its start, 1 = its end), and not at all below the
        /// slowest speed.
        /// </summary>
        /// <param name="floor">The least that shows while the motion goes on (<see cref="sustain"/>, or less as it is let go).</param>
        public readonly Vector3 Stretch(Vector3 back, float span, float life, float floor = 0f)
        {
            float speed = span > 0.0001f ? back.magnitude / span : 0f;
            float show = shape != null && shape.length > 0 ? Mathf.Clamp01(shape.Evaluate(Mathf.Clamp01(life))) : Mathf.Clamp01(1f - life);
            show = Mathf.Max(show, Mathf.Clamp01(floor));
            // Slow motion is not smeared at all, and the smear comes in over the next third of that speed.
            show *= Mathf.InverseLerp(slowest, slowest * WholeFrom + 0.01f, speed);
            return Vector3.ClampMagnitude(back, longest) * show;
        }
    }

    /// <summary>
    /// The comic motion smear: for a moment a character's trailing side is pulled back to where it was, in streaks.
    /// Drawn as a second skin over the character with the smear package's own shader (Assets/_ThirdParty/smear
    /// effect, <c>WuHaipeng/smear_urp</c>), so the character's materials are never touched. Played by the game feel
    /// prefab's Run Start and Death feedbacks (<see cref="MMF_Smear"/>) on every client, for every character.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Game Feel/Smear Settings", fileName = "SmearSettings")]
    public sealed class SmearSettings : ScriptableObject
    {
        [Tooltip("The smear package's shader: WuHaipeng/smear_urp (Assets/_ThirdParty/smear effect/Demo_urp/shader). Linked here so it ships in a build.")]
        public Shader shader;
        [Tooltip("How finely the smear breaks into streaks (the package's noise scale).")]
        [Min(0f)] public float noiseScale = 100f;
        [Tooltip("A body that covers ground faster than this from one frame to the next (metres a second) was put somewhere, not moved: a spawn, a teleport, a network correction. Its smear is dropped at once. Keep it well above the fastest real motion: the top run is 29, the first frame of a critical death's throw about 80.")]
        [Min(1f)] public float placedSpeed = 200f;

        [Header("Taking off at a run")]
        [Tooltip("Whole for the take-off, then (sustain) lighter for as long as the run goes on; it is let go when the run stops.")]
        public SmearProfile takeOff = new SmearProfile
        {
            seconds = 0.45f, trailSeconds = 0.1f, strength = 1.25f, longest = 1.8f, slowest = 5.5f, sustain = 0.35f,
            shape = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0f)),
        };

        [Header("A jump")]
        [Tooltip("Whole as the feet leave the ground, then (sustain) lighter through the air; it is let go on landing.")]
        public SmearProfile jump = new SmearProfile
        {
            seconds = 0.35f, trailSeconds = 0.08f, strength = 1.1f, longest = 1.4f, slowest = 4f, sustain = 0.3f,
            shape = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)),
        };
        [Tooltip("Seconds a sustained smear takes to fade once the motion ends (the run stops, the feet land).")]
        [Range(0f, 1f)] public float releaseSeconds = 0.15f;

        [Header("Thrown by the blow that kills")]
        [Tooltip("The throw of a critical death, backwards or forwards. A normal death only knocks the body over (at up to about 2.5 metres a second): that is below this smear's slowest speed, so it is not smeared. Lower 'slowest' to smear it too.")]
        public SmearProfile deathThrow = new SmearProfile
        {
            seconds = 0.5f, trailSeconds = 0.1f, strength = 1.25f, longest = 1.6f, slowest = 4f,
            shape = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)),
        };

        // A frame shorter than this is judged as this long when telling a move from a placement.
        private const float ShortestFrame = 1f / 60f;

        /// <summary>
        /// True when a body got from one place to the other within a single frame faster than anything really moves:
        /// it was put there, and a smear must not be drawn across the gap.
        /// </summary>
        public bool WasPlaced(Vector3 from, Vector3 to, float deltaTime)
        {
            float furthest = placedSpeed * Mathf.Max(deltaTime, ShortestFrame);
            return (to - from).sqrMagnitude > furthest * furthest;
        }
    }
}
