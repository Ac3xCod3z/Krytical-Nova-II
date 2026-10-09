using EmeraldAI;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Hostiles.Emerald
{
    /// <summary>
    /// Joins a Hostile's CombatEntity to its Emerald AI brain. The CombatEntity is the one truth for health:
    /// every hit is decided by the game's own combat rules, then shown to Emerald so that it flinches, turns on
    /// its attacker and dies when the game says so. Emerald's own damage numbers are never used. The Emerald
    /// parts are switched off in the prefab and only switched on here, where the game is decided.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatEntity))]
    public sealed class HostileBrainLink : MonoBehaviour, IHostileBrain
    {
        private static readonly int HitIndexParam = Animator.StringToHash("Hit Index");

        private EmeraldSystem system;
        private CombatEntity entity;
        private HostileNet net;
        private NavMeshAgent agent;
        private Animator animator;
        private Behaviour[] parts;
        private HostileDefinition definition;
        private bool awake;
        private bool frozen;
        private bool listening;
        private int wokeFrame = -1;

        private void Awake()
        {
            system = GetComponent<EmeraldSystem>();
            entity = GetComponent<CombatEntity>();
            net = GetComponent<HostileNet>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            parts = new Behaviour[]
            {
                system, GetComponent<EmeraldAnimation>(), GetComponent<EmeraldBehaviors>(), GetComponent<EmeraldCombat>(),
                GetComponent<EmeraldDetection>(), GetComponent<EmeraldHealth>(), GetComponent<EmeraldMovement>(),
                GetComponent<EmeraldSounds>()
            };
        }

        private void OnDestroy() => Listen(false);

        // Emerald keeps one list of targets no AI may pick; the game's own list of ignored players is copied into it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void FollowIgnoredPlayers()
        {
            EmeraldDetection.IgnoredTargetsList.Clear();
            HostileIgnore.Changed += (player, ignored) =>
            {
                if (ignored) { if (!EmeraldDetection.IgnoredTargetsList.Contains(player)) EmeraldDetection.IgnoredTargetsList.Add(player); }
                else EmeraldDetection.IgnoredTargetsList.Remove(player);
            };
        }

        // ------------------------------------------------------------------ IHostileBrain

        public void SetAwake(bool value)
        {
            if (system == null) return;
            if (value && !awake) wokeFrame = Time.frameCount;
            awake = value;
            // The agent first: Emerald's parts look for it on the NavMesh as they start.
            if (agent != null) agent.enabled = value;
            foreach (Behaviour part in parts)
                if (part != null) part.enabled = value && !(frozen && part == system);
            // Emerald switches the collider off when its AI dies and leaves the Animator off after the death clip.
            if (value)
            {
                if (animator != null) animator.enabled = true;
                if (TryGetComponent(out BoxCollider body)) body.enabled = true;
            }
            Listen(value);
        }

        public void SetHome(Vector3 home, int wanderRadius)
        {
            if (system == null) return;
            EmeraldMovement movement = GetComponent<EmeraldMovement>();
            movement.StartingDestination = home; // Emerald only reads its own position once, when it first starts
            movement.WanderRadius = Mathf.Max(1, wanderRadius);
        }

        public void Configure(HostileDefinition species, in HostileStats stats)
        {
            if (system == null || species == null) return;
            definition = species;

            EmeraldHealth health = GetComponent<EmeraldHealth>();
            health.StartingHealth = Mathf.Max(1, Mathf.CeilToInt(stats.MaxHealth));
            health.CurrentHealth = health.StartingHealth;
            health.HealRate = 0; // health is the CombatEntity's; Emerald never heals on its own
            health.Immortal = false;

            EmeraldMovement movement = GetComponent<EmeraldMovement>();
            movement.WalkSpeed = stats.WalkSpeed;
            movement.RunSpeed = stats.RunSpeed;

            EmeraldCombat combat = GetComponent<EmeraldCombat>();
            combat.AttackDistance = species.attackRange;

            EmeraldDetection detection = GetComponent<EmeraldDetection>();
            int radius = Mathf.Max(1, Mathf.CeilToInt(stats.AggroRadius));
            detection.DetectionRadius = radius;
            detection.StartingDetectionRadius = radius;
            // A species that only fights back holds players as Neutral: Emerald never picks a Neutral target by
            // itself, and turns on one that hits it.
            RelationTypes toPlayers = species.behaviour == HostileBehaviour.AttacksOnSight ? RelationTypes.Enemy : RelationTypes.Neutral;
            detection.FactionRelationsList.Clear();
            detection.FactionRelationsList.Add(new FactionClass(KNEmeraldPlayerBridge.PlayerFaction, (int)toPlayers));
            detection.AIFactionsList.Clear();
            detection.SetupFactions();
        }

        public bool Frozen
        {
            get => frozen;
            set
            {
                frozen = value;
                if (!awake || system == null) return;
                system.enabled = !value; // every Emerald part is run from the system's Update
                if (agent != null && agent.isOnNavMesh) agent.isStopped = value;
            }
        }

        public Transform Target => system != null ? system.CombatTarget : null;

        public void Aggro(Transform target)
        {
            if (awake && target != null && entity.IsAlive) system.DetectionComponent.SetDetectedTarget(target);
        }

        public void Calm()
        {
            if (awake && system.CombatTarget != null) system.CombatComponent.ClearTarget();
        }

        public string Describe()
        {
            if (system == null) return "no brain";
            if (!awake) return "asleep";
            if (!entity.IsAlive) return "dead";
            string state = frozen ? "frozen" : system.CombatComponent.CombatState ? "in combat" : "wandering";
            Transform target = system.CombatTarget;
            return target != null ? $"{state}, target {target.name}" : state;
        }

        // ------------------------------------------------------------------ the game tells Emerald

        private void Listen(bool on)
        {
            if (on == listening || entity == null) return;
            listening = on;
            if (on)
            {
                entity.Damaged += OnDamaged;
                entity.Killed += OnKilled;
                system.AnimationComponent.OnStartAttackAnimation += OnAttackStarted;
                system.AnimationComponent.OnGetHit += OnFlinched;
            }
            else
            {
                entity.Damaged -= OnDamaged;
                entity.Killed -= OnKilled;
                if (system != null && system.AnimationComponent != null)
                {
                    system.AnimationComponent.OnStartAttackAnimation -= OnAttackStarted;
                    system.AnimationComponent.OnGetHit -= OnFlinched;
                }
            }
        }

        /// <summary>
        /// Emerald's parts find each other in their Start, the frame after they are switched on. Until then they
        /// cannot be told of anything; the CombatEntity holds the truth meanwhile.
        /// </summary>
        private bool Started => Time.frameCount > wokeFrame;

        private void OnDamaged(DamageResult result, CharacterCombatStats attacker)
        {
            // An evaded swing did nothing; a killing blow is OnKilled's (Killed is raised first).
            if (!awake || !Started || result.IsEvaded || result.IsReflected || !entity.IsAlive) return;
            int amount = Mathf.RoundToInt(result.DamageDealt);
            if (amount <= 0) return;

            // Emerald asks an attacker for its faction, so it is only told of attackers that have one.
            Transform from = attacker != null && attacker.GetComponent<IFaction>() != null ? attacker.transform : null;
            // Left to itself Emerald waits 0.6 s before it turns on an unseen attacker.
            if (from != null && system.CombatTarget == null) system.DetectionComponent.SetDetectedTarget(from);

            EmeraldHealth health = system.HealthComponent;
            int max = Mathf.Max(1, Mathf.CeilToInt(entity.MaxHealth));
            int now = Mathf.Max(1, Mathf.CeilToInt(entity.CurrentHealth));
            // Emerald takes the hit from a pool that leaves it exactly where the CombatEntity is, so it reacts
            // to the hit and can never die of it. Whatever it made of the hit, the truth is written back.
            health.UpdateHealth(max, now + amount);
            health.Damage(amount, from, 0, result.IsCritical);
            health.UpdateHealth(max, now);
        }

        private void OnKilled(KillingBlow blow)
        {
            if (!awake) return;
            if (Started) EmeraldAPI.Combat.KillAI(system);
            else StartCoroutine(DieOnceStarted()); // killed in the frame it appeared
        }

        private System.Collections.IEnumerator DieOnceStarted()
        {
            yield return null;
            if (awake && !entity.IsAlive) EmeraldAPI.Combat.KillAI(system);
        }

        // ------------------------------------------------------------------ Emerald tells the game

        private void OnAttackStarted()
        {
            if (net != null) net.ReportAttack(system.CombatComponent.CurrentAnimationIndex);
        }

        private void OnFlinched()
        {
            if (net != null && animator != null) net.ReportHit(animator.GetInteger(HitIndexParam));
        }

        /// <summary>
        /// Emerald's attack reached a player: the hit is rolled by the game's combat rules with this Hostile's
        /// stats and the multiplier of the attack it is playing.
        /// </summary>
        public void Strike(CombatEntity target)
        {
            if (!awake || target == null || !target.IsAlive || !entity.IsAlive) return;
            CharacterCombatStats mine = entity.CombatStats;
            if (mine == null || target.CombatStats == null) return;

            float multiplier = 1f;
            int index = system.CombatComponent.CurrentAnimationIndex;
            if (definition != null && index >= 0 && index < definition.attacks.Length)
                multiplier = Mathf.Max(0.1f, definition.attacks[index].damageMultiplier);

            float reflectChance = 0f;
            var defenderBuffs = target.GetComponent<CharacterBuffManager>();
            bool reflects = defenderBuffs != null && defenderBuffs.HasReflection(out reflectChance, out _);

            DamageResult result = CombatCalculator.CalculateAttack(mine, target.CombatStats, multiplier, reflects, reflectChance);
            target.TakeDamage(result, mine);
            if (net != null) net.ReportStrike(target, result, index);
        }
    }
}
