using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// Picking things up and putting them down. The owner asks; the server checks reach, who the drop is held for
    /// and Pack space, then moves the item. What does not fit stays on the ground. Offline (the unspawned scene
    /// avatar) the same rules run locally.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerLoot : NetworkBehaviour
    {
        // A client sees itself a little ahead of where the server has it; allow for that when checking reach.
        private const float ReachTolerance = 1.5f;
        private const float DropForward = 0.9f;

        [SerializeField] private WorldDrop dropPrefab;
        [Tooltip("Seconds an item this player drops is held for them before others may take it.")]
        [SerializeField, Min(0f)] private float dropReserveSeconds = 20f;
        [Tooltip("On: once the hold ends anyone may take it, whatever their faction. Off: only the dropper's faction, " +
                 "the same rule as trading (a neutral trade zone still opens it to all).")]
        [SerializeField] private bool dropsOpenToEveryone = true;
        [Tooltip("How far the pickup key reaches, in metres.")]
        [SerializeField, Min(1f)] private float pickupKeyRange = 3.5f;

        private PlayerInventory inventory;
        private PlayerResources resources;
        private IGearWearer wearer;
        private IBodyGestures gestures;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerLoot Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerLoot>() : null;

        public float PickupKeyRange => pickupKeyRange;
        /// <summary>Seconds an item this player drops stays theirs alone.</summary>
        public float DropReserveSeconds => dropReserveSeconds;
        /// <summary>True when, after that, players of any faction may take it.</summary>
        public bool DropsOpenToEveryone => dropsOpenToEveryone;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            resources = GetComponent<PlayerResources>();
            wearer = GetComponent<IGearWearer>();
            gestures = GetComponent<IBodyGestures>();
        }

        // ------------------------------------------------------------------ owner

        /// <summary>Ask for a drop. Call on the machine that owns this player.</summary>
        public void RequestPickup(WorldDrop drop)
        {
            if (drop == null) return;
            if (!IsSpawned) ServerPickup(drop);
            else if (IsOwner && drop.IsSpawned) PickupRpc(drop.NetworkObject);
        }

        /// <summary>The pickup key: take the nearest drop in reach. Returns false (with a message) when there is none.</summary>
        public bool RequestPickupNearest()
        {
            WorldDrop nearest = null;
            float best = pickupKeyRange * pickupKeyRange;
            foreach (WorldDrop drop in WorldDrop.Active)
            {
                if (drop == null) continue;
                float distance = FlatSqrDistance(drop.Position);
                if (distance > best) continue;
                best = distance;
                nearest = drop;
            }
            if (nearest == null)
            {
                ClientPresentationRegistry.Messages?.ShowMessage("Nothing to pick up here.");
                return false;
            }
            RequestPickup(nearest);
            return true;
        }

        /// <summary>Put an item from the Pack or a slot on the ground at this player's feet.</summary>
        public void RequestDrop(ItemLocation at)
        {
            if (!IsSpawned) ServerDrop(at);
            else if (IsOwner) DropRpc(at);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PickupRpc(NetworkObjectReference dropReference)
        {
            WorldDrop drop = dropReference.TryGet(out NetworkObject dropObject) ? dropObject.GetComponent<WorldDrop>() : null;
            if (drop == null) { Tell(LootRules.ReasonGone); return; }
            ServerPickup(drop);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DropRpc(ItemLocation at) => ServerDrop(at);

        // ------------------------------------------------------------------ server

        private void ServerPickup(WorldDrop drop)
        {
            if (IsSpawned && !IsServer) return;
            if (drop == null || drop.Stack.IsEmpty) { Tell(LootRules.ReasonGone); return; }
            if (resources != null && !resources.IsAlive) { Tell(LootRules.ReasonDefeated); return; }

            float distance = Mathf.Sqrt(FlatSqrDistance(drop.Position));
            FactionId mine = wearer != null ? wearer.Faction : FactionId.None;
            string blocked = LootRules.PickupBlockedReason(distance, Mathf.Max(drop.InteractRange, pickupKeyRange) + ReachTolerance,
                drop.IsReservedForAnother(NetworkObject), drop.Faction, mine);
            if (blocked != null) { Tell(blocked); return; }

            ItemStack stack = drop.Stack;
            ItemDefinition item = drop.Definition;
            if (LootRules.IsCreds(stack.itemId))
            {
                // Creds never enter the Pack: they are paid into the wallet, as much as it has room for.
                var purse = GetComponent<ICurrencyReceiver>();
                int paid = purse != null ? (int)purse.ServerReceiveCreds(stack.count) : 0;
                if (paid <= 0) { Tell("Your wallet is full."); return; }
                Vector3 where = drop.Position;
                drop.ServerTake(paid, transform);
                gestures?.ShowPickUp(where);
                Tell(paid < stack.count ? $"+{paid} Creds. Your wallet is full: the rest stays." : $"+{paid} Creds.");
                return;
            }
            int taken = inventory.ServerGive(stack); // tells the player when the Pack is full
            if (taken <= 0) return;
            Vector3 lay = drop.Position;
            drop.ServerTake(taken, transform);
            gestures?.ShowPickUp(lay);
            Tell(taken < stack.count
                ? $"Picked up {LootRules.Label(item, taken)}. Your Pack is full: the rest stays."
                : $"Picked up {LootRules.Label(item, taken)}.");
        }

        private void ServerDrop(ItemLocation at)
        {
            if (IsSpawned && !IsServer) return;
            if (dropPrefab == null) { Tell("Items can't be dropped here."); return; }
            if (resources != null && !resources.IsAlive) { Tell("You can't drop items while defeated."); return; }
            if (!inventory.ServerTake(at, out ItemStack taken)) return;
            FactionId openTo = dropsOpenToEveryone || wearer == null ? FactionId.None : wearer.Faction;
            // The HUD counts the hold down for the dropper (LootHudController): no message is needed here.
            ServerPlace(taken, NetworkObject, dropReserveSeconds, openTo);
        }

        /// <summary>
        /// Server (or offline): puts a stack on the ground in front of this player. For tools and for systems that
        /// hand out loot; <paramref name="heldFor"/> may reserve it for one player.
        /// </summary>
        public WorldDrop ServerPlace(ItemStack stack, NetworkObject heldFor = null, float reserveSeconds = 0f, FactionId dropFaction = FactionId.None)
        {
            Vector3 feet = transform.position + transform.forward * DropForward;
            return WorldDrop.ServerSpawn(dropPrefab, stack, feet, heldFor != null && heldFor.IsSpawned ? heldFor : null, reserveSeconds, dropFaction);
        }

        /// <summary>
        /// Server (or offline): puts a stack on the ground at a point in the world, held for a player for the same
        /// time as a drop of their own. For loot that falls somewhere else than at this player's feet (a Hostile's).
        /// </summary>
        public WorldDrop ServerPlaceAt(ItemStack stack, Vector3 position, NetworkObject heldFor = null)
        {
            return WorldDrop.ServerSpawn(dropPrefab, stack, position, heldFor != null && heldFor.IsSpawned ? heldFor : null, dropReserveSeconds);
        }

        private float FlatSqrDistance(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude;
        }

        // ------------------------------------------------------------------ telling the owner

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwnedByServer) TellRpc(text);
            else if (!IsSpawned || IsOwner) ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
