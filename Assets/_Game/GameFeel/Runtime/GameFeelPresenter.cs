using System.Collections;
using System.Collections.Generic;
using Animancer;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.UI;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Client-side game feel. Gameplay reports moments through <see cref="IGameFeelSink"/>; this turns them into FEEL
    /// (MMF_Player) feedbacks. A moment has up to two players: a camera one (impulses, FOV kicks, URP screen pulses,
    /// sound slots), played only for the local player's own actions or when the local player is hit, and a world one
    /// (particles through <see cref="MMF_PooledVfx"/>: POLYGON Particle FX, Cartoon FX impacts and comic text), played
    /// on every client. Skills get their own cast / hit players and buff auras from <see cref="SkillFeelCatalog"/>;
    /// lasting effects on a character (auras, run dust, weapon trail) live in <see cref="ActorVfx"/>. Hit-stop slows
    /// only the attacker's and target's Animancer graphs: Time.timeScale is never touched, so netcode ticks and other
    /// players are unaffected.
    /// Built by GameFeelBuilder (Krytical Nova ▸ GameFeel ▸ Build Game Feel); tune the MMF_Players on the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFeelPresenter : MonoBehaviour, IGameFeelSink
    {
        [Header("FEEL players (camera / screen / sound)")]
        [SerializeField] private MMF_Player swingFeel;
        [SerializeField] private MMF_Player hitFeel;
        [SerializeField] private MMF_Player heavyHitFeel;
        [SerializeField] private MMF_Player damagedFeel;
        [SerializeField] private MMF_Player clickFeel;
        [SerializeField] private MMF_Player selectFeel;
        [SerializeField] private MMF_Player runFeel;
        [SerializeField] private MMF_Player jumpFeel;
        [SerializeField] private MMF_Player landFeel;
        [SerializeField] private MMF_Player attackCastFeel;
        [SerializeField] private MMF_Player buffCastFeel;
        [Tooltip("Held while the local player charges a skill; authored one second long and stretched to the charge time.")]
        [SerializeField] private MMF_Player chargeFeel;
        [Tooltip("The local player died, or killed someone.")]
        [SerializeField] private MMF_Player deathFeel;
        [Tooltip("The same for a critical kill: the body is thrown.")]
        [SerializeField] private MMF_Player critDeathFeel;
        [Tooltip("On top of the death feel when the local player is the one who killed. Full strength on a critical kill.")]
        [SerializeField] private MMF_Player killConfirmFeel;
        [Tooltip("The thrown or falling body of that death hits the ground.")]
        [SerializeField] private MMF_Player bodyLandFeel;
        [SerializeField] private MMF_Player respawnFeel;
        [Tooltip("The local player's pet evolved: a bright pulse and a sound.")]
        [SerializeField] private MMF_Player petEvolveFeel;
        [Tooltip("The local player's own attempt at the forge: it worked, it failed, the piece broke.")]
        [SerializeField] private MMF_Player forgeSuccessFeel;
        [SerializeField] private MMF_Player forgeFailFeel;
        [SerializeField] private MMF_Player forgeBreakFeel;

        [Header("FEEL players (world particles, every client)")]
        [SerializeField] private MMF_Player hitWorldFeel;
        [SerializeField] private MMF_Player heavyHitWorldFeel;
        [Tooltip("Added on top of the heavy hit when the hit was critical (comic text).")]
        [SerializeField] private MMF_Player critWorldFeel;
        [SerializeField] private MMF_Player runWorldFeel;
        [SerializeField] private MMF_Player jumpWorldFeel;
        [SerializeField] private MMF_Player landWorldFeel;
        [Tooltip("Cast / hit bursts for skills the catalog has no entry for.")]
        [SerializeField] private MMF_Player attackCastWorldFeel;
        [SerializeField] private MMF_Player buffCastWorldFeel;
        [SerializeField] private MMF_Player skillHitWorldFeel;
        [SerializeField] private MMF_Player buffEndWorldFeel;
        [SerializeField] private MMF_Player healWorldFeel;
        [SerializeField] private MMF_Player chiWorldFeel;
        [SerializeField] private MMF_Player deathWorldFeel;
        [SerializeField] private MMF_Player critDeathWorldFeel;
        [SerializeField] private MMF_Player bodyLandWorldFeel;
        [SerializeField] private MMF_Player critBodyLandWorldFeel;
        [SerializeField] private MMF_Player respawnWorldFeel;
        [Tooltip("Any pet evolved: the burst on the pet itself.")]
        [SerializeField] private MMF_Player petEvolveWorldFeel;
        [Tooltip("Anyone's attempt at the forge, on their character: a success, a fail, a fail a Failsafe took, a break.")]
        [SerializeField] private MMF_Player forgeSuccessWorldFeel;
        [SerializeField] private MMF_Player forgeFailWorldFeel;
        [SerializeField] private MMF_Player forgeSavedWorldFeel;
        [SerializeField] private MMF_Player forgeBreakWorldFeel;
        private const float NovaBurstHeight = 2f; // where a new Nova tier bursts, above the feet

        [Header("World VFX spawned directly")]
        [SerializeField] private GameObject clickVfx;
        [SerializeField] private GameObject selectHostileVfx;
        [SerializeField] private GameObject selectFriendlyVfx;
        [Tooltip("Ground ring of an area attack. Authored 1 m in radius and one second long: it is full size at " +
                 "Area Ring Grow Time, then flashes and fades. Scaled to the skill's reach and timed to its hit.")]
        [SerializeField] private GameObject areaRingVfx;
        [Range(0.1f, 0.9f)] [SerializeField] private float areaRingGrowTime = 0.6f;

        [Header("Lasting effects on a character")]
        [SerializeField] private SkillFeelCatalog skills;
        [Tooltip("Looping aura for buffs the catalog has no entry for.")]
        [SerializeField] private GameObject defaultAuraVfx;
        [Tooltip("Build-up for charged skills the catalog has no entry for.")]
        [SerializeField] private GameObject defaultChargeVfx;
        [SerializeField] private GameObject runLoopVfx;
        [SerializeField] private GameObject airborneLoopVfx;
        [Tooltip("Rides on a body thrown by a critical kill until it lands.")]
        [SerializeField] private GameObject critDeathTrailVfx;
        [Tooltip("Run, jump and landing dust takes the colour of the ground under the feet.")]
        [SerializeField] private DustPalette dustPalette = DustPalette.Default;
        [SerializeField] private TrailRenderer bladeTrail;
        [Tooltip("Trail length for a basic combo strike (x) and for the finisher and skills (y), when gameplay gives none.")]
        [SerializeField] private Vector2 trailSeconds = new Vector2(0.22f, 0.4f);

        [Header("Hit-stop (Animancer graphs only)")]
        [Range(0f, 0.2f)] [SerializeField] private float hitStopSeconds = 0.06f;
        [Range(0f, 0.2f)] [SerializeField] private float heavyHitStopSeconds = 0.11f;
        [Range(0f, 1f)] [SerializeField] private float hitStopSpeed = 0.05f;
        [Tooltip("The killer's own freeze on a kill (x) and on a critical kill (y). The dying body is never frozen: it is moving.")]
        [SerializeField] private Vector2 killStopSeconds = new Vector2(0.12f, 0.22f);

        [Header("Kill confirm (the local player's own kills)")]
        [Tooltip("How much of the kill confirm a normal kill gets; a critical kill gets all of it.")]
        [Range(0f, 1f)] [SerializeField] private float normalKillStrength = 0.55f;
        [Tooltip("How hard the camera is shoved the way the killing blow went (m/s) at full strength.")]
        [Min(0f)] [SerializeField] private float killShove = 0.7f;

        private readonly Dictionary<AnimancerComponent, int> frozen = new Dictionary<AnimancerComponent, int>();
        private MMF_Player[] cameraPlayers;
        private Transform chargingLocalActor;

        private void OnEnable()
        {
            ClientPresentationRegistry.RegisterFeel(this);
            cameraPlayers = new[] { swingFeel, hitFeel, heavyHitFeel, damagedFeel, clickFeel, selectFeel, runFeel, jumpFeel, landFeel,
                attackCastFeel, buffCastFeel, chargeFeel, deathFeel, critDeathFeel, killConfirmFeel, bodyLandFeel, respawnFeel, petEvolveFeel,
                forgeSuccessFeel, forgeFailFeel, forgeBreakFeel };
            if (skills == null) skills = GetComponent<SkillFeelCatalog>();
            GroundSurfaces.Palette = dustPalette;
            HudPreferences.Changed += ApplyScreenShakePreference;
            ApplyScreenShakePreference();
        }

        private void Start()
        {
            RebindShakers();
            // Pre-attach reactors so hits this client never got an RPC for (GM hits, remote attackers) still show.
            foreach (var entity in FindObjectsByType<Progression.CombatEntity>(FindObjectsSortMode.None))
                HitReactor.For(entity.transform);
        }

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterFeel(this);
            HudPreferences.Changed -= ApplyScreenShakePreference;
            foreach (var kv in frozen)
                if (kv.Key != null) kv.Key.Graph.Speed = 1f;
            frozen.Clear();
        }

        // ------------------------------------------------------------------ IGameFeelSink

        public void Swing(Transform attacker, int comboStep, bool isLocalPlayer, float seconds = 0f)
        {
            if (attacker == null) return;
            if (seconds <= 0f) seconds = comboStep >= 3 ? trailSeconds.y : trailSeconds.x;
            ActorVfx.For(attacker).Trail(bladeTrail, seconds);
            // Every swing whooshes; the finisher also leans the camera in.
            if (isLocalPlayer) Play(swingFeel, attacker.position, comboStep == 3 ? 1f : 0.45f);
        }

        public void Hit(in HitFeel hit)
        {
            bool heavy = hit.IsCritical || hit.ComboStep >= 3;
            Quaternion facing = hit.TowardAttacker.sqrMagnitude > 0.001f ? Quaternion.LookRotation(hit.TowardAttacker, Vector3.up) : Quaternion.identity;
            PlayWorld(heavy ? heavyHitWorldFeel : hitWorldFeel, hit.Point, facing, hit.Target);
            if (hit.IsCritical) PlayWorld(critWorldFeel, hit.Point, facing, hit.Target);

            // Characters with a hit animation of their own keep the squash and flash but not the lean: the clip leans.
            if (hit.Target != null)
                HitReactor.For(hit.Target)?.React(hit.Target.GetComponentInParent<PlayerAnimancerMotor>() != null ? Vector3.zero : hit.TowardAttacker, heavy);

            if (hit.AttackerIsLocalPlayer)
            {
                Play(heavy ? heavyHitFeel : hitFeel, hit.Point, hit.IsCritical ? 1f : 0.75f);
                float stop = heavy ? heavyHitStopSeconds : hitStopSeconds;
                HitStop(hit.Attacker, stop);
                HitStop(hit.Target, stop);
            }
        }

        public void LocalPlayerDamaged(float fraction01)
        {
            Play(damagedFeel, Vector3.zero, Mathf.Clamp(fraction01 * 6f, 0.4f, 1f));
        }

        public void MoveCommand(Vector3 point, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            Spawn(clickVfx, point + normal * 0.05f, Quaternion.FromToRotation(Vector3.up, normal));
            Play(clickFeel, point, 1f);
        }

        public void TargetSelected(Transform target, WorldTargetKind kind)
        {
            if (target == null) return;
            var vfx = kind == WorldTargetKind.Hostile ? selectHostileVfx : selectFriendlyVfx;
            Spawn(vfx, FootPoint(target), Quaternion.identity);
            Play(selectFeel, target.position, kind == WorldTargetKind.Hostile ? 1f : 0.6f);
        }

        public void RunStarted(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            PlayWorld(runWorldFeel, ActorAnchor.Feet(actor), Facing(actor), actor);
            ActorVfx.For(actor).SetRunning(true, runLoopVfx);
            if (isLocalPlayer) Play(runFeel, actor.position, 1f);
        }

        public void RunStopped(Transform actor)
        {
            ActorVfx.Find(actor)?.SetRunning(false, runLoopVfx);
        }

        public void JumpTakeoff(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            PlayWorld(jumpWorldFeel, ActorAnchor.Feet(actor), Facing(actor), actor);
            if (isLocalPlayer) Play(jumpFeel, actor.position, 1f);
        }

        public void Landed(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            PlayWorld(landWorldFeel, ActorAnchor.Feet(actor), Facing(actor), actor);
            if (isLocalPlayer) Play(landFeel, actor.position, 1f);
        }

        public void Airborne(Transform actor, bool airborne)
        {
            if (actor == null) return;
            if (airborne) ActorVfx.For(actor).SetAirborne(true, airborneLoopVfx);
            else ActorVfx.Find(actor)?.SetAirborne(false, airborneLoopVfx);
        }

        public void SkillCast(Transform caster, string skillId, bool isAttack, bool isLocalPlayer, float areaRadius = 0f, float areaSeconds = 0f)
        {
            if (caster == null) return;
            SkillFeel feel = skills != null ? skills.Find(skillId) : null;
            MMF_Player world = feel != null && feel.cast != null ? feel.cast : isAttack ? attackCastWorldFeel : buffCastWorldFeel;
            Vector3 feet = ActorAnchor.Feet(caster);
            PlayWorld(world, feet, Facing(caster), caster);
            if (areaRadius > 0f) SpawnAreaRing(feet, caster, areaRadius, areaSeconds);
            if (isLocalPlayer) Play(isAttack ? attackCastFeel : buffCastFeel, caster.position, 1f);
        }

        public void ChargeStarted(Transform actor, string skillId, float seconds, bool isLocalPlayer)
        {
            if (actor == null) return;
            SkillFeel feel = skills != null ? skills.Find(skillId) : null;
            ActorVfx vfx = ActorVfx.For(actor);
            vfx.BeginCharge(seconds);
            vfx.AddCharge(feel != null && feel.charge != null ? feel.charge : defaultChargeVfx, ChestOffset);
            if (feel != null && feel.chargeGround != null) vfx.AddCharge(feel.chargeGround, FeetOffset);

            if (!isLocalPlayer || chargeFeel == null) return;
            chargingLocalActor = actor;
            chargeFeel.DurationMultiplier = Mathf.Max(0.05f, seconds);
            Play(chargeFeel, actor.position, 1f);
        }

        public void ChargeEnded(Transform actor, string skillId, bool completed)
        {
            ActorVfx.Find(actor)?.ClearCharge();
            if (actor == null || actor != chargingLocalActor) return;
            chargingLocalActor = null;
            if (chargeFeel != null && chargeFeel.IsPlaying) chargeFeel.StopFeedbacks();
        }

        public void SkillHit(Transform caster, string skillId, Vector3 point)
        {
            SkillFeel feel = skills != null ? skills.Find(skillId) : null;
            Quaternion facing = Quaternion.identity;
            if (caster != null)
            {
                Vector3 toward = caster.position - point;
                toward.y = 0f;
                if (toward.sqrMagnitude > 0.001f) facing = Quaternion.LookRotation(toward, Vector3.up);
            }
            PlayWorld(feel != null && feel.hit != null ? feel.hit : skillHitWorldFeel, point, facing, null);
        }

        public void BuffStarted(Transform actor, string skillId, float duration)
        {
            if (actor == null) return;
            SkillFeel feel = skills != null ? skills.Find(skillId) : null;
            GameObject aura = feel != null && feel.aura != null ? feel.aura : defaultAuraVfx;
            AuraAnchor anchor = feel != null ? feel.auraAnchor : AuraAnchor.Feet;
            Vector3 offset = anchor == AuraAnchor.Head ? HeadOffset : anchor == AuraAnchor.Chest ? ChestOffset : FeetOffset;
            ActorVfx.For(actor).SetAura(skillId, aura, offset, duration);
        }

        public void BuffEnded(Transform actor, string skillId)
        {
            ActorVfx vfx = ActorVfx.Find(actor);
            if (vfx == null) return;
            if (vfx.ClearAura(skillId)) PlayWorld(buffEndWorldFeel, ActorAnchor.Feet(actor), Quaternion.identity, actor);
        }

        public void Consumed(Transform actor, bool restoredHealth, bool restoredChi)
        {
            if (actor == null) return;
            Vector3 feet = ActorAnchor.Feet(actor);
            if (restoredHealth) PlayWorld(healWorldFeel, feet, Quaternion.identity, actor);
            if (restoredChi) PlayWorld(chiWorldFeel, feet, Quaternion.identity, actor);
        }

        public void Death(in DeathFeel death)
        {
            // Effects face the way the body goes, so sparks and debris trail behind it.
            Quaternion along = death.Direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(death.Direction, Vector3.up) : Quaternion.identity;
            PlayWorld(death.IsCritical ? critDeathWorldFeel : deathWorldFeel, death.Point, along, death.Target);
            if (death.Target != null)
            {
                HitReactor.For(death.Target)?.React(Vector3.zero, true);
                ActorVfx vfx = ActorVfx.For(death.Target);
                vfx.SetRunning(false, runLoopVfx);
                if (death.IsCritical) vfx.SetAirborne(true, critDeathTrailVfx != null ? critDeathTrailVfx : airborneLoopVfx);
            }

            if (death.VictimIsLocalPlayer || death.KillerIsLocalPlayer)
                Play(death.IsCritical ? critDeathFeel : deathFeel, death.Point, 1f);
            if (death.KillerIsLocalPlayer)
            {
                PlayKillConfirm(death);
                HitStop(death.Killer, death.IsCritical ? killStopSeconds.y : killStopSeconds.x);
            }
        }

        /// <summary>The local player's own kill: the camera is shoved the way the blow went, so the kill reads as theirs.</summary>
        private void PlayKillConfirm(in DeathFeel death)
        {
            if (killConfirmFeel == null) return;
            Vector3 along = death.Direction.sqrMagnitude > 0.001f ? death.Direction.normalized : Vector3.zero;
            var feedbacks = killConfirmFeel.FeedbacksList;
            for (int i = 0; i < feedbacks.Count; i++)
                if (feedbacks[i] is MMF_CinemachineImpulse impulse) impulse.Velocity = (along + Vector3.up * 0.3f) * killShove;
            Play(killConfirmFeel, death.Point, death.IsCritical ? 1f : normalKillStrength);
        }

        public void BodyLanded(Transform actor, bool critical, bool involvesLocalPlayer)
        {
            if (actor == null) return;
            ActorVfx.Find(actor)?.SetAirborne(false, critDeathTrailVfx != null ? critDeathTrailVfx : airborneLoopVfx);
            PlayWorld(critical ? critBodyLandWorldFeel : bodyLandWorldFeel, ActorAnchor.Feet(actor), Facing(actor), actor);
            if (involvesLocalPlayer) Play(bodyLandFeel, actor.position, critical ? 1f : 0.5f);
        }

        public void Respawned(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            PlayWorld(respawnWorldFeel, ActorAnchor.Feet(actor), Quaternion.identity, actor);
            if (isLocalPlayer) Play(respawnFeel, actor.position, 1f);
        }

        /// <summary>
        /// An attempt at the forge ended. The burst rides on the character for everyone who can see it; the screen
        /// only answers for the player whose piece it was.
        /// </summary>
        public void GearWorked(Transform actor, int outcome, bool isLocalPlayer)
        {
            if (actor == null) return;
            Vector3 feet = ActorAnchor.Feet(actor);
            Quaternion facing = Facing(actor);
            switch (outcome)
            {
                case 0: // success
                    PlayWorld(forgeSuccessWorldFeel, feet, facing, actor);
                    if (isLocalPlayer) Play(forgeSuccessFeel, actor.position, 1f);
                    break;
                case 2: // a fail a Failsafe took
                    PlayWorld(forgeSavedWorldFeel, feet, facing, actor);
                    if (isLocalPlayer) Play(forgeFailFeel, actor.position, 0.6f);
                    break;
                case 3: // the piece broke
                    PlayWorld(forgeBreakWorldFeel, feet, facing, actor);
                    if (isLocalPlayer) Play(forgeBreakFeel, actor.position, 1f);
                    break;
                default: // a fail
                    PlayWorld(forgeFailWorldFeel, feet, facing, actor);
                    if (isLocalPlayer) Play(forgeFailFeel, actor.position, 1f);
                    break;
            }
        }

        /// <summary>
        /// A request at the Herald ended. It reads like the forge's answer (the same bursts on the character); a new
        /// Nova tier adds the burst of a pet evolving, up at the head where the Nova has just changed.
        /// </summary>
        public void NovaWorked(Transform actor, int outcome, bool newTier, bool isLocalPlayer)
        {
            if (actor == null) return;
            Vector3 feet = ActorAnchor.Feet(actor);
            Quaternion facing = Facing(actor);
            switch (outcome)
            {
                case 0: // a level gained
                    PlayWorld(forgeSuccessWorldFeel, feet, facing, actor);
                    if (newTier) PlayWorld(petEvolveWorldFeel, feet + Vector3.up * NovaBurstHeight, Quaternion.identity, actor);
                    if (isLocalPlayer) Play(newTier ? petEvolveFeel : forgeSuccessFeel, actor.position, 1f);
                    break;
                case 3: // a slip the Nova held
                    PlayWorld(forgeSavedWorldFeel, feet, facing, actor);
                    if (isLocalPlayer) Play(forgeFailFeel, actor.position, 0.6f);
                    break;
                case 2: // a level lost
                    PlayWorld(forgeFailWorldFeel, feet, facing, actor);
                    if (isLocalPlayer) Play(forgeFailFeel, actor.position, 1f);
                    break;
                default: // nothing changed
                    PlayWorld(forgeFailWorldFeel, feet, facing, actor);
                    if (isLocalPlayer) Play(forgeFailFeel, actor.position, 0.5f);
                    break;
            }
        }

        public void PetEvolved(Transform pet, Color glow, bool ownerIsLocalPlayer)
        {
            if (pet == null) return;
            PlayWorld(petEvolveWorldFeel, pet.position, Quaternion.identity, pet);
            PetPulse.Play(pet, glow);
            if (ownerIsLocalPlayer) Play(petEvolveFeel, pet.position, 1f);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// FEEL shakers subscribe to static events in Awake, and those events are cleared on every Play start. With
        /// Domain / Scene Reload off the scene's [ExecuteAlways] shakers get no new Awake, so they would stay deaf
        /// (no FOV kick, no chromatic punch) until re-subscribed here. A no-op after a normal scene load.
        /// </summary>
        private static void RebindShakers()
        {
            foreach (var shaker in FindObjectsByType<MMShaker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                shaker.ForceInitialization();
                shaker.StopListening();
                shaker.StartListening();
            }
        }

        // Metres above the soles (ActorVfx brings them into the root's space).
        private static readonly Vector3 FeetOffset = new Vector3(0f, 0.05f, 0f);
        private static readonly Vector3 ChestOffset = new Vector3(0f, 1.15f, 0f);
        private static readonly Vector3 HeadOffset = new Vector3(0f, 1.85f, 0f); // the top of the head
        private const float AreaRingLift = 0.06f;

        /// <summary>
        /// The reach of an area attack, drawn on the ground around the caster: it grows to the real radius as the blow
        /// lands, then flashes and fades, so the ring's edge is exactly where targets stop being hit.
        /// </summary>
        private void SpawnAreaRing(Vector3 feet, Transform caster, float radius, float seconds)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || areaRingVfx == null) return;
            // The effect is one second long and full size at areaRingGrowTime; play it at the speed that puts that
            // moment on the hit.
            float speed = areaRingGrowTime / Mathf.Max(0.1f, seconds);
            Vector3 ground = GroundSnap.Snap(feet, caster) + Vector3.up * AreaRingLift;
            GameObject ring = pool.Spawn(areaRingVfx, ground, Quaternion.identity, 1f / speed + 0.1f);
            if (ring == null) return;
            ring.transform.localScale = areaRingVfx.transform.localScale * radius;
            foreach (var ps in ring.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.simulationSpeed = speed;
            }
        }

        private static void Play(MMF_Player player, Vector3 position, float intensity)
        {
            if (player != null) player.PlayFeedbacks(position, intensity);
        }

        /// <summary>Plays a world player, first telling its pooled-VFX feedbacks which way to face and whom to follow.</summary>
        private static void PlayWorld(MMF_Player player, Vector3 position, Quaternion rotation, Transform target, float intensity = 1f)
        {
            if (player == null) return;
            var feedbacks = player.FeedbacksList;
            for (int i = 0; i < feedbacks.Count; i++)
            {
                if (!(feedbacks[i] is MMF_PooledVfx vfx)) continue;
                vfx.Rotation = rotation;
                vfx.Target = target;
            }
            player.PlayFeedbacks(position, intensity);
        }

        private static Quaternion Facing(Transform actor) => Quaternion.LookRotation(FlatForward(actor), Vector3.up);

        private static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool != null && prefab != null) pool.Spawn(prefab, position, rotation);
        }

        private void HitStop(Transform actor, float seconds)
        {
            if (actor == null || seconds <= 0f) return;
            // A dying body is being thrown by Motion Warping on its own clock: holding its pose would make it slide.
            var motor = actor.GetComponentInParent<PlayerAnimancerMotor>();
            if (motor != null && motor.IsDead) return;
            var animancer = actor.GetComponentInChildren<AnimancerComponent>();
            if (animancer == null || !animancer.IsGraphInitialized) return;
            StartCoroutine(HitStopRoutine(animancer, seconds));
        }

        private IEnumerator HitStopRoutine(AnimancerComponent animancer, float seconds)
        {
            frozen.TryGetValue(animancer, out int count);
            frozen[animancer] = count + 1;
            animancer.Graph.Speed = hitStopSpeed;
            yield return new WaitForSecondsRealtime(seconds);
            if (animancer == null) yield break;
            frozen[animancer] -= 1;
            if (frozen[animancer] <= 0)
            {
                frozen.Remove(animancer);
                animancer.Graph.Speed = 1f;
            }
        }

        private static Vector3 FootPoint(Transform t) => GroundSnap.Snap(t.position, t) + Vector3.up * 0.05f;

        private static Vector3 FlatForward(Transform t)
        {
            var f = t.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
        }

        /// <summary>Screen-shake toggle: mutes every camera / screen feedback but keeps sounds and world VFX.</summary>
        private void ApplyScreenShakePreference()
        {
            bool on = HudPreferences.ScreenShake;
            if (cameraPlayers == null) return;
            foreach (var player in cameraPlayers)
            {
                if (player == null) continue;
                foreach (var f in player.FeedbacksList)
                    if (IsScreenFeedback(f)) f.Active = on;
            }
        }

        private static bool IsScreenFeedback(MMF_Feedback f)
        {
            string type = f.GetType().Name;
            return type.Contains("Cinemachine") || type.Contains("FieldOfView") || type.EndsWith("_URP") || type.Contains("CameraShake") || type.Contains("Beautify");
        }
    }
}
