using System.Collections;
using System.Collections.Generic;
using Animancer;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Progression;
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
    /// on every client. A hit's comic mark and word come from <see cref="HitLookSettings"/>: the shape and the word say
    /// which weapon class struck, the colour says what kind of hit it was, and a critical hit on another player has a
    /// look used for nothing else. Skills get their own cast / hit players and buff auras from <see cref="SkillFeelCatalog"/>;
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
        [Tooltip("The local player lands a critical hit on another player, or takes one: the hit with a look of its own.")]
        [SerializeField] private MMF_Player pvpCritFeel;
        [Tooltip("The local player gained a level.")]
        [SerializeField] private MMF_Player levelUpFeel;
        [Tooltip("The local player took a Rare or better item off the ground.")]
        [SerializeField] private MMF_Player pickupFeel;
        [Tooltip("The local player's own moment at the Herald: a level gained, a new Nova tier, no change, a level lost, " +
                 "a slip the Nova held, a Standing rank bought. Their vignettes take the colour of the moment.")]
        [SerializeField] private MMF_Player novaRiseFeel;
        [SerializeField] private MMF_Player novaTierFeel;
        [SerializeField] private MMF_Player novaHoldFeel;
        [SerializeField] private MMF_Player novaSlipFeel;
        [SerializeField] private MMF_Player novaHeldFeel;
        [SerializeField] private MMF_Player standingFeel;

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
        [Tooltip("Anyone's moment at the Herald. All but the last two burst at the Nova itself, in its tier's colour; a new " +
                 "tier also lights the ground under the character, and a Standing rank is a seal at the feet.")]
        [SerializeField] private MMF_Player novaRiseWorldFeel;
        [SerializeField] private MMF_Player novaTierWorldFeel;
        [SerializeField] private MMF_Player novaHoldWorldFeel;
        [SerializeField] private MMF_Player novaSlipWorldFeel;
        [SerializeField] private MMF_Player novaHeldWorldFeel;
        [SerializeField] private MMF_Player novaTierGroundWorldFeel;
        [SerializeField] private MMF_Player standingWorldFeel;
        [Tooltip("Anyone gained a level: on the character, in its faction's colours.")]
        [SerializeField] private MMF_Player levelUpWorldFeel;
        [Tooltip("Anyone took a drop off the ground: where it lay. One for Common and Uncommon, one for Rare, one for Epic and better.")]
        [SerializeField] private MMF_Player pickupWorldFeel;
        [SerializeField] private MMF_Player rarePickupWorldFeel;
        [SerializeField] private MMF_Player epicPickupWorldFeel;

        [Header("Hit looks: shape and word by weapon class, colour by kind of hit")]
        [SerializeField] private HitLookSettings looks;
        [Tooltip("Seconds between looks for characters that have no hit reactor, effects and blade trail yet: they are given them ahead, so a first hit creates nothing.")]
        [Min(0.5f)] [SerializeField] private float prepareActorsEvery = 2f;

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
        [Tooltip("The local player's own pose holds this long as a new Nova tier lands: the beat before everything bursts.")]
        [Range(0f, 0.3f)] [SerializeField] private float novaTierStopSeconds = 0.14f;

        [Header("Kill confirm (the local player's own kills)")]
        [Tooltip("How much of the kill confirm a normal kill gets; a critical kill gets all of it.")]
        [Range(0f, 1f)] [SerializeField] private float normalKillStrength = 0.55f;
        [Tooltip("How hard the camera is shoved the way the killing blow went (m/s) at full strength.")]
        [Min(0f)] [SerializeField] private float killShove = 0.7f;
        [Tooltip("A critical kill of another player, on the killer's screen only: the thrown body is shown in slow motion for x seconds, then catches up with where it really is over y seconds. 0 = off. Only this screen's picture of the body lags: the game's clock, the body's real place and the network are untouched.")]
        [SerializeField] private Vector2 critKillSlowSeconds = new Vector2(0.12f, 0.26f);
        [Tooltip("How fast that slow motion runs: 0.25 = a quarter of real speed.")]
        [Range(0.05f, 1f)] [SerializeField] private float critKillSlowRate = 0.25f;

        private readonly Dictionary<AnimancerComponent, int> frozen = new Dictionary<AnimancerComponent, int>();
        private MMF_Player[] cameraPlayers;
        private Transform chargingLocalActor;

        /// <summary>A hit of this frame whose word is chosen at the end of it, once its skill (if any) has reported.</summary>
        private struct PendingWord
        {
            public Transform Attacker;
            public Vector3 Point;
            public HitKind Kind;
            public bool Critical, Local;
            public int ComboStep;
            public ClassSkillSlot Slot;
            public float Distance;
        }

        private readonly List<PendingWord> pendingWords = new List<PendingWord>(16);
        private readonly List<float> othersWordsUntil = new List<float>(8); // when each word of someone else's fight is over
        private readonly List<CombatEntity> actorScan = new List<CombatEntity>(64);
        // Effects of the characters themselves (a basic attack's swing and spark) already made ready in the pool.
        private readonly HashSet<GameObject> warmedForActors = new HashSet<GameObject>();
        private Camera viewCamera;
        private float nextActorCheck;
        private Coroutine warming;

        public static GameFeelPresenter Instance { get; private set; }

        /// <summary>
        /// The look of a critical hit on another player on this screen, when it should not be the one the settings
        /// name: for trying the three (gm.fx.pvpcrit). Null = the settings' own. Not saved.
        /// </summary>
        public PvpCritStyle? PvpCritOverride { get; set; }

        /// <summary>True once every pooled effect has been made ready (see <see cref="WarmPool"/>).</summary>
        public bool PoolIsWarm { get; private set; }
        /// <summary>Words left out since the scene started because too many were up or the gap had not passed. For tests and tuning.</summary>
        public int WordsHeldBack { get; private set; }
        public int WordsShown { get; private set; }
        public HitLookSettings Looks => looks;

        private void OnEnable()
        {
            Instance = this;
            PvpCritOverride = null;
            ClientPresentationRegistry.RegisterFeel(this);
            cameraPlayers = new[] { swingFeel, hitFeel, heavyHitFeel, damagedFeel, clickFeel, selectFeel, runFeel, jumpFeel, landFeel,
                attackCastFeel, buffCastFeel, chargeFeel, deathFeel, critDeathFeel, killConfirmFeel, bodyLandFeel, respawnFeel, petEvolveFeel,
                forgeSuccessFeel, forgeFailFeel, forgeBreakFeel,
                novaRiseFeel, novaTierFeel, novaHoldFeel, novaSlipFeel, novaHeldFeel, standingFeel,
                pvpCritFeel, levelUpFeel, pickupFeel };
            if (skills == null) skills = GetComponent<SkillFeelCatalog>();
            GroundSurfaces.Palette = dustPalette;
            HudPreferences.Changed += ApplyScreenShakePreference;
            ApplyScreenShakePreference();
        }

        private void Start()
        {
            RebindShakers();
            // Pre-attach reactors so hits this client never got an RPC for (GM hits, remote attackers) still show.
            PrepareActors();
            warming = StartCoroutine(WarmPool());
        }

        private void Update()
        {
            if (Time.unscaledTime < nextActorCheck) return;
            nextActorCheck = Time.unscaledTime + prepareActorsEvery;
            PrepareActors();
        }

        /// <summary>
        /// Gives every character that can be hit its hit reactor and its effects holder, and every one with a weapon
        /// its blade trail, before a fight needs them: those are created once per character, and this is when.
        /// </summary>
        private void PrepareActors()
        {
            actorScan.Clear();
            actorScan.AddRange(FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude));
            for (int i = 0; i < actorScan.Count; i++)
            {
                CombatEntity entity = actorScan[i];
                if (entity == null) continue;
                HitReactor.For(entity.transform);
                ActorVfx.For(entity.transform)?.PrepareTrail(bladeTrail);
                if (entity.TryGetComponent(out KryticalNova.Combat.PlayerMeleeCombat melee))
                {
                    WarmOnce(melee.HitVfxPrefab, looks != null ? looks.warmSparks : 12);
                    WarmOnce(melee.SlashVfxPrefab, looks != null ? looks.warmOthers : 2);
                }
            }
            actorScan.Clear();
        }

        /// <summary>
        /// Makes copies of an effect a character shows by itself ready in the pool, the first time a character with
        /// it is seen and never again: later the pool only grows when a fight is busier than any before it.
        /// </summary>
        private void WarmOnce(GameObject prefab, int count)
        {
            if (prefab == null || warmedForActors.Contains(prefab)) return;
            NetworkCombatVFXPool pool = NetworkCombatVFXPool.Instance;
            if (pool == null) return;
            warmedForActors.Add(prefab);
            pool.Prewarm(prefab, count);
        }

        /// <summary>
        /// Fills the pool with every effect the game feel can show, a few copies a frame, so that none is created
        /// later in the middle of a fight. How many of each: the Warm of its feedback, and the hit looks' own counts.
        /// </summary>
        private IEnumerator WarmPool()
        {
            PoolIsWarm = false;
            NetworkCombatVFXPool pool = null;
            while ((pool = NetworkCombatVFXPool.Instance) == null) yield return null;

            var wanted = new Dictionary<GameObject, int>();
            void Want(GameObject prefab, int count)
            {
                if (prefab == null) return;
                wanted[prefab] = wanted.TryGetValue(prefab, out int had) ? Mathf.Max(had, count) : count;
            }

            int others = looks != null ? looks.warmOthers : 2;
            foreach (MMF_Player player in GetComponentsInChildren<MMF_Player>(true))
                foreach (MMF_Feedback feedback in player.FeedbacksList)
                    if (feedback is MMF_PooledVfx vfx) Want(vfx.Prefab, Mathf.Max(others, vfx.Warm));
            Want(clickVfx, others);
            Want(selectHostileVfx, others);
            Want(selectFriendlyVfx, others);
            Want(areaRingVfx, others);
            Want(defaultAuraVfx, others);
            Want(defaultChargeVfx, others);
            Want(runLoopVfx, others + 2);
            Want(airborneLoopVfx, others);
            Want(critDeathTrailVfx, others);
            if (skills != null)
                foreach (SkillFeel feel in skills.All)
                {
                    Want(feel.aura, others);
                    Want(feel.charge, others);
                    Want(feel.chargeGround, others);
                }
            if (looks != null)
            {
                void WantLook(WeaponHitLook look)
                {
                    if (look == null) return;
                    Want(look.lightImpact, looks.warmImpacts);
                    Want(look.heavyImpact, looks.warmImpacts);
                    Want(look.heavyWord, looks.warmWords);
                    Want(look.criticalWord, looks.warmWords);
                    Want(look.skillWord, looks.warmWords);
                }
                foreach (WeaponHitLook look in looks.classes) WantLook(look);
                WantLook(looks.unarmed);
                Want(looks.reflectedWord, looks.warmWords);
                Want(looks.shatteredWord, looks.warmWords);
                foreach (PvpCritLook crit in looks.pvpCrit)
                {
                    if (crit == null) continue;
                    foreach (GameObject panel in crit.panels) Want(panel, looks.warmWords);
                    Want(crit.lines, looks.warmWords);
                }
            }

            int perFrame = looks != null ? looks.warmPerFrame : 6;
            int made = 0;
            foreach (KeyValuePair<GameObject, int> each in wanted)
            {
                // One copy at a time, so a frame never makes more than its share.
                while (pool != null && pool.Ready(each.Key) < each.Value)
                {
                    pool.Prewarm(each.Key, pool.Ready(each.Key) + 1);
                    if (++made % perFrame == 0) yield return null;
                }
            }
            PoolIsWarm = true;
            warming = null;
        }

        private void OnDisable()
        {
            if (warming != null) StopCoroutine(warming);
            warming = null;
            pendingWords.Clear();
            if (Instance == this) Instance = null;
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
            // The ribbon is the colour of the weapon class that swings it.
            Color swing = looks != null ? looks.Look(ClassOf(attacker)).swing : default;
            ActorVfx.For(attacker).Trail(bladeTrail, seconds, swing);
            // Every swing whooshes; the finisher also leans the camera in.
            if (isLocalPlayer) Play(swingFeel, attacker.position, comboStep == 3 ? 1f : 0.45f);
        }

        public void Hit(in HitFeel hit)
        {
            // The local player's pet has something to say about a critical hit, and knows its owner is not idle.
            PetChatterDirector pet = PetChatterDirector.Instance;
            if (pet != null && (hit.AttackerIsLocalPlayer || hit.TargetIsLocalPlayer))
            {
                pet.MarkActive();
                if (hit.IsCritical && hit.AttackerIsLocalPlayer) pet.Notify(KryticalNova.Pets.PetChatterTrigger.CriticalHit);
            }
            HitKind kind = HitLooks.KindOf(hit.IsCritical, hit.IsReflected, hit.ShieldShattered, hit.IsElemental);
            bool heavy = HitLooks.IsHeavy(hit.IsCritical, hit.ComboStep, kind);
            bool pvpCrit = looks != null && HitLooks.IsPvpCrit(hit.IsCritical, hit.IsReflected, IsPlayer(hit.Attacker), IsPlayer(hit.Target),
                hit.Attacker == hit.Target);
            Quaternion facing = hit.TowardAttacker.sqrMagnitude > 0.001f ? Quaternion.LookRotation(hit.TowardAttacker, Vector3.up) : Quaternion.identity;
            // The local player's own fight always shows; anyone else's sparks come from the ready pool or not at all.
            bool othersFight = !hit.AttackerIsLocalPlayer && !hit.TargetIsLocalPlayer;
            PlayWorld(heavy ? heavyHitWorldFeel : hitWorldFeel, hit.Point, facing, hit.Target, onlyIfReady: othersFight);
            if (hit.IsCritical && !pvpCrit) PlayWorld(critWorldFeel, hit.Point, facing, hit.Target, onlyIfReady: othersFight);
            bool shownPvpCrit = ShowHitLook(hit, kind, heavy, pvpCrit);

            // Characters with a hit animation of their own keep the squash and flash but not the lean: the clip leans.
            if (hit.Target != null)
                HitReactor.For(hit.Target)?.React(hit.Target.GetComponentInParent<PlayerAnimancerMotor>() != null ? Vector3.zero : hit.TowardAttacker, heavy);

            if (shownPvpCrit && (hit.AttackerIsLocalPlayer || hit.TargetIsLocalPlayer))
            {
                // The hit with a look of its own: its own screen feedback, and both fighters hold their pose.
                Play(pvpCritFeel, hit.Point, hit.AttackerIsLocalPlayer ? 1f : looks.pvpCritVictimStrength);
                HitStop(hit.Attacker, looks.pvpCritStopSeconds);
                HitStop(hit.Target, looks.pvpCritStopSeconds);
            }
            else if (hit.AttackerIsLocalPlayer)
            {
                Play(heavy ? heavyHitFeel : hitFeel, hit.Point, hit.IsCritical ? 1f : 0.75f);
                float stop = heavy ? heavyHitStopSeconds : hitStopSeconds;
                HitStop(hit.Attacker, stop);
                HitStop(hit.Target, stop);
            }
        }

        // ------------------------------------------------------------------ hit looks

        /// <summary>
        /// The comic part of a hit: the attacker's weapon class's mark in the colours of the kind of hit, and (at the
        /// end of the frame, once a skill has said which it was) its word. A critical hit on another player shows
        /// the look kept for that and nothing else; returns true when it did.
        /// </summary>
        private bool ShowHitLook(in HitFeel hit, HitKind kind, bool heavy, bool pvpCrit)
        {
            if (looks == null) return false;
            bool local = hit.AttackerIsLocalPlayer || hit.TargetIsLocalPlayer;
            float distance = DistanceToCamera(hit.Point);
            if (!local && distance > looks.impactDistance) return false;

            WeaponHitLook look = looks.Look(ClassOf(hit.Attacker));
            ComicRamp ramp = looks.Ramp(kind);
            SpawnComic(heavy ? look.heavyImpact : look.lightImpact, hit.Point, ramp, looks.markScale, towardCamera: looks.towardCamera);
            if (pvpCrit) return ShowPvpCrit(hit, local);

            pendingWords.Add(new PendingWord
            {
                Attacker = hit.Attacker, Point = hit.Point, Kind = kind, Critical = hit.IsCritical, Local = local,
                ComboStep = hit.ComboStep, Slot = ClassSkillSlot.None, Distance = distance,
            });
            return false;
        }

        private bool ShowPvpCrit(in HitFeel hit, bool local)
        {
            PvpCritLook crit = PvpCritOverride.HasValue ? looks.PvpCrit(PvpCritOverride.Value) : looks.PvpCrit();
            if (crit == null) return false;
            ActorVfx actor = ActorVfx.For(hit.Attacker);
            if (actor != null)
            {
                if (Time.time - actor.LastPvpCritTime < looks.pvpCritGapSeconds) return false;
                actor.LastPvpCritTime = Time.time;
                actor.LastWordTime = Time.time; // no ordinary word on top of it
                actor.LastWord = HitWord.Critical;
            }
            FactionId faction = FactionOf(hit.Attacker);
            ComicRamp ramp = crit.factionColours ? looks.Ramp(faction) : crit.ramp;
            GameObject panel = null;
            if (crit.panels != null && crit.panels.Length > 0)
                panel = crit.panels.Length >= 3 ? crit.panels[Mathf.Clamp((int)faction - 1, 0, 2)] : crit.panels[0];
            Vector3 at = hit.Point + Vector3.up * looks.wordHeight;
            // The local player's own are created if they must be; anyone else's only when a copy lies ready.
            SpawnComic(panel, at, ramp, crit.scale, mayCreate: local, towardCamera: looks.towardCamera);
            if (crit.lines != null)
                SpawnComic(crit.lines, hit.Point, new ComicRamp(Color.white, crit.linesColour, crit.linesColour, ramp.ink), crit.scale, mayCreate: local);
            WordsShown++;
            return true;
        }

        /// <summary>
        /// Shows the look of a critical hit on another player at a character, with the screen's part of it, without
        /// any hit: for choosing between the looks (gm.fx.pvpcrit). The attacker's faction decides the colours and
        /// the word of the look that speaks in them.
        /// </summary>
        public void PreviewPvpCrit(Transform attacker, Transform target)
        {
            if (looks == null || target == null) return;
            ActorVfx actor = ActorVfx.For(attacker);
            if (actor != null) actor.LastPvpCritTime = float.NegativeInfinity;
            var hit = new HitFeel
            {
                Point = target.position + Vector3.up * 0.2f, Attacker = attacker, Target = target, ComboStep = 3, IsCritical = true,
                AttackerIsLocalPlayer = true,
            };
            if (!ShowPvpCrit(hit, true)) return;
            Play(pvpCritFeel, hit.Point, 1f);
            HitStop(attacker, looks.pvpCritStopSeconds);
            HitStop(target, looks.pvpCritStopSeconds);
        }

        private void LateUpdate()
        {
            if (pendingWords.Count == 0) return;
            float now = Time.time;
            for (int i = othersWordsUntil.Count - 1; i >= 0; i--)
                if (othersWordsUntil[i] <= now) othersWordsUntil.RemoveAt(i);

            for (int i = 0; i < pendingWords.Count; i++)
            {
                PendingWord pending = pendingWords[i];
                GameObject prefab;
                HitWord which;
                if (pending.Kind == HitKind.Reflected) { prefab = looks.reflectedWord; which = HitWord.Critical; }
                else if (pending.Kind == HitKind.Shattered) { prefab = looks.shatteredWord; which = HitWord.Critical; }
                else
                {
                    which = HitLooks.WordFor(pending.Critical, pending.ComboStep, pending.Slot);
                    WeaponHitLook look = looks.Look(ClassOf(pending.Attacker));
                    prefab = which == HitWord.Critical ? look.criticalWord : which == HitWord.Skill ? look.skillWord
                        : which == HitWord.Heavy ? look.heavyWord : null;
                }
                if (prefab == null || which == HitWord.None) continue;

                ActorVfx actor = pending.Attacker != null ? ActorVfx.For(pending.Attacker) : null;
                bool gap = actor == null || HitLooks.GapAllows(which, now, actor.LastWordTime, actor.LastWord, looks.wordGapSeconds);
                bool budget = HitLooks.BudgetAllows(pending.Local, pending.Distance, othersWordsUntil.Count, looks.othersWords, looks.othersWordDistance);
                if (!gap || !budget)
                {
                    WordsHeldBack++;
                    continue;
                }
                Vector2 scatter = Random.insideUnitCircle * looks.wordScatter;
                Vector3 at = pending.Point + Vector3.up * looks.wordHeight + CameraRight() * scatter.x + Vector3.up * (scatter.y * 0.5f);
                if (SpawnComic(prefab, at, looks.Ramp(pending.Kind), looks.wordScale, mayCreate: pending.Local, towardCamera: looks.towardCamera) == null) continue;
                WordsShown++;
                if (actor != null)
                {
                    actor.LastWordTime = now;
                    actor.LastWord = which;
                }
                if (!pending.Local) othersWordsUntil.Add(now + looks.wordSeconds);
            }
            pendingWords.Clear();
        }

        /// <summary>
        /// Shows a comic effect turned to the camera, in a colour ramp. It comes from the pool; when none lies ready
        /// it is left out, unless <paramref name="mayCreate"/> (the local player's own moments are never left out).
        /// It is drawn <paramref name="towardCamera"/> metres nearer the camera than <paramref name="position"/>: an
        /// effect at the very point of a hit is half inside the body that was hit.
        /// </summary>
        private GameObject SpawnComic(GameObject prefab, Vector3 position, in ComicRamp ramp, float scale, bool mayCreate = false,
            float towardCamera = 0f)
        {
            if (prefab == null) return null;
            NetworkCombatVFXPool pool = NetworkCombatVFXPool.Instance;
            if (pool == null) return null;
            Quaternion rotation = Quaternion.identity;
            Camera camera = ViewCamera();
            if (camera != null)
            {
                Vector3 toCamera = camera.transform.position - position;
                float distance = toCamera.magnitude;
                if (distance > 0.03f)
                {
                    rotation = Quaternion.LookRotation(toCamera);
                    // Never past the camera, however close it stands.
                    if (towardCamera > 0f) position += toCamera * (Mathf.Min(towardCamera, distance * 0.5f) / distance);
                }
            }
            GameObject instance = mayCreate ? pool.Spawn(prefab, position, rotation) : pool.TrySpawn(prefab, position, rotation);
            if (instance == null) return null;
            instance.transform.localScale = prefab.transform.localScale * scale;
            if (instance.TryGetComponent(out ComicFx comic)) comic.Apply(ramp);
            return instance;
        }

        private Camera ViewCamera()
        {
            if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
            return viewCamera;
        }

        private float DistanceToCamera(Vector3 point)
        {
            Camera camera = ViewCamera();
            return camera != null ? Vector3.Distance(camera.transform.position, point) : 0f;
        }

        private Vector3 CameraRight()
        {
            Camera camera = ViewCamera();
            return camera != null ? camera.transform.right : Vector3.right;
        }

        /// <summary>The weapon class a character strikes with (the weapon in its hand), or None for one that has none.</summary>
        private static WeaponClassId ClassOf(Transform actor)
        {
            if (actor == null) return WeaponClassId.None;
            var source = actor.GetComponentInParent<PlayerWeaponClass>();
            return source != null ? source.ActiveClass : WeaponClassId.None;
        }

        private static FactionId FactionOf(Transform actor)
        {
            if (actor == null) return FactionId.None;
            var wearer = actor.GetComponentInParent<IGearWearer>();
            return wearer != null ? wearer.Faction : FactionId.None;
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

        public void SkillHit(Transform caster, string skillId, Vector3 point, int classSlot = 0)
        {
            // This frame's hits by this caster were this skill's: their word is chosen with it in mind.
            if (classSlot != 0)
                for (int i = 0; i < pendingWords.Count; i++)
                {
                    PendingWord pending = pendingWords[i];
                    if (pending.Attacker != caster || pending.Slot != ClassSkillSlot.None) continue;
                    pending.Slot = (ClassSkillSlot)classSlot;
                    pendingWords[i] = pending;
                }
            SkillFeel feel = skills != null ? skills.Find(skillId) : null;
            Quaternion facing = Quaternion.identity;
            if (caster != null)
            {
                Vector3 toward = caster.position - point;
                toward.y = 0f;
                if (toward.sqrMagnitude > 0.001f) facing = Quaternion.LookRotation(toward, Vector3.up);
            }
            PlayWorld(feel != null && feel.hit != null ? feel.hit : skillHitWorldFeel, point, facing, null, onlyIfReady: !IsLocalPlayer(caster));
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
            if (death.VictimIsLocalPlayer) PetChatterDirector.Instance?.Notify(KryticalNova.Pets.PetChatterTrigger.OwnerDied);
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
                // The punch of a critical kill on another player: for a blink the body hangs in the air.
                if (death.IsCritical && !death.VictimIsLocalPlayer && critKillSlowSeconds.x > 0f && IsPlayer(death.Target))
                    ActorTimeSlow.For(death.Target)?.Play(critKillSlowSeconds.x, critKillSlowRate, critKillSlowSeconds.y);
            }
        }

        private static bool IsPlayer(Transform actor)
        {
            if (actor == null) return false;
            var net = actor.GetComponentInParent<Unity.Netcode.NetworkObject>();
            return net != null && net.IsPlayerObject;
        }

        /// <summary>The player of this machine (or anyone, in a game with no network).</summary>
        private static bool IsLocalPlayer(Transform actor)
        {
            if (actor == null) return false;
            var net = actor.GetComponentInParent<Unity.Netcode.NetworkObject>();
            return net == null || !net.IsSpawned || (net.IsPlayerObject && net.IsOwner);
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
        /// A moment at the Herald. The bursts happen at the Nova itself, in its tier's colour, for everyone who can
        /// see the character; the screen only answers for the player whose moment it is. A new tier is the big one:
        /// the star bursts, light stands round the character and a ring runs out over the ground.
        /// </summary>
        public void NovaWorked(in NovaFeel moment)
        {
            Transform actor = moment.Actor;
            if (actor == null) return;
            Vector3 feet = ActorAnchor.Feet(actor);
            Vector3 nova = moment.NovaPoint;
            Color colour = Vivid(moment.Colour);
            bool local = moment.IsLocalPlayer;
            if (moment.IsStanding)
            {
                PlayWorld(standingWorldFeel, feet, Facing(actor), actor, 1f, colour);
                if (local) PlayTinted(standingFeel, actor.position, colour);
                return;
            }
            switch (moment.Outcome)
            {
                case 0 when moment.NewTier:
                    PlayWorld(novaTierWorldFeel, nova, Quaternion.identity, actor, 1f, colour);
                    PlayWorld(novaTierGroundWorldFeel, feet, Quaternion.identity, actor, 1f, colour);
                    if (local)
                    {
                        PlayTinted(novaTierFeel, actor.position, colour);
                        HitStop(actor, novaTierStopSeconds);
                    }
                    break;
                case 0: // a level gained
                    PlayWorld(novaRiseWorldFeel, nova, Quaternion.identity, actor, 1f, colour);
                    if (local) PlayTinted(novaRiseFeel, actor.position, colour);
                    break;
                case 3: // a slip the Nova held
                    PlayWorld(novaHeldWorldFeel, nova, Quaternion.identity, actor);
                    if (local) Play(novaHeldFeel, actor.position, 1f);
                    break;
                case 2: // a level lost
                    PlayWorld(novaSlipWorldFeel, nova, Quaternion.identity, actor);
                    if (local) Play(novaSlipFeel, actor.position, 1f);
                    break;
                default: // nothing changed
                    PlayWorld(novaHoldWorldFeel, nova, Quaternion.identity, actor);
                    if (local) Play(novaHoldFeel, actor.position, 1f);
                    break;
            }
        }

        /// <summary>A colour at full strength whatever its brightness was: tints want the hue, not the glow.</summary>
        private static Color Vivid(Color colour)
        {
            float max = colour.maxColorComponent;
            if (max < 0.01f) return Color.white;
            return new Color(colour.r / max, colour.g / max, colour.b / max, 1f);
        }

        /// <summary>Plays a screen player with its vignette pulses in the colour of the moment (each keeps its own strength).</summary>
        private static void PlayTinted(MMF_Player player, Vector3 position, Color colour)
        {
            if (player == null) return;
            var feedbacks = player.FeedbacksList;
            for (int i = 0; i < feedbacks.Count; i++)
                if (feedbacks[i] is MMF_BeautifyVignette vignette)
                    vignette.PulseColor = new Color(colour.r, colour.g, colour.b, vignette.PulseColor.a);
            player.PlayFeedbacks(position, 1f);
        }

        public void LevelUp(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            // Several levels in one go are one moment.
            ActorVfx vfx = ActorVfx.For(actor);
            if (vfx != null)
            {
                if (Time.time - vfx.LastLevelUpTime < 1f) return;
                vfx.LastLevelUpTime = Time.time;
            }
            ComicRamp? ramp = looks != null ? looks.Ramp(FactionOf(actor)) : (ComicRamp?)null;
            PlayWorld(levelUpWorldFeel, ActorAnchor.Feet(actor), Facing(actor), actor, 1f, null, ramp);
            if (isLocalPlayer) Play(levelUpFeel, actor.position, 1f);
        }

        public void PickedUp(Transform taker, Vector3 point, int rarity, bool isLocalPlayer)
        {
            // A puff where the drop lay (a character often stands in front of it); a find worth telling of is told
            // over the head of the one who made it.
            PlayWorld(pickupWorldFeel, point, Quaternion.identity, null);
            if (rarity >= 2 && taker != null)
                PlayWorld(rarity >= 3 ? epicPickupWorldFeel : rarePickupWorldFeel, ActorAnchor.Feet(taker), Quaternion.identity, taker);
            if (isLocalPlayer && rarity >= 2) Play(pickupFeel, point, rarity >= 3 ? 1f : 0.6f);
        }

        public void PetEvolved(Transform pet, Color glow, bool ownerIsLocalPlayer)
        {
            if (pet == null) return;
            // Its word is drawn in the colour of the stage it grew into.
            Color vivid = Vivid(glow);
            var ramp = new ComicRamp(Color.white, vivid, Color.Lerp(vivid, Color.black, 0.45f), looks != null ? looks.normal.ink : Color.black);
            PlayWorld(petEvolveWorldFeel, pet.position, Quaternion.identity, pet, 1f, null, ramp);
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
        /// <param name="tint">The colour of the moment, for the effects that take one (<see cref="MMF_PooledVfx.TintWithMoment"/>).</param>
        /// <param name="ramp">The colour ramp of the moment, for the comic effects that take one (<see cref="MMF_PooledVfx.RampWithMoment"/>).</param>
        /// <param name="onlyIfReady">Its effects are left out when no copy lies ready in the pool, instead of being created (<see cref="MMF_PooledVfx.OnlyIfReady"/>).</param>
        private static void PlayWorld(MMF_Player player, Vector3 position, Quaternion rotation, Transform target, float intensity = 1f,
            Color? tint = null, ComicRamp? ramp = null, bool onlyIfReady = false)
        {
            if (player == null) return;
            var feedbacks = player.FeedbacksList;
            for (int i = 0; i < feedbacks.Count; i++)
            {
                if (feedbacks[i] is MMF_Smear smear)
                {
                    smear.Target = target;
                    continue;
                }
                if (!(feedbacks[i] is MMF_PooledVfx vfx)) continue;
                vfx.Rotation = rotation;
                vfx.Target = target;
                if (tint.HasValue) vfx.MomentTint = tint.Value;
                vfx.HasMomentRamp = ramp.HasValue;
                if (ramp.HasValue) vfx.MomentRamp = ramp.Value;
                vfx.OnlyIfReady = onlyIfReady;
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
