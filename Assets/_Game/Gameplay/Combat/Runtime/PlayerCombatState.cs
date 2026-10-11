using System;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Whether this player is in a fight, for everything that behaves differently in one (a pet keeps to its idle
    /// and its gait). The server decides and every machine is told: a player is in combat from the moment they take
    /// or deal a blow, cast an attack skill, set upon a target or stand in a duel, until a few seconds have gone by
    /// without any of it. Offline (the unspawned scene avatar) the local copy decides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatState : NetworkBehaviour
    {
        // How often the server looks again: the end of a fight need not be told to the frame.
        private const float CheckSeconds = 0.25f;

        [Tooltip("Seconds without a blow given or taken, or an attack skill cast, before the fight counts as over.")]
        [SerializeField, Min(0.5f)] private float lingerSeconds = 6f;

        private readonly NetworkVariable<bool> netInCombat = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CombatEntity entity;
        private PlayerMeleeCombat melee;
        private PlayerCombatSkills skills;
        private PlayerDuel duel;
        private bool offlineInCombat; // the unspawned avatar keeps its own
        private float lastAct = float.NegativeInfinity;
        private float nextCheck;

        /// <summary>True while this player is in a fight. The same on every machine.</summary>
        public bool InCombat => IsSpawned ? netInCombat.Value : offlineInCombat;

        /// <summary>Raised on every machine when the player enters or leaves a fight.</summary>
        public event Action<bool> Changed;

        private bool Decides => !IsSpawned || IsServer;

        /// <summary>
        /// The rule itself: in a fight while attacking or duelling, and for <paramref name="linger"/> seconds after
        /// the last act of one.
        /// </summary>
        public static bool StillFighting(float now, float lastAct, float linger, bool attacking, bool duelling) =>
            attacking || duelling || now - lastAct < linger;

        private void Awake()
        {
            entity = GetComponent<CombatEntity>();
            melee = GetComponent<PlayerMeleeCombat>();
            skills = GetComponent<PlayerCombatSkills>();
            duel = GetComponent<PlayerDuel>();
        }

        private void OnEnable()
        {
            if (entity != null) entity.Damaged += OnDamaged;
            if (melee != null) melee.HitResolved += OnHitResolved;
            if (skills != null) skills.SkillCast += OnSkillCast;
        }

        private void OnDisable()
        {
            if (entity != null) entity.Damaged -= OnDamaged;
            if (melee != null) melee.HitResolved -= OnHitResolved;
            if (skills != null) skills.SkillCast -= OnSkillCast;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netInCombat.OnValueChanged += OnNetChanged;
            // The offline avatar's fight does not come along into a session.
            if (offlineInCombat)
            {
                offlineInCombat = false;
                if (!netInCombat.Value) Changed?.Invoke(false);
            }
            else if (netInCombat.Value) Changed?.Invoke(true); // joined while this player was fighting
            lastAct = float.NegativeInfinity;
        }

        public override void OnNetworkDespawn()
        {
            netInCombat.OnValueChanged -= OnNetChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetChanged(bool previous, bool current) => Changed?.Invoke(current);

        private void OnDamaged(DamageResult result, CharacterCombatStats attacker) => Note();
        private void OnHitResolved(IDamageable target, DamageResult result) => Note();
        private void OnSkillCast(SkillDefinitionSO skill) { if (skill != null && skill.IsAttack) Note(); }

        /// <summary>An act of a fight just happened: the player is in one from now.</summary>
        private void Note()
        {
            if (!Decides) return;
            lastAct = Time.time;
            Set(true);
        }

        private void Update()
        {
            if (!Decides || Time.time < nextCheck) return;
            nextCheck = Time.time + CheckSeconds;
            Set(StillFighting(Time.time, lastAct, lingerSeconds, melee != null && melee.IsAttacking, duel != null && duel.IsDuelling));
        }

        private void Set(bool fighting)
        {
            if (IsSpawned)
            {
                if (netInCombat.Value != fighting) netInCombat.Value = fighting; // OnValueChanged tells everyone, the server too
                return;
            }
            if (offlineInCombat == fighting) return;
            offlineInCombat = fighting;
            Changed?.Invoke(fighting);
        }
    }
}
