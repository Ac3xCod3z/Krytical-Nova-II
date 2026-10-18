using KryticalNova.Networking;
using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Restyle
{
    /// <summary>
    /// Changing a character's look after it was made: hair at the Stylist, the face at the Reshaper, build and
    /// muscle at the Coach. The owner sends the look it would like; the server keeps only what that service owns
    /// out of it (<see cref="RestyleRules.Merge"/>), checks the result like a new character's, uses up the pass
    /// (a Restyle Pass, or a Training Pass at the Coach: from the Pack or from what is stored) and gives the
    /// character its new look, which everyone sees and the save keeps. The pass items are in <see cref="EconomySettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerIdentity), typeof(PlayerInventory), typeof(PlayerItemSources))]
    public sealed class PlayerRestyle : NetworkBehaviour
    {
        private const float ReachPadding = 2.5f;
        private const string ReasonDefeated = "You can't do that while defeated.";

        [SerializeField] private EconomySettings settings;

        private PlayerIdentity identity;
        private PlayerInventory inventory;
        private PlayerItemSources sources;
        private PlayerResources resources;
        private PlayerSaveAgent saveAgent;
        private readonly ItemCost[] cost = new ItemCost[1];

        /// <summary>GM switch: the three services work anywhere until the session ends.</summary>
        public static bool GmAnywhere;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => GmAnywhere = false;

        /// <summary>The restyle of the player this machine controls.</summary>
        public static PlayerRestyle Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerRestyle>() : null;

        public PlayerIdentity Identity => identity;
        /// <summary>Raised on the owner when the server has given the character a new look.</summary>
        public event Action Restyled;

        public static NpcOffer OfferOf(RestyleService service) =>
            service == RestyleService.Stylist ? NpcOffer.Stylist : service == RestyleService.Reshaper ? NpcOffer.Reshaper : NpcOffer.Coach;

        public static string NpcName(RestyleService service) =>
            service == RestyleService.Stylist ? "Stylist" : service == RestyleService.Reshaper ? "Reshaper" : "Coach";

        /// <summary>True while the NPC of a service is in reach (or the GM switch is on): its window closes when it stops being so.</summary>
        public bool InReach(RestyleService service) => GmAnywhere || NpcInteractable.IsOffered(OfferOf(service), transform.position, ReachPadding);

        /// <summary>The item one change at a service uses up (a Restyle Pass, a Training Pass), or 0 when it is free.</summary>
        public ushort PassItemId(RestyleService service) =>
            settings == null ? (ushort)0 : service == RestyleService.Coach ? settings.trainingItemId : settings.restyleItemId;

        /// <summary>How many of a service's pass the player has, carried or stored.</summary>
        public int PassesHeld(RestyleService service) => PassItemId(service) != 0 ? sources.Count(PassItemId(service)) : 0;

        public string PassName(RestyleService service)
        {
            ItemDefinition item = inventory.Catalog != null ? inventory.Catalog.Get(PassItemId(service)) : null;
            return item != null ? item.displayName : service == RestyleService.Coach ? "Training Pass" : "Restyle Pass";
        }

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            inventory = GetComponent<PlayerInventory>();
            sources = GetComponent<PlayerItemSources>();
            resources = GetComponent<PlayerResources>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        // ------------------------------------------------------------------ requests (owner)

        public void RequestRestyle(RestyleService service, in CharacterRecipe proposed)
        {
            if (IsSpawned) RestyleRpc((byte)service, CharacterNetState.From(identity.CharacterName, proposed));
            else ServerRestyle(service, proposed);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RestyleRpc(byte service, CharacterNetState proposed)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(RestyleRpc))) return;
            ServerRestyle((RestyleService)service, proposed.ToRecipe());
        }

        // ------------------------------------------------------------------ server

        private bool HasAuthority => !IsSpawned || IsServer;

        private void ServerRestyle(RestyleService service, CharacterRecipe proposed)
        {
            if (!HasAuthority || !identity.HasIdentity || service > RestyleService.Coach) return;
            if (resources != null && !resources.IsAlive) { Tell(ReasonDefeated); return; }
            if (!InReach(service)) { Tell($"Visit a {NpcName(service)} to do that."); return; }

            CharacterRecipe current = identity.State.ToRecipe();
            CharacterCreationDatabase database = identity.Database;
            CharacterRecipe wanted = RestyleRules.Merge(current, proposed, service, database != null ? database.Parts : null);
            if (RestyleRules.Same(current, wanted)) { Tell(RestyleRules.ReasonNothingChanged); return; }
            if (!CharacterRecipeValidator.Validate(wanted, database, out string reason)) { Tell(reason); return; }

            ushort pass = PassItemId(service);
            if (pass != 0)
            {
                cost[0] = new ItemCost(pass, 1);
                if (!sources.ServerSpend(cost)) { Tell($"You have no {PassName(service)}."); return; }
            }
            if (!identity.ServerRestyle(wanted))
            {
                // Checked a moment ago: this cannot fail. The pass is handed back all the same.
                if (pass != 0) inventory.ServerGive(pass, 1);
                return;
            }
            if (saveAgent != null) saveAgent.SaveSoon();
            Tell(service == RestyleService.Coach ? "Your new build is on." : "Your new look is on.");
            if (IsSpawned) RestyledRpc();
            else Restyled?.Invoke();
        }

        // ------------------------------------------------------------------ telling the owner

        [Rpc(SendTo.Owner)]
        private void RestyledRpc() => Restyled?.Invoke();

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
