using KryticalNova.Networking;
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
    public sealed class PlayerDuel : NetworkBehaviour, IDuelState, IBodyDuel, IServerLeaving
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
        public static PlayerDuel Local => LocalPlayer.Get<PlayerDuel>();

        public string DuelistName => identity != null ? identity.CharacterName : name;
        public bool IsDuelling => (IsSpawned && netOpponent.Value != NoOpponent) || shownOpponent != null;

        // a view of the entity netcode: the opponent its ghost names (M4 8b); it decides nothing with it
        private GameObject shownOpponent;

        public Component OpponentBody => EntityServer && opponent != null ? opponent.transform : null;

        public void MirrorOpponent(Component opponentBody)
        {
            GameObject next = opponentBody != null ? opponentBody.gameObject : null;
            if (BodyRoleTag.Of(this) is not { Kind: NetRoleKind.ClientView } || next == shownOpponent) return;
            shownOpponent = next;
            Changed?.Invoke();
        }
        private bool Busy => opponent != null || startingWith != null;
        private bool Alive => combatEntity == null || combatEntity.IsAlive;
        // a server body of the entity netcode decides as NGO's server did (M4 step 8); it is never NGO-spawned
        private bool EntityServer => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ServerBody };
        private bool Decides => (IsSpawned && IsServer) || EntityServer;
        // NGO's "still in the game" (IsSpawned): a server body that is still there
        private bool Present => IsSpawned || (EntityServer && isActiveAndEnabled);

        /// <summary>True on every peer while this player and <paramref name="other"/> are duelling each other.</summary>
        public bool IsDuellingWith(NetworkObject other) =>
            other != null && other.IsSpawned && IsSpawned && netOpponent.Value == other.NetworkObjectId;

        /// <summary>A server body of the entity netcode: true while it duels the player of <paramref name="other"/>'s body.</summary>
        public bool IsDuellingWithBody(Component other) =>
            other != null && (EntityServer ? opponent != null && opponent.gameObject == other.gameObject : shownOpponent != null && shownOpponent == other.gameObject);

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
            // the entity netcode's own view asks its server (M4 step 8), the target by its body
            if (BodyRoleTag.RequestsOf(this) is IBodyRequests requests) { if (target != null) requests.DuelOp(DuelOpKind.Request, target.transform, false); return; }
            if (!IsSpawned || !IsOwner || target == null || !target.IsSpawned) return;
            RequestRpc(target.NetworkObject);
        }

        /// <summary>Answer the request this player was sent.</summary>
        public void Respond(bool accept)
        {
            if (BodyRoleTag.RequestsOf(this) is IBodyRequests requests) { requests.DuelOp(DuelOpKind.Respond, null, accept); return; }
            if (IsSpawned && IsOwner) RespondRpc(accept);
        }

        /// <summary>Give up the duel this player is in: the opponent wins, nobody dies.</summary>
        public void Forfeit()
        {
            if (BodyRoleTag.RequestsOf(this) is IBodyRequests requests) { requests.DuelOp(DuelOpKind.Forfeit, null, false); return; }
            if (IsSpawned && IsOwner) ForfeitRpc();
        }

        /// <summary>A server body of the entity netcode: its owner's Callout request, NGO's RPC bodies after the Social limit (M4 step 8).</summary>
        public void RunDuelOp(DuelOpKind op, Component target, bool accept)
        {
            if (!EntityServer) return;
            switch (op)
            {
                case DuelOpKind.Request: ServerRequest(target != null ? target.GetComponent<PlayerDuel>() : null); break;
                case DuelOpKind.Respond: ServerRespond(accept); break;
                case DuelOpKind.Forfeit: ServerForfeit(); break;
            }
        }

        /// <summary>The entity netcode's own view: what its server told it about its Callout (NGO's owner RPC bodies).</summary>
        public void MirrorShow(DuelShowKind kind, string name, float seconds, bool won)
        {
            switch (kind)
            {
                case DuelShowKind.Prompt: ClientPresentationRegistry.Duel?.ShowRequest(name, seconds); break;
                case DuelShowKind.PromptClosed: ClientPresentationRegistry.Duel?.HideRequest(); break;
                case DuelShowKind.Result: ClientPresentationRegistry.Duel?.ShowResult(won, name); break;
                case DuelShowKind.Countdown: LocalCountdownStarted?.Invoke(Mathf.RoundToInt(seconds), name); break;
                case DuelShowKind.CountdownCancelled: LocalCountdownCancelled?.Invoke(); break;
            }
        }

        /// <summary>A server body of the entity netcode leaving the game: NGO's OnNetworkDespawn on the server.</summary>
        public void OnServerLeaving()
        {
            if (!EntityServer) return;
            if (opponent != null) Finish(opponent, this, $"{DuelistName} left the game.");
            else if (startingWith != null) CancelStart("The callout was called off.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRpc(NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RequestRpc))) return;
            ServerRequest(targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerDuel>() : null);
        }

        private void ServerRequest(PlayerDuel target)
        {
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
            string blocked = KryticalNova.Core.SocialGate.Refusal(this, target);
            if (blocked != null) { Tell(blocked); return; }

            target.challenger = this;
            target.challengeExpires = Time.time + requestSeconds;
            Tell($"You called out {target.DuelistName}.");
            target.Tell($"{DuelistName} calls you out. Accept with /accept, refuse with /decline.");
            target.ShowOwner(DuelShowKind.Prompt, DuelistName, requestSeconds);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(bool accept)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RespondRpc))) return;
            ServerRespond(accept);
        }

        private void ServerRespond(bool accept)
        {
            PlayerDuel from = challenger;
            bool live = from != null && from.Present && Time.time <= challengeExpires;
            challenger = null;
            ShowOwner(DuelShowKind.PromptClosed, string.Empty, 0f);
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
            // Both duelists' screens count down with it (the Showdown letterbox, owner 22 Oct 2026).
            ShowOwner(DuelShowKind.Countdown, from.DuelistName, countdownSeconds);
            from.ShowOwner(DuelShowKind.Countdown, DuelistName, countdownSeconds);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ForfeitRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(ForfeitRpc))) return;
            ServerForfeit();
        }

        private void ServerForfeit()
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
                if (other == null || !other.Present || !Alive || !other.Alive || startingWith != other)
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
            ShowOwner(DuelShowKind.CountdownCancelled, string.Empty, 0f);
            if (other == null) return;
            if (other.countdown != null) other.StopCoroutine(other.countdown);
            other.countdown = null;
            other.startingWith = null;
            other.Tell(reason);
            other.ShowOwner(DuelShowKind.CountdownCancelled, string.Empty, 0f);
        }

        private void Begin(PlayerDuel other)
        {
            startingWith = other.startingWith = null;
            opponent = other;
            other.opponent = this;
            origin = other.origin = (transform.position + other.transform.position) * 0.5f;
            keepsWatch = true;
            other.keepsWatch = false;
            if (IsSpawned) netOpponent.Value = other.NetworkObjectId;
            if (other.IsSpawned) other.netOpponent.Value = NetworkObjectId;
            Tell($"The callout with {other.DuelistName} has begun. It ends when one of you falls.");
            other.Tell($"The callout with {DuelistName} has begun. It ends when one of you falls.");
        }

        /// <summary>Server only: start a duel between this player and <paramref name="other"/> at once, for GM tools.</summary>
        public bool ServerForceStart(PlayerDuel other)
        {
            if (!Decides || other == null || other == this || !other.Present) return false;
            if (Busy || other.Busy || !Alive || !other.Alive) return false;
            Begin(other);
            return true;
        }

        /// <summary>Server only: stop this player's duel with no winner.</summary>
        public bool ServerCancel(string reason)
        {
            if (!Decides || opponent == null) return false;
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
            if (!Decides) return;
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
            winner.ShowOwner(DuelShowKind.Result, loserName, 0f, true);
            loser.ShowOwner(DuelShowKind.Result, winnerName, 0f, false);
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

        // NGO's owner RPCs, or on a server body of the entity netcode its owner link (M4 step 8)
        private void ShowOwner(DuelShowKind kind, string name, float seconds, bool won = false)
        {
            if (IsSpawned)
            {
                switch (kind)
                {
                    case DuelShowKind.Prompt: PromptRpc(name, seconds); break;
                    case DuelShowKind.PromptClosed: PromptClosedRpc(); break;
                    case DuelShowKind.Result: ResultRpc(won, name); break;
                    case DuelShowKind.Countdown: CountdownRpc(Mathf.RoundToInt(seconds), name); break;
                    case DuelShowKind.CountdownCancelled: CountdownCancelledRpc(); break;
                }
                return;
            }
            if (EntityServer) DuelOwnerLink.Current?.Show(this, kind, name ?? string.Empty, seconds, won);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptRpc(string challengerName, float seconds) => ClientPresentationRegistry.Duel?.ShowRequest(challengerName, seconds);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptClosedRpc() => ClientPresentationRegistry.Duel?.HideRequest();

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ResultRpc(bool won, string opponentName) => ClientPresentationRegistry.Duel?.ShowResult(won, opponentName);

        /// <summary>
        /// Raised on this machine when a callout its own player is in starts counting down: the seconds to go and who
        /// against. The bout itself is <see cref="IsDuelling"/>, which ends however the callout ends.
        /// </summary>
        public static event Action<int, string> LocalCountdownStarted;

        /// <summary>Raised on this machine when its own player's countdown is called off before the bout began.</summary>
        public static event Action LocalCountdownCancelled;

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void CountdownRpc(int seconds, string opponentName) => LocalCountdownStarted?.Invoke(seconds, opponentName);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void CountdownCancelledRpc() => LocalCountdownCancelled?.Invoke();
    }
}
