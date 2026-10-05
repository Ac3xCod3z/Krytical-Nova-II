using System;
using System.Collections;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Duels: the only way two players who are not enemies (the same faction) can fight. One asks, the other
    /// accepts, and after a short countdown the two are hostile to each other and to nobody else. The duel is to the
    /// death: the loser dies like in any other fight and respawns. It also ends when one of them forfeits, walks too
    /// far from where it started or leaves the game (the other wins), or is killed by someone else (no winner).
    /// The server decides all of it; the opponent is replicated so every machine colours targets the same way.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDuel : NetworkBehaviour
    {
        private const ulong NoOpponent = ulong.MaxValue;

        [Tooltip("How close the two players must be for a duel request, in metres.")]
        [SerializeField, Min(1f)] private float requestRange = 20f;
        [Tooltip("Seconds a request waits for an answer.")]
        [SerializeField, Min(5f)] private float requestSeconds = 30f;
        [Tooltip("Seconds counted down once the request is accepted.")]
        [SerializeField, Min(0)] private int countdownSeconds = 3;
        [Tooltip("A duelist who goes this far (m) from where the duel started forfeits.")]
        [SerializeField, Min(10f)] private float leashRadius = 60f;

        private readonly NetworkVariable<ulong> netOpponent = new NetworkVariable<ulong>(NoOpponent,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerIdentity identity;
        private CombatEntity combatEntity;

        // Server only.
        private PlayerDuel challenger;      // who asked this player, until answered or expired
        private float challengeExpires;
        private PlayerDuel startingWith;    // accepted: the countdown is running
        private PlayerDuel opponent;        // fighting
        private Vector3 origin;
        private bool keepsWatch;            // one of the two checks the leash for both
        private Coroutine countdown;

        /// <summary>Raised on every peer when this player starts or stops duelling.</summary>
        public event Action Changed;
        /// <summary>Server only: a notice for this player (shown in their chat).</summary>
        public event Action<string> Notice;
        /// <summary>Server only: news for the players around this one (a duel was won).</summary>
        public event Action<string> Announcement;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerDuel Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerDuel>() : null;

        public string DuelistName => identity != null ? identity.CharacterName : name;
        public bool IsDuelling => IsSpawned && netOpponent.Value != NoOpponent;
        private bool Busy => opponent != null || startingWith != null;
        private bool Alive => combatEntity == null || combatEntity.IsAlive;

        /// <summary>True on every peer while this player and <paramref name="other"/> are duelling each other.</summary>
        public bool IsDuellingWith(NetworkObject other) =>
            other != null && other.IsSpawned && IsSpawned && netOpponent.Value == other.NetworkObjectId;

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            combatEntity = GetComponent<CombatEntity>();
        }

        private void OnEnable()
        {
            if (combatEntity != null) combatEntity.Killed += OnKilled;
        }

        private void OnDisable()
        {
            if (combatEntity != null) combatEntity.Killed -= OnKilled;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netOpponent.OnValueChanged += OnOpponentChanged;
        }

        public override void OnNetworkDespawn()
        {
            netOpponent.OnValueChanged -= OnOpponentChanged;
            if (IsServer)
            {
                // Leaving the game mid-duel hands the win to the one who stayed.
                if (opponent != null) Finish(opponent, this, $"{DuelistName} left the game.");
                else if (startingWith != null) CancelStart("The callout was called off.");
            }
            base.OnNetworkDespawn();
        }

        private void OnOpponentChanged(ulong previous, ulong current) => Changed?.Invoke();

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Ask <paramref name="target"/> for a duel. Call on the machine that owns this player.</summary>
        public void Request(PlayerDuel target)
        {
            if (!IsSpawned || !IsOwner || target == null || !target.IsSpawned) return;
            RequestRpc(target.NetworkObject);
        }

        /// <summary>Answer the request this player was sent.</summary>
        public void Respond(bool accept)
        {
            if (IsSpawned && IsOwner) RespondRpc(accept);
        }

        /// <summary>Give up the duel this player is in: the opponent wins, nobody dies.</summary>
        public void Forfeit()
        {
            if (IsSpawned && IsOwner) ForfeitRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRpc(NetworkObjectReference targetReference)
        {
            PlayerDuel target = targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerDuel>() : null;
            if (target == null || target == this) { Tell("Select another player to call out."); return; }
            if (!Alive) { Tell("You cannot ask for a callout while defeated."); return; }
            if (Busy) { Tell("You are already in a callout."); return; }
            if (!target.Alive) { Tell($"{target.DuelistName} is defeated."); return; }
            if (target.Busy) { Tell($"{target.DuelistName} is busy with another callout."); return; }
            if (CombatHostility.IsHostile(transform, target.combatEntity)) { Tell($"{target.DuelistName} is already your enemy."); return; }
            if (Vector3.Distance(transform.position, target.transform.position) > requestRange)
            { Tell($"{target.DuelistName} is too far away for a callout."); return; }
            if (target.challenger != null && Time.time <= target.challengeExpires && target.challenger != this)
            { Tell($"{target.DuelistName} is answering another request."); return; }

            target.challenger = this;
            target.challengeExpires = Time.time + requestSeconds;
            Tell($"You called out {target.DuelistName}.");
            target.Tell($"{DuelistName} calls you out. Accept with /accept, refuse with /decline.");
            target.PromptRpc(DuelistName, requestSeconds);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(bool accept)
        {
            PlayerDuel from = challenger;
            bool live = from != null && from.IsSpawned && Time.time <= challengeExpires;
            challenger = null;
            PromptClosedRpc();
            if (!live) { Tell("There is no callout request to answer."); return; }

            if (!accept)
            {
                Tell($"You declined {from.DuelistName}'s callout.");
                from.Tell($"{DuelistName} declined your callout.");
                return;
            }
            if (Busy || from.Busy || !Alive || !from.Alive)
            {
                Tell("That callout can no longer start.");
                from.Tell($"The callout with {DuelistName} can no longer start.");
                return;
            }

            startingWith = from;
            from.startingWith = this;
            from.countdown = from.StartCoroutine(from.CountdownRoutine(this));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ForfeitRpc()
        {
            if (opponent == null) { Tell("You are not in a callout."); return; }
            Finish(opponent, this, $"{DuelistName} forfeited.");
        }

        // ------------------------------------------------------------------ the duel (server)

        private IEnumerator CountdownRoutine(PlayerDuel other)
        {
            for (int left = countdownSeconds; left > 0; left--)
            {
                Tell($"Callout with {other.DuelistName} starts in {left}...");
                other.Tell($"Callout with {DuelistName} starts in {left}...");
                yield return new WaitForSeconds(1f);
                if (other == null || !other.IsSpawned || !Alive || !other.Alive || startingWith != other)
                {
                    CancelStart("The callout was called off.");
                    yield break;
                }
            }
            countdown = null;
            Begin(other);
        }

        private void CancelStart(string reason)
        {
            PlayerDuel other = startingWith;
            if (countdown != null) StopCoroutine(countdown);
            countdown = null;
            startingWith = null;
            Tell(reason);
            if (other == null) return;
            if (other.countdown != null) other.StopCoroutine(other.countdown);
            other.countdown = null;
            other.startingWith = null;
            other.Tell(reason);
        }

        private void Begin(PlayerDuel other)
        {
            startingWith = other.startingWith = null;
            opponent = other;
            other.opponent = this;
            origin = other.origin = (transform.position + other.transform.position) * 0.5f;
            keepsWatch = true;
            other.keepsWatch = false;
            netOpponent.Value = other.NetworkObjectId;
            other.netOpponent.Value = NetworkObjectId;
            Tell($"The callout with {other.DuelistName} has begun. It ends when one of you falls.");
            other.Tell($"The callout with {DuelistName} has begun. It ends when one of you falls.");
        }

        /// <summary>Server only: start a duel between this player and <paramref name="other"/> at once, for GM tools.</summary>
        public bool ServerForceStart(PlayerDuel other)
        {
            if (!IsSpawned || !IsServer || other == null || other == this || !other.IsSpawned) return false;
            if (Busy || other.Busy || !Alive || !other.Alive) return false;
            Begin(other);
            return true;
        }

        /// <summary>Server only: stop this player's duel with no winner.</summary>
        public bool ServerCancel(string reason)
        {
            if (!IsSpawned || !IsServer || opponent == null) return false;
            PlayerDuel other = opponent;
            Clear(this);
            Clear(other);
            Tell(reason);
            other.Tell(reason);
            return true;
        }

        private void Update()
        {
            if (!keepsWatch || opponent == null) return;
            float leash = leashRadius * leashRadius;
            if ((transform.position - origin).sqrMagnitude > leash) Finish(opponent, this, $"{DuelistName} left the callout.");
            else if ((opponent.transform.position - origin).sqrMagnitude > leash) Finish(this, opponent, $"{opponent.DuelistName} left the callout.");
        }

        // Raised where combat is decided, so this only ever runs on the server.
        private void OnKilled(KillingBlow blow)
        {
            if (!IsSpawned || !IsServer) return;
            if (startingWith != null) CancelStart("The callout was called off.");
            if (opponent == null) return;

            PlayerDuel killer = blow.Attacker != null ? blow.Attacker.GetComponent<PlayerDuel>() : null;
            if (killer == opponent) Finish(opponent, this, null);
            else ServerCancel("The callout was interrupted.");
        }

        private static void Finish(PlayerDuel winner, PlayerDuel loser, string how)
        {
            string winnerName = winner.DuelistName, loserName = loser.DuelistName;
            Clear(winner);
            Clear(loser);
            if (!string.IsNullOrEmpty(how))
            {
                winner.Tell(how);
                loser.Tell(how);
            }
            if (winner.IsSpawned) winner.ResultRpc(true, loserName);
            if (loser.IsSpawned) loser.ResultRpc(false, winnerName);
            winner.Announcement?.Invoke($"{winnerName} won the callout against {loserName}.");
        }

        private static void Clear(PlayerDuel duelist)
        {
            duelist.opponent = null;
            duelist.keepsWatch = false;
            // A player on its way out of the game can no longer be written to.
            if (duelist.IsSpawned && duelist.NetworkObject != null && duelist.NetworkObject.IsSpawned) duelist.netOpponent.Value = NoOpponent;
        }

        private void Tell(string text)
        {
            if (!string.IsNullOrEmpty(text)) Notice?.Invoke(text);
        }

        // ------------------------------------------------------------------ server -> owner (presentation)

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptRpc(string challengerName, float seconds) => ClientPresentationRegistry.Duel?.ShowRequest(challengerName, seconds);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptClosedRpc() => ClientPresentationRegistry.Duel?.HideRequest();

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ResultRpc(bool won, string opponentName) => ClientPresentationRegistry.Duel?.ShowResult(won, opponentName);
    }
}
