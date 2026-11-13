using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Pets;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using KryticalNova.Wallet;

namespace KryticalNova.Restyle
{
    /// <summary>
    /// What a character shows in place of what it wears, and the two services that change it: the Holoweaver puts
    /// a collected look (a Weave) over the piece in a slot, the Outfitter dyes an outfit piece in another colourway.
    /// A Weave is kept per weapon class / per gear line (build 17 item 6, <see cref="WovenLooks"/>): it holds through weapon switches,
    /// gear swaps and re-logs and never spreads to another class or set; a dye stays with its piece. The owner asks; the server checks that the player
    /// stands by the right NPC, runs <see cref="WeaveRules"/>, takes the cost and writes the result into
    /// <see cref="PlayerEquipment"/>, which shows it to everyone. A Weave costs Creds; a dye uses up one Chroma
    /// Cell a piece, from the Pack or from what is stored. Both costs are in <see cref="EconomySettings"/>.
    /// The Holoweaver also puts a Morph over the pet that is out (Morphweave, <see cref="MorphRules"/>): the same
    /// steps and the same fee as a Weave.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerEquipment), typeof(PlayerWeaveLibrary))]
    [RequireComponent(typeof(PlayerItemSources))]
    public sealed class PlayerWeaves : NetworkBehaviour, ISaveParticipant, IBodyWeaves, IMigratingSave
    {
        public const string WeaveSaveKey = "weave";
        private const float ReachPadding = 2.5f;
        private const string ReasonDefeated = "You can't do that while defeated.";
        private const string ReasonWhereWeave = "Visit a Holoweaver to change a Weave.";
        private const string ReasonWhereDye = "Visit an Outfitter to dye gear.";
        private const string ReasonNoCreds = "You don't have enough Creds.";

        [SerializeField] private EconomySettings settings;

        private PlayerInventory inventory;
        private PlayerEquipment equipment;
        private PlayerWeaveLibrary library;
        private PlayerItemSources sources;
        private PlayerWallet wallet;
        private PlayerResources resources;
        private PlayerSaveAgent saveAgent;
        private PlayerPet pet;
        private IGearWearer wearer;
        // This character's gear from before looks were collected has been added to the library.
        private bool collectedOwned;
        // Build 17 item 6: the Weaves kept by key (server body only), and old slot Weaves waiting for their piece.
        private readonly List<WovenLook> woven = new List<WovenLook>();
        private readonly List<UnboundWeave> unbound = new List<UnboundWeave>();

        /// <summary>GM switch: both services work anywhere until the session ends.</summary>
        public static bool GmAnywhere;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => GmAnywhere = false;

        /// <summary>The Weaves of the player this machine controls.</summary>
        public static PlayerWeaves Local => LocalPlayer.Get<PlayerWeaves>();

        public EconomySettings Settings => settings;
        public PlayerEquipment Equipment => equipment;
        public PlayerWeaveLibrary Library => library;
        public long Creds => wallet != null ? wallet.Creds : 0;
        public long WeaveFee => settings != null ? Math.Max(0, settings.weaveApplyFee) : 0;
        public long WeaveClearFee => settings != null ? Math.Max(0, settings.weaveClearFee) : 0;
        /// <summary>The item one dye of a slot uses up (a Prism Cell for a weapon, a Chroma Cell for an outfit piece), or 0 when it is free.</summary>
        public ushort DyeItemIdFor(EquipSlot slot) =>
            settings == null ? (ushort)0 : slot == EquipSlot.Weapon ? settings.weaponDyeItemId : settings.dyeItemId;
        /// <summary>How many of a slot's dye item the player has, carried or stored.</summary>
        public int DyeItemsHeldFor(EquipSlot slot) => DyeItemIdFor(slot) != 0 ? sources.Count(DyeItemIdFor(slot)) : 0;
        public string DyeItemNameFor(EquipSlot slot) => NameOf(DyeItemIdFor(slot), slot == EquipSlot.Weapon ? "Prism Cell" : "Chroma Cell");
        /// <summary>True while the NPC of a service is in reach (or the GM switch is on): its window closes when it stops being so.</summary>
        public bool InReach(NpcOffer offer) => GmAnywhere || NpcInteractable.IsOffered(offer, transform.position, ReachPadding);

        /// <summary>Why this character may not put a collected look on a slot (shown beside it), or null. The same answer on the owner and the server.</summary>
        public string WeaveBlockedReason(EquipSlot slot, ushort lineId)
        {
            ItemDefinition weave = inventory.Catalog != null ? inventory.Catalog.Get(WeaveRules.ItemOf(lineId)) : null;
            return WeaveRules.ApplyBlockedReason(inventory.Gear, slot, equipment.GetDefinition(slot), weave, library.Has(lineId),
                wearer != null ? wearer.Faction : Factions.FactionId.None, wearer != null ? wearer.Body : BodyFit.None);
        }

        /// <summary>The pet system of this player: the pet that is out and the looks a Morph can show.</summary>
        public PlayerPet Pet => pet;

        /// <summary>Why this character may not put a collected Morph over the pet it has out (shown beside it), or null. The same answer on the owner and the server.</summary>
        public string MorphBlockedReason(ushort petItemId, int stage)
        {
            if (pet == null || pet.Catalog == null) return MorphRules.ReasonNotAMorph;
            return MorphRules.ApplyBlockedReason(pet.Pet, pet.Stage, pet.Catalog.Get(petItemId), stage, library.HasMorph(petItemId, stage));
        }

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            equipment = GetComponent<PlayerEquipment>();
            library = GetComponent<PlayerWeaveLibrary>();
            sources = GetComponent<PlayerItemSources>();
            wallet = GetComponent<PlayerWallet>();
            resources = GetComponent<PlayerResources>();
            saveAgent = GetComponent<PlayerSaveAgent>();
            pet = GetComponent<PlayerPet>();
            wearer = GetComponent<IGearWearer>();
        }

        private void OnEnable()
        {
            if (saveAgent != null) saveAgent.Loaded += OnLoaded;
            if (equipment != null) equipment.SlotChanged += OnSlotChanged;
        }

        private void OnDisable()
        {
            if (saveAgent != null) saveAgent.Loaded -= OnLoaded;
            if (equipment != null) equipment.SlotChanged -= OnSlotChanged;
        }

        // A piece came or went: the slots show the Weaves kept for what is worn now.
        private void OnSlotChanged(EquipSlot slot, ushort before, ushort after)
        {
            if (!HasAuthority || !WeaveRules.IsWeaveSlot(slot)) return;
            if (ShowKept() && saveAgent != null) saveAgent.SaveSoon();
        }

        /// <summary>Binds waiting old Weaves and writes the kept Weave of each worn piece into the looks. True when an old Weave was bound.</summary>
        private bool ShowKept()
        {
            ItemCatalog catalog = inventory.Catalog;
            bool bound = unbound.Count > 0 && catalog != null
                && WovenLooks.Bind(unbound, woven, equipment.GetDefinition, catalog.Get);
            EquipmentLooks looks = equipment.Looks;
            EquipmentLooks shown = looks;
            foreach (EquipSlot slot in WeaveRules.WeaveSlots) shown.SetWeave(slot, WovenLooks.WeaveFor(woven, equipment.GetDefinition(slot)));
            if (!shown.Equals(looks)) equipment.ServerSetLooks(shown);
            return bound;
        }

        // ------------------------------------------------------------------ requests (owner)

        /// <summary>Put a collected look over the piece worn in a slot.</summary>
        public void RequestWeave(EquipSlot slot, ushort lineId)
        {
            if (AskServer(KryticalNova.Core.WeaveOpKind.Weave, (byte)slot, lineId, 0)) return;
            if (IsSpawned) WeaveRpc((byte)slot, lineId);
            else ServerWeave(slot, lineId);
        }

        /// <summary>Show the worn piece in a slot as it is again.</summary>
        public void RequestClearWeave(EquipSlot slot)
        {
            if (AskServer(KryticalNova.Core.WeaveOpKind.ClearWeave, (byte)slot, 0, 0)) return;
            if (IsSpawned) ClearWeaveRpc((byte)slot);
            else ServerClearWeave(slot);
        }

        /// <summary>Put a collected Morph (a pet and one of its growth stages) over the pet that is out.</summary>
        public void RequestMorph(ushort petItemId, int stage)
        {
            if (AskServer(KryticalNova.Core.WeaveOpKind.Morph, 0, petItemId, (byte)Mathf.Clamp(stage, 0, PetRules.LastStage))) return;
            if (IsSpawned) MorphRpc(petItemId, (byte)Mathf.Clamp(stage, 0, PetRules.LastStage));
            else ServerMorph(petItemId, stage);
        }

        /// <summary>Show the pet that is out as it is again.</summary>
        public void RequestClearMorph()
        {
            if (AskServer(KryticalNova.Core.WeaveOpKind.ClearMorph, 0, 0, 0)) return;
            if (IsSpawned) ClearMorphRpc();
            else ServerClearMorph();
        }

        /// <summary>Dye the piece shown in a slot in a colourway; 0 gives it its own colours back.</summary>
        public void RequestDye(EquipSlot slot, ushort colourway)
        {
            if (AskServer(KryticalNova.Core.WeaveOpKind.Dye, (byte)slot, colourway, 0)) return;
            if (IsSpawned) DyeRpc((byte)slot, colourway);
            else ServerDye(slot, colourway);
        }

        /// <summary>Show or hide the helmet worn with the outfit (the Equipment page's toggle). Free, anywhere.</summary>
        public void RequestHelmetShown(bool shown)
        {
            if (AskServer(KryticalNova.Core.WeaveOpKind.Helmet, 0, 0, (byte)(shown ? 1 : 0))) return;
            if (IsSpawned) HelmetRpc(shown);
            else ServerHelmet(shown);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HelmetRpc(bool shown)
        {
            if (!RpcBudget.Allow(this, CallKind.Items, nameof(HelmetRpc))) return;
            ServerHelmet(shown);
        }

        /// <summary>Show or hide the worn cape's look (the Equipment page's toggle, as the helmet's). Free, anywhere; the cape keeps its stats.</summary>
        public void RequestCapeShown(bool shown)
        {
            if (AskServer(KryticalNova.Core.WeaveOpKind.Cape, 0, 0, (byte)(shown ? 1 : 0))) return;
            if (IsSpawned) CapeRpc(shown);
            else ServerCape(shown);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CapeRpc(bool shown)
        {
            if (!RpcBudget.Allow(this, CallKind.Items, nameof(CapeRpc))) return;
            ServerCape(shown);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WeaveRpc(byte slot, ushort lineId)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(WeaveRpc))) return;
            ServerWeave((EquipSlot)slot, lineId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ClearWeaveRpc(byte slot)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(ClearWeaveRpc))) return;
            ServerClearWeave((EquipSlot)slot);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DyeRpc(byte slot, ushort colourway)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(DyeRpc))) return;
            ServerDye((EquipSlot)slot, colourway);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void MorphRpc(ushort petItemId, byte stage)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(MorphRpc))) return;
            ServerMorph(petItemId, stage);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ClearMorphRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(ClearMorphRpc))) return;
            ServerClearMorph();
        }

        // ------------------------------------------------------------------ server

        // A client view of the entity netcode never weaves or dyes itself (it would spend its own mirror Pack / wallet); its server body does.
        private bool HasAuthority => (!IsSpawned || IsServer) && !EntityView;
        private bool EntityView => KryticalNova.Core.BodyRoleTag.Of(this) is { Kind: KryticalNova.Core.NetRoleKind.ClientView };
        private bool EntityServer => KryticalNova.Core.BodyRoleTag.Of(this) is { Kind: KryticalNova.Core.NetRoleKind.ServerBody };

        private bool AskServer(KryticalNova.Core.WeaveOpKind op, byte slot, ushort id, byte stage)
        {
            if (!(KryticalNova.Core.BodyRoleTag.RequestsOf(this) is KryticalNova.Core.IBodyRequests requests)) return false;
            requests.WeaveOp(op, slot, id, stage);
            return true;
        }

        /// <summary>A server body of the entity netcode runs its owner's request (NGO's RPC bodies, after the caller's limit; M8 8.3.9).</summary>
        public void RunWeaveOp(KryticalNova.Core.WeaveOpKind op, byte slot, ushort id, byte stage)
        {
            if (!EntityServer) return;
            switch (op)
            {
                case KryticalNova.Core.WeaveOpKind.Weave: ServerWeave((EquipSlot)slot, id); break;
                case KryticalNova.Core.WeaveOpKind.ClearWeave: ServerClearWeave((EquipSlot)slot); break;
                case KryticalNova.Core.WeaveOpKind.Dye: ServerDye((EquipSlot)slot, id); break;
                case KryticalNova.Core.WeaveOpKind.Morph: ServerMorph(id, stage); break;
                case KryticalNova.Core.WeaveOpKind.ClearMorph: ServerClearMorph(); break;
                case KryticalNova.Core.WeaveOpKind.Helmet: ServerHelmet(stage != 0); break;
                case KryticalNova.Core.WeaveOpKind.Cape: ServerCape(stage != 0); break;
            }
        }

        /// <summary>True when this player may use a service now; otherwise they are told why not.</summary>
        private bool Ready(NpcOffer offer, string where)
        {
            if (!HasAuthority || inventory.Catalog == null) return false;
            string blocked = resources != null && !resources.IsAlive ? ReasonDefeated : !InReach(offer) ? where : null;
            if (blocked == null) return true;
            Tell(blocked);
            return false;
        }

        private bool Pay(long fee)
        {
            if (fee <= 0) return true;
            if (wallet == null || !wallet.CanAfford(fee)) { Tell(ReasonNoCreds); return false; }
            return wallet.ServerSpend(fee, CredsSink.Appearance);
        }

        private void Commit(EquipmentLooks looks)
        {
            equipment.ServerSetLooks(looks);
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        private void ServerHelmet(bool shown)
        {
            if (!HasAuthority) return;
            EquipmentLooks looks = equipment.Looks;
            if (looks.helmetHidden == !shown) return;
            looks.helmetHidden = !shown;
            Commit(looks);
        }

        private void ServerCape(bool shown)
        {
            if (!HasAuthority) return;
            EquipmentLooks looks = equipment.Looks;
            if (looks.capeHidden == !shown) return;
            looks.capeHidden = !shown;
            Commit(looks);
        }

        private void ServerWeave(EquipSlot slot, ushort lineId)
        {
            if (!Ready(NpcOffer.Holoweave, ReasonWhereWeave)) return;
            string blocked = WeaveBlockedReason(slot, lineId);
            if (blocked != null) { Tell(blocked); return; }

            ItemDefinition worn = equipment.GetDefinition(slot);
            ItemDefinition weave = inventory.Catalog.Get(WeaveRules.ItemOf(lineId));
            // The look the piece has anyway is no Weave: it is the Weave coming off.
            if (WeaveRules.KeyOf(worn) == lineId) { ServerClearWeave(slot); return; }
            EquipmentLooks looks = equipment.Looks;
            if (looks.GetWeave(slot) == weave.itemId) { Tell("That Weave is already on."); return; }
            if (!WovenLooks.TryKeyFor(worn, out byte kind, out ushort key)) { Tell(WeaveRules.ReasonNotAWeave); return; }
            if (!Pay(WeaveFee)) return;
            WovenLooks.Set(woven, kind, key, weave.itemId);
            looks.SetWeave(slot, weave.itemId);
            Commit(looks);
            Tell($"{weave.displayName} woven over your {SlotName(slot)}.");
        }

        private void ServerClearWeave(EquipSlot slot)
        {
            if (!Ready(NpcOffer.Holoweave, ReasonWhereWeave)) return;
            EquipmentLooks looks = equipment.Looks;
            if (!WeaveRules.IsWeaveSlot(slot) || looks.GetWeave(slot) == 0) { Tell("There is no Weave on that slot."); return; }
            if (!Pay(WeaveClearFee)) return;
            if (WovenLooks.TryKeyFor(equipment.GetDefinition(slot), out byte kind, out ushort key)) WovenLooks.Set(woven, kind, key, 0);
            looks.SetWeave(slot, 0);
            Commit(looks);
            Tell($"The Weave is off your {SlotName(slot)}.");
        }

        private void ServerMorph(ushort petItemId, int stage)
        {
            if (!Ready(NpcOffer.Holoweave, ReasonWhereWeave)) return;
            string blocked = MorphBlockedReason(petItemId, stage);
            if (blocked != null) { Tell(blocked); return; }

            PetDefinition morph = pet.Catalog.Get(petItemId);
            // The look the pet has anyway is no Morph: it is the Morph coming off.
            if (morph == pet.Pet && stage == pet.Stage) { ServerClearMorph(); return; }
            EquipmentLooks looks = equipment.Looks;
            if (looks.pet == petItemId && looks.petStage == stage) { Tell("That Morph is already on."); return; }
            if (!Pay(WeaveFee)) return;
            looks.SetMorph(petItemId, stage);
            Commit(looks);
            Tell($"{MorphRules.NameOf(morph, stage)} morphed over your {pet.Pet.displayName}.");
        }

        private void ServerClearMorph()
        {
            if (!Ready(NpcOffer.Holoweave, ReasonWhereWeave)) return;
            EquipmentLooks looks = equipment.Looks;
            if (looks.pet == 0) { Tell("There is no Morph on your pet."); return; }
            if (!Pay(WeaveClearFee)) return;
            looks.SetMorph(0, 0);
            Commit(looks);
            Tell("The Morph is off your pet.");
        }

        private void ServerDye(EquipSlot slot, ushort colourway)
        {
            if (!Ready(NpcOffer.Dye, ReasonWhereDye)) return;
            ItemDefinition shown = equipment.GetShownDefinition(slot);
            string blocked = WeaveRules.DyeBlockedReason(slot, shown, colourway, equipment.Colourways);
            if (blocked != null) { Tell(blocked); return; }

            // The dye goes on the piece worn in the slot and stays with it, wherever the piece goes.
            ItemDefinition worn = equipment.GetDefinition(slot);
            ItemStack piece = inventory.GetEquipped(slot);
            if (worn == null || !worn.IsLadderGear || piece.IsEmpty || colourway > GearItemState.MaxDye) { Tell(WeaveRules.ReasonNoDye); return; }
            // The colourway a piece was made in is no dye: it is the dye coming off.
            if (colourway == shown.colorTheme) colourway = 0;
            GearItemState state = GearItemState.Of(piece);
            if (state.Dye == colourway) { Tell(colourway == 0 ? "That piece is not dyed." : "It is that colour already."); return; }
            ushort dyeItem = DyeItemIdFor(slot);
            if (colourway != 0 && dyeItem != 0)
            {
                dyeCost[0] = new ItemCost(dyeItem, 1);
                if (!sources.ServerSpend(dyeCost)) { Tell($"You have no {DyeItemNameFor(slot)}."); return; }
            }
            inventory.ServerSetEquippedData(slot, state.WithDye(colourway).PackInto(piece.data));
            if (saveAgent != null) saveAgent.SaveSoon();
            Tell(colourway == 0 ? $"Your {worn.displayName} has its own colours again." : $"Your {worn.displayName} is dyed.");
        }

        private readonly ItemCost[] dyeCost = new ItemCost[1];

        private static string SlotName(EquipSlot slot) => slot.ToString().ToLowerInvariant();

        private string NameOf(ushort itemId, string fallback)
        {
            ItemDefinition item = inventory.Catalog != null ? inventory.Catalog.Get(itemId) : null;
            return item != null ? item.displayName : fallback;
        }

        /// <summary>GM: writes looks without a fee or an NPC. Server (or offline) only.</summary>
        public void ServerSetLooks(EquipmentLooks looks)
        {
            if (!HasAuthority) return;
            // what the GM shows on a worn piece is kept for it, as a paid Weave would be
            foreach (EquipSlot slot in WeaveRules.WeaveSlots)
                if (WovenLooks.TryKeyFor(equipment.GetDefinition(slot), out byte kind, out ushort key)) WovenLooks.Set(woven, kind, key, looks.GetWeave(slot));
            Commit(looks);
        }

        /// <summary>GM: takes the dye off every worn piece. Server (or offline) only.</summary>
        public void ServerClearDyes()
        {
            if (!HasAuthority) return;
            foreach (EquipSlot slot in WeaveRules.DyeSlots)
            {
                ItemDefinition worn = equipment.GetDefinition(slot);
                ItemStack piece = inventory.GetEquipped(slot);
                if (worn == null || !worn.IsLadderGear || piece.IsEmpty) continue;
                inventory.ServerSetEquippedData(slot, GearItemState.Of(piece).WithDye(0).PackInto(piece.data));
            }
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        private void OnLoaded()
        {
            if (!HasAuthority) return;
            // the worn pieces are back now: old Weaves find their piece and every slot shows its kept Weave
            if (ShowKept() && saveAgent != null) saveAgent.SaveSoon();
            if (collectedOwned) return;
            collectedOwned = true;
            library.ServerCollectOwned();
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        // ------------------------------------------------------------------ telling the owner

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (EntityServer) { KryticalNova.Core.OwnerMessages.TryTell(this, text); return; }   // its player's message line
            if (IsSpawned && !IsOwnedByServer) TellRpc(text);
            else if (KryticalNova.Core.BodyRoleTag.IsMine(this, !IsSpawned || IsOwner)) ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ saving

        public string SaveKey => WeaveSaveKey;
        public SaveScope SaveScope => SaveScope.Character;

        public int SaveVersion => WeaveSaveSteps.Version;
        public string Migrate(string json, int from) => WeaveSaveSteps.Migrate(json, from);

        public string CaptureSave()
        {
            var save = new WeaveSaveSteps.V2 { looks = equipment.Looks, collectedOwned = collectedOwned };
            save.woven.AddRange(woven);
            save.unbound.AddRange(unbound);
            return JsonUtility.ToJson(save);
        }

        /// <summary>Puts the Weaves back; one the game no longer has is dropped. A dye is not here: it is on its piece.</summary>
        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            var save = JsonUtility.FromJson<WeaveSaveSteps.V2>(json);
            if (save == null) return;
            collectedOwned = save.collectedOwned;

            EquipmentLooks looks = save.looks;
            ItemCatalog catalog = inventory.Catalog;
            woven.Clear();
            unbound.Clear();
            if (save.woven != null)
                foreach (WovenLook w in save.woven)
                    if (catalog != null && WeaveRules.KeyOf(catalog.Get(w.item)) != 0) woven.Add(w);
            if (save.unbound != null) unbound.AddRange(save.unbound);
            // the slots show what is kept for the pieces worn (worked out again once they are all back, OnLoaded)
            foreach (EquipSlot slot in WeaveRules.WeaveSlots) looks.SetWeave(slot, WovenLooks.WeaveFor(woven, equipment.GetDefinition(slot)));
            if (looks.pet != 0 && (pet == null || pet.Catalog == null || pet.Catalog.Get(looks.pet) == null || looks.petStage > PetRules.LastStage))
                looks.SetMorph(0, 0);
            equipment.ServerSetLooks(looks);
        }
    }
}
