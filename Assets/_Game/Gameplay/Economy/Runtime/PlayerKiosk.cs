using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Pets;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>What the owner is told about their own Kiosk, beside its shelf.</summary>
    public struct KioskOwnerView : INetworkSerializable
    {
        public bool exists;
        public bool open;
        public FixedString64Bytes shopName;
        public long till;
        public long expiresUtc;
        public int soldCount;
        public long soldCreds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref exists);
            serializer.SerializeValue(ref open);
            serializer.SerializeValue(ref shopName);
            serializer.SerializeValue(ref till);
            serializer.SerializeValue(ref expiresUtc);
            serializer.SerializeValue(ref soldCount);
            serializer.SerializeValue(ref soldCreds);
        }
    }

    /// <summary>
    /// A player's side of the Kiosk system: opening one where they stand, closing it, collecting what it earned, and
    /// buying from other players' Kiosks. Everything is a request; <see cref="KioskService"/> on the server decides.
    /// The owner is sent the state of their own Kiosk so the Kiosk window works anywhere in the world.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerItemSources))]
    public sealed class PlayerKiosk : NetworkBehaviour, IBodyKiosk
    {
        [SerializeField] private EconomySettings settings;
        [SerializeField] private KioskStand standPrefab;

        private PlayerInventory inventory;
        private PlayerItemSources sources;
        private PlayerWallet wallet;
        private PlayerResources resources;
        private PlayerIdentity identity;
        private PlayerChat chat;
        private PlayerSaveAgent saveAgent;
        private PlayerPet pet;

        // Owner only: what the server sent last.
        private KioskOwnerView view;
        private KioskListing[] shelf = Array.Empty<KioskListing>();

        /// <summary>Raised on the owner's machine when its own Kiosk changes.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerKiosk Local => LocalPlayer.Get<PlayerKiosk>();

        public EconomySettings Settings => settings;
        public KioskStand StandPrefab => standPrefab;
        public PlayerInventory Inventory => inventory;
        public PlayerItemSources Sources => sources;
        public PlayerWallet Wallet => wallet;
        public bool IsAlive => resources == null || resources.IsAlive;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;
        public string OwnerName => identity != null ? identity.CharacterName : name;
        public string CharacterId => identity != null ? identity.CharacterId : string.Empty;
        public string AccountId => identity != null ? identity.AccountId : string.Empty;
        public CharacterRecipe Look => identity != null && identity.HasIdentity ? identity.State.ToRecipe() : default;
        public PetTuning PetTuning => pet != null && pet.Catalog != null ? pet.Catalog.Tuning : PetTuning.Default;

        /// <summary>The owner's own Kiosk as last told by the server.</summary>
        public KioskOwnerView View => view;
        public string ShopName => view.exists ? view.shopName.ToString() : string.Empty;
        public IReadOnlyList<KioskListing> Shelf => shelf;

        /// <summary>The most one unit of a stack may be listed for. The same answer on the client and the server.</summary>
        public long PriceCap(in ItemStack stack)
        {
            ItemDefinition item = inventory != null && inventory.Catalog != null ? inventory.Catalog.Get(stack.itemId) : null;
            return KioskService.PriceCap(settings, item, stack, PetTuning);
        }

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            sources = GetComponent<PlayerItemSources>();
            wallet = GetComponent<PlayerWallet>();
            resources = GetComponent<PlayerResources>();
            identity = GetComponent<PlayerIdentity>();
            chat = GetComponent<PlayerChat>();
            saveAgent = GetComponent<PlayerSaveAgent>();
            pet = GetComponent<PlayerPet>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            KioskService.ServerRestoreStands(standPrefab, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            if (saveAgent == null) return;
            saveAgent.Loaded += OnCharacterLoaded;
            if (saveAgent.IsLoaded) OnCharacterLoaded();
        }

        public override void OnNetworkDespawn()
        {
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            view = default;
            shelf = Array.Empty<KioskListing>();
            base.OnNetworkDespawn();
        }

        private void OnCharacterLoaded() => KioskService.ServerOwnerArrived(this);

        // ------------------------------------------------------------------ the entity netcode (M7.6 + 8.2.4)

        private bool EntityServer => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ServerBody };
        private IBodyRequests Requests => BodyRoleTag.RequestsOf(this) is IBodyRequests requests ? requests : null;

        /// <summary>True where the game is decided: NGO's server, or the entity netcode's server body.</summary>
        public bool IsServerSide => (IsSpawned && IsServer) || EntityServer;

        // The entity netcode's server body is never network-spawned: NGO's spawn hook (the scene's stands, the owner's arrival) runs here.
        private void Start()
        {
            if (!EntityServer) return;
            KioskService.ServerRestoreStands(standPrefab, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            if (saveAgent == null) return;
            saveAgent.Loaded += OnCharacterLoaded;
            if (saveAgent.IsLoaded) OnCharacterLoaded();
        }

        public override void OnDestroy()
        {
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            base.OnDestroy();
        }

        public void RunOpen(string shopName, BodyKioskOffer[] offers, int tier)
        {
            if (!EntityServer) return;
            var kioskOffers = new KioskOffer[offers != null ? offers.Length : 0];
            for (int i = 0; i < kioskOffers.Length; i++)
                kioskOffers[i] = new KioskOffer { cell = offers[i].cell, count = offers[i].count, unitPrice = offers[i].unitPrice };
            Answer(KioskService.ServerOpen(this, shopName ?? string.Empty, kioskOffers, tier));
        }

        public void RunClose()
        {
            if (EntityServer) Answer(KioskService.ServerClose(this));
        }

        public void RunCollect()
        {
            if (EntityServer) Answer(KioskService.ServerCollect(this));
        }

        public void RunBuy(uint standId, int index, BodyKioskListing expected, int count)
        {
            if (!EntityServer) return;
            KioskStand stand = KioskStand.FindById(standId);
            Answer(stand != null ? KioskService.ServerBuy(this, stand, index, new KioskListing(expected.stack, expected.unitPrice), count) : "That Kiosk has closed.");
        }

        /// <summary>The entity netcode's own view body shows the server's state of its own Kiosk (NGO's ViewRpc).</summary>
        public void MirrorView(in BodyKioskView shown)
        {
            if (!BodyRoleTag.IsLocalClientView(this)) return;
            var header = new KioskOwnerView
            {
                exists = shown.exists, open = shown.open, till = shown.till, expiresUtc = shown.expiresUtc,
                soldCount = shown.soldCount, soldCreds = shown.soldCreds,
            };
            header.shopName.CopyFromTruncated(shown.shopName ?? string.Empty);
            var listings = new KioskListing[shown.shelf != null ? shown.shelf.Length : 0];
            for (int i = 0; i < listings.Length; i++) listings[i] = new KioskListing(shown.shelf[i].stack, shown.shelf[i].unitPrice);
            view = header;
            shelf = listings;
            Changed?.Invoke();
        }

        public IKioskStand ShowStandView(uint serverId, Vector3 position, float yaw) => KioskStand.ShowAsView(standPrefab, serverId, position, yaw);

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Open a Kiosk here with these Pack items. <paramref name="tier"/> is the index of the duration in the economy settings.</summary>
        public void RequestOpen(string shopName, KioskOffer[] offers, int tier)
        {
            if (Requests is { } requests)
            {
                offers ??= Array.Empty<KioskOffer>();
                var asked = new BodyKioskOffer[offers.Length];
                for (int i = 0; i < offers.Length; i++) asked[i] = new BodyKioskOffer { cell = offers[i].cell, count = offers[i].count, unitPrice = offers[i].unitPrice };
                requests.KioskOpen(shopName ?? string.Empty, asked, (byte)Mathf.Clamp(tier, 0, 255));
                return;
            }
            if (IsSpawned && IsOwner) OpenRpc(shopName ?? string.Empty, offers ?? Array.Empty<KioskOffer>(), (byte)Mathf.Clamp(tier, 0, 255));
            else if (!IsSpawned) ClientPresentationRegistry.Messages?.ShowMessage("Kiosks need an online session.");
        }

        /// <summary>Close the Kiosk and take back what is left.</summary>
        public void RequestClose()
        {
            if (Requests is { } requests) { requests.KioskClose(); return; }
            if (IsSpawned && IsOwner) CloseRpc();
        }

        /// <summary>Take the till (and, of a closed Kiosk, the unsold items).</summary>
        public void RequestCollect()
        {
            if (Requests is { } requests) { requests.KioskCollect(); return; }
            if (IsSpawned && IsOwner) CollectRpc();
        }

        /// <summary>Buy from another player's Kiosk: <paramref name="expected"/> is the listing as this machine shows it.</summary>
        public void RequestBuy(KioskStand stand, int index, KioskListing expected, int count)
        {
            if (Requests is { } requests)
            {
                if (stand != null && stand.ServerId != 0)
                    requests.KioskBuy(stand.ServerId, (byte)Mathf.Clamp(index, 0, 255), expected.stack.itemId, expected.stack.count, expected.stack.data, expected.unitPrice,
                        (ushort)Mathf.Clamp(count, 1, ushort.MaxValue));
                return;
            }
            if (stand != null && stand.IsSpawned && IsSpawned && IsOwner)
                BuyRpc(stand.NetworkObject, (byte)Mathf.Clamp(index, 0, 255), expected, (ushort)Mathf.Clamp(count, 1, ushort.MaxValue));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void OpenRpc(string shopName, KioskOffer[] offers, byte tier)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(OpenRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(OpenRpc), shopName)) return;
            // The Kiosk window never offers more stacks than a Kiosk holds.
            if (offers != null && Settings != null && offers.Length > Settings.kioskSlots)
            {
                RpcBudget.Refuse(this, nameof(OpenRpc), $"Listed {offers.Length} stacks in a Kiosk", $"A Kiosk holds {Settings.kioskSlots}");
                return;
            }
            Answer(KioskService.ServerOpen(this, shopName, offers, tier));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CloseRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(CloseRpc))) return;
            Answer(KioskService.ServerClose(this));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CollectRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(CollectRpc))) return;
            Answer(KioskService.ServerCollect(this));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BuyRpc(NetworkObjectReference standReference, byte index, KioskListing expected, ushort count)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(BuyRpc))) return;
            KioskStand stand = standReference.TryGet(out NetworkObject standObject) ? standObject.GetComponent<KioskStand>() : null;
            Answer(stand != null ? KioskService.ServerBuy(this, stand, index, expected, count) : "That Kiosk has closed.");
        }

        /// <summary>A refusal goes to the player as a notice; success was already announced by the service.</summary>
        private void Answer(string refusal)
        {
            if (string.IsNullOrEmpty(refusal)) return;
            if (EntityServer) { OwnerMessages.TryTell(this, refusal); return; }   // its player's message line (M7.6)
            if (IsOwnedByServer) ClientPresentationRegistry.Messages?.ShowMessage(refusal);
            else RefusedRpc(refusal);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void RefusedRpc(string reason) => ClientPresentationRegistry.Messages?.ShowMessage(reason);

        // ------------------------------------------------------------------ server helpers (used by KioskService)

        /// <summary>Server: a line in this player's chat log.</summary>
        public void Tell(string text)
        {
            // a server body of the entity netcode: its player's chat log as NGO (M8 8.4.1); the message line only without chat
            if (EntityServer && chat == null) { OwnerMessages.TryTell(this, text); return; }
            if (chat != null && ((IsSpawned && IsServer) || EntityServer) && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
        }

        /// <summary>Server: writes this character now. A sale or a purchase is never lost to a crash.</summary>
        public void ServerSaveNow()
        {
            if (saveAgent != null) saveAgent.Save();
        }

        /// <summary>
        /// Server: writes this character and the world document <paramref name="world"/> (the Kiosks) as one change (online
        /// phase 4, milestone 3): a sale is in both or in neither.
        /// </summary>
        public void ServerSaveWith(string world) => ServerSave.SaveTogether(saveAgent, world);

        /// <summary>Server: sends the owner the state of their own Kiosk.</summary>
        public void ServerPushView()
        {
            if (!IsServerSide) return;
            KioskRecord record = KioskService.Find(CharacterId);
            var header = new KioskOwnerView();
            KioskListing[] listings = Array.Empty<KioskListing>();
            if (record != null)
            {
                header.exists = true;
                header.open = record.open;
                header.shopName.CopyFromTruncated(record.shopName ?? string.Empty);
                header.till = record.till;
                header.expiresUtc = record.expiresUtc;
                header.soldCount = record.soldCount;
                header.soldCreds = record.soldCreds;
                listings = record.listings.ToArray();
            }
            if (EntityServer)
            {
                var shown = new BodyKioskListing[listings.Length];
                for (int i = 0; i < shown.Length; i++) shown[i] = new BodyKioskListing(listings[i].stack, listings[i].unitPrice);
                KioskOwnerLink.Current?.View(this, new BodyKioskView
                {
                    exists = header.exists, open = header.open, shopName = header.shopName.ToString(), till = header.till,
                    expiresUtc = header.expiresUtc, soldCount = header.soldCount, soldCreds = header.soldCreds, shelf = shown,
                });
                return;
            }
            ViewRpc(header, listings);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ViewRpc(KioskOwnerView header, KioskListing[] listings)
        {
            view = header;
            shelf = listings ?? Array.Empty<KioskListing>();
            Changed?.Invoke();
        }
    }
}
