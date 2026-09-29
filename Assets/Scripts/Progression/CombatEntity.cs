using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Concrete combat participant (Player or NPC/Monster/Dummy) that can receive and process damage.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatEntity : MonoBehaviour, IDamageable
    {
        [Header("Identity & Tuning (ScriptableObjects)")]
        [SerializeField] private string entityName = "Combat Dummy";
        [SerializeField] private CharacterClassData classData;
        [SerializeField] private CombatStatTuningData tuningData;

        [Header("Health Pool")]
        [SerializeField] private float overrideMaxHealth = 500f;
        [SerializeField] private bool useCombatStatsForMaxHealth = true;

        private CharacterCombatStats combatStats;
        private PlayerResources playerResources;
        private CharacterBuffManager buffManager;
        private float standaloneHealth;
        private bool initialized;

        public event Action<DamageResult, CharacterCombatStats> Damaged;
        public event Action Died;
        public event Action HealthChanged;

        public string EntityName => entityName;
        public Transform Transform => transform;
        public CharacterCombatStats CombatStats => combatStats;

        public float MaxHealth
        {
            get
            {
                if (playerResources != null) return playerResources.MaxHealth;
                if (useCombatStatsForMaxHealth && combatStats != null) return combatStats.GetStatValue(StatType.MaxHP);
                return overrideMaxHealth;
            }
        }

        public float CurrentHealth
        {
            get
            {
                if (!initialized) InitializeEntity();
                if (playerResources != null) return playerResources.Health;
                return standaloneHealth;
            }
        }

        public bool IsAlive => CurrentHealth > 0f;

        private void Awake()
        {
            InitializeEntity();
        }

        public void InitializeEntity()
        {
            if (initialized) return;

            combatStats = GetComponent<CharacterCombatStats>();
            if (combatStats == null)
            {
                combatStats = gameObject.AddComponent<CharacterCombatStats>();
            }

            if (classData != null && combatStats.ClassData == null)
            {
                combatStats.ClassData = classData;
            }
            if (tuningData != null && combatStats.TuningData == null)
            {
                combatStats.TuningData = tuningData;
            }

            playerResources = GetComponent<PlayerResources>();
            if (playerResources != null) playerResources.EnsureInitialized();
            buffManager = GetComponent<CharacterBuffManager>();
            if (playerResources != null)
            {
                playerResources.Changed += OnResourcesChanged;
                playerResources.Depleted += OnResourcesDepleted;
            }
            else
            {
                standaloneHealth = MaxHealth;
            }

            initialized = true;
        }

        private void OnDestroy()
        {
            if (playerResources != null)
            {
                playerResources.Changed -= OnResourcesChanged;
                playerResources.Depleted -= OnResourcesDepleted;
            }
        }

        private void OnResourcesChanged() => HealthChanged?.Invoke();
        private void OnResourcesDepleted() => Died?.Invoke();

        public DamageResult TakeDamage(DamageResult damage, CharacterCombatStats attacker)
        {
            if (!IsAlive) return damage;

            if (damage.IsReflected)
            {
                // Reflected strike: attacker takes the reflected damage (which bypasses Heavenly Shield)
                if (attacker != null)
                {
                    var attackerEntity = attacker.GetComponent<IDamageable>();
                    if (attackerEntity != null)
                    {
                        var reflectResult = new DamageResult
                        {
                            DamageDealt = damage.ReflectedDamage,
                            RawPhysicalDamage = damage.ReflectedDamage,
                            MitigatedPhysicalDamage = damage.ReflectedDamage,
                            BypassesShield = true // Reflected damage bypasses target's Heavenly Shield
                        };
                        attackerEntity.TakeDamage(reflectResult, combatStats);
                    }
                }
                return damage;
            }

            if (damage.IsEvaded)
            {
                Damaged?.Invoke(damage, attacker);
                return damage;
            }

            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();

            // 1. Check if attack shattered defender's shield (Heavenly Destruction proc)
            if (damage.ShieldShattered && buffManager != null)
            {
                buffManager.ShatterShield();
            }

            // 2. Physical damage hits Heavenly Shield first; Elemental damage bypasses shield directly to life
            float unabsorbedPhysical = damage.MitigatedPhysicalDamage;
            if (!damage.BypassesShield && buffManager != null && buffManager.HasActiveShield && unabsorbedPhysical > 0f)
            {
                unabsorbedPhysical = buffManager.AbsorbDamage(unabsorbedPhysical);
            }

            float directElemental = damage.NetElementalDamage;
            float totalDamageToLife = unabsorbedPhysical + directElemental;

            // 3. Any unabsorbed damage reaching life interrupts channeled skills (Heavenly Serenity)
            if (totalDamageToLife > 0f && buffManager != null)
            {
                buffManager.InterruptChanneled();
            }

            if (totalDamageToLife > 0f)
            {
                if (playerResources != null)
                {
                    playerResources.ApplyDamage(totalDamageToLife);
                }
                else
                {
                    standaloneHealth = Mathf.Max(0f, standaloneHealth - totalDamageToLife);
                    HealthChanged?.Invoke();
                    if (standaloneHealth <= 0f)
                    {
                        Died?.Invoke();
                    }
                }
            }

            Damaged?.Invoke(damage, attacker);
            return damage;
        }

        public void ResetHealth()
        {
            if (playerResources != null)
            {
                playerResources.RestoreAll();
            }
            else
            {
                standaloneHealth = MaxHealth;
                HealthChanged?.Invoke();
            }
        }
    }
}
