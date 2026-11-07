using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// Every number of the mount system in one place (owner, 25 Oct 2026: feed, EXP and evolution like the pets, as a
    /// system of its own). Growth and hunger work as the pets' do, with three steps instead of five; change the
    /// numbers here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Mounts/Mount Settings")]
    public sealed class MountSettings : ScriptableObject
    {
        [Header("Growth (like a pet's)")]
        [Tooltip("Mount EXP that takes the growth meter from 0 % to 100 %.")]
        [Min(1f)] public float xpToFullGrowth = 100_000f;
        [Tooltip("Mount EXP for each Hostile its owner kills while it is the selected mount, ridden or not.")]
        [Min(0f)] public float xpPerHostileKill = 0.04f;

        [Header("Hunger (only while ridden)")]
        [Tooltip("Percent of the hunger meter lost per minute while the mount is ridden.")]
        [Min(0f)] public float hungerDrainPercentPerMinute = 0.05f;
        [Tooltip("The owner is warned once when hunger falls to this percent.")]
        [Range(0f, 100f)] public float hungerWarningPercent = 20f;
        [Tooltip("Seconds between the server writing the ridden mount's hunger into the character's mounts.")]
        [Min(1f)] public float hungerWriteSeconds = 10f;

        [Header("Riding speed (grows with growth; owner, 25 Oct 2026: top speed 34 m/s)")]
        [Tooltip("Walking on the mount, in metres a second: at 0 % growth (x) and at 100 % (y). A starving mount goes at x.")]
        public Vector2 walkSpeed = new Vector2(6f, 9f);
        [Tooltip("Running on the mount (the Run selection, as on foot), in metres a second: at 0 % growth (x) and at 100 % (y). " +
                 "The Run skill on foot tops out at 28. The Move Speed stat does not add to it.")]
        public Vector2 runSpeed = new Vector2(20f, 34f);

        [Header("The mount's own clips")]
        [Tooltip("The ground speed, in metres a second, at which the walk clip (x) and the run clip (y) look right played at 1x. Faster or slower travel plays them faster or slower.")]
        public Vector2 clipMetresPerSecond = new Vector2(4f, 14f);
        [Tooltip("The slowest (x) and fastest (y) the walk and run clips are ever played.")]
        public Vector2 clipPlaybackRange = new Vector2(0.7f, 2.4f);

        [Header("The rider at a gallop (owner, 25 Oct 2026: lean forward and bob a little)")]
        [Tooltip("Degrees the rider leans forward from the hips at a gallop, on top of the riding clip.")]
        [Range(0f, 40f)] public float gallopLean = 16f;
        [Tooltip("How far the rider rises and drops with each of the mount's strides at a gallop, in metres. Keep it very subtle (owner).")]
        [Range(0f, 0.2f)] public float gallopBob = 0.02f;
        [Tooltip("The same at a walk: a slight lean and bob.")]
        [Range(0f, 20f)] public float walkLean = 3f;
        [Range(0f, 0.1f)] public float walkBob = 0.006f;
        [Tooltip("Strides the mount's walk and run clips each hold (the Brambleback's hold three).")]
        [Min(1)] public int stridesPerClip = 3;
        [Tooltip("How quickly the lean comes and goes, in seconds.")]
        [Min(0.01f)] public float leanEase = 0.25f;

        [Header("Getting on and off (owner picks, 6 Nov 2026: Torrent's timing; a running version of each)")]
        [Tooltip("Standing still (or walking: the rider stops first): the summon hop. Dip, hop with the leg over, drop into the seat, settle.")]
        public MountTransition getOn = MountTransition.StandingOn();
        [Tooltip("Running (the Run selection and moving): a forward hop off the stride, landing already riding. No stop.")]
        public MountTransition getOnRunning = MountTransition.RunningOn();
        [Tooltip("Standing still (or walking: the rider stops first): a front flip off the mount, a three-point landing, a rise to idle.")]
        public MountTransition getOff = MountTransition.StandingOff();
        [Tooltip("Running: a launch forward out of the saddle into a front flip, landing in a forward roll that comes up running.")]
        public MountTransition getOffRunning = MountTransition.RunningOff();

        /// <summary>The getting-on or getting-off in play: on or off, standing or running.</summary>
        public MountTransition Transition(bool gettingOn, bool running) =>
            gettingOn ? (running ? getOnRunning : getOn) : (running ? getOffRunning : getOff);

        [Header("Idling")]
        [Tooltip("At rest the mount and its rider play one of their other idles (a sniff, a look round, a pat on the neck) every this many seconds. 0 = never.")]
        [Min(0f)] public float idleVarietySeconds = 9f;

        public MountTuning Tuning => new MountTuning(xpToFullGrowth, xpPerHostileKill, hungerDrainPercentPerMinute);
    }
    /// <summary>
    /// One way of getting on or off a mount (owner, 6 Nov 2026): how long it takes, when the rider is free, how the
    /// rider rises to (or drops from) the seat and arcs forward, and when the mount forms or vanishes. The clips are
    /// keyed in place; the height and the forward arc come from these curves and are only drawn, on each machine:
    /// the player's real position never changes. Curve time runs 0 to 1 over <see cref="seconds"/>.
    /// </summary>
    [System.Serializable]
    public sealed class MountTransition
    {
        [Tooltip("Seconds the whole clip takes.")]
        [Min(0.1f)] public float seconds = 1f;
        [Tooltip("Seconds after the press when the rider is free: on the mount and able to ride, or on foot. The rest of the clip plays on unless the rider moves.")]
        [Min(0.1f)] public float freeSeconds = 0.8f;
        [Tooltip("Height of the rider over the clip: 0 = on the ground, 1 = on the stage's seat, above 1 = higher than the seat.")]
        public AnimationCurve lift = AnimationCurve.Constant(0f, 1f, 1f);
        [Tooltip("How far ahead of the player the rider is drawn over the clip, in metres (a visual arc only). 0 = on the player.")]
        public AnimationCurve forward = AnimationCurve.Constant(0f, 1f, 0f);
        [Tooltip("Getting on: when in the clip (0 to 1) the feet start going into the stirrups. Getting off: they come out at once.")]
        [Range(0f, 1f)] public float stirrupsAt = 0.77f;
        [Tooltip("Seconds into the clip before the mount starts forming (getting on) or vanishing (getting off).")]
        [Min(0f)] public float effectDelay = 0.1f;
        [Tooltip("Seconds the mount takes to form or to vanish. A mount's own materialise asset may override it.")]
        [Min(0.05f)] public float effectSeconds = 0.5f;

        private static Keyframe K(float t, float v) => new Keyframe(t, v);

        /// <summary>The summon hop: 1.0 s, free at 0.8 s; the mount forms 0.12 to 0.77 s, solid as the rider lands.</summary>
        public static MountTransition StandingOn() => new MountTransition
        {
            seconds = 1f, freeSeconds = 0.8f, stirrupsAt = 0.77f, effectDelay = 0.12f, effectSeconds = 0.65f,
            lift = new AnimationCurve(K(0f, 0f), K(0.13f, -0.05f), K(0.47f, 1.12f), K(0.77f, 1f), K(0.86f, 0.97f), K(1f, 1f)),
        };

        /// <summary>The running hop: 0.8 s, on the seat and free at 0.55 s; the mount forms 0.05 to 0.55 s.</summary>
        public static MountTransition RunningOn() => new MountTransition
        {
            seconds = 0.8f, freeSeconds = 0.55f, stirrupsAt = 0.6875f, effectDelay = 0.05f, effectSeconds = 0.5f,
            lift = new AnimationCurve(K(0f, 0f), K(0.1f, 0.05f), K(0.44f, 1.08f), K(0.69f, 1f), K(0.8f, 0.97f), K(1f, 1f)),
        };

        /// <summary>The front flip: 1.0 s, free at 0.85 s; crouch, push, flip down through where the mount was, land at
        /// 0.6 s; the mount vanishes top-down 0.1 to 0.6 s.</summary>
        public static MountTransition StandingOff() => new MountTransition
        {
            seconds = 1f, freeSeconds = 0.85f, stirrupsAt = 0f, effectDelay = 0.1f, effectSeconds = 0.5f,
            lift = new AnimationCurve(K(0f, 1f), K(0.15f, 0.95f), K(0.25f, 1.15f), K(0.42f, 1.35f), K(0.6f, 0f), K(1f, 0f)),
        };

        /// <summary>The launch flip: 1.0 s, free at 0.85 s; launch, flip on a forward arc, land about 1 m ahead at
        /// 0.55 s and roll back onto the player by 0.85 s, coming up running; the mount vanishes 0.1 to 0.6 s.</summary>
        public static MountTransition RunningOff() => new MountTransition
        {
            seconds = 1f, freeSeconds = 0.85f, stirrupsAt = 0f, effectDelay = 0.1f, effectSeconds = 0.5f,
            lift = new AnimationCurve(K(0f, 1f), K(0.1f, 1.1f), K(0.3f, 1.4f), K(0.55f, 0f), K(1f, 0f)),
            forward = new AnimationCurve(K(0f, 0f), K(0.55f, 1f), K(0.85f, 0f), K(1f, 0f)),
        };
    }
}
