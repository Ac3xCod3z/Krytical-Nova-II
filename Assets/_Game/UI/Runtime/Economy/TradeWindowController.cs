using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The trade window. It shows what the server says the trade is (<see cref="PlayerTrade"/>) and sends requests:
    /// it opens by itself when a trade starts and closes when it ends. While it is open the Pack hands it items
    /// (<see cref="IPackTarget"/>): right-click or drag offers, and offered cells are held until the trade ends.
    /// Also adds TRADE to the target strip and to the player menu.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class TradeWindowController : MonoBehaviour, IPackTarget
    {
        private const int RightButton = 1;
        private const int Slots = 12; // what the window lays out; the server's limit is in EconomySettings

        private VisualElement root, window, mySide, theirSide;
        private Label partnerLabel, theirName, myState, theirState, myCreds, theirCreds, allowance, notice;
        private Button lockButton, confirmButton, stripButton;
        private readonly VisualElement[] myCells = new VisualElement[Slots];
        private readonly VisualElement[] theirCells = new VisualElement[Slots];
        private PlayerTrade trade;
        private bool initialized;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("trade-window");
            if (window == null)
            {
                Debug.LogError("[Trade] EconomyWindows.uxml is not instanced in this UI document.", this);
                return;
            }
            mySide = window.Q("trade-mine");
            theirSide = window.Q("trade-theirs");
            partnerLabel = window.Q<Label>("trade-partner");
            theirName = window.Q<Label>("trade-their-name");
            myState = window.Q<Label>("trade-my-state");
            theirState = window.Q<Label>("trade-their-state");
            myCreds = window.Q<Label>("trade-my-creds");
            theirCreds = window.Q<Label>("trade-their-creds");
            allowance = window.Q<Label>("trade-allowance");
            notice = window.Q<Label>("trade-notice");
            lockButton = window.Q<Button>("trade-lock");
            confirmButton = window.Q<Button>("trade-confirm");

            BuildCells(window.Q("trade-my-grid"), myCells, true);
            BuildCells(window.Q("trade-their-grid"), theirCells, false);
            Bind(window, "close-trade", Cancel);
            Bind(window, "trade-cancel", Cancel);
            Bind(window, "trade-set-creds", AskCreds);
            Bind(window, "trade-lock", () => { if (trade != null) trade.SetLocked(!trade.View.myLocked); });
            Bind(window, "trade-confirm", () => trade?.Confirm());
            SetOpen(false);

            stripButton = root.Q<Button>("duel-target-trade");
            if (stripButton != null) stripButton.clicked += TradeWithSelected;
            PlayerMenuController.Register("Trade", 40, target => PlayerTrade.Local != null, TradeWith);

            PlayerInventory.LocalChanged += Rebind;
            initialized = true;
            Rebind();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            PlayerInventory.LocalChanged -= Rebind;
            if (stripButton != null) stripButton.clicked -= TradeWithSelected;
            BindTrade(null);
            if (GearUIController.Instance != null) GearUIController.Instance.UnregisterPackTarget(this);
        }

        private static void Bind(VisualElement scope, string elementName, Action callback)
        {
            Button button = scope.Q<Button>(elementName);
            if (button != null) button.clicked += callback;
        }

        private void Rebind() => BindTrade(PlayerTrade.Local);

        private void BindTrade(PlayerTrade next)
        {
            if (trade == next) return;
            if (trade != null) trade.Changed -= Refresh;
            trade = next;
            if (trade != null) trade.Changed += Refresh;
            if (initialized) Refresh();
        }

        private void BuildCells(VisualElement grid, VisualElement[] cells, bool own)
        {
            grid.Clear();
            for (int i = 0; i < cells.Length; i++)
            {
                int index = i;
                VisualElement cell = GearUIController.CreateItemVisual(new VisualElement(), true);
                cell.AddToClassList("item-cell");
                grid.Add(cell);
                cells[i] = cell;
                cell.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(cell, index, own));
                cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
                if (!own) continue;
                cell.RegisterCallback<PointerDownEvent>(evt =>
                {
                    evt.StopPropagation();
                    GearUIController.Instance?.SuppressWorldInput();
                    if (evt.button != RightButton || trade == null || index >= trade.Mine.Count) return;
                    GearUIController.Instance?.HideItemTooltip();
                    trade.Withdraw(index);
                });
            }
        }

        // ------------------------------------------------------------------ starting a trade

        private void TradeWithSelected()
        {
            PlayerTrade mine = PlayerTrade.Local;
            if (mine == null) return;
            Transform selected = mine.TryGetComponent(out PointAndClickPlayer controls) ? controls.SelectedTarget : null;
            PlayerTrade target = selected != null ? selected.GetComponentInParent<PlayerTrade>() : null;
            if (target != null && target != mine) mine.Request(target);
        }

        private static void TradeWith(PlayerMenuTarget target)
        {
            PlayerTrade mine = PlayerTrade.Local;
            if (mine == null) return;
            if (target.InSight && target.Player.TryGetComponent(out PlayerTrade theirs)) mine.Request(theirs);
            else mine.Request(target.Name);
        }

        // ------------------------------------------------------------------ the window

        public bool IsOpen => initialized && window.style.display == DisplayStyle.Flex;

        private void SetOpen(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", open);
        }

        private void Update()
        {
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            bool amountOpen = AmountDialogController.Instance != null && AmountDialogController.Instance.IsOpen;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !amountOpen && !HudTyping.IsTyping(root)) Cancel();
        }

        private void Cancel()
        {
            if (trade != null && trade.IsTrading) trade.Cancel();
            GearUIController.Instance?.SuppressWorldInput();
        }

        private ItemDefinition Definition(in ItemStack stack)
        {
            PlayerInventory local = PlayerInventory.Local;
            return stack.IsEmpty || local == null || local.Catalog == null ? null : local.Catalog.Get(stack.itemId);
        }

        private void Refresh()
        {
            if (!initialized) return;
            GearUIController gear = GearUIController.Instance;
            bool trading = trade != null && trade.IsTrading;
            if (trading != IsOpen)
            {
                SetOpen(trading);
                if (gear != null)
                {
                    gear.HideItemTooltip();
                    if (trading)
                    {
                        gear.UnregisterPackTarget(this); // last opened is asked first
                        gear.RegisterPackTarget(this);
                        gear.SetInventoryOpen(true);
                        window.BringToFront();
                    }
                    gear.SuppressWorldInput();
                }
            }
            if (gear != null) gear.RefreshPack(); // offered cells are held, released ones are free again
            if (!trading) return;

            TradeHeader view = trade.View;
            string partner = trade.PartnerName;
            partnerLabel.text = $"WITH {partner.ToUpperInvariant()}";
            theirName.text = $"{partner.ToUpperInvariant()}'S OFFER";
            Fill(myCells, trade.Mine);
            Fill(theirCells, trade.Theirs);
            myCreds.text = WalletRules.Format(view.myCreds);
            theirCreds.text = WalletRules.Format(view.theirCreds);
            SetSide(mySide, myState, view.myLocked, view.myConfirmed);
            SetSide(theirSide, theirState, view.theirLocked, view.theirConfirmed);

            allowance.text = $"Creds: up to {WalletRules.Format(view.perTrade)} each way per trade  ·  " +
                             $"you can send {WalletRules.Format(view.remainingToday)} more today";
            notice.text = trade.LastNotice;
            lockButton.text = view.myLocked ? "UNLOCK" : "LOCK OFFER";
            bool bothLocked = view.myLocked && view.theirLocked;
            confirmButton.SetEnabled(bothLocked && !view.myConfirmed);
            confirmButton.text = view.myConfirmed ? "WAITING FOR THEM" : bothLocked ? "CONFIRM TRADE" : "BOTH MUST LOCK";
        }

        private void Fill(VisualElement[] cells, IReadOnlyList<TradeLine> lines)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ItemStack stack = i < lines.Count ? lines[i].stack : ItemStack.Empty;
                GearUIController.SetItemVisual(cells[i], Definition(stack), stack);
            }
        }

        private static void SetSide(VisualElement side, Label state, bool locked, bool confirmed)
        {
            side.EnableInClassList("locked", locked);
            side.EnableInClassList("confirmed", confirmed);
            state.text = confirmed ? "CONFIRMED" : locked ? "LOCKED" : "CHOOSING";
        }

        private void ShowTooltip(VisualElement cell, int index, bool own)
        {
            GearUIController gear = GearUIController.Instance;
            if (gear == null || trade == null) return;
            IReadOnlyList<TradeLine> lines = own ? trade.Mine : trade.Theirs;
            ItemStack stack = index < lines.Count ? lines[index].stack : ItemStack.Empty;
            ItemDefinition def = Definition(stack);
            if (def == null) gear.HideItemTooltip();
            else gear.ShowItemTooltip(cell, def, stack, own ? "Right-click: take it back" : "What you would receive");
        }

        private void AskCreds()
        {
            PlayerWallet wallet = PlayerWallet.Local;
            if (trade == null || !trade.IsTrading || wallet == null) return;
            TradeHeader view = trade.View;
            long most = Math.Max(0, Math.Min(wallet.Creds, Math.Min(view.perTrade, view.remainingToday)));
            PlayerTrade target = trade;
            AmountDialogController.Ask("Offer Creds",
                $"You carry {WalletRules.Format(wallet.Creds)}. A trade moves up to {WalletRules.Format(view.perTrade)} each way; " +
                $"you can send {WalletRules.Format(view.remainingToday)} more today.",
                0, most, Math.Min(most, view.myCreds), amount => target.SetCreds(amount));
        }

        // ------------------------------------------------------------------ the Pack hands items over (IPackTarget)

        public string QuickHint => "offer in the trade (Shift: choose how many)";

        public bool LocksCell(int cell)
        {
            if (trade == null || !trade.IsTrading) return false;
            IReadOnlyList<TradeLine> lines = trade.Mine;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].cell == cell) return true;
            return false;
        }

        public bool TryDrop(ItemLocation from, Vector2 panelPosition)
        {
            if (!IsOpen || !from.IsBag || !window.worldBound.Contains(panelPosition)) return false;
            trade.Offer(from.index);
            return true;
        }

        public bool TryQuick(ItemLocation from)
        {
            if (!IsOpen || !from.IsBag) return false;
            PlayerInventory local = PlayerInventory.Local;
            ItemStack stack = local != null ? local.Get(from.index) : ItemStack.Empty;
            if (stack.IsEmpty) return false;
            int cell = from.index;
            var keyboard = Keyboard.current;
            bool choose = keyboard != null && keyboard.shiftKey.isPressed && stack.count > 1;
            if (!choose) trade.Offer(cell);
            else
            {
                PlayerTrade target = trade;
                AmountDialogController.Ask($"Offer {Definition(stack)?.displayName}", $"You have {stack.count} in this stack.",
                    1, stack.count, stack.count, amount => target.Offer(cell, (int)amount));
            }
            return true;
        }
    }
}
