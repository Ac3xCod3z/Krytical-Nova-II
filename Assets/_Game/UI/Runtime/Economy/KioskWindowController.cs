using KryticalNova.Player;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Kiosk windows. The player's own [J]: lay out items and prices, pick how long it runs, start it; then see
    /// what is left, the till and the time remaining, collect and close, from anywhere. And another player's shop,
    /// opened by clicking its stand: browse and buy. Also the shop names over the stands. Everything it does is a
    /// request to <see cref="PlayerKiosk"/>; the server decides. While the set-up is open the Pack hands it items
    /// (<see cref="IPackTarget"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class KioskWindowController : MonoBehaviour, IPackTarget
    {
        private const int RightButton = 1;
        private const int Slots = 24; // what the windows lay out; the server's limit is in EconomySettings
        private const float SlowRefreshSeconds = 0.5f;
        private const float BrowsePadding = 3f;
        private const float PlateDistance = 40f;
        private const string BuyPrompt = "kiosk-buy";

        private struct Draft
        {
            public int cell;
            public ItemStack stack;
            public long price;
        }

        private VisualElement root, window, shop, grid, shopGrid, tierRow, nameRow, tillRow, plateLayer;
        private Label status, shopNameLabel, zoneLabel, tillLabel, soldLabel, helpLabel;
        private Label shopTitle, shopOwner, shopLeft, shopCreds;
        private TextField nameField;
        private Button startButton, collectButton, closeShopButton;
        private readonly VisualElement[] cells = new VisualElement[Slots];
        private readonly VisualElement[] shopCells = new VisualElement[Slots];
        private readonly List<Button> tierButtons = new List<Button>(3);
        private readonly List<Draft> draft = new List<Draft>(Slots);
        private readonly Dictionary<KioskStand, Label> plates = new Dictionary<KioskStand, Label>();
        private readonly List<KioskStand> stalePlates = new List<KioskStand>();

        private PlayerKiosk kiosk;
        private PlayerWallet wallet;
        private KioskStand browsing;
        private Camera targetCamera;
        private int tier;
        private float nextSlowRefresh;
        private bool initialized;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("kiosk-window");
            shop = root?.Q("shop-window");
            if (window == null || shop == null)
            {
                Debug.LogError("[Kiosk] EconomyWindows.uxml is not instanced in this UI document.", this);
                return;
            }
            plateLayer = root.Q("kiosk-plates");
            grid = window.Q("kiosk-grid");
            tierRow = window.Q("kiosk-tiers");
            nameRow = window.Q("kiosk-name-row");
            tillRow = window.Q("kiosk-till-row");
            status = window.Q<Label>("kiosk-status");
            shopNameLabel = window.Q<Label>("kiosk-shop-name");
            zoneLabel = window.Q<Label>("kiosk-zone");
            tillLabel = window.Q<Label>("kiosk-till");
            soldLabel = window.Q<Label>("kiosk-sold");
            helpLabel = window.Q<Label>("kiosk-help");
            nameField = window.Q<TextField>("kiosk-name");
            HudTyping.Guard(nameField);
            startButton = window.Q<Button>("kiosk-start");
            collectButton = window.Q<Button>("kiosk-collect");
            closeShopButton = window.Q<Button>("kiosk-close-shop");
            startButton.clicked += StartKiosk;
            collectButton.clicked += () => kiosk?.RequestCollect();
            closeShopButton.clicked += () => kiosk?.RequestClose();
            window.Q<Button>("close-kiosk").clicked += () => SetOwnOpen(false);

            shopGrid = shop.Q("shop-grid");
            shopTitle = shop.Q<Label>("shop-title");
            shopOwner = shop.Q<Label>("shop-owner");
            shopLeft = shop.Q<Label>("shop-left");
            shopCreds = shop.Q<Label>("shop-creds");
            shop.Q<Button>("close-shop").clicked += CloseShop;

            BuildCells(grid, cells, true);
            BuildCells(shopGrid, shopCells, false);
            SetDisplay(window, false);
            SetDisplay(shop, false);
            shopOpen = false;

            KioskStand.Browsed += Browse;
            NpcServices.Register(ServiceIds.Kiosk, NpcOffer.None, "KIOSK", () => SetOwnOpen(true));
            PlayerInventory.LocalChanged += Rebind;
            initialized = true;
            Rebind();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            KioskStand.Browsed -= Browse;
            NpcServices.Unregister(ServiceIds.Kiosk);
            PlayerInventory.LocalChanged -= Rebind;
            Bind(null);
            SetBrowsing(null);
            foreach (Label plate in plates.Values) plate.RemoveFromHierarchy();
            plates.Clear();
            if (GearUIController.Instance != null) GearUIController.Instance.UnregisterPackTarget(this);
        }

        private void Rebind() => Bind(PlayerKiosk.Local);

        private void Bind(PlayerKiosk next)
        {
            if (kiosk == next) return;
            if (kiosk != null) kiosk.Changed -= RefreshOwn;
            if (wallet != null) wallet.Changed -= RefreshShop;
            kiosk = next;
            wallet = next != null ? next.Wallet : null;
            if (kiosk != null) kiosk.Changed += RefreshOwn;
            if (wallet != null) wallet.Changed += RefreshShop;
            draft.Clear();
            BuildTiers();
            if (initialized) RefreshOwn();
        }

        private void BuildCells(VisualElement into, VisualElement[] list, bool own)
        {
            into.Clear();
            for (int i = 0; i < list.Length; i++)
            {
                int index = i;
                VisualElement cell = GearUIController.CreateItemVisual(new VisualElement(), true);
                cell.AddToClassList("item-cell");
                cell.AddToClassList("kiosk-cell");
                var price = new Label { pickingMode = PickingMode.Ignore };
                price.AddToClassList("kiosk-price");
                cell.Add(price);
                into.Add(cell);
                list[i] = cell;
                cell.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(cell, index, own));
                cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
                cell.RegisterCallback<PointerDownEvent>(evt =>
                {
                    evt.StopPropagation();
                    GearUIController.Instance?.SuppressWorldInput();
                    if (evt.button != RightButton) return;
                    GearUIController.Instance?.HideItemTooltip();
                    if (own) RemoveDraft(index); else AskBuy(index);
                });
            }
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            element.EnableInClassList("visible", visible);
        }

        private static bool Shown(VisualElement element) => element != null && element.style.display == DisplayStyle.Flex;

        // Browsing someone's Kiosk, as against the shop window merely shown: the layout editor shows it to have it placed.
        private bool shopOpen;

        private ItemDefinition Definition(in ItemStack stack)
        {
            PlayerInventory local = PlayerInventory.Local;
            return stack.IsEmpty || local == null || local.Catalog == null ? null : local.Catalog.Get(stack.itemId);
        }

        private static void Notify(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ keys and upkeep

        private void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            bool typing = HudTyping.IsTyping(root);
            bool amountOpen = AmountDialogController.Instance != null && AmountDialogController.Instance.IsOpen;
            if (keyboard != null && !typing && !amountOpen)
            {
                if (KeyBindings.Pressed(KeyBindings.Kiosk)) SetOwnOpen(!Shown(window));
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (shopOpen) CloseShop();
                    else if (Shown(window)) SetOwnOpen(false);
                }
            }

            if (Time.unscaledTime < nextSlowRefresh) return;
            nextSlowRefresh = Time.unscaledTime + SlowRefreshSeconds;
            if (Shown(window)) RefreshSlow();
            if (!shopOpen) return;
            PlayerInventory local = PlayerInventory.Local;
            if (browsing == null || !browsing.IsSpawned || local == null) { CloseShop(); return; }
            Vector3 offset = browsing.Position - local.transform.position;
            offset.y = 0f;
            if (offset.magnitude > browsing.InteractRange + BrowsePadding)
            {
                CloseShop();
                Notify("You walked away from the Kiosk.");
            }
            else shopLeft.text = KioskRules.TimeLeft(DateTime.UtcNow.Ticks, browsing.ExpiresUtc) + " left";
        }

        // ------------------------------------------------------------------ own Kiosk

        private bool HasKiosk => kiosk != null && kiosk.View.exists;

        private void SetOwnOpen(bool open)
        {
            if (!initialized || open == Shown(window)) return;
            if (open && kiosk == null) return;
            if (open && shopOpen) CloseShop(); // one Kiosk window at a time: they share a place on screen
            SetDisplay(window, open);
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.HideItemTooltip();
                gear.SuppressWorldInput();
                if (open)
                {
                    gear.UnregisterPackTarget(this); // last opened is asked first
                    gear.RegisterPackTarget(this);
                    if (!HasKiosk) gear.SetInventoryOpen(true);
                    window.BringToFront();
                }
                gear.RefreshPack();
            }
            if (open) RefreshOwn();
            else nameField.Blur();
        }

        private void RefreshOwn()
        {
            if (!initialized) return;
            bool has = HasKiosk;
            if (has && draft.Count > 0) draft.Clear(); // it opened: what was laid out is on the shelf now
            SetDisplay(nameRow, !has);
            SetDisplay(tierRow, !has);
            SetDisplay(zoneLabel, !has);
            SetDisplay(startButton, !has);
            SetDisplay(shopNameLabel, has);
            SetDisplay(tillRow, has);
            SetDisplay(collectButton, has);
            SetDisplay(closeShopButton, has && kiosk.View.open);

            for (int i = 0; i < cells.Length; i++)
            {
                ItemStack stack = ItemStack.Empty;
                long price = 0;
                if (has) { if (i < kiosk.Shelf.Count) { stack = kiosk.Shelf[i].stack; price = kiosk.Shelf[i].unitPrice; } }
                else if (i < draft.Count) { stack = draft[i].stack; price = draft[i].price; }
                Fill(cells[i], stack, price);
            }

            if (has)
            {
                KioskOwnerView view = kiosk.View;
                shopNameLabel.text = kiosk.ShopName;
                tillLabel.text = WalletRules.Format(view.till);
                soldLabel.text = view.soldCount > 0 ? $"SOLD {view.soldCount} FOR {WalletRules.Format(view.soldCreds)}" : "NOTHING SOLD YET";
                collectButton.text = view.open ? "COLLECT TILL" : "COLLECT ALL";
                helpLabel.text = view.open
                    ? "Your stand-in sells for you while you play or are away. The till can be collected from anywhere."
                    : "This Kiosk has closed. Collect returns the unsold items to your Pack and the till to your wallet.";
            }
            else
            {
                helpLabel.text = "Right-click a Pack item to put it up for sale  ·  Right-click a listing to take it off";
                UpdateTiers();
            }
            RefreshSlow();
            if (GearUIController.Instance != null) GearUIController.Instance.RefreshPack();
        }

        /// <summary>What changes with time or with where the player stands: time left, and whether a Kiosk may open here.</summary>
        private void RefreshSlow()
        {
            if (kiosk == null) return;
            if (HasKiosk)
            {
                KioskOwnerView view = kiosk.View;
                status.text = view.open ? $"OPEN  ·  {KioskRules.TimeLeft(DateTime.UtcNow.Ticks, view.expiresUtc).ToUpperInvariant()} LEFT" : "CLOSED";
                return;
            }
            status.text = $"{draft.Count} / {MaxListings}";
            string blocked = KioskService.PlaceBlockedReason(kiosk.transform.position);
            TradeZone zone = TradeZone.KioskZoneAt(kiosk.transform.position);
            zoneLabel.text = blocked ?? $"{(zone != null ? zone.ZoneName : "Here")}: your Kiosk can open on this spot. It will stand where you are standing.";
            startButton.SetEnabled(blocked == null && draft.Count > 0);
        }

        private int MaxListings => kiosk != null && kiosk.Settings != null ? Mathf.Min(Slots, kiosk.Settings.kioskSlots) : Slots;

        private void Fill(VisualElement cell, in ItemStack stack, long price)
        {
            ItemDefinition def = Definition(stack);
            GearUIController.SetItemVisual(cell, def, stack);
            var label = cell.Q<Label>(className: "kiosk-price");
            label.text = def != null ? KioskRules.ShortPrice(price) : string.Empty;
            label.style.display = def != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowTooltip(VisualElement cell, int index, bool own)
        {
            GearUIController gear = GearUIController.Instance;
            if (gear == null) return;
            ItemStack stack = ItemStack.Empty;
            long price = 0;
            string hint;
            if (!own)
            {
                KioskListing listing = browsing != null ? browsing.GetListing(index) : default;
                stack = listing.stack;
                price = listing.unitPrice;
                hint = $"{WalletRules.FormatCreds(price)} each   ·   Right-click: buy";
            }
            else if (HasKiosk)
            {
                if (index < kiosk.Shelf.Count) { stack = kiosk.Shelf[index].stack; price = kiosk.Shelf[index].unitPrice; }
                hint = $"{WalletRules.FormatCreds(price)} each   ·   {stack.count} left";
            }
            else
            {
                if (index < draft.Count) { stack = draft[index].stack; price = draft[index].price; }
                hint = $"{WalletRules.FormatCreds(price)} each   ·   Right-click: take it off";
            }
            ItemDefinition def = Definition(stack);
            if (def == null) gear.HideItemTooltip();
            else gear.ShowItemTooltip(cell, def, stack, hint);
        }

        // ------------------------------------------------------------------ how long it runs

        private void BuildTiers()
        {
            if (tierRow == null) return;
            tierRow.Clear();
            tierButtons.Clear();
            KioskTier[] tiers = kiosk != null && kiosk.Settings != null ? kiosk.Settings.kioskTiers : null;
            if (tiers == null) return;
            for (int i = 0; i < tiers.Length; i++)
            {
                int index = i;
                var button = new Button(() => ChooseTier(index));
                button.AddToClassList("kiosk-tier");
                tierRow.Add(button);
                tierButtons.Add(button);
            }
            tier = 0;
        }

        private int PermitsHeld(in KioskTier option)
        {
            PlayerItemSources local = PlayerItemSources.Local; // a permit kept in the Vault counts
            return option.IsFree || local == null ? 0 : local.Count(option.permitItemId);
        }

        private void UpdateTiers()
        {
            KioskTier[] tiers = kiosk != null && kiosk.Settings != null ? kiosk.Settings.kioskTiers : null;
            if (tiers == null) return;
            for (int i = 0; i < tierButtons.Count && i < tiers.Length; i++)
            {
                KioskTier option = tiers[i];
                int permits = PermitsHeld(option);
                bool locked = !option.IsFree && permits == 0;
                tierButtons[i].text = option.IsFree ? $"{option.days} DAYS\nFREE"
                    : locked ? $"{option.days} DAYS\nPERMIT {option.storePrice}"
                    : $"{option.days} DAYS\nUSES 1 OF {permits} PERMIT{(permits == 1 ? string.Empty : "S")}";
                tierButtons[i].EnableInClassList("locked", locked);
                tierButtons[i].EnableInClassList("selected", i == tier);
            }
        }

        private void ChooseTier(int index)
        {
            KioskTier[] tiers = kiosk != null && kiosk.Settings != null ? kiosk.Settings.kioskTiers : null;
            if (tiers == null || index < 0 || index >= tiers.Length) return;
            KioskTier option = tiers[index];
            if (!option.IsFree && PermitsHeld(option) == 0)
            {
                ItemDefinition permit = PlayerInventory.Local != null && PlayerInventory.Local.Catalog != null
                    ? PlayerInventory.Local.Catalog.Get(option.permitItemId) : null;
                Notify($"A {option.days}-day Kiosk uses up a {(permit != null ? permit.displayName : "Kiosk Permit")} " +
                       $"({option.storePrice} in the Nova Store, which is not open yet).");
                return;
            }
            tier = index;
            UpdateTiers();
        }

        // ------------------------------------------------------------------ laying out the shelf

        private void BeginListing(int packCell)
        {
            if (kiosk == null || HasKiosk) return;
            PlayerInventory local = PlayerInventory.Local;
            ItemStack stack = local != null ? local.Get(packCell) : ItemStack.Empty;
            ItemDefinition def = Definition(stack);
            if (def == null) return;
            foreach (Draft line in draft)
                if (line.cell == packCell) { Notify(TradeRules.ReasonAlreadyOffered); return; }
            if (draft.Count >= MaxListings) { Notify($"A Kiosk holds at most {MaxListings} stacks."); return; }

            if (stack.count > 1)
                AmountDialogController.Ask($"Sell {def.displayName}", $"How many of the {stack.count} in this stack go on sale?",
                    1, stack.count, stack.count, amount => AskPrice(packCell, stack.WithCount((int)amount), def));
            else AskPrice(packCell, stack, def);
        }

        private void AskPrice(int packCell, ItemStack stack, ItemDefinition def)
        {
            long cap = kiosk.PriceCap(stack);
            AmountDialogController.Ask($"Price of {def.displayName}",
                stack.count > 1
                    ? $"For one of them: buyers may take any number of the {stack.count}. The most that can be asked is {WalletRules.Format(cap)}."
                    : $"The most that can be asked for this is {WalletRules.Format(cap)}.",
                1, cap, Math.Max(1, cap / 10), price =>
                {
                    draft.Add(new Draft { cell = packCell, stack = stack, price = price });
                    RefreshOwn();
                });
        }

        private void RemoveDraft(int index)
        {
            if (HasKiosk || index < 0 || index >= draft.Count) return;
            draft.RemoveAt(index);
            RefreshOwn();
        }

        private void StartKiosk()
        {
            if (kiosk == null || HasKiosk || draft.Count == 0) return;
            var offers = new KioskOffer[draft.Count];
            for (int i = 0; i < offers.Length; i++)
                offers[i] = new KioskOffer { cell = (short)draft[i].cell, count = draft[i].stack.count, unitPrice = draft[i].price };
            kiosk.RequestOpen(nameField.value, offers, tier);
            GearUIController.Instance?.SuppressWorldInput();
        }

        // ------------------------------------------------------------------ the Pack hands items over (IPackTarget)

        /// <summary>Only while a Kiosk is being set up: an open one takes nothing more.</summary>
        public bool IsOpen => initialized && Shown(window) && !HasKiosk;

        public string QuickHint => "put up for sale in your Kiosk";

        public bool LocksCell(int cell)
        {
            if (!IsOpen) return false;
            foreach (Draft line in draft)
                if (line.cell == cell) return true;
            return false;
        }

        public bool TryDrop(ItemLocation from, Vector2 panelPosition)
        {
            if (!IsOpen || !from.IsBag || !window.worldBound.Contains(panelPosition)) return false;
            BeginListing(from.index);
            return true;
        }

        public bool TryQuick(ItemLocation from)
        {
            if (!IsOpen || !from.IsBag) return false;
            BeginListing(from.index);
            return true;
        }

        // ------------------------------------------------------------------ somebody else's Kiosk

        private void Browse(KioskStand stand)
        {
            if (!initialized || stand == null) return;
            SetOwnOpen(false);
            SetBrowsing(stand);
            SetDisplay(shop, true);
            shopOpen = true;
            shop.BringToFront();
            GearUIController.Instance?.SuppressWorldInput();
            RefreshShop();
        }

        private void SetBrowsing(KioskStand next)
        {
            if (browsing != null) browsing.Changed -= RefreshShop;
            browsing = next;
            if (browsing != null) browsing.Changed += RefreshShop;
        }

        private void CloseShop()
        {
            if (!initialized) return;
            ClientPresentationRegistry.Prompts?.Hide(BuyPrompt);
            SetBrowsing(null);
            SetDisplay(shop, false);
            shopOpen = false;
            GearUIController gear = GearUIController.Instance;
            if (gear == null) return;
            gear.HideItemTooltip();
            gear.SuppressWorldInput();
        }

        private void RefreshShop()
        {
            if (!initialized || !shopOpen || browsing == null) return;
            shopTitle.text = browsing.ShopName.ToUpperInvariant();
            shopOwner.text = OwnerLine(browsing);
            shopLeft.text = KioskRules.TimeLeft(DateTime.UtcNow.Ticks, browsing.ExpiresUtc) + " left";
            shopCreds.text = WalletRules.Format(wallet != null ? wallet.Creds : 0);
            for (int i = 0; i < shopCells.Length; i++)
            {
                KioskListing listing = browsing.GetListing(i);
                Fill(shopCells[i], listing.stack, listing.unitPrice);
            }
        }

        /// <summary>"Vex's Kiosk" under a shop's own name; nothing when the shop is simply called that.</summary>
        private static string OwnerLine(KioskStand stand)
        {
            string line = $"{stand.OwnerName}'s Kiosk";
            return string.Equals(line, stand.ShopName, StringComparison.OrdinalIgnoreCase) ? string.Empty : line;
        }

        private void AskBuy(int index)
        {
            if (kiosk == null || browsing == null) return;
            KioskListing listing = browsing.GetListing(index);
            ItemDefinition def = Definition(listing.stack);
            if (def == null) return;
            long creds = wallet != null ? wallet.Creds : 0;
            long affordable = listing.unitPrice > 0 ? creds / listing.unitPrice : listing.stack.count;
            if (affordable < 1) { Notify(KioskRules.ReasonNotEnoughCreds); return; }

            if (listing.stack.count > 1)
                AmountDialogController.Ask($"Buy {def.displayName}", $"{WalletRules.FormatCreds(listing.unitPrice)} each. {listing.stack.count} for sale.",
                    1, Math.Min(listing.stack.count, affordable), 1, amount => ConfirmBuy(index, listing, def, (int)amount));
            else ConfirmBuy(index, listing, def, 1);
        }

        /// <summary>Nothing is bought on a single click: the price is said once more first.</summary>
        private void ConfirmBuy(int index, KioskListing listing, ItemDefinition def, int count)
        {
            KioskStand stand = browsing;
            if (stand == null || !KioskRules.TryTotal(listing.unitPrice, count, out long total)) return;
            IRequestPromptSink prompts = ClientPresentationRegistry.Prompts;
            if (prompts == null) { kiosk.RequestBuy(stand, index, listing, count); return; }
            prompts.Show(BuyPrompt, "KIOSK", $"Buy {LootRules.Label(def, count)} for {WalletRules.FormatCreds(total)}?", 20f,
                yes => { if (yes && kiosk != null && stand != null) kiosk.RequestBuy(stand, index, listing, count); });
        }

        // ------------------------------------------------------------------ shop names over the stands

        private void LateUpdate()
        {
            if (!initialized || plateLayer == null || root.panel == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            Vector3 cameraPosition = targetCamera.transform.position;
            IReadOnlyList<KioskStand> stands = KioskStand.Active;
            for (int i = 0; i < stands.Count; i++)
            {
                KioskStand stand = stands[i];
                if (stand == null || !stand.IsSpawned) continue;
                if (!plates.TryGetValue(stand, out Label plate))
                {
                    plate = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
                    plate.AddToClassList("kiosk-plate");
                    plateLayer.Add(plate);
                    plates.Add(stand, plate);
                }
                Vector3 world = stand.PlatePosition;
                Vector3 screen = targetCamera.WorldToScreenPoint(world);
                bool visible = screen.z > 0f && (world - cameraPosition).sqrMagnitude <= PlateDistance * PlateDistance;
                plate.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                string owner = OwnerLine(stand);
                string text = owner.Length > 0 ? $"{stand.ShopName}\n<size=75%><color=#9CACBC>{owner}</color></size>" : stand.ShopName;
                if (plate.text != text) plate.text = text;
                Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                plate.style.left = point.x - plate.resolvedStyle.width * 0.5f;
                plate.style.top = point.y - plate.resolvedStyle.height;
                Hud.HudChrome.HideOver(plate, point);
            }

            stalePlates.Clear();
            foreach (var pair in plates)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled || !pair.Key.IsSpawned) stalePlates.Add(pair.Key);
            foreach (KioskStand gone in stalePlates)
            {
                if (plates.TryGetValue(gone, out Label plate)) plate.RemoveFromHierarchy();
                plates.Remove(gone);
            }
        }
    }
}
