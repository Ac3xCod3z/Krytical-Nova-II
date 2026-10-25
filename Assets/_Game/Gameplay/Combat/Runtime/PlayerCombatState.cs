using System;
using System.Collections.Generic;
using KryticalNova.Player;
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
        /// <summary>
        /// Working a Strike Dummy: from the first blow on it until it is no longer selected and the player is 30 m from
        /// it (owner, 24 Oct 2026: the fight does not end because a timer ran out).
        /// </summary>
        Dummy = 32,
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
        [Tooltip("After striking a Strike Dummy the fight holds until the dummy is no longer selected and the player is at least this far from it (m).")]
        [SerializeField, Min(1f)] private float dummyLeaveMetres = 30f;

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
        // The Strike Dummy being worked (where the fight is decided), and what the player has selected: on the owner's
        // machine it is read from the body, and the server is told its NetworkObject id (0: nothing) when it changes.
        private PointAndClickPlayer body;
        private CombatEntity dummy;
        private ulong selectedId;
        private Transform sentSelection;
        private bool selectionSent;

        /// <summary>True while this player is in a fight. The same on every machine.</summary>
        public bool InCombat => IsSpawned ? netInCombat.Value : offlineInCombat;

        /// <summary>True while this player stands in a Callout. The same on every machine.</summary>
        public bool InCallout => duel != null && duel.IsDuelling;

        /// <summary>
        /// In a fight that counts for the fight music and Cadence: any fight, or where the zone keeps them for Callouts
        /// (<paramref name="calloutsOnly"/>), only a Callout.
        /// </summary>
        public bool InFightFor(bool calloutsOnly) => InCombat && (!calloutsOnly || InCallout);

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
            body = GetComponent<PointAndClickPlayer>();
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
            dummy = null;
            selectedId = 0;
            selectionSent = false;
        }

        public override void OnNetworkDespawn()
        {
            netInCombat.OnValueChanged -= OnNetChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetChanged(bool previous, bool current) => Changed?.Invoke(current);

        private void OnDamaged(DamageResult result, CharacterCombatStats attacker) => Note();
        private void OnHitResolved(IDamageable target, DamageResult result)
        {
            if (Decides && target is CombatEntity struck && struck.IsStrikeDummy) dummy = struck;
            Note();
        }
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
            if (IsSpawned && IsOwner && !IsServer) SendSelection();
            if (!Decides || Time.time < nextCheck) return;
            Look();
        }

        private void Look()
        {
            nextCheck = Time.time + CheckSeconds;
            Reasons = Why(Time.time, lastAct, lastThreat, lingerSeconds, melee != null && melee.IsAttacking,
                duel != null && duel.IsDuelling, holders.Count > 0)
                | (WorkingDummy() ? CombatReasons.Dummy : CombatReasons.None);
            Set(Reasons != CombatReasons.None);
        }

        /// <summary>
        /// The Strike Dummy rule (owner, 24 Oct 2026): the fight with a dummy goes on while it is still selected or the
        /// player is within <paramref name="leaveMetres"/> of it; it ends only when both have changed.
        /// </summary>
        public static bool DummyHolds(bool selected, float distance, float leaveMetres) => selected || distance < leaveMetres;

        private bool WorkingDummy()
        {
            if (dummy == null) return false;
            float distance = Vector3.Distance(transform.position, dummy.transform.position);
            if (DummyHolds(DummySelected(), distance, dummyLeaveMetres)) return true;
            dummy = null;
            return false;
        }

        private bool DummySelected()
        {
            // The owner's own machine (offline, or the host's player) reads the body; the server is told by the owner.
            if (!IsSpawned || IsOwner)
            {
                Transform selected = body != null ? body.SelectedTarget : null;
                return selected != null && selected.GetComponentInParent<CombatEntity>() == dummy;
            }
            var net = dummy.GetComponentInParent<NetworkObject>();
            return net != null && selectedId != 0 && net.NetworkObjectId == selectedId;
        }

        // Owner (a client): tells the server what is selected when it changes.
        private void SendSelection()
        {
            Transform selected = body != null ? body.SelectedTarget : null;
            if (selectionSent && selected == sentSelection) return;
            selectionSent = true;
            sentSelection = selected;
            NetworkObject net = selected != null ? selected.GetComponentInParent<NetworkObject>() : null;
            SelectionRpc(net != null ? net.NetworkObjectId : 0);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SelectionRpc(ulong networkObjectId) => selectedId = networkObjectId;

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
