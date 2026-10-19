using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using KryticalNova.Wallet;

namespace KryticalNova.Crafting
{
    /// <summary>
    /// Crafting at the Fabricator: Alloys from lesser Alloys, a Chroma Cell from its four colours, a Dossier from
    /// its four Pages. The client only names the recipe and how many; the server checks everything again through
    /// <see cref="CraftRules"/>, takes the materials (from the Pack, then the Vault, then the Deep Vault) and the
    /// Creds, and puts what was made in the Pack. Every recipe is in <see cref="CraftingSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerItemSources))]
    public sealed class PlayerCrafting : NetworkBehaviour
    {
        private const float ReachPadding = 2.5f;
        private const string ReasonDefeated = "You can't craft while defeated.";
        private const string ReasonWhere = "Visit a Fabricator to craft.";

        [SerializeField] private CraftingSettings settings;

        private PlayerInventory inventory;
        private PlayerItemSources sources;
        private PlayerWallet wallet;
        private PlayerCharacterProgression progression;
        private PlayerResources resources;
        private PlayerSaveAgent saveAgent;
        private readonly List<ItemCost> costs = new List<ItemCost>(4);

        /// <summary>GM switch: crafting works anywhere until the session ends.</summary>
        public static bool GmAnywhere;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => GmAnywhere = false;

        /// <summary>The crafting of the player this machine controls.</summary>
        public static PlayerCrafting Local => LocalPlayer.Get<PlayerCrafting>();

        public CraftingSettings Settings => settings;
        public PlayerInventory Inventory => inventory;
        public PlayerItemSources Sources => sources;
        public long Creds => wallet != null ? wallet.Creds : 0;
        public int Level => progression != null ? progression.CurrentLevel : 1;
        /// <summary>True while a Fabricator is in reach (or the GM switch is on): the window closes when it stops being so.</summary>
        public bool InReach => GmAnywhere || NpcInteractable.IsOffered(NpcOffer.Craft, transform.position, ReachPadding);

        /// <summary>Raised on the owner when the server has made something: the recipe and how many times.</summary>
        public event Action<ushort, int> Crafted;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            sources = GetComponent<PlayerItemSources>();
            wallet = GetComponent<PlayerWallet>();
            progression = GetComponent<PlayerCharacterProgression>();
            resources = GetComponent<PlayerResources>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        /// <summary>Why this character cannot make a recipe so many times (shown beside it), or null. The same answer on the owner and the server.</summary>
        public string BlockedReason(in CraftRecipe recipe, int times) =>
            CraftRules.BlockedReason(recipe, times, Level, Creds, sources.Count, NameOf);

        /// <summary>How many times what the player has would make a recipe.</summary>
        public int MaxCrafts(in CraftRecipe recipe) =>
            settings != null ? CraftRules.MaxCrafts(recipe, Creds, sources.Count, settings.maxPerRequest) : 0;

        public string NameOf(ushort itemId)
        {
            ItemDefinition item = inventory.Catalog != null ? inventory.Catalog.Get(itemId) : null;
            return item != null ? item.displayName : "material";
        }

        // ------------------------------------------------------------------ requests (owner)

        public void RequestCraft(ushort recipeId, int times)
        {
            if (IsSpawned) CraftRpc(recipeId, (ushort)Mathf.Clamp(times, 1, ushort.MaxValue));
            else ServerCraft(recipeId, times);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CraftRpc(ushort recipeId, ushort times)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(CraftRpc))) return;
            ServerCraft(recipeId, times);
        }

        // ------------------------------------------------------------------ server

        private bool HasAuthority => !IsSpawned || IsServer;

        private void ServerCraft(ushort recipeId, int times)
        {
            if (!HasAuthority || inventory.Catalog == null) return;
            string blocked = settings == null ? CraftRules.ReasonNoRecipe
                : resources != null && !resources.IsAlive ? ReasonDefeated
                : !InReach ? ReasonWhere : null;
            if (blocked != null) { Tell(blocked); return; }
            if (!settings.TryGet(recipeId, out CraftRecipe recipe)) { Tell(CraftRules.ReasonNoRecipe); return; }
            ItemDefinition result = inventory.Catalog.Get(recipe.resultItemId);
            if (result == null) { Tell(CraftRules.ReasonNoRecipe); return; }

            times = Mathf.Clamp(times, 1, Mathf.Max(1, settings.maxPerRequest));
            blocked = BlockedReason(recipe, times);
            if (blocked != null) { Tell(blocked); return; }
            CraftRules.TryCreds(recipe, times, out long fee);

            // The materials leave first: the room they free is room for what is made.
            int made = recipe.resultCount * times;
            bool room = true;
            CraftRules.Costs(recipe, times, costs);
            bool done = sources.ServerSpend(costs, null, pack =>
            {
                room = InventoryRules.TryAdd(pack, inventory.Catalog, recipe.resultItemId, made) == made;
                return room;
            });
            if (!done) { Tell(room ? CraftRules.ReasonNoRecipe : CraftRules.ReasonNoRoom); return; }
            if (fee > 0 && wallet != null) wallet.ServerSpend(fee, CredsSink.Crafting);
            if (saveAgent != null) saveAgent.SaveSoon();

            Tell($"Crafted {LootRules.Label(result, made)}.");
            if (IsSpawned) CraftedRpc(recipeId, (ushort)times);
            else Crafted?.Invoke(recipeId, times);
        }

        // ------------------------------------------------------------------ telling the owner

        [Rpc(SendTo.Owner)]
        private void CraftedRpc(ushort recipeId, ushort times) => Crafted?.Invoke(recipeId, times);

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
