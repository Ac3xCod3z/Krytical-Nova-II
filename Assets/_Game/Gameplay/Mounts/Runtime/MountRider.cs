using Animancer;
using KryticalNova.Core;
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
        // A seat higher (or lower) than the one the getting-on/off clips were keyed on (the Circuit Strider's against the
        // Brambleback's): the rider is lowered by groundDrop while the feet are on the ground (onGround 1), so they stand
        // on it, and sits on this mount's own seat in the saddle (onGround 0). Owner, 7 Nov 2026.
        private float groundDrop;
        private float onGround;
        private float airRise; // metres over the seat in the mount-up hop's air phase (MountSettings.hopClearance)
        // The hand-over out of a getting-off: the origin's offset from rest when it began, and its clock (see HandOver).
        private Vector3 handOverFrom;
        private float handOverStart, handOverSeconds;
        // The getting-off in play whose beats are still to be shown (see Beats), and which have been.
        private MountTransition beats;
        private int beatsFired;
        private bool beatsEndRunning;
        private bool beatsGettingOn; // the beats in play are a getting-on's (take-off, saddle landing)
        private MountTransition transition; // the getting on or off in play (standing or running)
        private bool runningTransition;
        private float phaseStartedAt;
        private float settleEnds;
        private float tailEnds; // the getting-off clip still playing after the rider is free
        private float phaseClipLength; // the getting-on or getting-off clip being played

        private static bool DrawsNothing => Application.isBatchMode;

        private void Awake()
        {
            mounts = GetComponent<PlayerMounts>();
            motor = GetComponent<PlayerAnimancerMotor>();
            movement = GetComponent<PlayerMovementSkills>();
            agent = GetComponent<NavMeshAgent>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
        }

        // ------------------------------------------------------------------ the leap off (owner, 7 Nov 2026 retest 2)
        // The game's own jump: its clip chain (a crouch on the saddle, the take-off, the flip, the landing) on the motor,
        // and the jump's Motion Warping asset moving the rider's body (not the player's root) from the seat over an apex
        // to a mark under the root, so it lands where the body is, standing or running. Every machine plays it from
        // the replicated phase. A running leap hands into its clip's roll at the touchdown; a standing one into the
        // jump's own landing and the idle.
        private PlayerJumpMotor jumpMotor;
        private Kinemation.MotionWarping.Runtime.Core.MotionWarping leapWarp;
        private Transform leapMark;
        private bool leaping, leapLanded, leapRolling;
        private AnimationClip leapRollClip;
        private float leapRollFrom;

        private bool StartLeap(AnimationClip bakedClip)
        {
            if (motor == null || !motor.HasWarpedJump || jumpMotor == null || jumpMotor.WarpAsset == null || rider == null) return false;
            if (!EnsureLeapWarp()) return false;
            MountSettings settings = mounts.Settings;
            MountTransition baked = settings != null ? settings.Transition(false, runningTransition, mounts.DismountEndsRunning) : null;
            leapRollClip = runningTransition ? bakedClip : null;
            leapRollFrom = baked != null ? baked.rollStartsAt : 0f;
            leaping = true;
            leapLanded = leapRolling = false;

            // Where it lands: the rider's own rest under the root (it follows the root, so a running body is caught).
            leapMark.localPosition = riderRest;
            leapMark.localRotation = riderRestRotation;
            Quaternion facing = transform.rotation * riderRestRotation;
            // The apex: over the seat and part of the way to where the body will be, the seat's height plus a rise.
            float flight = transition.arrivesAt;
            Vector3 travel = movement != null && runningTransition ? transform.forward * movement.CurrentSpeed * flight * 0.5f : Vector3.zero;
            Vector3 apex = rider.position + travel + Vector3.up * (settings != null ? settings.leapApexAboveSeat : 0.7f);

            try
            {
                leapWarp.Play(jumpMotor.WarpAsset, new[]
                {
                    new Kinemation.MotionWarping.Runtime.Utility.WarpPoint { position = apex, rotation = facing },
                    MountLeapWarp.LandingPoint(leapMark), // in the mark's own space (it faces as the rider at rest)
                });
            }
            catch (System.Exception e)
            {
                // Never a half-started warp: stop it and play the baked dismount instead.
                Debug.LogException(e);
                try { leapWarp.Stop(); } catch (System.Exception) { }
                leaping = false;
                return false;
            }
            if (!leapWarp.IsActive()) { leaping = false; return false; }
            motor.PlayWarpedJump();
            return true;
        }

        private bool EnsureLeapWarp()
        {
            if (leapWarp != null) return true;
            leapWarp = MountLeapWarp.Create(transform, rider);
            if (leapWarp == null) return false;
            leapMark = new GameObject("Mount Leap Landing").transform;
            leapMark.SetParent(transform, false);
            return true;
        }

        /// <summary>Seconds past the getting-off's end after which a leap still running is ended anyway.</summary>
        public const float LeapGiveUpSeconds = 0.5f;

        private void Leap()
        {
            if (!leaping || transition == null) return;
            float since = Time.time - phaseStartedAt;
            if (since > transition.seconds + LeapGiveUpSeconds) { EndLeap(true); return; } // never a stuck rider
            if (!leapLanded && since >= transition.arrivesAt)
            {
                leapLanded = true;
                // On the ground now: the motor is no longer seated, so what follows the landing is the on-foot idle or run.
                motor.SetSeated(null, null, null);
                if (leapRollClip != null)
                {
                    // The roll of the baked clip, from its own touchdown: the body keeps going (PlayerMounts drives it).
                    motor.PlayWarpedAction(leapRollClip, leapRollFrom, 1f, 0.08f);
                    leapRolling = true;
                }
                else motor.PlayWarpedJumpRecovery();
            }
            if (leapRolling && since >= transition.seconds - 0.1f) EndLeap(false);
        }

        // The leap is over (its roll ran out) or was cut short: the warp stops and the motor goes back to its own feet.
        private void EndLeap(bool cutShort)
        {
            if (!leaping) return;
            leaping = false;
            if (leapWarp != null && leapWarp.IsActive())
            {
                try { leapWarp.Stop(); } catch (System.Exception e) { Debug.LogException(e); }
            }
            if (leapRolling) motor.EndWarpedAction(0.2f, mounts != null && mounts.DismountEndsRunning ? 1f : 0f);
            else if (cutShort) motor.ResetJumpAnimation();
            leapRolling = false;
            // Landed: the body is on the ground under the root; cut short in the air: back to the seat it left.
            if (rider != null && riderLifted) rider.localPosition = leapLanded ? riderRest : riderRest + seatOffset * lift;
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
                // Free before the getting-off clip has ended (review, 7 Nov 2026): the mount vanishes as usual, but the
                // rider plays the rest of the clip (the landing settle / the roll's last frames) unless they move off.
                if (phase == RidePhase.OnFoot && shownPhase == RidePhase.GettingOff && mount != null && mount.hopMountUp
                    && transition != null && isActiveAndEnabled)
                {
                    float left = phaseStartedAt + Mathf.Max(transition.seconds, phaseClipLength) - Time.time;
                    if (left > 0.02f)
                    {
                        LetModelVanish();
                        shownPhase = RidePhase.OnFoot;
                        tailEnds = Time.time + left;
                        return;
                    }
                }
                // The end of a hop getting-off hands over into the idle (owner, 7 Nov 2026: the snap at the stand-up).
                Clear(eased: phase == RidePhase.OnFoot && shownPhase == RidePhase.GettingOff && mount != null && mount.hopMountUp);
                return;
            }
            tailEnds = 0f;
            int stage = mounts.Stage;
            bool rebuilt = mount != shownMount || stage != shownStage;
            RidePhase before = shownPhase;
            if (rebuilt) BuildModel(mount, stage);
            if (phase != before) BeginPhase(mount, before, phase);
            else if (rebuilt && materialiser != null && transition != null
                     && (phase == RidePhase.GettingOn || phase == RidePhase.GettingOff))
            {
                // An evolution mid-way: the new stage's model takes up the effect for the time that is left.
                float since = Time.time - phaseStartedAt;
                bool on = phase == RidePhase.GettingOn;
                float seconds = mount.materialise != null ? mount.materialise.EffectSeconds(on, transition) : transition.effectSeconds;
                float delay = Mathf.Max(0f, transition.effectDelay - since);
                float left = Mathf.Max(0.05f, transition.effectDelay + seconds - Mathf.Max(since, transition.effectDelay));
                if (on) materialiser.Appear(mount.materialise, delay, left);
                else materialiser.Vanish(mount.materialise, delay, left, false);
            }
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
                transition = mounts.TransitionInPlay(on, runningTransition, mounts.DismountEndsRunning);
                if (mount.hopMountUp) { lift = Lift(phase, 0f); forward = Forward(0f); onGround = OnGround(on, 0f); }
                // The beats for the GameFeel layer: getting off (take-off, apex, touchdown, roll, stand-up), and getting
                // on (owner, 7 Nov 2026 retest: the push off the ground, the weight landing in the saddle).
                EndBeats(); // a getting-on or off cut short by this new phase ends its feel first
                EndLeap(true);
                beats = mount.hopMountUp ? transition : null;
                beatsGettingOn = on;
                beatsFired = 0;
                beatsEndRunning = runningTransition && mounts.DismountEndsRunning;
                // Owner, 7 Nov 2026 (retest): the mount slid along under the rider on the running getting-off, since the
                // body keeps travelling (the fluid dismount). It stays where it stood while it vanishes there.
                if (!on && model != null && isActiveAndEnabled) model.transform.SetParent(null, true);
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

        /// <summary>How much the rider's feet are on the ground (1) rather than off it or in the saddle (0), a time into
        /// the getting on or off in play: eased over the air phase between the transition's leaves-at and arrives-at.</summary>
        private float OnGround(bool gettingOn, float seconds) =>
            transition == null ? 0f : MountRideMotion.OnGround(gettingOn, seconds, transition.leavesAt, transition.arrivesAt);

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

        // The mount's body springing back after a kick: metres down (x) and back (y), their speeds, and what was applied.
        private Vector2 kick, kickSpeed;
        private Vector3 kickApplied;

        private void Kick(MountSettings tuning, float down, float back)
        {
            kickSpeed += new Vector2(MountRideMotion.KickVelocity(down, tuning.kickHz, tuning.kickDamping),
                MountRideMotion.KickVelocity(back, tuning.kickHz, tuning.kickDamping));
        }

        // Moves the model by the change in the spring's offset only, so it works on the player (riding) and in the world
        // (vanishing where it stood) alike.
        private void SpringModel()
        {
            if (model == null) { kick = kickSpeed = Vector2.zero; kickApplied = Vector3.zero; return; }
            if (kickSpeed == Vector2.zero && kick == Vector2.zero && kickApplied == Vector3.zero) return;
            MountSettings tuning = mounts != null ? mounts.Settings : null;
            float hz = tuning != null ? tuning.kickHz : 6f, damping = tuning != null ? tuning.kickDamping : 0.35f;
            float dt = Time.deltaTime;
            MountRideMotion.SpringStep(ref kick.x, ref kickSpeed.x, hz, damping, dt);
            MountRideMotion.SpringStep(ref kick.y, ref kickSpeed.y, hz, damping, dt);
            if (kick.sqrMagnitude < 1e-8f && kickSpeed.sqrMagnitude < 1e-6f) kick = kickSpeed = Vector2.zero;
            Transform body = model.transform;
            Vector3 offset = -body.up * kick.x - body.forward * kick.y;
            body.position += offset - kickApplied;
            kickApplied = offset;
        }

        private void ForgetModel()
        {
            kick = kickSpeed = Vector2.zero;
            kickApplied = Vector3.zero;
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
            FinishHandOver(); // a new ride during the last hand-over: the rest position is the real one again first
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
            seatOffset = seat + (shownMount != null ? shownMount.riderSeatNudge : Vector3.zero);
            groundDrop = shownMount != null && shownMount.hopMountUp
                ? MountRideMotion.GroundDrop(seatOffset.y, shownMount.Stage(shownStage).clipSeatHeight) : 0f;
            rider.localPosition = riderRest + seatOffset * lift + Vector3.down * (groundDrop * onGround);
            if (stirrups != null && model != null && shownMount != null)
            {
                MountStageLook look = shownMount.Stage(shownStage);
                if (look.stirrupLeft != Vector3.zero) stirrups.SetStirrups(model.transform, look.stirrupLeft, look.stirrupRight);
                else stirrups.SetStirrups(null, default, default);
            }
        }

        private void LowerRider(bool eased = false)
        {
            if (!riderLifted) return;
            if (rider != null)
            {
                rider.localRotation = riderRestRotation;
                // In step with the motor's fade into the idle (the same seconds: MountSettings.standHandOverSeconds).
                float fade = eased && mounts != null && mounts.Settings != null ? mounts.Settings.standHandOverSeconds
                    : motor != null ? motor.LocomotionFadeSeconds : 0f;
                handOverFrom = rider.localPosition - riderRest;
                if (eased && fade > 0f && handOverFrom.sqrMagnitude > 1e-6f)
                {
                    handOverStart = Time.time;
                    handOverSeconds = fade;
                }
                else rider.localPosition = riderRest;
            }
            lean = bobAmount = 0f;
            lift = 1f;
            forward = 0f;
            onGround = groundDrop = 0f;
            if (stirrups != null) stirrups.SetStirrups(null, default, default);
            if (footIK != null) footIK.enabled = footIKWasOn;
            riderLifted = false;
        }

        /// <summary>
        /// The hand-over out of a getting-off (owner, 7 Nov 2026: the body dropped and the legs crossed for a moment as the
        /// rider stood up). The getting-off clip is keyed with the rider's origin on the seat; the idle with it on the
        /// ground. Moving the origin down at once left the clip, still fading out, a seat's height too low for the whole
        /// fade. So the origin comes down over the same fade, in step with it: at fade weight w the origin is
        /// (1 - w) of the way up, and the body stays where the clip left it.
        /// </summary>
        private void HandOver()
        {
            if (handOverSeconds <= 0f || rider == null) return;
            rider.localPosition = riderRest + handOverFrom * MountRideMotion.HandOverShare(Time.time - handOverStart, handOverSeconds);
            if (Time.time - handOverStart >= handOverSeconds) handOverSeconds = 0f;
        }

        /// <summary>
        /// The getting-off's beats at the times measured on its clip (MountTransition: leaves, apex, arrives, roll, stands),
        /// reported to the GameFeel layer through Core (owner, 7 Nov 2026: dust, lines, smear, impact). Every machine shows
        /// them: they follow the replicated phase. Runs on through the clip's tail after the rider is free.
        /// </summary>
        private void Beats()
        {
            if (beats == null) return;
            float since = Time.time - phaseStartedAt;
            MountSettings tuning = mounts != null ? mounts.Settings : null;
            float lead = tuning != null ? tuning.crouchLead : 0f;
            Beat(DismountBeat.MountShows, Mathf.Max(0.0001f, beats.effectDelay), since); // as the mount forms or vanishes
            if (lead > 0f && beats.leavesAt > lead) Beat(DismountBeat.Crouch, beats.leavesAt - lead, since);
            Beat(DismountBeat.TakeOff, beats.leavesAt, since);
            Beat(DismountBeat.Apex, beats.apexAt, since);
            Beat(DismountBeat.Touchdown, beats.arrivesAt, since);
            Beat(DismountBeat.RollStart, beats.rollStartsAt, since);
            Beat(DismountBeat.SlideEnd, beats.slideEndsAt, since);
            Beat(DismountBeat.RollEnd, beats.rollStartsAt > 0f ? beats.rollEndsAt : 0f, since);
            Beat(DismountBeat.StandUp, beats.standsAt, since);
            if (since > beats.seconds) EndBeats();
        }

        // The last beat, always: what the getting-off started (lines, smear, dust) ends, finished or cut short.
        private void EndBeats()
        {
            if (beats == null) return;
            Beat(DismountBeat.Ended, Mathf.Max(0.0001f, Time.time - phaseStartedAt), Time.time - phaseStartedAt);
            beats = null;
        }

        private void Beat(DismountBeat beat, float at, float since)
        {
            int bit = 1 << (int)beat;
            if (at <= 0f || since < at || (beatsFired & bit) != 0) return;
            beatsFired |= bit;
            // The mount takes the weight (owner, 7 Nov 2026 retest 2): shoved down and back by the push-off, giving under
            // the rider landing in the saddle.
            MountSettings tuning = mounts != null ? mounts.Settings : null;
            if (tuning != null && beat == DismountBeat.TakeOff && !beatsGettingOn) Kick(tuning, tuning.pushOffDip, tuning.pushOffBack);
            if (tuning != null && beat == DismountBeat.Touchdown && beatsGettingOn) Kick(tuning, tuning.saddleDip, 0f);
            IGameFeelSink feel = ClientPresentationRegistry.Feel;
            if (feel == null || mounts == null) return;
            feel.Dismount(new DismountFeel
            {
                Actor = mounts.transform,
                Beat = beat,
                Point = mounts.transform.position,
                Speed = movement != null ? movement.CurrentSpeed : 0f,
                EndsRunning = beatsEndRunning,
                GettingOn = beatsGettingOn,
                IsLocalPlayer = mounts.IsOwner,
                MountVoice = shownMount != null ? shownMount.soundSet : null,
            });
        }

        private void FinishHandOver()
        {
            if (handOverSeconds <= 0f) return;
            if (rider != null) rider.localPosition = riderRest;
            handOverSeconds = 0f;
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
                    phaseClipLength = on != null ? on.length : 0f;
                    motor.SetSeated(on, on, on);
                    break;
                case RidePhase.GettingOff:
                    AnimationClip off = null;
                    if (runningTransition && !mounts.DismountEndsRunning)
                        off = mount.RiderClip(stage, l => l.dismountRunningToIdle, mount.dismountRunningToIdle);
                    if (runningTransition && off == null) off = mount.RiderClip(stage, l => l.dismountRunning, mount.dismountRunning);
                    if (off == null) off = mount.RiderClip(stage, l => l.dismount, mount.dismount);
                    if (off == null) off = idle;
                    phaseClipLength = off != null ? off.length : 0f;
                    if (transition != null && transition.leap && StartLeap(off)) { phaseClipLength = transition.seconds; break; }
                    motor.SetSeated(off, off, off);
                    break;
                default:
                    motor.SetSeated(extraIdle != null ? extraIdle : idle, mount.RiderClip(stage, l => l.riderWalk, mount.riderWalk),
                        mount.RiderClip(stage, l => l.riderRun, mount.riderRun), inSaddle: true);
                    break;
            }
        }

        /// <param name="eased">The end of a getting-off: the rider's origin comes down from the seat over the motor's fade
        /// into the idle, in step with it, instead of at once (see <see cref="HandOver"/>).</param>
        private void Clear(bool eased = false)
        {
            EndLeap(!leapLanded);
            if (!eased || !isActiveAndEnabled) { FinishHandOver(); EndBeats(); }
            float standFade = eased && mounts != null && mounts.Settings != null ? mounts.Settings.standHandOverSeconds : 0f;
            if (shownMount != null || riderLifted) motor?.SetSeated(null, null, null, standFade: standFade);
            if (isActiveAndEnabled) LetModelVanish();
            else DestroyModel();
            LowerRider(eased && isActiveAndEnabled);
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
            float leanScale = shownMount != null ? shownMount.riderLeanScale : 1f;
            float bobScale = shownMount != null ? shownMount.riderBobScale : 1f;
            float wantLean = (!travelling ? 0f : running ? settings.gallopLean : settings.walkLean) * leanScale;
            float wantBob = (!travelling ? 0f : running ? settings.gallopBob : settings.walkBob) * bobScale;
            lean = Mathf.SmoothDamp(lean, wantLean, ref leanVelocity, settings.leanEase);
            bobAmount = Mathf.SmoothDamp(bobAmount, wantBob, ref bobVelocity, settings.leanEase);
            // The bob follows the mount's own clip: it rises at the push of each stride.
            float phase = 0f;
            AnimancerState state = mountAnimancer != null ? mountAnimancer.States.Current : null;
            if (state != null) phase = state.NormalizedTime * settings.stridesPerClip;
            float bob = bobAmount * Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f));
            // Turn about the hips (the seat), forward about the mount's side-to-side axis.
            Quaternion turn = Quaternion.AngleAxis(lean, Vector3.right);
            Vector3 seat = seatOffset * lift + Vector3.forward * forward + Vector3.down * (groundDrop * onGround) + Vector3.up * airRise;
            Vector3 hips = riderRest + seat + Vector3.up * HipHeight;
            if (leaping) return; // the leap's warp moves the rider
            rider.localRotation = turn * riderRestRotation;
            rider.localPosition = hips + turn * (riderRest + seat - hips) + Vector3.up * bob;
        }

        // The summon hop: the rider rises from the ground to the seat (and back down getting off) along the Mount
        // Settings curves, the feet go into the stirrups as they land, and the riding clips take over after the settle
        // (or as soon as the rider moves).
        private void Hop(MountSettings settings, bool travelling)
        {
            forward = 0f;
            onGround = 0f;
            airRise = 0f;
            if (shownMount == null || !shownMount.hopMountUp || settings == null || transition == null) { lift = 1f; return; }
            float since = Time.time - phaseStartedAt;
            if (shownPhase == RidePhase.GettingOn || shownPhase == RidePhase.GettingOff)
                onGround = OnGround(shownPhase == RidePhase.GettingOn, since);
            if (shownPhase == RidePhase.GettingOn) // clear the seat in the air, then settle into it
                airRise = settings.hopClearance[Mathf.Clamp(shownStage, 0, 2)] * shownMount.hopClearanceScale
                          * MountRideMotion.AirBump(since, transition.leavesAt, transition.arrivesAt);
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

        // What the rest of the game sees of the lift: effects hung on the body (a buff's aura over the head) rise with it
        // (owner, 7 Nov 2026 retest 2: the Redline aura sat inside the rider on a mount).
        private void LateUpdate()
        {
            if (motor == null) return;
            motor.BodyLift = rider != null && (riderLifted || handOverSeconds > 0f) ? rider.localPosition - riderRest : Vector3.zero;
        }

        private void Update()
        {
            HandOver();
            Beats();
            SpringModel();
            Leap();
            // The rest of a getting-off clip after the rider is free: ends at its length, or as soon as the rider moves.
            if (tailEnds > 0f && (Time.time >= tailEnds || (motor != null && motor.IsTravelling)))
            {
                tailEnds = 0f;
                Clear(eased: true);
            }
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
                    // A stage keyed with its own stride says at what speed its clips match the ground; else the shared value.
                    Vector2 clipSpeeds = look.clipMetresPerSecond.x > 0f && look.clipMetresPerSecond.y > 0f
                        ? look.clipMetresPerSecond : settings.clipMetresPerSecond;
                    float reference = running ? clipSpeeds.y : clipSpeeds.x;
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
