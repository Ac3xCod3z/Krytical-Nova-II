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
    /// An NPC's shop. Opened from the vendor's dialogue, beside the Pack: the shelf is what that vendor sells to this
    /// character (gear only where the character can wear it), each item with its price. Right-click buys; a stack
    /// asks how many, a single piece of gear says its price once more first. Selling is done from the Pack while the
    /// window is open: right-click or drop an item on the window. Anything Rare or better, a pet and worked gear are
    /// never sold without a notice naming the item and what it would fetch, and a yes.
    /// It sends requests to <see cref="PlayerShopping"/>; the server decides. Prices come from the same rules the
    /// server uses (<see cref="VendorRules"/>). Closed when the player walks away.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class VendorWindowController : MonoBehaviour, IPackTarget
    {
        private const float RangeCheckSeconds = 0.25f;
        private const float PromptSeconds = 20f;
        private const int RightButton = 1;
        private const string WindowName = "vendor-window";
        private const string PackWindowName = "inventory-window";
        private const string BuyPrompt = "vendor-buy";
        private const string SellPrompt = "vendor-sell";
        private const string TooDearClass = "too-dear";

        private VisualElement root, window, grid;
        private Label title, credsLabel;
        private readonly List<VendorOffer> offers = new List<VendorOffer>(64);
        private readonly List<VisualElement> cells = new List<VisualElement>(64);
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private PlayerShopping shopping;
        private VendorStock stock;
        private bool initialized;
        // Open at the vendor, as against merely shown: the layout editor shows the window to have it placed.
        private bool open, previewing;
        private bool openedPack;
        private float nextRangeCheck;
        private long shownCreds = -1;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q(WindowName);
            if (window == null)
            {
                Debug.LogError("[Vendor] EconomyWindows.uxml is not instanced in this UI document.", this);
                return;
            }
            grid = window.Q("vendor-grid");
            title = window.Q<Label>("vendor-title");
            credsLabel = window.Q<Label>("vendor-creds");
            window.Q<Button>("close-vendor").clicked += Close;
            SetOpen(false);

            NpcServices.Register(ServiceIds.Shop, NpcOffer.Shop, "BROWSE WARES", Open);
            PlayerInventory.LocalChanged += Close;
            HudLayoutEditor.RegisterPreview(WindowName, Preview);
            initialized = true;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            Close();
            initialized = false;
            previewing = false;
            HudLayoutEditor.UnregisterPreview(WindowName);
            PlayerInventory.LocalChanged -= Close;
            NpcServices.Unregister(ServiceIds.Shop);
            if (GearUIController.Instance != null) GearUIController.Instance.UnregisterPackTarget(this);
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && open;

        /// <summary>Opens the shop of the vendor whose dialogue asked for it, or of the vendor the player stands by.</summary>
        public void Open()
        {
            if (!initialized) return;
            previewing = false;
            shopping = PlayerShopping.Local;
            if (shopping == null || shopping.Vendors == null)
            {
                Notify("Shops aren't available for this character.");
                return;
            }
            NpcInteractable vendor = NpcServices.Opener != null && (NpcServices.Opener.Offers & NpcOffer.Shop) != 0 ? NpcServices.Opener : shopping.VendorInReach();
            stock = shopping.StockOf(vendor);
            if (stock == null)
            {
                Notify("There is nothing for sale here.");
                return;
            }

            Fill();
            SetOpen(true);
            window.parent?.BringToFront();
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;

            // Selling is done from the Pack: it opens with the shop, and the shop sits beside it.
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.UnregisterPackTarget(this); // last opened is asked first
                gear.RegisterPackTarget(this);
                openedPack = !gear.IsInventoryOpen;
                if (openedPack) gear.SetInventoryOpen(true);
                gear.RefreshPack();
                gear.SuppressWorldInput();
            }
            placement.Open(window, root.Q(PackWindowName));
        }

        public void Close()
        {
            if (!initialized || !open) return;
            ClientPresentationRegistry.Prompts?.Hide(BuyPrompt);
            ClientPresentationRegistry.Prompts?.Hide(SellPrompt);
            SetOpen(false);
            placement.Close();
            stock = null;
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.UnregisterPackTarget(this);
                gear.HideItemTooltip();
                if (openedPack) gear.SetInventoryOpen(false);
                gear.RefreshPack();
                gear.SuppressWorldInput();
            }
            openedPack = false;
        }

        private void SetOpen(bool shown)
        {
            open = shown;
            window.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", shown);
        }

        /// <summary>
        /// The layout editor has the window out to be placed, away from any vendor: it shows a shelf (the first
        /// shop's) so it has its real size, and nothing on it can be bought (the editor's frame lies over it).
        /// </summary>
        private void Preview(bool on)
        {
            if (!initialized || open) return; // open at a vendor: it is its own likeness
            previewing = on;
            if (!on)
            {
                stock = null;
                return;
            }
            shopping = PlayerShopping.Local;
            if (shopping == null || shopping.Vendors == null || shopping.Vendors.stocks.Count == 0) return;
            stock = shopping.Vendors.stocks[0];
            Fill();
        }

        private void Update()
        {
            if (!open) return;
            var keyboard = Keyboard.current;
            bool amountOpen = AmountDialogController.Instance != null && AmountDialogController.Instance.IsOpen;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !amountOpen && !HudTyping.IsTyping(root)) { Close(); return; }
            if (shopping == null || stock == null) { Close(); return; }
            placement.Tick();
            if (shopping.Creds != shownCreds) ShowCreds();
            if (Time.unscaledTime < nextRangeCheck) return;
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;
            if (shopping.InReachOf(stock)) return;
            string name = stock.Title;
            Close();
            Notify($"You walked away from the {name}.");
        }

        // ------------------------------------------------------------------ the shelf

        private void Fill()
        {
            title.text = stock.Title.ToUpperInvariant();
            shopping.List(stock, offers);
            while (cells.Count < offers.Count) cells.Add(CreateCell(cells.Count));
            for (int i = 0; i < cells.Count; i++)
            {
                VisualElement cell = cells[i];
                bool used = i < offers.Count;
                cell.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                GearUIController.SetItemVisual(cell, offers[i].Item, 1);
                cell.Q<Label>(className: "kiosk-price").text = KioskRules.ShortPrice(offers[i].Price);
            }
            ShowCreds();
        }

        private VisualElement CreateCell(int index)
        {
            VisualElement cell = GearUIController.CreateItemVisual(new VisualElement(), true);
            cell.AddToClassList("item-cell");
            cell.AddToClassList("kiosk-cell");
            var price = new Label { pickingMode = PickingMode.Ignore };
            price.AddToClassList("kiosk-price");
            cell.Add(price);
            grid.Add(cell);
            cell.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(cell, index));
            cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                GearUIController.Instance?.SuppressWorldInput();
                if (evt.button != RightButton) return;
                GearUIController.Instance?.HideItemTooltip();
                AskBuy(index, evt.shiftKey);
            });
            return cell;
        }

        /// <summary>The player's Creds, and which prices they do not reach.</summary>
        private void ShowCreds()
        {
            shownCreds = shopping != null ? shopping.Creds : 0;
            credsLabel.text = WalletRules.Format(shownCreds);
            for (int i = 0; i < offers.Count && i < cells.Count; i++)
                cells[i].Q<Label>(className: "kiosk-price").EnableInClassList(TooDearClass, offers[i].Price > shownCreds);
        }

        private void ShowTooltip(VisualElement cell, int index)
        {
            GearUIController gear = GearUIController.Instance;
            if (gear == null || index >= offers.Count) return;
            VendorOffer offer = offers[index];
            string how = offer.Item.IsStackable ? "Right-click: buy one   ·   Shift: choose how many" : "Right-click: buy";
            gear.ShowItemTooltip(cell, offer.Item, new ItemStack(offer.Item.itemId, 1), $"{WalletRules.FormatCreds(offer.Price)} each   ·   {how}");
        }

        // ------------------------------------------------------------------ buying

        private void AskBuy(int index, bool chooseAmount)
        {
            if (!open || shopping == null || stock == null || index >= offers.Count) return;
            VendorOffer offer = offers[index];
            long affordable = offer.Price > 0 ? shopping.Creds / offer.Price : 0;
            if (affordable < 1) { Notify(VendorRules.ReasonNotEnoughCreds); return; }

            VendorStock from = stock;
            if (offer.Item.IsStackable)
            {
                if (!chooseAmount) { shopping.RequestBuy(from, offer.Item, 1); return; }
                long most = Math.Min(Math.Min(affordable, PlayerShopping.MaxPerPurchase), offer.Item.maxStack);
                AmountDialogController.Ask($"Buy {offer.Item.displayName}", $"{WalletRules.FormatCreds(offer.Price)} each.", 1, most, Math.Min(10, most),
                    amount => { if (shopping != null) shopping.RequestBuy(from, offer.Item, (int)amount); });
                return;
            }

            // One piece of gear is not bought on a single click: its price is said once more first.
            IRequestPromptSink prompts = ClientPresentationRegistry.Prompts;
            if (prompts == null) { shopping.RequestBuy(from, offer.Item, 1); return; }
            prompts.Show(BuyPrompt, from.Title.ToUpperInvariant(), $"Buy {offer.Item.displayName} for {WalletRules.FormatCreds(offer.Price)}?", PromptSeconds,
                yes => { if (yes && shopping != null) shopping.RequestBuy(from, offer.Item, 1); });
        }

        // ------------------------------------------------------------------ selling: the Pack hands items over (IPackTarget)

        public string QuickHint => "sell";

        public bool LocksCell(int cell) => false;

        public bool TryDrop(ItemLocation from, Vector2 panelPosition)
        {
            if (!IsOpen || !from.IsBag || !window.worldBound.Contains(panelPosition)) return false;
            AskSell(from.index);
            return true; // dropped on the shop: it never falls through to whatever is behind
        }

        public bool TryQuick(ItemLocation from)
        {
            if (!IsOpen || !from.IsBag) return false;
            AskSell(from.index);
            return true;
        }

        private void AskSell(int cell)
        {
            PlayerInventory pack = PlayerInventory.Local;
            if (shopping == null || pack == null) return;
            ItemStack stack = pack.Get(cell);
            ItemDefinition item = stack.IsEmpty ? null : pack.GetDefinition(cell);
            if (item == null) return;
            long unit = shopping.SellValue(item);
            if (unit <= 0) { Notify(VendorRules.ReasonNotBought); return; }

            // A stack is never sold whole on one click: how many is asked, which is also the moment to think again.
            if (stack.count > 1)
                AmountDialogController.Ask($"Sell {item.displayName}", $"{WalletRules.FormatCreds(unit)} each. You have {stack.count} in this stack.",
                    1, stack.count, stack.count, amount => Sell(cell, stack, item, (int)amount, unit));
            else Sell(cell, stack, item, 1, unit);
        }

        private void Sell(int cell, ItemStack stack, ItemDefinition item, int count, long unit)
        {
            if (shopping == null || !VendorRules.TryTotal(unit, count, out long total)) return;
            if (!shopping.NeedsConfirm(item, stack))
            {
                shopping.RequestSell(cell, count, false);
                return;
            }

            // Rare and better, a pet, worked gear: said back in full, and sold only on a yes.
            IRequestPromptSink prompts = ClientPresentationRegistry.Prompts;
            if (prompts == null) { Notify("That sale has to be confirmed, and nothing here can ask you."); return; }
            prompts.Show(SellPrompt, "SELL TO VENDOR", SaleNotice(item, stack, count, total), PromptSeconds,
                yes => { if (yes && shopping != null) shopping.RequestSell(cell, count, true); });
        }

        /// <summary>What is about to be sold, in full: its rarity, its name, what has been done to it, and the Creds it would fetch.</summary>
        public static string SaleNotice(ItemDefinition item, in ItemStack stack, int count, long total)
        {
            string what = $"{item.rarity} {LootRules.Label(item, count)}";
            if (item.equipSlot == EquipSlot.Pet) what += ", a pet, with all its growth";
            else if (item.IsLadderGear)
            {
                GearItemState state = GearItemState.Of(stack);
                if (!state.IsClean) what += $" ({state})";
            }
            return $"Sell {what} for {WalletRules.FormatCreds(total)}? It cannot be bought back.";
        }

        private static void Notify(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
