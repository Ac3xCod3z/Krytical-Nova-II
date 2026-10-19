using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Combat
{
    /// <summary>What has a player in a fight. Several can hold at once.</summary>
    [Flags]
    public enum CombatReasons : byte
    {
        None = 0,
        /// <summary>A blow given or taken, or an attack skill cast, a few seconds ago at most.</summary>
        Blow = 1,
        /// <summary>Set upon a target: the basic combo is running.</summary>
        Attack = 2,
        /// <summary>Standing in a Callout.</summary>
        Callout = 4,
        /// <summary>A Hostile is after this player, or was a few seconds ago.</summary>
        Hostile = 8,
        /// <summary>On ground that is always a fight (a war, The Scar), or held by a GM.</summary>
        Held = 16,
    }

    /// <summary>
    /// Whether this player is in a fight, for everything that behaves differently in one (a pet keeps to its idle
    /// and its gait; Cadence and its music run only in one). The server decides and every machine is told: a player
    /// is in combat from the moment they take or deal a blow, cast an attack skill, set upon a target, stand in a
    /// duel, have a Hostile after them or stand on ground that is always a fight, until a few seconds have gone by
    /// without any of it. Offline (the unspawned scene avatar) the local copy decides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatState : NetworkBehaviour
    {
        // How often the server looks again: the end of a fight need not be told to the frame.
        private const float CheckSeconds = 0.25f;

        [Tooltip("Seconds without a blow given or taken, an attack skill cast or a Hostile after the player, before the fight counts as over.")]
        [SerializeField, Min(0.5f)] private float lingerSeconds = 6f;

        private readonly NetworkVariable<bool> netInCombat = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CombatEntity entity;
        private PlayerMeleeCombat melee;
        private PlayerCombatSkills skills;
        private PlayerDuel duel;
        private bool offlineInCombat; // the unspawned avatar keeps its own
        private float lastAct = float.NegativeInfinity;
        private float lastThreat = float.NegativeInfinity;
        private readonly HashSet<object> holders = new HashSet<object>();
        private float nextCheck;

        /// <summary>True while this player is in a fight. The same on every machine.</summary>
        public bool InCombat => IsSpawned ? netInCombat.Value : offlineInCombat;

        /// <summary>Why, where the fight is decided (server or offline). None on a client, which is only told that.</summary>
        public CombatReasons Reasons { get; private set; }

        /// <summary>Raised on every machine when the player enters or leaves a fight.</summary>
        public event Action<bool> Changed;

        private bool Decides => !IsSpawned || IsServer;

        /// <summary>
        /// The rule itself: in a fight while attacking, duelling or held, and for <paramref name="linger"/> seconds
        /// after the last blow or the last moment a Hostile was after the player.
        /// </summary>
        public static CombatReasons Why(float now, float lastAct, float lastThreat, float linger, bool attacking, bool duelling, bool held) =>
            (now - lastAct < linger ? CombatReasons.Blow : CombatReasons.None)
            | (attacking ? CombatReasons.Attack : CombatReasons.None)
            | (duelling ? CombatReasons.Callout : CombatReasons.None)
            | (now - lastThreat < linger ? CombatReasons.Hostile : CombatReasons.None)
            | (held ? CombatReasons.Held : CombatReasons.None);

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
            lastThreat = float.NegativeInfinity;
            holders.Clear();
            Reasons = CombatReasons.None;
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
            Look();
        }

        /// <summary>
        /// Server or offline: something is after this player right now (a Hostile that has turned on them). Called
        /// for as long as it lasts; the fight is over a few seconds after the last call.
        /// </summary>
        public void ServerNoteThreat()
        {
            if (!Decides) return;
            lastThreat = Time.time;
            Look();
        }

        /// <summary>
        /// Server or offline: keeps this player in a fight for as long as <paramref name="by"/> holds them, whatever
        /// they do. For ground that is always a fight (a war, The Scar); each holder lets go for itself.
        /// </summary>
        public void ServerSetHeld(object by, bool held)
        {
            if (!Decides || by == null) return;
            if (held ? holders.Add(by) : holders.Remove(by)) Look();
        }

        private void Update()
        {
            if (!Decides || Time.time < nextCheck) return;
            Look();
        }

        private void Look()
        {
            nextCheck = Time.time + CheckSeconds;
            Reasons = Why(Time.time, lastAct, lastThreat, lingerSeconds, melee != null && melee.IsAttacking,
                duel != null && duel.IsDuelling, holders.Count > 0);
            Set(Reasons != CombatReasons.None);
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
