using Animancer;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// Shows a player on their mount, on every machine, from what <see cref="PlayerMounts"/> replicates: the selected
    /// mount's model at its growth stage under the player, the rider lifted onto its seat socket and given the riding
    /// clips (<see cref="PlayerAnimancerMotor.SetSeated"/>), and the mount playing its idle, walk or run. Walk is the
    /// default and run is the Run selection, the same as on foot (owner, 25 Oct 2026). At rest the mount and its rider
    /// now and then play one of their other idles. Purely visual: nothing here is sent. A dedicated server, which draws
    /// nothing, still seats the rider (the motor's state) but makes no model.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMounts))]
    public sealed class MountRider : MonoBehaviour
    {
        private PlayerMounts mounts;
        private PlayerAnimancerMotor motor;
        private PlayerMovementSkills movement;
        private NavMeshAgent agent;

        private MountDefinition shownMount;
        private int shownStage = -1;
        private RidePhase shownPhase;
        private GameObject model;
        private AnimancerComponent mountAnimancer;
        private AnimationClip mountClip;
        private Transform rider;
        private Vector3 riderRest;
        private Quaternion riderRestRotation = Quaternion.identity;
        private bool riderLifted;
        private FootGroundingIK footIK;
        private bool footIKWasOn;
        private StirrupIK stirrups;
        private float nextVariety;
        private float varietyEnds;
        private AnimationClip riderExtra;
        private Vector3 seatOffset;
        private float lean, leanVelocity, bobAmount, bobVelocity;
        private MountMaterialiser materialiser;
        // The summon hop (owner, 6 Nov 2026): how high the rider is between the ground (0) and the seat (1), when the
        // getting on or off began, and until when the hop's settle plays on after the rider is free to move.
        private float lift = 1f;
        private float forward; // metres the rider is drawn ahead of the player (the running dismount's arc)
        private MountTransition transition; // the getting on or off in play (standing or running)
        private bool runningTransition;
        private float phaseStartedAt;
        private float settleEnds;

        private static bool DrawsNothing => Application.isBatchMode;

        private void Awake()
        {
            mounts = GetComponent<PlayerMounts>();
            motor = GetComponent<PlayerAnimancerMotor>();
            movement = GetComponent<PlayerMovementSkills>();
            agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable()
        {
            mounts.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            mounts.Changed -= Apply;
            Clear();
        }

        private void Apply()
        {
            MountDefinition mount = mounts.Selected;
            RidePhase phase = mounts.Phase;
            if (mount == null || !mount.IsBuilt || phase == RidePhase.OnFoot)
            {
                Clear();
                return;
            }
            int stage = mounts.Stage;
            bool rebuilt = mount != shownMount || stage != shownStage;
            RidePhase before = shownPhase;
            if (rebuilt) BuildModel(mount, stage);
            if (phase != before) BeginPhase(mount, before, phase);
            shownPhase = phase;
            // The hop's settle plays on into the ride; the riding clips take over when it ends (Hop).
            if (settleEnds > 0f && !rebuilt) return;
            if (phase != before || rebuilt) Seat(mount, phase, null); // an evolution brings its stage's rider clips
        }

        // Getting on: the mount forms under the hopping rider. Getting off: it vanishes top-down as the rider leaves.
        private void BeginPhase(MountDefinition mount, RidePhase before, RidePhase phase)
        {
            MountSettings settings = mounts.Settings;
            phaseStartedAt = Time.time;
            settleEnds = 0f;
            if (phase == RidePhase.GettingOn || phase == RidePhase.GettingOff)
            {
                bool on = phase == RidePhase.GettingOn;
                runningTransition = mounts.IsRunningTransition;
                transition = settings != null ? settings.Transition(on, runningTransition) : null;
                if (mount.hopMountUp) { lift = Lift(phase, 0f); forward = Forward(0f); }
                if (materialiser != null)
                {
                    float delay = transition != null ? transition.effectDelay : 0f;
                    float seconds = mount.materialise != null ? mount.materialise.EffectSeconds(on, transition)
                        : transition != null ? transition.effectSeconds : 0f;
                    if (on) materialiser.Appear(mount.materialise, delay, seconds);
                    else materialiser.Vanish(mount.materialise, delay, seconds, false);
                }
            }
            else if (phase == RidePhase.Riding && before == RidePhase.GettingOn && mount.hopMountUp && transition != null)
            {
                // The rider is free before the hop has settled: the settle plays on until the clip's end.
                phaseStartedAt = Time.time - transition.freeSeconds;
                settleEnds = Time.time + Mathf.Max(0f, transition.seconds - transition.freeSeconds);
            }
        }

        /// <summary>How high the rider is (0 = ground, 1 = seat) a time into the getting on or off in play.</summary>
        private float Lift(RidePhase phase, float seconds)
        {
            if (transition == null) return phase == RidePhase.GettingOff ? 0f : 1f;
            return MountRideMotion.Lift(transition.lift, seconds, transition.seconds);
        }

        /// <summary>How far ahead of the player the rider is drawn, in metres (only the running dismount arcs).</summary>
        private float Forward(float seconds) =>
            transition == null ? 0f : MountRideMotion.Ahead(transition.forward, seconds, transition.seconds);

        // ------------------------------------------------------------------ the mount

        private void BuildModel(MountDefinition mount, int stage)
        {
            DestroyModel();
            shownMount = mount;
            shownStage = stage;
            MountStageLook look = mount.Stage(stage);
            if (!DrawsNothing && look.model != null)
            {
                model = Instantiate(look.model, transform);
                model.name = $"Mount ({mount.displayName}, {mount.StageName(stage)})";
                model.transform.localPosition = new Vector3(0f, -FeetHeight, 0f);
                model.transform.localRotation = Quaternion.identity;
                materialiser = model.AddComponent<MountMaterialiser>();
                Animator animator = model.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    mountAnimancer = animator.GetComponent<AnimancerComponent>();
                    if (mountAnimancer == null) mountAnimancer = animator.gameObject.AddComponent<AnimancerComponent>();
                    mountAnimancer.Animator = animator;
                }
            }
            mountClip = null;
            LiftRider(look.seat);
        }

        private void DestroyModel()
        {
            if (model != null) Destroy(model);
            ForgetModel();
        }

        // Off the mount: the model is let go into the world where it stands and vanishes there (at once after a fall
        // or a jump, or finishing the vanish its getting-off began), then destroys itself.
        private void LetModelVanish()
        {
            if (model == null || materialiser == null) { DestroyModel(); return; }
            model.transform.SetParent(null, true);
            if (mountAnimancer != null) mountAnimancer.Stop();
            MountMaterialiseSettings look = shownMount != null ? shownMount.materialise : null;
            MountTransition off = mounts.Settings != null ? mounts.Settings.getOff : null;
            materialiser.VanishAndDestroy(look, look != null ? look.EffectSeconds(false, off) : 0f);
            ForgetModel();
        }

        private void ForgetModel()
        {
            model = null;
            materialiser = null;
            mountAnimancer = null;
            mountClip = null;
            shownMount = null;
            shownStage = -1;
        }

        // The player's root stands this far above the ground it walks on (the agent's base offset).
        private float FeetHeight => agent != null ? agent.baseOffset : 0f;

        // ------------------------------------------------------------------ the rider

        private void LiftRider(Vector3 seat)
        {
            if (motor == null || motor.Animancer == null) return;
            if (!riderLifted)
            {
                rider = motor.Animancer.transform;
                riderRest = rider.localPosition;
                riderRestRotation = rider.localRotation;
                footIK = rider.GetComponent<FootGroundingIK>();
                footIKWasOn = footIK != null && footIK.enabled;
                if (footIK != null) footIK.enabled = false; // the feet are in the stirrups, not on the ground
                stirrups = rider.GetComponent<StirrupIK>();
                if (stirrups == null) stirrups = rider.gameObject.AddComponent<StirrupIK>();
                riderLifted = true;
            }
            seatOffset = seat;
            rider.localPosition = riderRest + seat * lift;
            if (stirrups != null && model != null && shownMount != null)
            {
                MountStageLook look = shownMount.Stage(shownStage);
                if (look.stirrupLeft != Vector3.zero) stirrups.SetStirrups(model.transform, look.stirrupLeft, look.stirrupRight);
                else stirrups.SetStirrups(null, default, default);
            }
        }

        private void LowerRider()
        {
            if (!riderLifted) return;
            if (rider != null) { rider.localPosition = riderRest; rider.localRotation = riderRestRotation; }
            lean = bobAmount = 0f;
            lift = 1f;
            forward = 0f;
            if (stirrups != null) stirrups.SetStirrups(null, default, default);
            if (footIK != null) footIK.enabled = footIKWasOn;
            riderLifted = false;
        }

        private void Seat(MountDefinition mount, RidePhase phase, AnimationClip extraIdle)
        {
            riderExtra = extraIdle;
            // Getting on or off, the clip places the feet (out of the stirrups); seated, the IK holds them in.
            // On a summon hop the feet go in as the rider lands (Hop), not when the getting-on starts.
            if (stirrups != null) stirrups.SetWeight(phase == RidePhase.Riding ? 1f : 0f);
            if (motor == null) return;
            int stage = shownStage < 0 ? 0 : shownStage;
            AnimationClip idle = mount.RiderClip(stage, l => l.riderIdle, mount.riderIdle);
            switch (phase)
            {
                case RidePhase.GettingOn:
                    AnimationClip on = runningTransition ? mount.RiderClip(stage, l => l.mountUpRunning, mount.mountUpRunning) : null;
                    if (on == null) on = mount.RiderClip(stage, l => l.mountUp, mount.mountUp);
                    if (on == null) on = idle;
                    motor.SetSeated(on, on, on);
                    break;
                case RidePhase.GettingOff:
                    AnimationClip off = runningTransition ? mount.RiderClip(stage, l => l.dismountRunning, mount.dismountRunning) : null;
                    if (off == null) off = mount.RiderClip(stage, l => l.dismount, mount.dismount);
                    if (off == null) off = idle;
                    motor.SetSeated(off, off, off);
                    break;
                default:
                    motor.SetSeated(extraIdle != null ? extraIdle : idle, mount.RiderClip(stage, l => l.riderWalk, mount.riderWalk),
                        mount.RiderClip(stage, l => l.riderRun, mount.riderRun));
                    break;
            }
        }

        private void Clear()
        {
            if (shownMount != null || riderLifted) motor?.SetSeated(null, null, null);
            if (isActiveAndEnabled) LetModelVanish();
            else DestroyModel();
            LowerRider();
            shownPhase = RidePhase.OnFoot;
            riderExtra = null;
            settleEnds = 0f;
        }

        /// <summary>
        /// The rider leans forward from the hips and rises and drops with the mount's strides: a lot at a gallop, a
        /// little at a walk, none at rest. On top of the riding clip, so it shows on any character.
        /// </summary>
        private void LeanAndBob(MountSettings settings, bool travelling, bool running)
        {
            if (!riderLifted || rider == null || settings == null) return;
            float wantLean = !travelling ? 0f : running ? settings.gallopLean : settings.walkLean;
            float wantBob = !travelling ? 0f : running ? settings.gallopBob : settings.walkBob;
            lean = Mathf.SmoothDamp(lean, wantLean, ref leanVelocity, settings.leanEase);
            bobAmount = Mathf.SmoothDamp(bobAmount, wantBob, ref bobVelocity, settings.leanEase);
            // The bob follows the mount's own clip: it rises at the push of each stride.
            float phase = 0f;
            AnimancerState state = mountAnimancer != null ? mountAnimancer.States.Current : null;
            if (state != null) phase = state.NormalizedTime * settings.stridesPerClip;
            float bob = bobAmount * Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f));
            // Turn about the hips (the seat), forward about the mount's side-to-side axis.
            Quaternion turn = Quaternion.AngleAxis(lean, Vector3.right);
            Vector3 seat = seatOffset * lift + Vector3.forward * forward;
            Vector3 hips = riderRest + seat + Vector3.up * HipHeight;
            rider.localRotation = turn * riderRestRotation;
            rider.localPosition = hips + turn * (riderRest + seat - hips) + Vector3.up * bob;
        }

        // The summon hop: the rider rises from the ground to the seat (and back down getting off) along the Mount
        // Settings curves, the feet go into the stirrups as they land, and the riding clips take over after the settle
        // (or as soon as the rider moves).
        private void Hop(MountSettings settings, bool travelling)
        {
            forward = 0f;
            if (shownMount == null || !shownMount.hopMountUp || settings == null || transition == null) { lift = 1f; return; }
            float since = Time.time - phaseStartedAt;
            switch (shownPhase)
            {
                case RidePhase.GettingOn:
                    lift = Lift(RidePhase.GettingOn, since);
                    if (stirrups != null && since >= transition.stirrupsAt * transition.seconds) stirrups.SetWeight(1f);
                    break;
                case RidePhase.GettingOff:
                    lift = Lift(RidePhase.GettingOff, since);
                    forward = Forward(since);
                    break;
                default:
                    lift = settleEnds > 0f ? Lift(RidePhase.GettingOn, since) : 1f;
                    // A running rider rides straight on; a standing one settles until the clip's end unless they move.
                    if (settleEnds > 0f && (travelling || Time.time >= settleEnds))
                    {
                        settleEnds = 0f;
                        lift = 1f;
                        Seat(shownMount, RidePhase.Riding, null);
                    }
                    break;
            }
        }

        // About where the hips sit above the rider's origin; the lean turns about them.
        private const float HipHeight = 1.0f;

        // ------------------------------------------------------------------ every frame: the mount's gait and idles

        private void Update()
        {
            if (shownMount == null) return;
            MountStageLook look = shownMount.Stage(shownStage);
            MountSettings settings = mounts.Settings;
            bool travelling = motor != null && motor.IsTravelling && shownPhase == RidePhase.Riding;
            bool running = travelling && movement != null && movement.IsRunning;

            Hop(settings, travelling);

            // The rider's other idle has run its course: back to the plain one.
            if (riderExtra != null && (Time.time >= varietyEnds || travelling || shownPhase != RidePhase.Riding))
                Seat(shownMount, shownPhase, null);

            LeanAndBob(settings, travelling, running);

            if (mountAnimancer == null) return;
            AnimationClip clip = travelling ? (running && look.run != null ? look.run : look.walk) : look.idle;
            if (!travelling && mountClip != null && mountClip != look.idle && mountClip != look.walk && mountClip != look.run
                && Time.time < varietyEnds)
                clip = mountClip; // an other idle still playing
            if (clip != null && clip != mountClip)
            {
                mountClip = clip;
                mountAnimancer.Play(clip, 0.25f);
            }
            if (mountClip == null) return;

            AnimancerState state = mountAnimancer.States.Current;
            if (state != null && settings != null)
            {
                if (travelling)
                {
                    float reference = running ? settings.clipMetresPerSecond.y : settings.clipMetresPerSecond.x;
                    float speed = movement != null ? movement.CurrentSpeed : reference;
                    state.Speed = Mathf.Clamp(speed / Mathf.Max(0.1f, reference), settings.clipPlaybackRange.x, settings.clipPlaybackRange.y);
                }
                else state.Speed = 1f;
            }

            // Now and then at rest, the mount and its rider play one of their other idles.
            if (travelling || shownPhase != RidePhase.Riding || settings == null || settings.idleVarietySeconds <= 0f)
            {
                nextVariety = Time.time + (settings != null ? settings.idleVarietySeconds : 9f);
                return;
            }
            if (Time.time < nextVariety) return;
            nextVariety = Time.time + settings.idleVarietySeconds * Random.Range(0.8f, 1.4f);
            if (look.idleExtras != null && look.idleExtras.Length > 0)
            {
                AnimationClip extra = look.idleExtras[Random.Range(0, look.idleExtras.Length)];
                if (extra != null)
                {
                    mountClip = extra;
                    mountAnimancer.Play(extra, 0.25f).Time = 0f;
                    varietyEnds = Time.time + extra.length;
                }
            }
            AnimationClip[] riderExtras = look.riderIdleExtras != null && look.riderIdleExtras.Length > 0 ? look.riderIdleExtras : shownMount.riderIdleExtras;
            if (riderExtras != null && riderExtras.Length > 0)
            {
                AnimationClip extra = riderExtras[Random.Range(0, riderExtras.Length)];
                if (extra != null)
                {
                    Seat(shownMount, shownPhase, extra);
                    varietyEnds = Mathf.Max(varietyEnds, Time.time + extra.length);
                }
            }
        }
    }
}
