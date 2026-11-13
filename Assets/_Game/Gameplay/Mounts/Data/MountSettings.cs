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

        [Header("Getting on and off: weight (owner picks, 7 Nov 2026 retest 2: the mount reacts, a crouch before the leap)")]
        [Tooltip("Getting off: metres the mount's body is shoved down by the rider's push-off, and back (away from the leap).")]
        [Range(0f, 0.3f)] public float pushOffDip = 0.07f;
        [Range(0f, 0.3f)] public float pushOffBack = 0.05f;
        [Tooltip("Getting on: metres the mount's body gives under the rider landing in the saddle, before it settles.")]
        [Range(0f, 0.3f)] public float saddleDip = 0.06f;
        [Tooltip("How fast the mount's body springs back (cycles a second) and how quickly the spring dies down (0 = rings, 1 = no bounce).")]
        [Range(1f, 15f)] public float kickHz = 6f;
        [Range(0.05f, 1f)] public float kickDamping = 0.35f;
        [Tooltip("Seconds before the feet leave (the saddle or the ground) that the rider crouches into the push.")]
        [Range(0f, 0.3f)] public float crouchLead = 0.1f;
        [Tooltip("Seconds a getting-off hands over into the on-foot idle: the clip fades out and the body comes down from " +
                 "the seat together (owner, 7 Nov 2026 retest 2: the hand-over must not be noticeable).")]
        [Range(0.1f, 1f)] public float standHandOverSeconds = 0.45f;

        [Header("The leap off (owner pick, 7 Nov 2026 retest 2: the game's own jump, Motion-Warped off the mount's back)")]
        [Tooltip("Getting off plays the game's jump chain (crouch on the saddle, take-off, flip, landing) and Motion-Warps the " +
                 "rider from the seat to the ground, in the player's own space (MountLeapSpace). Off = the baked dismount clips. " +
                 "Back ON 8 Nov 2026 after headless proof run 2: both leaps land on the player and end in Locomotion.")]
        public bool leapWithJump = true;
        [Tooltip("Metres the leap's apex rises above the seat.")]
        [Range(0f, 2f)] public float leapApexAboveSeat = 0.7f;
        [Tooltip("Seconds after the jump's rise begins (its first warp phase) that the feet visibly leave the saddle. The " +
                 "push-off beat, the mount's stop and recoil and its vanish all happen then (owner retest 4, 8 Nov 2026: the " +
                 "vanish started while the rider still sat on the mount).")]
        [Range(0f, 0.6f)] public float leapFeetLeaveAfter = 0.1f;
        [Tooltip("Metres the rider first rises out of the saddle before crouching and jumping off; the crouch never dips " +
                 "below the seat into the mount (owner retest 5, 8 Nov 2026: get up, crouch, jump off cleanly; final test: " +
                 "6-8 cm higher than 0.12).")]
        [Range(0f, 0.4f)] public float leapSaddleRise = 0.19f;
        [Tooltip("Standing: seconds after the leap's landing before the rider is free (the jump's own landing plays on into the idle).")]
        [Range(0.1f, 1.5f)] public float leapStandRecoverSeconds = 0.5f;

        [Header("The mounted vault (owner, 13 Nov 2026, day list 15.2: the mount dissolves, the rider flips over, it re-forms under them)")]
        [Tooltip("Riding into a vault keeps the rider mounted: the mount dissolves as the feet leave it, the rider flips over the " +
                 "obstacle and lands back in the saddle as the mount re-forms. Off = the rider vaults on the seat as before.")]
        public bool vaultMounted = true;
        [Tooltip("Share of the vault's air time over which the rider leaves the seat and the mount dissolves.")]
        [Range(0.05f, 0.45f)] public float vaultLeaveShare = 0.2f;
        [Tooltip("Share of the vault's air time, before the touchdown, over which the mount re-forms and the rider settles into the saddle.")]
        [Range(0.05f, 0.45f)] public float vaultLandShare = 0.3f;
        [Tooltip("The rider's vault clip while mounted, whatever the obstacle's height: 2 = the tall style (the front flip).")]
        [Range(0, 2)] public int vaultRiderHeightClass = 2;

        [Header("Getting on and off (owner picks, 6 Nov 2026: Torrent's timing; a running version of each)")]
        [Tooltip("Standing still (or walking: the rider stops first): the summon hop. Dip, hop with the leg over, drop into the seat, settle.")]
        public MountTransition getOn = MountTransition.StandingOn();
        [Tooltip("Running (the Run selection and moving): a forward hop off the stride, landing already riding. No stop.")]
        public MountTransition getOnRunning = MountTransition.RunningOn();
        [Tooltip("Standing still (or walking: the rider stops first): a front flip off the mount, a three-point landing, a rise to idle.")]
        public MountTransition getOff = MountTransition.StandingOff();
        [Tooltip("Metres the rider rises above the seat in the middle of a getting-on's hop, at the 0 / 50 / 100 % stage, " +
                 "before settling into the saddle (owner, 7 Nov 2026: every mount-up clears the seat). Scaled per mount by " +
                 "MountDefinition.hopClearanceScale.")]
        public Vector3 hopClearance = new Vector3(0.08f, 0.1f, 0.12f);
        [Tooltip("Running, with the path still going on: the game's take-off from the saddle, the game's front flip, a forward roll, up running.")]
        public MountTransition getOffRunning = MountTransition.RunningOff();
        [Tooltip("Running, with the clicked spot close: the same take-off, flip and roll, ending standing (idle).")]
        public MountTransition getOffRunningToIdle = MountTransition.RunningOffToIdle();
        [Tooltip("Metres of path that must still be left to the clicked spot when the roll ends for the rider to come up " +
                 "running; less, and the roll ends standing (owner, 7 Nov 2026).")]
        [Min(0f)] public float rollIntoRunMinDistance = 4f;
        [Tooltip("Running getting-off: the share of the run speed the body keeps through the roll (owner, 7 Nov 2026 retest 2: " +
                 "the roll covered too much ground). 1 = the full run.")]
        [Range(0.1f, 1f)] public float rollSpeedScale = 0.55f;
        [Tooltip("Running getting-off that ends standing: seconds of a slight slide after the roll, the speed easing to a stop.")]
        [Min(0f)] public float rollSlideSeconds = 0.3f;

        /// <summary>The getting-on or getting-off in play: on or off, standing or running, and for a running
        /// getting-off whether it ends running (path left) or standing.</summary>
        public MountTransition Transition(bool gettingOn, bool running, bool endsRunning = true) =>
            gettingOn ? (running ? getOnRunning : getOn) : (running ? (endsRunning ? getOffRunning : getOffRunningToIdle) : getOff);

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
        [Tooltip("Where the rider's origin is over the clip: 1 = on the stage's seat, 0 = on the ground. The Brambleback's clips are " +
                 "keyed per stage from the ground to the seat (the seat is only 0.2-0.5 m above the feet, too little to scale a hop), " +
                 "so this stays 1; it is here for a mount whose clips are keyed in place on the ground.")]
        public AnimationCurve lift = AnimationCurve.Constant(0f, 1f, 1f);
        [Tooltip("How far ahead of the player the rider is drawn over the clip, in metres (a visual arc only). 0 = on the player.")]
        public AnimationCurve forward = AnimationCurve.Constant(0f, 1f, 0f);
        [Tooltip("Getting on: when in the clip (0 to 1) the feet start going into the stirrups. Getting off: they come out at once.")]
        [Range(0f, 1f)] public float stirrupsAt = 0.77f;
        [Tooltip("Seconds into the clip before the mount starts forming (getting on) or vanishing (getting off).")]
        [Min(0f)] public float effectDelay = 0.1f;
        [Tooltip("Seconds the mount takes to form or to vanish. A mount's own materialise asset may override it.")]
        [Min(0.05f)] public float effectSeconds = 0.5f;
        [Tooltip("Running getting-off: seconds into the clip when the roll ends (the run-or-stand choice looks at the path left then).")]
        [Min(0f)] public float rollEndsAt;
        [Tooltip("Seconds into the clip when the rider's feet leave where they started: the ground (getting on) or the " +
                 "saddle (getting off). With Arrives At, says when a mount whose seat differs from the clip's keyed seat " +
                 "moves the rider between the two (MountStageLook.clipSeatHeight). Both zero = never.")]
        [Min(0f)] public float leavesAt;
        [Tooltip("Seconds into the clip when the rider arrives: on the seat (getting on) or with the feet on the ground (getting off).")]
        [Min(0f)] public float arrivesAt;
        [Header("Getting-off beats (owner, 7 Nov 2026: measured on the clips; the GameFeel layer shows them)")]
        [Tooltip("Seconds into the clip at the top of the flip. 0 = no beat.")]
        [Min(0f)] public float apexAt;
        [Tooltip("Seconds into the clip when the roll begins: from the touchdown, where the planted feet first slide. 0 = no roll.")]
        [Min(0f)] public float rollStartsAt;
        [Tooltip("Seconds into the clip when the rider is up on both feet, standing. 0 = it ends running.")]
        [Min(0f)] public float standsAt;
        [Tooltip("Seconds into the clip when the planted feet stop sliding after the touchdown (measured: the roll's first stretch). " +
                 "The dust under them runs from Roll Starts At to here. 0 = no slide.")]
        [Min(0f)] public float slideEndsAt;

        private static Keyframe K(float t, float v) => new Keyframe(t, v);

        /// <summary>This getting-off is the leap (the jump chain, Motion-Warped): set on the runtime copy only.</summary>
        [System.NonSerialized] public bool leap;

        /// <summary>
        /// The getting-off as the leap plays it (owner, 7 Nov 2026 retest 2): the feet leave the saddle at the jump's
        /// take-off, the apex and landing are the jump's, and the mount vanishes as the feet leave it (retest 4). A running one
        /// then plays this clip's roll from its touchdown, every later beat shifted by the same time; a standing one is
        /// free <paramref name="recover"/> seconds after the landing. A copy: the settings stay as they are.
        /// </summary>
        public MountTransition WithLeap(float takeoff, float apex, float land, float recover)
        {
            var leapt = (MountTransition)MemberwiseClone();
            leapt.leap = true;
            leapt.leavesAt = takeoff;
            leapt.apexAt = apex;
            leapt.effectDelay = takeoff; // the vanish starts as the feet leave it (owner retest 4)
            if (rollStartsAt > 0f)
            {
                float shift = land - arrivesAt;
                leapt.rollStartsAt = rollStartsAt + shift;
                leapt.slideEndsAt = slideEndsAt > 0f ? slideEndsAt + shift : 0f;
                leapt.rollEndsAt = rollEndsAt + shift;
                leapt.standsAt = standsAt > 0f ? standsAt + shift : 0f;
                leapt.seconds = seconds + shift;
                leapt.freeSeconds = freeSeconds + shift;
            }
            else
            {
                leapt.standsAt = land + recover;
                leapt.freeSeconds = land + recover;
                leapt.seconds = land + recover + 0.15f;
            }
            leapt.arrivesAt = land;
            return leapt;
        }

        /// <summary>
        /// Getting off, the mount starts vanishing this long after the rider's feet leave the saddle (owner, 7 Nov 2026
        /// retest: the leap comes first, the vanish follows the push-off, so both read smoothly).
        /// </summary>
        public const float VanishAfterPushOff = 0.04f;

        /// <summary>The summon hop: 1.0 s, free at 0.8 s; the mount forms 0.12 to 0.77 s, solid as the rider lands. The
        /// crouch ends and the feet leave the ground at 0.2 s; on the seat at 0.77 s.</summary>
        public static MountTransition StandingOn() => new MountTransition
        {
            seconds = 1f, freeSeconds = 0.8f, stirrupsAt = 0.77f, effectDelay = 0.12f, effectSeconds = 0.65f,
            leavesAt = 0.2f, arrivesAt = 0.77f,
        };

        /// <summary>The running hop: 0.8 s, on the seat and free at 0.55 s; the mount forms 0.05 to 0.55 s. Off the
        /// ground at 0.1 s.</summary>
        public static MountTransition RunningOn() => new MountTransition
        {
            seconds = 0.8f, freeSeconds = 0.55f, stirrupsAt = 0.6875f, effectDelay = 0.05f, effectSeconds = 0.5f,
            leavesAt = 0.1f, arrivesAt = 0.55f,
        };

        /// <summary>The standing dismount, owner-approved 7 Nov 2026: a front flip too. The game's take-off from the
        /// saddle (0-0.40 s), the game's front flip about the body's centre (0.42-1.00 s), touchdown 1.02 s, settling into
        /// idle (no roll); 1.70 s, free at 1.55 s. The mount vanishes top-down from just after the push-off.</summary>
        public static MountTransition StandingOff() => new MountTransition
        {
            seconds = 1.7f, freeSeconds = 1.55f, stirrupsAt = 0f, effectDelay = 0.4f + VanishAfterPushOff, effectSeconds = 0.5f,
            leavesAt = 0.4f, arrivesAt = 1.02f, apexAt = 0.7f, standsAt = 1.53f,
        };

        /// <summary>The moving dismount, redone and owner-approved 7 Nov 2026: a sprung take-off from the saddle
        /// (0-0.36 s), the game's front flip about the body's centre (0.38-0.96 s), touchdown 0.98 s, a forward roll to
        /// 1.76 s, up into the run; 2.0 s, free at 1.93 s. Everything is in the clip (no drawn arc). The mount vanishes
        /// top-down from just after the push-off.</summary>
        public static MountTransition RunningOff() => new MountTransition
        {
            seconds = 2.0f, freeSeconds = 1.93f, stirrupsAt = 0f, effectDelay = 0.36f + VanishAfterPushOff, effectSeconds = 0.5f, rollEndsAt = 1.76f,
            leavesAt = 0.36f, arrivesAt = 0.98f, apexAt = 0.63f, rollStartsAt = 1.0f, slideEndsAt = 1.23f,
        };

        /// <summary>The same take-off, flip and roll, ending standing: 2.1 s, free at 1.98 s.</summary>
        public static MountTransition RunningOffToIdle() => new MountTransition
        {
            seconds = 2.1f, freeSeconds = 1.98f, stirrupsAt = 0f, effectDelay = 0.36f + VanishAfterPushOff, effectSeconds = 0.5f, rollEndsAt = 1.76f,
            leavesAt = 0.36f, arrivesAt = 0.98f, apexAt = 0.63f, rollStartsAt = 1.0f, slideEndsAt = 1.23f, standsAt = 1.97f,
        };
    }
}
