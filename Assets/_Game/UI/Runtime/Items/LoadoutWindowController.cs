using System;
using KryticalNova.Core;
using KryticalNova.Items;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Equipment loadouts (owner, 17 Oct 2026). The Pack's LOADOUTS button opens the ten slots beside the Pack: each
    /// shows its weapon large and the other pieces small (greyed when a piece is neither in the Pack nor worn, so
    /// WEAR would skip it), its name, WEAR, and RENAME / DELETE under the dots. The equipment page's SAVE LOADOUT asks
    /// for a name and a slot, the next free one to start with; a slot that holds a loadout is overwritten only after
    /// a yes. Everything is a request to <see cref="PlayerLoadouts"/>; the server decides and says what it did.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LoadoutWindowController : MonoBehaviour
    {
        private const string WindowName = "loadout-window";
        private const string PackWindowName = "inventory-window";
        private const string PromptId = "loadout";
        private const float PromptSeconds = 20f;

        // The pieces in a row's strip, after the weapon: every slot but the weapon, in the equipment page's order.
        private static readonly EquipSlot[] StripSlots =
            { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Amulet, EquipSlot.Ring, EquipSlot.Cape, EquipSlot.Pet };

        private sealed class Row
        {
            public VisualElement Root, Menu, Weapon, Strip;
            public readonly VisualElement[] Minis = new VisualElement[StripSlots.Length];
            public Label Name, Empty;
            public Button Wear, More;
        }

        private VisualElement root, window, list, dialog, slotsRow;
        private Label dialogTitle, dialogDetail, dialogError, slotsLabel;
        private TextField nameField;
        private Button okButton;
        private readonly Row[] rows = new Row[LoadoutRules.Slots];
        private readonly Button[] slotButtons = new Button[LoadoutRules.Slots];
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private PlayerLoadouts loadouts;
        private PlayerInventory inventory;
        private bool initialized, open, renaming;
        private int dialogSlot = -1;
        private int dialogOpenedFrame = -1;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q(WindowName);
            dialog = root?.Q("loadout-dialog");
            if (window == null || dialog == null)
            {
                Debug.LogError("[Loadouts] Loadouts.uxml is not instanced in this UI document (ItemWindows.uxml).", this);
                return;
            }
            list = window.Q("loadout-list");
            window.Q<Button>("close-loadouts").clicked += Close;
            list.Clear();
            for (int i = 0; i < rows.Length; i++) rows[i] = BuildRow(i);

            dialogTitle = dialog.Q<Label>("loadout-dialog-title");
            dialogDetail = dialog.Q<Label>("loadout-dialog-detail");
            dialogError = dialog.Q<Label>("loadout-error");
            slotsLabel = dialog.Q<Label>("loadout-slots-label");
            slotsRow = dialog.Q("loadout-slots");
            nameField = dialog.Q<TextField>("loadout-name");
            HudTyping.Guard(nameField);
            nameField.RegisterValueChangedCallback(_ => dialogError.text = string.Empty);
            okButton = dialog.Q<Button>("loadout-ok");
            okButton.clicked += ConfirmDialog;
            dialog.Q<Button>("loadout-cancel").clicked += CloseDialog;
            slotsRow.Clear();
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i;
                var button = new Button { text = (i + 1).ToString() };
                button.AddToClassList("loadout-slot");
                button.clicked += () => PickSlot(slot);
                slotsRow.Add(button);
                slotButtons[i] = button;
            }

            Button packButton = root.Q<Button>("inventory-loadouts");
            if (packButton != null) packButton.clicked += Toggle;
            Button saveButton = root.Q<Button>("equipment-save-loadout");
            if (saveButton != null) saveButton.clicked += AskSave;

            window.style.display = DisplayStyle.None;
            dialog.style.display = DisplayStyle.None;
            PlayerInventory.LocalChanged += Bind;
            initialized = true;
            Bind();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            Close();
            CloseDialog();
            Unbind();
            PlayerInventory.LocalChanged -= Bind;
            initialized = false;
        }

        private void Bind()
        {
            Unbind();
            loadouts = PlayerLoadouts.Local;
            inventory = loadouts != null ? loadouts.Inventory : null;
            if (loadouts != null) loadouts.Changed += Refresh;
            if (inventory != null) inventory.Changed += Refresh;
            if (loadouts == null) Close();
            else Refresh();
        }

        private void Unbind()
        {
            if (loadouts != null) loadouts.Changed -= Refresh;
            if (inventory != null) inventory.Changed -= Refresh;
            loadouts = null;
            inventory = null;
        }

        // ------------------------------------------------------------------ the window

        public bool IsOpen => initialized && open;

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (!initialized) return;
            if (loadouts == null) Bind();
            if (loadouts == null) { Notify("Loadouts aren't available for this character."); return; }
            open = true;
            window.style.display = DisplayStyle.Flex;
            window.parent?.BringToFront();
            placement.Open(window, root.Q(PackWindowName));
            Refresh();
            GearUIController.Instance?.SuppressWorldInput();
        }

        public void Close()
        {
            if (!initialized || !open) return;
            open = false;
            window.style.display = DisplayStyle.None;
            placement.Close();
            CloseMenus();
            GearUIController.Instance?.HideItemTooltip();
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (dialog.style.display == DisplayStyle.Flex)
            {
                if (keyboard == null || Time.frameCount == dialogOpenedFrame) return;
                if (keyboard.escapeKey.wasPressedThisFrame) CloseDialog();
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) ConfirmDialog();
                return;
            }
            if (!open) return;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            placement.Tick();
        }

        // ------------------------------------------------------------------ rows

        private Row BuildRow(int index)
        {
            var row = new Row { Root = new VisualElement() };
            row.Root.AddToClassList("loadout-row");
            row.Root.AddToClassList("kn-card");

            row.Weapon = GearUIController.CreateItemVisual(new VisualElement(), false);
            row.Weapon.AddToClassList("item-cell");
            row.Weapon.AddToClassList("loadout-weapon");
            HoverPiece(row.Weapon, index, EquipSlot.Weapon);
            row.Root.Add(row.Weapon);

            var middle = new VisualElement();
            middle.AddToClassList("loadout-middle");
            row.Name = new Label();
            row.Name.AddToClassList("loadout-name");
            middle.Add(row.Name);
            row.Empty = new Label("Empty: save one from the Equipment window.");
            row.Empty.AddToClassList("help");
            row.Empty.AddToClassList("loadout-empty-text");
            middle.Add(row.Empty);
            row.Strip = new VisualElement();
            row.Strip.AddToClassList("loadout-strip");
            for (int i = 0; i < StripSlots.Length; i++)
            {
                VisualElement mini = GearUIController.CreateItemVisual(new VisualElement(), false);
                mini.AddToClassList("item-cell");
                mini.AddToClassList("loadout-mini");
                HoverPiece(mini, index, StripSlots[i]);
                row.Strip.Add(mini);
                row.Minis[i] = mini;
            }
            middle.Add(row.Strip);
            row.Root.Add(middle);

            row.Wear = new Button { text = "WEAR" };
            row.Wear.AddToClassList("kn-button-primary");
            row.Wear.AddToClassList("loadout-wear");
            row.Wear.clicked += () => loadouts?.RequestWear(index);
            row.Root.Add(row.Wear);
            row.More = new Button { text = "⋯", tooltip = "Rename or delete" };
            row.More.AddToClassList("loadout-more");
            row.More.clicked += () => ToggleMenu(index);
            row.Root.Add(row.More);
            list.Add(row.Root);

            row.Menu = new VisualElement();
            row.Menu.AddToClassList("loadout-menu");
            var rename = new Button { text = "RENAME" };
            rename.clicked += () => { CloseMenus(); AskRename(index); };
            row.Menu.Add(rename);
            var delete = new Button { text = "DELETE" };
            delete.clicked += () => { CloseMenus(); AskDelete(index); };
            row.Menu.Add(delete);
            list.Add(row.Menu);
            return row;
        }

        private void HoverPiece(VisualElement cell, int index, EquipSlot slot)
        {
            cell.RegisterCallback<PointerEnterEvent>(_ =>
            {
                ItemStack piece = loadouts != null ? loadouts.Get(index).Piece(slot) : ItemStack.Empty;
                ItemDefinition def = Definition(piece.itemId);
                if (def == null || GearUIController.Instance == null) return;
                GearUIController.Instance.ShowItemTooltip(cell, def, piece, IsAtHand(piece.itemId, slot) ? "In this loadout" : "In this loadout · not in your Pack: WEAR skips it");
            });
            cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
        }

        private void Refresh()
        {
            if (!initialized) return;
            for (int i = 0; i < rows.Length; i++)
            {
                Row row = rows[i];
                LoadoutEntry entry = loadouts != null ? loadouts.Get(i) : default;
                bool used = entry.used;
                row.Root.EnableInClassList("empty", !used);
                row.Name.text = loadouts != null ? loadouts.NameOf(i) : LoadoutRules.DefaultName(i);
                row.Empty.style.display = used ? DisplayStyle.None : DisplayStyle.Flex;
                row.Strip.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                row.Wear.SetEnabled(used);
                row.More.SetEnabled(used);
                if (!used) row.Menu.RemoveFromClassList("open");
                ShowPiece(row.Weapon, entry, EquipSlot.Weapon);
                for (int s = 0; s < StripSlots.Length; s++) ShowPiece(row.Minis[s], entry, StripSlots[s]);
            }
        }

        private void ShowPiece(VisualElement cell, in LoadoutEntry entry, EquipSlot slot)
        {
            ItemStack piece = entry.used ? entry.Piece(slot) : ItemStack.Empty;
            ItemDefinition def = Definition(piece.itemId);
            if (def != null) GearUIController.SetItemVisual(cell, def, piece);
            else GearUIController.SetItemVisual(cell, null, 0);
            cell.EnableInClassList("loadout-missing", def != null && !IsAtHand(piece.itemId, slot));
        }

        private ItemDefinition Definition(ushort itemId) =>
            itemId != 0 && inventory != null && inventory.Catalog != null ? inventory.Catalog.Get(itemId) : null;

        // In the Pack or worn: WEAR can find it.
        private bool IsAtHand(ushort itemId, EquipSlot slot) =>
            inventory != null && (inventory.CountItem(itemId) > 0 || (inventory.Equipment != null && inventory.Equipment.Get(slot) == itemId));

        private void ToggleMenu(int index)
        {
            bool wasOpen = rows[index].Menu.ClassListContains("open");
            CloseMenus();
            if (!wasOpen) rows[index].Menu.AddToClassList("open");
        }

        private void CloseMenus()
        {
            foreach (Row row in rows) row?.Menu?.RemoveFromClassList("open");
        }

        // ------------------------------------------------------------------ save, rename, delete

        /// <summary>The equipment page's SAVE LOADOUT: a name and a slot, the next free one to start with.</summary>
        public void AskSave()
        {
            if (!initialized) return;
            if (loadouts == null) Bind();
            if (loadouts == null) { Notify("Loadouts aren't available for this character."); return; }
            int free = loadouts.FirstFree();
            renaming = false;
            OpenDialog("SAVE LOADOUT", free >= 0
                    ? "Keeps what you wear now. Pick a slot; one that holds a loadout is replaced."
                    : "All ten slots hold a loadout: pick the one to replace.",
                free >= 0 ? free : 0, LoadoutRules.DefaultName(free >= 0 ? free : 0));
        }

        private void AskRename(int index)
        {
            if (loadouts == null || !loadouts.IsUsed(index)) return;
            renaming = true;
            OpenDialog("RENAME LOADOUT", $"A new name for slot {index + 1}.", index, loadouts.NameOf(index));
        }

        private void AskDelete(int index)
        {
            if (loadouts == null || !loadouts.IsUsed(index)) return;
            string name = loadouts.NameOf(index);
            IRequestPromptSink prompts = ClientPresentationRegistry.Prompts;
            if (prompts == null) { loadouts.RequestDelete(index); return; }
            prompts.Show(PromptId, "LOADOUTS", $"Delete {name}? The gear stays where it is.", PromptSeconds,
                yes => { if (yes && loadouts != null) loadouts.RequestDelete(index); });
        }

        private void OpenDialog(string heading, string detail, int slot, string name)
        {
            dialogTitle.text = heading;
            dialogDetail.text = detail;
            dialogError.text = string.Empty;
            okButton.text = renaming ? "RENAME  [ENTER]" : "SAVE  [ENTER]";
            slotsLabel.style.display = renaming ? DisplayStyle.None : DisplayStyle.Flex;
            slotsRow.style.display = renaming ? DisplayStyle.None : DisplayStyle.Flex;
            PickSlot(slot);
            nameField.SetValueWithoutNotify(name ?? string.Empty);
            dialog.style.display = DisplayStyle.Flex;
            dialog.parent?.BringToFront();
            dialog.BringToFront();
            dialogOpenedFrame = Time.frameCount;
            nameField.schedule.Execute(() => { nameField.Focus(); nameField.SelectAll(); });
        }

        private void PickSlot(int slot)
        {
            dialogSlot = Mathf.Clamp(slot, 0, LoadoutRules.Slots - 1);
            for (int i = 0; i < slotButtons.Length; i++)
            {
                bool used = loadouts != null && loadouts.IsUsed(i);
                slotButtons[i].EnableInClassList("chosen", i == dialogSlot);
                slotButtons[i].EnableInClassList("used", used);
                slotButtons[i].tooltip = used ? $"{loadouts.NameOf(i)} (replaced if you save here)" : "Empty";
            }
            // An empty slot's default name follows the slot while the player has not typed one.
            if (!renaming && nameField != null && IsDefaultName(nameField.value))
                nameField.SetValueWithoutNotify(loadouts != null && loadouts.IsUsed(dialogSlot) ? loadouts.NameOf(dialogSlot) : LoadoutRules.DefaultName(dialogSlot));
        }

        private static bool IsDefaultName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            for (int i = 0; i < LoadoutRules.Slots; i++) if (name == LoadoutRules.DefaultName(i)) return true;
            return false;
        }

        private void ConfirmDialog()
        {
            if (loadouts == null || dialogSlot < 0) { CloseDialog(); return; }
            string name = LoadoutRules.CleanName(nameField.value, dialogSlot);
            int slot = dialogSlot;
            bool rename = renaming;
            CloseDialog();
            if (rename) { loadouts.RequestRename(slot, name); return; }
            if (!loadouts.IsUsed(slot)) { loadouts.RequestSave(slot, name); return; }

            IRequestPromptSink prompts = ClientPresentationRegistry.Prompts;
            if (prompts == null) { loadouts.RequestSave(slot, name); return; }
            prompts.Show(PromptId, "LOADOUTS", $"Replace {loadouts.NameOf(slot)} with what you wear now?", PromptSeconds,
                yes => { if (yes && loadouts != null) loadouts.RequestSave(slot, name); });
        }

        private void CloseDialog()
        {
            if (dialog == null || dialog.style.display != DisplayStyle.Flex) return;
            dialog.style.display = DisplayStyle.None;
            nameField.Blur();
            dialogSlot = -1;
            GearUIController.Instance?.SuppressWorldInput();
        }

        private static void Notify(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
