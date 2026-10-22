using System.Collections.Generic;
using KryticalNova.Items.Visuals;
using KryticalNova.Networking;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The lasting effects on one character: buff auras (one per skill, for the buff's whole duration), the build-up of
    /// a held charge, the run and airborne loops, and the weapon trail. Added on demand by
    /// <see cref="GameFeelPresenter"/>, like HitReactor. Offsets are given in metres above the soles (see
    /// <see cref="ActorAnchor"/>). Everything is pooled and purely local; when the character is disabled or despawned
    /// its effects go with it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorVfx : MonoBehaviour
    {
        private const float ExpiryWarningSeconds = 3f;
        private const float ExpiryPulseHz = 3f;
        private const float ExpiryPulseDepth = 0.22f;
        private const float DefaultTrailSeconds = 0.25f;
        private const float ChargeStartScale = 0.5f;
        private const float ChargeFullScale = 1.15f;
        private const float DustCheckSeconds = 0.2f;
        private static readonly Vector3 RunLoopHeight = new Vector3(0f, 0.1f, 0f);
        private static readonly Vector3 AirLoopHeight = new Vector3(0f, 0.9f, 0f);

        private struct Aura
        {
            public GameObject Instance;
            public Vector3 BaseScale;
            public float EndTime;
        }

        private readonly Dictionary<string, Aura> auras = new Dictionary<string, Aura>();
        private readonly List<string> auraIds = new List<string>(); // iteration order without allocating
        private readonly List<Aura> charges = new List<Aura>(2);
        private float chargeStart, chargeSeconds;
        private float rootHeight;
        private GameObject runLoop, airLoop, windLoop, waterLoop;
        private GameObject windPrefab;
        private float windStrength;
        private ParticleSystem[] windSystems;
        private float[] windBaseRates;
        private Color[] windBaseColours;
        private float[] windBaseLives;
        private static readonly Vector3 WindHeight = new Vector3(0f, 0.9f, 0f);
        private ParticleSystem[] runLoopSystems;
        private float nextDustCheck;
        private GameObject runPrefab;
        private bool running, airborne, onWall, onWater;
        // Footsteps on the water: each foot is seen to come down (or, with no humanoid feet, a step every so often).
        private Transform[] waterFeet;
        private readonly bool[] footLifted = new bool[2];
        private float nextTimedStep;
        private Vector3 lastStepRoot;
        private const float FootPlanted = 0.13f;  // metres over the soles: the foot is down
        private const float FootLifted = 0.2f;    // ...and up again, ready for the next step
        private const float TimedStepSeconds = 0.3f;
        /// <summary>A foot of <c>actor</c> struck the water at <c>point</c> (Waterstride): GameFeelPresenter splashes it.</summary>
        public static event System.Action<Transform, Vector3> WaterStep;
        /// <summary>When this character last set off at a run or was ordered on while running (GameFeelPresenter.RunStarted).</summary>
        public float LastRunOrder { get; set; } = -100f;
        /// <summary>When a run's take-off last played on this character (<see cref="RunTakeOffRules"/>).</summary>
        public float LastRunTakeOff { get; set; } = -100f;
        /// <summary>When a take-off last kicked the view (the player's own character only).</summary>
        public float LastRunKick { get; set; } = -100f;
        /// <summary>When this character last stood still: the run that follows starts from a standstill, and kicks the view.</summary>
        public float LastStillTime { get; private set; }
        // Slower than this over the ground (metres a second) is standing still.
        private const float StillSpeed = 0.5f;
        private Vector3 lastPlace;
        private TrailRenderer trail;
        private float trailUntil;
        private Color trailColour = Color.clear;
        private Gradient trailGradient;

        // When this character last showed a word, a critical hit on another player, a level gained: the presenter
        // spaces those out per character.
        [System.NonSerialized] public float LastWordTime = float.NegativeInfinity;
        [System.NonSerialized] public HitWord LastWord;
        [System.NonSerialized] public float LastPvpCritTime = float.NegativeInfinity;
        [System.NonSerialized] public float LastLevelUpTime = float.NegativeInfinity;

        /// <summary>The character's effects, created on first use. Moments reported from a child (the visuals) land on the root.</summary>
        public static ActorVfx For(Transform actor)
        {
            if (actor == null) return null;
            actor = Root(actor);
            return actor.TryGetComponent(out ActorVfx vfx) ? vfx : actor.gameObject.AddComponent<ActorVfx>();
        }

        /// <summary>The character's effects if it has any yet, otherwise null (for moments that only end something).</summary>
        public static ActorVfx Find(Transform actor) =>
            actor != null && Root(actor).TryGetComponent(out ActorVfx vfx) ? vfx : null;

        private static Transform Root(Transform actor)
        {
            var net = actor.GetComponentInParent<NetworkObject>();
            return net != null ? net.transform : actor;
        }

        private void Awake() => rootHeight = ActorAnchor.RootHeight(transform);

        // A character that has just appeared, or come back, has not been seen to move: it counts as standing.
        private void OnEnable()
        {
            lastPlace = transform.position;
            LastStillTime = Time.time;
        }

        private void WatchStanding()
        {
            Vector3 place = transform.position;
            Vector3 moved = place - lastPlace;
            moved.y = 0f;
            lastPlace = place;
            float still = StillSpeed * Time.deltaTime;
            if (moved.sqrMagnitude <= still * still) LastStillTime = Time.time;
        }

        /// <summary>A point given in metres above the soles, in this root's local space.</summary>
        private Vector3 Local(Vector3 aboveSoles) => aboveSoles - Vector3.up * rootHeight;

        // ------------------------------------------------------------------ buff auras

        public void SetAura(string skillId, GameObject prefab, Vector3 aboveSoles, float duration)
        {
            ClearAura(skillId, false);
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || prefab == null) return;
            var instance = pool.SpawnAttached(prefab, transform, Local(aboveSoles), Quaternion.identity);
            if (instance == null) return;
            auras[skillId] = new Aura { Instance = instance, BaseScale = instance.transform.localScale, EndTime = Time.time + duration };
            auraIds.Add(skillId);
        }

        /// <summary>Ends the skill's aura. Returns false when it had none.</summary>
        public bool ClearAura(string skillId, bool graceful = true)
        {
            if (!auras.TryGetValue(skillId, out Aura aura)) return false;
            auras.Remove(skillId);
            auraIds.Remove(skillId);
            if (aura.Instance != null)
            {
                aura.Instance.transform.localScale = aura.BaseScale;
                NetworkCombatVFXPool.Instance?.Release(aura.Instance, graceful);
            }
            return true;
        }

        // ------------------------------------------------------------------ held charge

        /// <summary>Starts a charge that fills over <paramref name="seconds"/>; add its effects with <see cref="AddCharge"/>.</summary>
        public void BeginCharge(float seconds)
        {
            ClearCharge(false);
            chargeStart = Time.time;
            chargeSeconds = Mathf.Max(0.05f, seconds);
        }

        public void AddCharge(GameObject prefab, Vector3 aboveSoles)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || prefab == null) return;
            var instance = pool.SpawnAttached(prefab, transform, Local(aboveSoles), Quaternion.identity);
            if (instance == null) return;
            var charge = new Aura { Instance = instance, BaseScale = instance.transform.localScale };
            instance.transform.localScale = charge.BaseScale * ChargeStartScale;
            charges.Add(charge);
        }

        public void ClearCharge(bool graceful = true)
        {
            var pool = NetworkCombatVFXPool.Instance;
            foreach (Aura charge in charges)
            {
                if (charge.Instance == null) continue;
                charge.Instance.transform.localScale = charge.BaseScale;
                if (pool != null) pool.Release(charge.Instance, graceful);
            }
            charges.Clear();
        }

        // ------------------------------------------------------------------ locomotion loops

        public void SetRunning(bool value, GameObject loopPrefab)
        {
            running = value;
            runPrefab = loopPrefab;
            RefreshRunLoop();
        }

        /// <summary>
        /// Standing on a wall: the wall loop at the feet (a grip glow and puffs left by the distance covered), and no run
        /// dust (the wall loop has its own).
        /// </summary>
        /// <summary>On the water (Waterstride): the wake at the feet, ripples and spray, instead of the run's dust.</summary>
        public void SetOnWater(bool value, GameObject loopPrefab)
        {
            onWater = value;
            waterFeet = null;
            footLifted[0] = footLifted[1] = false;
            lastStepRoot = transform.position;
            SetLoop(ref waterLoop, value && loopPrefab != null, loopPrefab, Vector3.zero);
            RefreshRunLoop();
        }

        public void SetOnWall(bool value, GameObject loopPrefab)
        {
            onWall = value;
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null) return;
            if (!value)
            {
                for (int i = 0; i < wallLoops.Length; i++)
                {
                    if (wallLoops[i] != null) pool.Release(wallLoops[i]);
                    wallLoops[i] = null;
                }
                RefreshRunLoop();
                return;
            }
            if (loopPrefab != null && wallLoops[0] == null)
            {
                // One under each foot (owner, 18 Oct 2026), following the foot as it steps; one at the soles when the
                // body has no humanoid feet to follow.
                Transform[] feet = Feet();
                for (int i = 0; i < wallLoops.Length; i++)
                {
                    if (feet[i] == null && i > 0) break;
                    GameObject loop = pool.SpawnAttached(loopPrefab, transform, Local(Vector3.zero), Quaternion.identity);
                    if (loop == null) continue;
                    wallLoops[i] = loop;
                    if (feet[i] == null) continue;
                    if (!loop.TryGetComponent(out SoleFollow follow)) follow = loop.AddComponent<SoleFollow>();
                    follow.Follow(feet[i], transform);
                }
            }
            RefreshRunLoop();
        }

        private readonly GameObject[] wallLoops = new GameObject[2];

        /// <summary>The left and right foot bones of the body's humanoid rig, or nulls.</summary>
        private Transform[] Feet()
        {
            var feet = new Transform[2];
            foreach (Animator animator in GetComponentsInChildren<Animator>())
            {
                if (animator.avatar == null || !animator.isHuman) continue;
                feet[0] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                feet[1] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if (feet[0] != null) break;
            }
            return feet;
        }

        /// <summary>On the water: a footstep each time a foot comes down onto it (every machine shows its own).</summary>
        private void WatchWaterSteps()
        {
            if (!onWater) return;
            float soles = transform.position.y - rootHeight;
            waterFeet ??= Feet();
            if (waterFeet[0] == null)
            {
                // No feet to watch: a step every so often while the body travels.
                Vector3 moved = transform.position - lastStepRoot;
                moved.y = 0f;
                if (Time.time < nextTimedStep || moved.sqrMagnitude < 0.04f) return;
                nextTimedStep = Time.time + TimedStepSeconds;
                lastStepRoot = transform.position;
                WaterStep?.Invoke(transform, new Vector3(transform.position.x, soles, transform.position.z));
                return;
            }
            for (int i = 0; i < waterFeet.Length; i++)
            {
                Transform foot = waterFeet[i];
                if (foot == null) continue;
                float height = foot.position.y - soles;
                if (footLifted[i] && height < FootPlanted)
                {
                    footLifted[i] = false;
                    WaterStep?.Invoke(transform, new Vector3(foot.position.x, soles, foot.position.z));
                }
                else if (!footLifted[i] && height > FootLifted) footLifted[i] = true;
            }
        }

        public void SetAirborne(bool value, GameObject loopPrefab)
        {
            airborne = value;
            SetLoop(ref airLoop, airborne, loopPrefab, AirLoopHeight);
            RefreshRunLoop(); // no dust while the feet are off the ground
        }

        /// <summary>
        /// The wind lines of a run (owner, 22 Oct 2026): streaks and curls of air left behind the body, as many and as
        /// bright as <paramref name="strength"/> says (0 to 1, from the Run rank). Shown with the run, never on a wall.
        /// </summary>
        public void SetRunWind(GameObject prefab, float strength)
        {
            windPrefab = prefab;
            windStrength = Mathf.Clamp01(strength);
            RefreshRunLoop();
        }

        private void RefreshWind()
        {
            bool had = windLoop != null;
            SetLoop(ref windLoop, running && !onWall && windPrefab != null && windStrength > 0.001f, windPrefab, WindHeight);
            if (windLoop == null) { windSystems = null; return; }
            if (!had || windSystems == null)
            {
                windSystems = windLoop.GetComponentsInChildren<ParticleSystem>(true);
                // The fullest wind is the prefab's own (a pooled copy may still carry an earlier, thinner run's values).
                ParticleSystem[] authored = windPrefab.GetComponentsInChildren<ParticleSystem>(true);
                windBaseRates = new float[windSystems.Length];
                windBaseColours = new Color[windSystems.Length];
                windBaseLives = new float[windSystems.Length];
                for (int i = 0; i < windSystems.Length; i++)
                {
                    ParticleSystem source = i < authored.Length ? authored[i] : windSystems[i];
                    windBaseRates[i] = source.emission.rateOverTimeMultiplier;
                    windBaseColours[i] = source.main.startColor.color;
                    windBaseLives[i] = source.main.startLifetimeMultiplier;
                }
            }
            // More streaks and brighter ones the higher the rank: a rank 1 run barely stirs the air, rank 20 tears it.
            for (int i = 0; i < windSystems.Length; i++)
            {
                var emission = windSystems[i].emission;
                emission.rateOverTimeMultiplier = windBaseRates[i] * Mathf.Lerp(0.15f, 1f, windStrength);
                var main = windSystems[i].main;
                Color colour = windBaseColours[i];
                colour.a *= Mathf.Lerp(0.35f, 1f, windStrength);
                main.startColor = colour;
                main.startLifetimeMultiplier = windBaseLives[i] * Mathf.Lerp(0.55f, 1.25f, windStrength); // longer streaks
            }
        }

        private void RefreshRunLoop()
        {
            RefreshWind();
            bool had = runLoop != null;
            SetLoop(ref runLoop, running && !airborne && !onWall && !onWater, runPrefab, RunLoopHeight); // no dust off water
            if (runLoop == null) runLoopSystems = null;
            else if (!had)
            {
                runLoopSystems = runLoop.GetComponentsInChildren<ParticleSystem>();
                nextDustCheck = 0f; // colour it for the ground it starts on, before the first puff
                TintRunDust();
            }
        }

        /// <summary>The run's puffs take the colour of the ground being crossed; puffs already in the air keep theirs.</summary>
        private void TintRunDust()
        {
            if (runLoopSystems == null || Time.time < nextDustCheck) return;
            nextDustCheck = Time.time + DustCheckSeconds;
            GroundSurfaces.Tint(runLoopSystems, GroundSurfaces.DustAt(ActorAnchor.Feet(transform), transform));
        }

        private void SetLoop(ref GameObject loop, bool on, GameObject prefab, Vector3 aboveSoles)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (on == (loop != null) || pool == null) return;
            if (on)
            {
                if (prefab != null) loop = pool.SpawnAttached(prefab, transform, Local(aboveSoles), Quaternion.identity);
            }
            else
            {
                pool.Release(loop);
                loop = null;
            }
        }

        // ------------------------------------------------------------------ weapon trail

        /// <summary>
        /// Makes the blade's trail now if the character has a weapon mount and no trail yet, so that its first swing
        /// creates nothing. Returns true when there is a trail.
        /// </summary>
        public bool PrepareTrail(TrailRenderer prefab)
        {
            if (prefab == null) return false;
            var mount = GetComponentInChildren<WeaponVisualMount>();
            if (mount == null) return false;
            EnsureTrail(prefab, mount);
            return true;
        }

        private void EnsureTrail(TrailRenderer prefab, WeaponVisualMount mount)
        {
            if (trail != null && trail.transform.parent == mount.transform) return;
            if (trail != null) Destroy(trail.gameObject);
            trail = Instantiate(prefab, mount.transform, false);
            trail.name = prefab.name;
            trail.gameObject.layer = mount.gameObject.layer;
            trail.emitting = false;
            trailColour = Color.clear; // the new ribbon wears the prefab's colours until it is told otherwise
        }

        /// <summary>
        /// Draws the blade's trail for <paramref name="seconds"/> (the cutting part of a swing), in the colour of
        /// the weapon class that swings it when one is given (alpha 0 = the trail's own colours).
        /// </summary>
        public void Trail(TrailRenderer prefab, float seconds, Color colour = default)
        {
            if (prefab == null) return;
            var mount = GetComponentInChildren<WeaponVisualMount>();
            if (mount == null) return;
            EnsureTrail(prefab, mount);

            if (colour.a > 0f && colour != trailColour)
            {
                // White at the blade, the class's colour along the ribbon, fading out: the prefab's own shape of fade.
                trailColour = colour;
                trailGradient ??= new Gradient();
                trailGradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(colour, 0.35f), new GradientColorKey(colour, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.55f, 0.4f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = trailGradient;
            }

            // The blade runs along the mount's +Z; ride the outer half so the ribbon follows the tip's arc.
            if (mount.TryGetComponent(out BoxCollider blade))
            {
                trail.transform.localPosition = blade.center + Vector3.forward * (blade.size.z * 0.2f);
                trail.widthMultiplier = Mathf.Max(0.3f, blade.size.z * 0.6f);
            }
            if (!trail.emitting) trail.Clear();
            trail.emitting = true;
            trailUntil = Time.time + (seconds > 0f ? seconds : DefaultTrailSeconds);
        }

        // ------------------------------------------------------------------ lifecycle

        private void Update()
        {
            if (trail != null && trail.emitting && Time.time >= trailUntil) trail.emitting = false;
            WatchStanding();
            TintRunDust();
            WatchWaterSteps();

            if (charges.Count > 0)
            {
                float fill = Mathf.Clamp01((Time.time - chargeStart) / chargeSeconds);
                float scale = Mathf.Lerp(ChargeStartScale, ChargeFullScale, fill);
                for (int i = 0; i < charges.Count; i++)
                    if (charges[i].Instance != null) charges[i].Instance.transform.localScale = charges[i].BaseScale * scale;
            }

            // A buff about to run out breathes faster and smaller, so its end never comes as a surprise.
            for (int i = 0; i < auraIds.Count; i++)
            {
                Aura aura = auras[auraIds[i]];
                if (aura.Instance == null) continue;
                float left = aura.EndTime - Time.time;
                float pulse = left < ExpiryWarningSeconds
                    ? 1f - ExpiryPulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * ExpiryPulseHz * Mathf.PI * 2f))
                    : 1f;
                aura.Instance.transform.localScale = aura.BaseScale * pulse;
            }
        }

        // The character is being deactivated or destroyed: its children cannot be re-parented right now, so the
        // effects are only stopped here and the pool collects whatever survives on its next Update.
        private void OnDisable()
        {
            var pool = NetworkCombatVFXPool.Instance;
            foreach (var aura in auras.Values) Drop(pool, aura);
            auras.Clear();
            auraIds.Clear();
            foreach (Aura charge in charges) Drop(pool, charge);
            charges.Clear();
            if (pool != null)
            {
                pool.ReleaseDeferred(runLoop);
                pool.ReleaseDeferred(airLoop);
                pool.ReleaseDeferred(windLoop);
                pool.ReleaseDeferred(waterLoop);
                foreach (GameObject loop in wallLoops) pool.ReleaseDeferred(loop);
            }
            runLoop = airLoop = windLoop = waterLoop = null;
            windSystems = null;
            wallLoops[0] = wallLoops[1] = null;
            runLoopSystems = null;
            running = airborne = onWall = onWater = false;
            if (trail != null) trail.emitting = false;
        }

        private static void Drop(NetworkCombatVFXPool pool, Aura effect)
        {
            if (effect.Instance == null) return;
            effect.Instance.transform.localScale = effect.BaseScale;
            if (pool != null) pool.ReleaseDeferred(effect.Instance);
        }
    }
}
