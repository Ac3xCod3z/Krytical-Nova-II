using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Pets;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// Every Kiosk on the server: opening, selling, expiring, closing and paying out. The records are a world
    /// document (<see cref="ServerSave"/>), so a Kiosk goes on selling while its owner is offline and comes back
    /// with the server. Runs only where the game is decided; players reach it through <see cref="PlayerKiosk"/>.
    /// </summary>
    public static class KioskService
    {
        public const string WorldName = "kiosks";
        // Room to walk between two stands, and around an NPC.
        private const float StandSpacing = 2.5f;
        // A client sees itself a little ahead of where the server has it.
        private const float ReachPadding = 2f;

        private static readonly Dictionary<string, KioskStand> Stands = new Dictionary<string, KioskStand>(StringComparer.Ordinal);
        private static string restoredScene;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Stands.Clear();
            restoredScene = null;
        }

        private static KioskWorld World => ServerSave.World<KioskWorld>(WorldName);

        private static bool InSession
        {
            get
            {
                var network = NetworkManager.Singleton;
                return network != null && network.IsListening && network.IsServer;
            }
        }

        /// <summary>The Kiosk of a character, open or waiting to be collected, or null.</summary>
        public static KioskRecord Find(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            List<KioskRecord> all = World.kiosks;
            for (int i = 0; i < all.Count; i++)
                if (all[i].ownerCharacterId == characterId) return all[i];
            return null;
        }

        /// <summary>Server: the open Kiosks of this scene get their stands back (once per scene); the ones that ran out meanwhile close.</summary>
        public static void ServerRestoreStands(KioskStand prefab, string scene)
        {
            if (!InSession || prefab == null || restoredScene == scene) return;
            restoredScene = scene;
            Stands.Clear();
            // When this session ends its stands go with it: the next one puts them back.
            NetworkManager network = NetworkManager.Singleton;
            network.OnServerStopped -= OnServerStopped;
            network.OnServerStopped += OnServerStopped;
            long now = DateTime.UtcNow.Ticks;
            bool changed = false;
            foreach (KioskRecord record in World.kiosks)
            {
                if (!record.open || record.scene != scene) continue;
                if (KioskRules.IsExpired(now, record.expiresUtc)) { record.open = false; changed = true; }
                else SpawnStand(prefab, record);
            }
            if (changed) ServerSave.SaveWorld(WorldName);
        }

        private static void OnServerStopped(bool wasHost)
        {
            Stands.Clear();
            restoredScene = null;
        }

        private static void SpawnStand(KioskStand prefab, KioskRecord record)
        {
            KioskStand stand = UnityEngine.Object.Instantiate(prefab, record.position, Quaternion.Euler(0f, record.yaw, 0f));
            stand.ServerBind(record);
            stand.NetworkObject.Spawn(true);
            Stands[record.ownerCharacterId] = stand;
        }

        private static void RemoveStand(KioskRecord record)
        {
            if (!Stands.TryGetValue(record.ownerCharacterId, out KioskStand stand)) return;
            Stands.Remove(record.ownerCharacterId);
            if (stand != null && stand.IsSpawned) stand.NetworkObject.Despawn(true);
        }

        private static PlayerKiosk OnlineOwner(KioskRecord record)
        {
            PlayerSaveAgent agent = ServerSave.FindCharacter(record.ownerCharacterId);
            return agent != null ? agent.GetComponent<PlayerKiosk>() : null;
        }

        // ------------------------------------------------------------------ opening

        /// <summary>The most one unit of a stack may be listed for.</summary>
        public static long PriceCap(EconomySettings settings, ItemDefinition item, in ItemStack stack, in PetTuning petTuning)
        {
            if (settings == null || item == null) return long.MaxValue;
            float growth = item.equipSlot == EquipSlot.Pet ? PetRules.GrowthPercent(PetItemState.Of(stack), petTuning) / 100f : -1f;
            return settings.KioskPriceCap(item.rarity, item.maxStack > 1, growth);
        }

        /// <summary>Why a Kiosk cannot be set up at a point (shown to the player), or null. The same on every machine.</summary>
        public static string PlaceBlockedReason(Vector3 point)
        {
            if (TradeZone.KioskZoneAt(point) == null) return "Kiosks can only be set up in a trade zone.";
            foreach (KioskStand other in KioskStand.Active)
                if (other != null && Flat(other.Position - point) < StandSpacing) return "Too close to another Kiosk. Move a few steps away.";
            foreach (NpcInteractable npc in NpcInteractable.Active)
                if (npc != null && Flat(npc.Position - point) < StandSpacing) return "Too close to a vendor. Move a few steps away.";
            return null;
        }

        private static float Flat(Vector3 offset)
        {
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>Server: opens a Kiosk for a player where they stand. Returns why not (told to them), or null when it opened.</summary>
        public static string ServerOpen(PlayerKiosk owner, string shopName, KioskOffer[] offers, int tierIndex)
        {
            if (!InSession || !owner.IsSpawned) return "Kiosks need an online session.";
            EconomySettings settings = owner.Settings;
            if (settings == null || owner.StandPrefab == null) return "Kiosks are not set up on this server.";
            string characterId = owner.CharacterId;
            if (string.IsNullOrEmpty(characterId)) return "Only a saved character can run a Kiosk.";
            if (!owner.IsAlive) return "You can't open a Kiosk while defeated.";
            KioskRecord existing = Find(characterId);
            if (existing != null) return existing.open ? "You already have a Kiosk open." : "Collect what your last Kiosk left before opening another.";
            string place = PlaceBlockedReason(owner.transform.position);
            if (place != null) return place;
            if (offers == null || offers.Length == 0) return "Put at least one item in your Kiosk.";
            if (offers.Length > settings.kioskSlots) return $"A Kiosk holds at most {settings.kioskSlots} stacks.";
            if (settings.kioskTiers == null || tierIndex < 0 || tierIndex >= settings.kioskTiers.Length) return "Choose how long the Kiosk stays open.";
            KioskTier tier = settings.kioskTiers[tierIndex];

            PlayerInventory inventory = owner.Inventory;
            ItemCatalog catalog = inventory.Catalog;
            PetTuning petTuning = owner.PetTuning;
            var listings = new List<KioskListing>(offers.Length);
            string refusal = null;
            // The permit is used up, from the Pack or from what is stored. It is taken after the listings left the
            // Pack, so a permit put up for sale does not count.
            ItemCost[] permit = tier.IsFree ? null : new[] { new ItemCost(tier.permitItemId, 1) };
            bool taken = owner.Sources.ServerSpend(permit, pack =>
            {
                var seen = new HashSet<int>();
                foreach (KioskOffer offer in offers)
                {
                    if (offer.cell < 0 || offer.cell >= pack.Length || pack[offer.cell].IsEmpty) { refusal = InventoryRules.ReasonEmpty; return false; }
                    if (!seen.Add(offer.cell)) { refusal = TradeRules.ReasonAlreadyOffered; return false; }
                    ItemStack held = pack[offer.cell];
                    int count = offer.count == 0 ? held.count : offer.count;
                    if (count > held.count) { refusal = StorageRules.ReasonNotThatMany; return false; }
                    ItemDefinition item = catalog.Get(held.itemId);
                    if (item == null) { refusal = InventoryRules.ReasonUnknownItem; return false; }
                    refusal = KioskRules.PriceBlockedReason(offer.unitPrice, PriceCap(settings, item, held, petTuning));
                    if (refusal != null) { refusal = $"{item.displayName}: {refusal}"; return false; }
                    listings.Add(new KioskListing(held.WithCount(count), offer.unitPrice));
                    pack[offer.cell] = held.WithCount(held.count - count);
                }
                return true;
            });
            if (!taken && refusal == null && !tier.IsFree)
            {
                ItemDefinition permitItem = catalog.Get(tier.permitItemId);
                refusal = $"{tier.label} needs a {(permitItem != null ? permitItem.displayName : "Kiosk Permit")} in your Pack or Vault.";
            }
            if (!taken) return refusal ?? "Your Kiosk could not be opened.";

            long now = DateTime.UtcNow.Ticks;
            var record = new KioskRecord
            {
                ownerCharacterId = characterId,
                ownerAccountId = owner.AccountId,
                ownerName = owner.OwnerName,
                faction = (int)owner.Faction,
                shopName = KioskRules.ShopName(shopName, owner.OwnerName),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                position = GroundSnap.Snap(owner.transform.position),
                yaw = owner.transform.eulerAngles.y,
                openedUtc = now,
                expiresUtc = KioskRules.ExpiryTicks(now, tier.days),
                open = true,
                listings = listings,
                look = owner.Look,
            };
            World.kiosks.Add(record);
            SpawnStand(owner.StandPrefab, record);
            ServerSave.SaveWorld(WorldName);
            owner.ServerSaveNow();
            owner.ServerPushView();
            owner.Tell($"{record.shopName} is open for {tier.label}. It sells while you are away; collect your earnings from the Kiosk window [T].");
            return null;
        }

        // ------------------------------------------------------------------ buying

        /// <summary>Server: a player buys from a stand. Returns why not (told to them), or null when it was bought.</summary>
        public static string ServerBuy(PlayerKiosk buyer, KioskStand stand, int index, KioskListing expected, int count)
        {
            if (!InSession) return "Kiosks need an online session.";
            KioskRecord record = stand != null ? stand.Record : null;
            if (record == null || !record.open || !stand.IsSpawned) return "That Kiosk has closed.";
            if (KioskRules.IsExpired(DateTime.UtcNow.Ticks, record.expiresUtc)) { ServerExpire(record); return "That Kiosk has closed."; }
            if (!buyer.IsAlive) return "You can't buy while defeated.";
            if (record.ownerCharacterId == buyer.CharacterId && !string.IsNullOrEmpty(buyer.CharacterId)) return "This is your own Kiosk.";
            EconomySettings settings = buyer.Settings;
            float reach = Mathf.Max(stand.InteractRange, settings != null ? settings.kioskRange : 5f) + ReachPadding;
            if (Flat(stand.Position - buyer.transform.position) > reach) return "Move closer to the Kiosk.";
            if (!TradeRules.CanTrade(buyer.Faction, (FactionId)record.faction, TradeZone.CrossFactionAt(stand.Position)))
                return "This Kiosk only sells to its owner's faction.";

            PlayerInventory inventory = buyer.Inventory;
            PlayerWallet wallet = buyer.Wallet;
            if (wallet == null) return "You have no wallet.";
            if (index < 0 || index >= record.listings.Count) return KioskRules.ReasonGone;
            ItemDefinition item = inventory.Catalog.Get(record.listings[index].stack.itemId);
            if (item == null) return InventoryRules.ReasonUnknownItem;
            // A faction's own item (a furnishing) is on show to everyone and sold only to its own, on neutral ground too.
            if (!ItemFactionRules.MayHave(item, buyer.Faction)) return ItemFactionRules.ReasonOtherFaction;

            // Room first: nothing is paid for that cannot be carried.
            var pack = new ItemStack[PlayerInventory.Capacity];
            inventory.CopyPack(pack);
            ItemStack wanted = record.listings[index].stack.WithCount(Mathf.Max(1, count));
            if (StorageRules.RoomFor(pack, wanted, Mathf.Max(1, (int)item.maxStack)) < wanted.count) return InventoryRules.ReasonBagFull;

            // The shelf changes only once the Creds have changed hands.
            var shelf = new List<KioskListing>(record.listings);
            if (!KioskRules.TryBuy(shelf, index, expected, count, wallet.Creds, out ItemStack bought, out long total, out string reason))
                return reason;
            if (!wallet.ServerSpend(total, CredsSink.KioskPurchase)) return KioskRules.ReasonNotEnoughCreds;
            record.listings = shelf;
            inventory.ServerGive(bought);

            long net = settings != null ? settings.AfterTax(total, out long tax) : total;
            record.till += net;
            record.soldCount += bought.count;
            record.soldCreds += net;
            string label = LootRules.Label(item, bought.count);
            buyer.Tell($"Bought {label} from {record.shopName} for {WalletRules.FormatCreds(total)}.");

            PlayerKiosk owner = OnlineOwner(record);
            if (owner != null) owner.Tell($"Kiosk: {buyer.OwnerName} bought {label} for {WalletRules.FormatCreds(total)}. {WalletRules.FormatCreds(net)} after tax is in your till.");
            else record.unseenSales.Add(new KioskSale { itemId = bought.itemId, count = bought.count, creds = net, buyer = buyer.OwnerName, utc = DateTime.UtcNow.Ticks });

            if (record.listings.Count == 0)
            {
                // Nothing left to sell: the stand goes, the till waits.
                record.open = false;
                RemoveStand(record);
                if (owner != null) owner.Tell($"{record.shopName} sold out and closed. Collect your earnings from the Kiosk window [T].");
            }
            else stand.Publish();

            ServerSave.SaveWorld(WorldName);
            buyer.ServerSaveNow();
            if (owner != null) owner.ServerPushView();
            return null;
        }

        // ------------------------------------------------------------------ closing and paying out

        /// <summary>Server: a Kiosk ran out of time. The stand goes; what is left waits for the owner.</summary>
        public static void ServerExpire(KioskRecord record)
        {
            if (record == null || !record.open) return;
            record.open = false;
            RemoveStand(record);
            ServerSave.SaveWorld(WorldName);
            PlayerKiosk owner = OnlineOwner(record);
            if (owner == null) return;
            owner.Tell($"{record.shopName} has closed: its time ran out. Collect what is left from the Kiosk window [T].");
            owner.ServerPushView();
        }

        /// <summary>Server: the owner closes the Kiosk and takes what is left, as far as it fits.</summary>
        public static string ServerClose(PlayerKiosk owner)
        {
            KioskRecord record = Find(owner.CharacterId);
            if (record == null) return "You have no Kiosk.";
            if (record.open)
            {
                record.open = false;
                RemoveStand(record);
            }
            return ServerCollect(owner);
        }

        /// <summary>
        /// Server: the till goes to the owner's wallet; of a closed Kiosk, the unsold items go back to the Pack.
        /// What does not fit stays for the next time. A closed Kiosk with nothing left is forgotten.
        /// </summary>
        public static string ServerCollect(PlayerKiosk owner)
        {
            KioskRecord record = Find(owner.CharacterId);
            if (record == null) return "You have no Kiosk.";
            PlayerWallet wallet = owner.Wallet;
            long paid = wallet != null && record.till > 0 ? wallet.ServerEarn(record.till, CredsSource.KioskSale) : 0;
            record.till -= paid;

            int returned = 0, left = 0;
            if (!record.open)
            {
                for (int i = record.listings.Count - 1; i >= 0; i--)
                {
                    KioskListing listing = record.listings[i];
                    int fitted = owner.Inventory.ServerGive(listing.stack);
                    returned += fitted;
                    listing.stack = listing.stack.WithCount(listing.stack.count - fitted);
                    if (listing.stack.IsEmpty) record.listings.RemoveAt(i);
                    else { record.listings[i] = listing; left += listing.stack.count; }
                }
            }
            record.unseenSales.Clear();
            bool done = !record.open && record.listings.Count == 0 && record.till == 0;
            if (done) World.kiosks.Remove(record);
            ServerSave.SaveWorld(WorldName);
            owner.ServerSaveNow();
            owner.ServerPushView();

            var parts = new List<string>(3);
            if (paid > 0) parts.Add($"{WalletRules.FormatCreds(paid)} collected");
            if (returned > 0) parts.Add($"{returned} unsold item{(returned == 1 ? string.Empty : "s")} returned");
            if (record.till > 0) parts.Add($"{WalletRules.FormatCreds(record.till)} did not fit in your wallet and stays in the till");
            if (left > 0) parts.Add($"{left} item{(left == 1 ? string.Empty : "s")} did not fit in your Pack and wait in the Kiosk");
            owner.Tell(parts.Count > 0 ? "Kiosk: " + string.Join(", ", parts) + "." : record.open ? "Kiosk: nothing to collect yet." : "Kiosk closed.");
            return null;
        }

        /// <summary>Server: the owner came into the world. Tells them what their Kiosk did while they were away.</summary>
        public static void ServerOwnerArrived(PlayerKiosk owner)
        {
            KioskRecord record = Find(owner.CharacterId);
            if (record == null) { owner.ServerPushView(); return; }
            if (record.open && KioskRules.IsExpired(DateTime.UtcNow.Ticks, record.expiresUtc)) ServerExpire(record);
            if (record.unseenSales.Count > 0)
            {
                int items = 0;
                long creds = 0;
                foreach (KioskSale sale in record.unseenSales) { items += sale.count; creds += sale.creds; }
                owner.Tell($"While you were away {record.shopName} sold {items} item{(items == 1 ? string.Empty : "s")} for {WalletRules.FormatCreds(creds)} after tax. Collect it from the Kiosk window [T].");
                record.unseenSales.Clear();
                ServerSave.SaveWorld(WorldName);
            }
            else if (!record.open)
                owner.Tell($"{record.shopName} has closed. What it left is waiting in the Kiosk window [T].");
            owner.ServerPushView();
        }

        /// <summary>GM: every Kiosk the server knows, for a console listing.</summary>
        public static IReadOnlyList<KioskRecord> All => World.kiosks;
    }
}
