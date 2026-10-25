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
            if (rebuilt) BuildModel(mount, stage);
            if (phase != shownPhase || rebuilt) Seat(mount, phase, null); // an evolution brings its stage's rider clips
            shownPhase = phase;
        }

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
            model = null;
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
            rider.localPosition = riderRest + seat;
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
            if (stirrups != null) stirrups.SetStirrups(null, default, default);
            if (footIK != null) footIK.enabled = footIKWasOn;
            riderLifted = false;
        }

        private void Seat(MountDefinition mount, RidePhase phase, AnimationClip extraIdle)
        {
            riderExtra = extraIdle;
            // Getting on or off, the clip places the feet (out of the stirrups); seated, the IK holds them in.
            if (stirrups != null) stirrups.SetWeight(phase == RidePhase.Riding ? 1f : 0f);
            if (motor == null) return;
            int stage = shownStage < 0 ? 0 : shownStage;
            AnimationClip idle = mount.RiderClip(stage, l => l.riderIdle, mount.riderIdle);
            switch (phase)
            {
                case RidePhase.GettingOn:
                    AnimationClip on = mount.RiderClip(stage, l => l.mountUp, mount.mountUp);
                    if (on == null) on = idle;
                    motor.SetSeated(on, on, on);
                    break;
                case RidePhase.GettingOff:
                    AnimationClip off = mount.RiderClip(stage, l => l.dismount, mount.dismount);
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
            DestroyModel();
            LowerRider();
            shownPhase = RidePhase.OnFoot;
            riderExtra = null;
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
            Vector3 hips = riderRest + seatOffset + Vector3.up * HipHeight;
            rider.localRotation = turn * riderRestRotation;
            rider.localPosition = hips + turn * (riderRest + seatOffset - hips) + Vector3.up * bob;
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
