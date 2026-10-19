using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Housing;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// The Recall Tag (Twelve Sky 2: Scroll of Return): a quick trip back to town. Used from the Pack or the Hotbar,
    /// the character channels for a few seconds, standing still, and is then put on one of the map's spawn points,
    /// where a death would lead. A move, a hit or a fight stops it, and the Tag goes back into the Pack. Refused in a
    /// fight, in a Callout and in a Crew Hall. The server decides; everyone nearby sees the channel and the arrival.
    /// Numbers: <see cref="RecallSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRecall : NetworkBehaviour, IItemUseHandler
    {
        // The id the charge aura is shown under: no skill has it, so it takes the game feel's default charge.
        private const string FeelId = "Recall";

        [SerializeField] private RecallSettings settings;

        private PlayerInventory inventory;
        private PlayerResources resources;
        private CombatEntity entity;
        private PlayerCombatState combat;
        private PlayerDuel duel;
        private PlayerHousing housing;
        private NavMeshAgent agent;

        private bool channelling;
        private float endsAt;
        private Vector3 start;
        private ushort tagItemId;
        private bool hitDuringChannel;

        private bool Decides => !IsSpawned || IsServer;
        private bool IsLocal => !IsSpawned || IsOwner;

        /// <summary>True while this character is channelling a Recall (on the machine that decides).</summary>
        public bool Recalling => channelling;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            resources = GetComponent<PlayerResources>();
            entity = GetComponent<CombatEntity>();
            combat = GetComponent<PlayerCombatState>();
            duel = GetComponent<PlayerDuel>();
            housing = GetComponent<PlayerHousing>();
            agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable()
        {
            if (entity != null) entity.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (entity != null) entity.Damaged -= OnDamaged;
        }

        private void OnDamaged(DamageResult result, CharacterCombatStats attacker)
        {
            if (channelling) hitDuringChannel = true;
        }

        // ------------------------------------------------------------------ the Tag is used (server)

        public ItemUseEffect Effect => ItemUseEffect.Recall;

        public string UseBlockedReason(UsedItem item) =>
            RecallRules.BlockedReason(channelling,
                combat != null && combat.InCombat,
                duel != null && duel.IsDuelling,
                housing != null && (housing.InHome || housing.Travelling),
                PlayerSpawnPoint.Active.Count > 0);

        public void ServerUse(UsedItem item)
        {
            if (!Decides || channelling) return;
            float seconds = settings != null ? settings.channelSeconds : 5f;
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath(); // stand still for it
            channelling = true;
            hitDuringChannel = false;
            tagItemId = !item.IsNone ? item.Id : (ushort)0;
            start = transform.position;
            endsAt = Time.time + seconds;
            if (seconds <= 0f) { Arrive(); return; }
            Present(true, seconds);
            Tell($"Recalling to town in {seconds:0} s. Moving, being hit or a fight stops it.");
        }

        private void Update()
        {
            if (!channelling || !Decides) return;
            if (resources != null && !resources.IsAlive) { Stop(RecallRules.StopDied, false); return; }
            if (hitDuringChannel) { Stop(RecallRules.StopHit, true); return; }
            if (combat != null && combat.InCombat) { Stop(RecallRules.StopFight, true); return; }
            float tolerance = settings != null ? settings.moveTolerance : 0.5f;
            if (RecallRules.Moved(start, transform.position, tolerance)) { Stop(RecallRules.StopMoved, true); return; }
            if (RecallRules.SecondsLeft(Time.time, endsAt) <= 0f) Arrive();
        }

        private void Arrive()
        {
            channelling = false;
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath();
            if (!PlayerSpawnService.Respawn(transform))
            {
                Stop(RecallRules.ReasonNoTown, true);
                return;
            }
            Present(false, 0f);
            Arrived();
            Tell("Back in town.");
        }

        /// <summary>The trip is off: the aura goes, the player is told why, and the Tag comes back when it should.</summary>
        private void Stop(string reason, bool refund)
        {
            channelling = false;
            Present(false, 0f);
            bool given = refund && (settings == null || settings.refundWhenStopped) && tagItemId != 0 && inventory != null &&
                         inventory.ServerGive(tagItemId, 1) > 0;
            Tell(given ? reason + " Your Recall Tag is back in your Pack." : reason);
        }

        /// <summary>Server: stops a channel that is running, with the Tag given back (a GM tool, a zone change).</summary>
        public void ServerCancel(string reason)
        {
            if (Decides && channelling) Stop(reason, true);
        }

        // ------------------------------------------------------------------ what everyone sees

        private void Present(bool begun, float seconds)
        {
            if (IsSpawned) ChannelRpc(begun, seconds);
            else ShowChannel(begun, seconds);
        }

        [Rpc(SendTo.Everyone)]
        private void ChannelRpc(bool begun, float seconds) => ShowChannel(begun, seconds);

        private void ShowChannel(bool begun, float seconds)
        {
            var feel = ClientPresentationRegistry.Feel;
            if (feel == null) return;
            if (begun) feel.ChargeStarted(transform, FeelId, seconds, IsLocal);
            else feel.ChargeEnded(transform, FeelId, false);
        }

        private void Arrived()
        {
            if (IsSpawned) ArrivedRpc();
            else ClientPresentationRegistry.Feel?.Respawned(transform, true);
        }

        [Rpc(SendTo.Everyone)]
        private void ArrivedRpc() => ClientPresentationRegistry.Feel?.Respawned(transform, IsOwner);

        private void Tell(string text)
        {
            if (IsSpawned && !IsOwner) TellRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
