using System;
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
    /// The Vault and Deep Vault window: one window showing whichever storage was opened. It only asks: every move
    /// is a request to <see cref="PlayerStorage"/>, which the server checks. Opened from the NPC that offers the
    /// service and closed when the player walks away. While it is open the Pack hands it items
    /// (<see cref="IPackTarget"/>): right-click deposits, dragging drops onto a cell.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class StorageWindowController : MonoBehaviour, IPackTarget
    {
        private const int LeftButton = 0;
        private const int RightButton = 1;
        private const float DragThresholdSquared = 36f;
        private const float RangeCheckSeconds = 0.25f;
        // More forgiving than the server's own check, so the window never closes on a player the server still serves.
        private const float RangePadding = 3.5f;
        private const string WithdrawHint = "Right-click: withdraw   ·   Shift: choose how many   ·   Drag to move";

        private VisualElement root, window, grid;
        private Label title, scope, countLabel, credsLabel;
        private readonly VisualElement[] cells = new VisualElement[StorageRules.Capacity];
        private readonly Button[] pageTabs = new Button[StorageRules.PageCount];
        private int currentPage;
        private PlayerStorage storage;
        private bool initialized;
        // Open at its NPC, as against merely shown: the layout editor shows the window to have it placed.
        private bool open;

        private int pressCell = -1;
        private int pressPointerId = -1;
        private Vector2 pressPosition;
        private bool dragging;
        private float nextRangeCheck;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("storage-window");
            if (window == null)
            {
                Debug.LogError("[Storage] EconomyWindows.uxml is not instanced in this UI document.", this);
                return;
            }
            grid = window.Q("storage-grid");
            title = window.Q<Label>("storage-title");
            scope = window.Q<Label>("storage-scope");
            countLabel = window.Q<Label>("storage-count");
            credsLabel = window.Q<Label>("storage-creds");

            grid.Clear();
            for (int i = 0; i < cells.Length; i++)
            {
                VisualElement cell = GearUIController.CreateItemVisual(new VisualElement(), true);
                cell.AddToClassList("item-cell");
                grid.Add(cell);
                cells[i] = cell;
                BindCell(cell, i);
            }
            for (int i = 0; i < pageTabs.Length; i++)
            {
                int page = i;
                pageTabs[i] = window.Q<Button>($"storage-page-{i}");
                if (pageTabs[i] != null) pageTabs[i].clicked += () => ShowPage(page);
            }
            Bind("close-storage", Close);
            Bind("storage-sort", () => storage?.RequestSort());
            Bind("storage-deposit", AskDepositCreds);
            Bind("storage-withdraw", AskWithdrawCreds);
            SetOpen(false);

            NpcServices.Register(ServiceIds.Vault, NpcOffer.Vault, "OPEN VAULT", () => Open(StorageKind.Vault));
            NpcServices.Register(ServiceIds.DeepVault, NpcOffer.DeepVault, "OPEN DEEP VAULT", () => Open(StorageKind.DeepVault));
            PlayerInventory.LocalChanged += Close;
            initialized = true;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            PlayerInventory.LocalChanged -= Close;
            NpcServices.Unregister(ServiceIds.Vault);
            NpcServices.Unregister(ServiceIds.DeepVault);
            BindStorage(null);
            if (GearUIController.Instance != null) GearUIController.Instance.UnregisterPackTarget(this);
        }

        private void Bind(string elementName, Action callback)
        {
            Button button = window.Q<Button>(elementName);
            if (button != null) button.clicked += callback;
        }

        private void BindStorage(PlayerStorage next)
        {
            if (storage != null) storage.Changed -= Refresh;
            storage = next;
            if (storage != null) storage.Changed += Refresh;
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && open;

        /// <summary>Opens the window on one of the local player's storages.</summary>
        public void Open(StorageKind kind)
        {
            if (!initialized) return;
            PlayerStorage next = PlayerStorage.Local(kind);
            if (next == null)
            {
                ClientPresentationRegistry.Messages?.ShowMessage("Storage isn't available for this character.");
                return;
            }
            CancelDrag();
            BindStorage(next);
            title.text = next.DisplayName.ToUpperInvariant();
            scope.text = kind == StorageKind.Vault ? "THIS CHARACTER" : "ALL YOUR CHARACTERS";
            SetOpen(true);
            window.BringToFront();
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;

            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.UnregisterPackTarget(this); // last opened is asked first
                gear.RegisterPackTarget(this);
                gear.SetInventoryOpen(true);
                gear.RefreshPack();
                gear.SuppressWorldInput();
            }
            Refresh();
        }

        public void Close()
        {
            if (!initialized || !IsOpen) return;
            CancelDrag();
            SetOpen(false);
            BindStorage(null);
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.HideItemTooltip();
                gear.SuppressWorldInput();
            }
        }

        private void SetOpen(bool shown)
        {
            open = shown;
            window.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", shown);
        }

        private void Update()
        {
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            bool amountOpen = AmountDialogController.Instance != null && AmountDialogController.Instance.IsOpen;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !amountOpen && !HudTyping.IsTyping(root))
            {
                if (dragging) CancelDrag();
                else Close();
                return;
            }

            if (storage == null) { Close(); return; }
            if (Time.unscaledTime < nextRangeCheck) return;
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;
            if (PlayerStorage.GmAnywhere || NpcInteractable.IsOffered(storage.Offer, storage.transform.position, RangePadding)) return;
            string name = storage.DisplayName;
            Close();
            ClientPresentationRegistry.Messages?.ShowMessage($"You walked away from the {name}.");
        }

        // ------------------------------------------------------------------ contents

        private void ShowPage(int page)
        {
            currentPage = Mathf.Clamp(page, 0, StorageRules.PageCount - 1);
            for (int i = 0; i < cells.Length; i++)
                cells[i].style.display = i / StorageRules.PageSize == currentPage ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < pageTabs.Length; i++)
                pageTabs[i]?.EnableInClassList("selected", i == currentPage);
        }

        private ItemDefinition Definition(in ItemStack stack)
        {
            PlayerInventory local = PlayerInventory.Local;
            return stack.IsEmpty || local == null || local.Catalog == null ? null : local.Catalog.Get(stack.itemId);
        }

        private void Refresh()
        {
            if (!initialized || storage == null) return;
            for (int i = 0; i < cells.Length; i++)
            {
                ItemStack stack = storage.Get(i);
                GearUIController.SetItemVisual(cells[i], Definition(stack), stack);
            }
            ShowPage(currentPage);
            countLabel.text = $"{storage.UsedCells} / {StorageRules.Capacity}";
            credsLabel.text = WalletRules.Format(storage.Creds);
        }

        private int CellAt(Vector2 panelPosition)
        {
            if (!IsOpen) return -1;
            int first = currentPage * StorageRules.PageSize;
            for (int i = first; i < first + StorageRules.PageSize; i++)
                if (cells[i].worldBound.Contains(panelPosition)) return i;
            return -1;
        }

        // ------------------------------------------------------------------ the Pack hands items over (IPackTarget)

        public string QuickHint => "deposit (Shift: choose how many)";

        public bool LocksCell(int cell) => false;

        public bool TryDrop(ItemLocation from, Vector2 panelPosition)
        {
            if (!IsOpen || storage == null || !from.IsBag || !window.worldBound.Contains(panelPosition)) return false;
            storage.RequestDeposit(from.index, CellAt(panelPosition));
            return true;
        }

        public bool TryQuick(ItemLocation from)
        {
            if (!IsOpen || storage == null || !from.IsBag) return false;
            PlayerInventory local = PlayerInventory.Local;
            ItemStack stack = local != null ? local.Get(from.index) : ItemStack.Empty;
            if (stack.IsEmpty) return false;
            int cell = from.index;
            var keyboard = Keyboard.current;
            bool choose = keyboard != null && keyboard.shiftKey.isPressed && stack.count > 1;
            if (!choose) storage.RequestDeposit(cell);
            else
            {
                PlayerStorage target = storage;
                AmountDialogController.Ask($"Deposit {Definition(stack)?.displayName}", $"You have {stack.count} in this stack.",
                    1, stack.count, stack.count, amount => target.RequestDeposit(cell, -1, (int)amount));
            }
            return true;
        }

        // ------------------------------------------------------------------ the window's own cells

        private void BindCell(VisualElement cell, int index)
        {
            cell.RegisterCallback<PointerDownEvent>(evt => OnCellDown(evt, cell, index));
            cell.RegisterCallback<PointerMoveEvent>(OnCellMove);
            cell.RegisterCallback<PointerUpEvent>(evt => OnCellUp(evt, cell));
            cell.RegisterCallback<PointerCaptureOutEvent>(_ => { if (pressCell == index && !dragging) ResetPress(); });
            cell.RegisterCallback<PointerEnterEvent>(_ => { if (!dragging) ShowTooltip(cell, index); });
            cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
        }

        private void ShowTooltip(VisualElement cell, int index)
        {
            if (storage == null) return;
            ItemStack stack = storage.Get(index);
            GearUIController gear = GearUIController.Instance;
            if (gear == null) return;
            ItemDefinition def = Definition(stack);
            if (def == null) gear.HideItemTooltip();
            else gear.ShowItemTooltip(cell, def, stack, WithdrawHint);
        }

        private void OnCellDown(PointerDownEvent evt, VisualElement cell, int index)
        {
            evt.StopPropagation();
            if (storage == null) return;
            ItemStack stack = storage.Get(index);
            if (stack.IsEmpty) return;
            GearUIController gear = GearUIController.Instance;
            if (gear != null) gear.SuppressWorldInput();

            if (evt.button == RightButton)
            {
                gear?.HideItemTooltip();
                if (!evt.shiftKey || stack.count <= 1) storage.RequestWithdraw(index);
                else
                {
                    PlayerStorage target = storage;
                    AmountDialogController.Ask($"Withdraw {Definition(stack)?.displayName}", $"There are {stack.count} in this stack.",
                        1, stack.count, stack.count, amount => target.RequestWithdraw(index, -1, (int)amount));
                }
                return;
            }
            if (evt.button != LeftButton) return;
            pressCell = index;
            pressPointerId = evt.pointerId;
            pressPosition = evt.position;
            dragging = false;
            cell.CapturePointer(evt.pointerId);
        }

        private void OnCellMove(PointerMoveEvent evt)
        {
            if (pressCell < 0 || evt.pointerId != pressPointerId) return;
            GearUIController gear = GearUIController.Instance;
            if (gear == null) return;
            Vector2 position = evt.position;
            if (!dragging && (position - pressPosition).sqrMagnitude >= DragThresholdSquared)
            {
                ItemDefinition def = storage != null ? Definition(storage.Get(pressCell)) : null;
                if (def == null) { ResetPress(); return; }
                dragging = true;
                gear.HideItemTooltip();
                cells[pressCell].AddToClassList("drag-source");
                gear.ShowGhost(def, position);
            }
            if (!dragging) return;
            gear.MoveGhost(position);
            gear.SuppressWorldInput();
        }

        private void OnCellUp(PointerUpEvent evt, VisualElement cell)
        {
            if (pressCell < 0 || evt.pointerId != pressPointerId) return;
            evt.StopPropagation();
            int from = pressCell;
            bool wasDragging = dragging;
            Vector2 position = evt.position;
            if (cell.HasPointerCapture(evt.pointerId)) cell.ReleasePointer(evt.pointerId);
            EndDragVisuals();
            ResetPress();
            if (!wasDragging || storage == null) return;

            int target = CellAt(position);
            if (target >= 0)
            {
                if (target != from) storage.RequestMove(from, target);
                return;
            }
            GearUIController gear = GearUIController.Instance;
            if (gear == null) return;
            int packCell = gear.PackCellAt(position);
            if (packCell >= 0) storage.RequestWithdraw(from, packCell);
            else if (gear.IsOverPack(position)) storage.RequestWithdraw(from);
        }

        private void EndDragVisuals()
        {
            if (pressCell >= 0) cells[pressCell].RemoveFromClassList("drag-source");
            if (dragging && GearUIController.Instance != null) GearUIController.Instance.HideGhost();
        }

        private void CancelDrag()
        {
            if (pressCell >= 0 && pressPointerId >= 0 && cells[pressCell].HasPointerCapture(pressPointerId))
                cells[pressCell].ReleasePointer(pressPointerId);
            EndDragVisuals();
            ResetPress();
        }

        private void ResetPress()
        {
            pressCell = -1;
            pressPointerId = -1;
            dragging = false;
        }

        // ------------------------------------------------------------------ Creds

        private void AskDepositCreds()
        {
            PlayerWallet wallet = PlayerWallet.Local;
            if (storage == null || wallet == null) return;
            long most = Math.Min(wallet.Creds, storage.CredsRoom);
            if (most <= 0)
            {
                ClientPresentationRegistry.Messages?.ShowMessage(wallet.Creds <= 0
                    ? "You carry no Creds." : $"The {storage.DisplayName} can't hold more Creds.");
                return;
            }
            PlayerStorage target = storage;
            AmountDialogController.Ask("Deposit Creds",
                $"You carry {WalletRules.Format(wallet.Creds)}. The {storage.DisplayName} holds {WalletRules.Format(storage.Creds)}.",
                1, most, most, amount => target.RequestDepositCreds(amount));
        }

        private void AskWithdrawCreds()
        {
            PlayerWallet wallet = PlayerWallet.Local;
            if (storage == null || wallet == null) return;
            long most = Math.Min(storage.Creds, wallet.Room);
            if (most <= 0)
            {
                ClientPresentationRegistry.Messages?.ShowMessage(storage.Creds <= 0
                    ? $"The {storage.DisplayName} holds no Creds." : "Your wallet can't hold more Creds.");
                return;
            }
            PlayerStorage target = storage;
            AmountDialogController.Ask("Withdraw Creds",
                $"The {storage.DisplayName} holds {WalletRules.Format(storage.Creds)}. You carry {WalletRules.Format(wallet.Creds)}.",
                1, most, most, amount => target.RequestWithdrawCreds(amount));
        }
    }
}
