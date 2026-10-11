using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Restyle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Holoweaver's and the Outfitter's window: one window, two services. At the Holoweaver it lists the looks
    /// in the Weave Library for a slot, and puts one over the piece worn there (a Weave). At the Outfitter it lists
    /// the colourways, and dyes the outfit piece a slot shows. A click shows the choice on the equipment page's
    /// doll, which opens beside the window; the button pays for it. It sends requests to
    /// <see cref="PlayerWeaves"/>; the server decides. Closed when the player walks away.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class WeaveWindowController : MonoBehaviour
    {
        private enum Mode { Weave, Dye }

        private const float RangeCheckSeconds = 0.25f;
        private const string WindowName = "weave-window";
        private const string EquipmentWindowName = "equipment-window";
        private const string ChosenClass = "chosen";
        private const string ActiveTabClass = "active";
        private static readonly string[] TabNames = { "weapon", "armor", "gloves", "boots", "cape" };

        /// <summary>One thing that can be picked: a look (by gear line) or a colourway. 0 is the piece's own.</summary>
        private struct Choice
        {
            public ushort key;
            public ItemDefinition item;      // Weave: the piece that shows the look
            public string name;
            public string blocked;           // why it cannot be put on, or null
        }

        private VisualElement root, window, grid;
        private Label title, wornLabel, choiceLabel, credsLabel, reasonLabel, helpLabel;
        private Button act, clear;
        private readonly Button[] tabs = new Button[TabNames.Length];
        private readonly List<Choice> choices = new List<Choice>(64);
        private readonly List<VisualElement> cells = new List<VisualElement>(64);
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private PlayerWeaves weaves;
        private PlayerEquipment equipment;
        private Mode mode;
        private EquipSlot slot = EquipSlot.Armor;
        private int chosen = -1;
        private bool initialized;
        // Open at the NPC, as against merely shown: the layout editor shows the window to have it placed.
        private bool open, previewing;
        private bool openedEquipment;
        private float nextRangeCheck;
        private long shownCreds = -1;
        private int shownDyeItems = -1;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q(WindowName);
            if (window == null)
            {
                Debug.LogError("[Weave] RestyleWindows.uxml is not instanced in this UI document.", this);
                return;
            }
            grid = window.Q("weave-grid");
            title = window.Q<Label>("weave-title");
            wornLabel = window.Q<Label>("weave-worn");
            choiceLabel = window.Q<Label>("weave-choice");
            credsLabel = window.Q<Label>("weave-creds");
            reasonLabel = window.Q<Label>("weave-reason");
            helpLabel = window.Q<Label>("weave-help");
            act = window.Q<Button>("weave-act");
            clear = window.Q<Button>("weave-clear");
            window.Q<Button>("close-weave").clicked += Close;
            act.clicked += Act;
            clear.clicked += Clear;
            for (int i = 0; i < tabs.Length; i++)
            {
                EquipSlot target = WeaveRules.WeaveSlots[i];
                tabs[i] = window.Q<Button>("weave-tab-" + TabNames[i]);
                tabs[i].clicked += () => ShowSlot(target);
            }
            SetOpen(false);

            NpcServices.Register(ServiceIds.Holoweave, NpcOffer.Holoweave, "HOLOWEAVE", () => Open(Mode.Weave));
            NpcServices.Register(ServiceIds.Dye, NpcOffer.Dye, "DYE GEAR", () => Open(Mode.Dye));
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
            NpcServices.Unregister(ServiceIds.Holoweave);
            NpcServices.Unregister(ServiceIds.Dye);
            Bind(null);
        }

        private void Bind(PlayerWeaves next)
        {
            if (weaves != null) weaves.Library.Changed -= Refresh;
            if (equipment != null)
            {
                equipment.SlotChanged -= OnWornChanged;
                equipment.LookChanged -= OnLookChanged;
            }
            weaves = next;
            equipment = next != null ? next.Equipment : null;
            if (weaves != null) weaves.Library.Changed += Refresh;
            if (equipment != null)
            {
                equipment.SlotChanged += OnWornChanged;
                equipment.LookChanged += OnLookChanged;
            }
        }

        private void OnWornChanged(EquipSlot changed, ushort previous, ushort next) => Refresh();

        // What was paid for is on: nothing is picked any more, and the doll shows the character as it now is.
        private void OnLookChanged(EquipSlot changed)
        {
            chosen = -1;
            Refresh();
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && open;

        private NpcOffer Offer => mode == Mode.Weave ? NpcOffer.Holoweave : NpcOffer.Dye;

        private void Open(Mode which)
        {
            if (!initialized) return;
            previewing = false;
            if (open) Close();
            Bind(PlayerWeaves.Local);
            if (weaves == null)
            {
                Notify(which == Mode.Weave ? "Weaves aren't available for this character." : "Dyes aren't available for this character.");
                return;
            }
            mode = which;
            title.text = mode == Mode.Weave ? "HOLOWEAVER" : "OUTFITTER";
            helpLabel.text = mode == Mode.Weave
                ? "A Weave shows another look over what you wear  ·  Its stats, reach and glow stay your own gear's"
                : "One Chroma Cell dyes one outfit piece, one Prism Cell a weapon  ·  The dye stays on the piece, wherever it goes";
            chosen = -1;

            SetOpen(true);
            window.parent?.BringToFront();
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;

            // The choice is judged on the doll: the equipment page opens with the window, which sits beside it.
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                openedEquipment = !gear.IsEquipmentOpen;
                bool packWasOpen = gear.IsInventoryOpen;
                gear.SetEquipmentOpen(true);
                if (!packWasOpen) gear.SetInventoryOpen(false); // the page brings the Pack with it; it is not needed here
                gear.SuppressWorldInput();
            }
            placement.Open(window, root.Q(EquipmentWindowName));
            Refresh();
        }

        public void Close()
        {
            if (!initialized || !open) return;
            SetOpen(false);
            placement.Close();
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.ClearPreview();
                gear.HideItemTooltip();
                if (openedEquipment) gear.SetEquipmentOpen(false);
                gear.SuppressWorldInput();
            }
            openedEquipment = false;
            chosen = -1;
            Bind(null);
        }

        private void SetOpen(bool shown)
        {
            open = shown;
            window.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", shown);
        }

        /// <summary>The layout editor has the window out to be placed, away from any NPC: it shows what it would at a Holoweaver.</summary>
        private void Preview(bool on)
        {
            if (!initialized || open) return; // open at an NPC: it is its own likeness
            previewing = on;
            if (!on) { Bind(null); return; }
            Bind(PlayerWeaves.Local);
            if (weaves == null) return;
            mode = Mode.Weave;
            Refresh();
        }

        private void Update()
        {
            if (!open) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            if (weaves == null) { Close(); return; }
            placement.Tick();
            if (weaves.Creds != shownCreds || (mode == Mode.Dye && weaves.DyeItemsHeldFor(slot) != shownDyeItems)) ShowAction();
            if (Time.unscaledTime < nextRangeCheck) return;
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;
            if (weaves.InReach(Offer)) return;
            string who = mode == Mode.Weave ? "Holoweaver" : "Outfitter";
            Close();
            Notify($"You walked away from the {who}.");
        }

        // ------------------------------------------------------------------ what can be picked

        private void ShowSlot(EquipSlot target)
        {
            if (slot == target) return;
            slot = target;
            chosen = -1;
            GearUIController.Instance?.ClearPreview();
            Refresh();
        }

        private void Refresh()
        {
            if (weaves == null || equipment == null || (!open && !previewing)) return;
            for (int i = 0; i < tabs.Length; i++) tabs[i].EnableInClassList(ActiveTabClass, WeaveRules.WeaveSlots[i] == slot);

            ItemDefinition worn = equipment.GetDefinition(slot);
            ItemDefinition shown = equipment.GetShownDefinition(slot);
            if (worn == null) wornLabel.text = "Nothing is worn in this slot.";
            else if (mode == Mode.Weave)
                wornLabel.text = shown != worn ? $"Wearing {worn.displayName}\nShowing {shown.displayName}" : $"Wearing {worn.displayName}\nNo Weave on it";
            else
                wornLabel.text = $"Showing {shown.displayName}\n{(equipment.Looks.GetDye(slot) != 0 ? "Dyed " + ColourwayName(equipment.Looks.GetDye(slot)) : "In its own colours")}";

            choices.Clear();
            if (mode == Mode.Weave) ListWeaves(worn); else ListColourways(shown);
            if (chosen >= choices.Count) chosen = -1;

            while (cells.Count < choices.Count) cells.Add(CreateCell(cells.Count));
            for (int i = 0; i < cells.Count; i++)
            {
                bool used = i < choices.Count;
                cells[i].style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (used) Dress(cells[i], i);
            }
            ShowAction();
        }

        /// <summary>The worn piece's own look first, then every collected look of the slot: those this character can put on before those it cannot.</summary>
        private void ListWeaves(ItemDefinition worn)
        {
            ItemCatalog catalog = equipment.Catalog;
            if (catalog == null) return;
            if (worn != null) choices.Add(new Choice { key = 0, item = worn, name = "Its own look" });
            int first = choices.Count, usable = first;
            foreach (ushort line in weaves.Library.Lines)
            {
                ItemDefinition item = catalog.Get(WeaveRules.ItemOf(line));
                if (item == null || item.equipSlot != slot) continue;
                if (worn != null && WeaveRules.KeyOf(worn) == line) continue; // the first cell
                var choice = new Choice { key = line, item = item, name = item.displayName, blocked = weaves.WeaveBlockedReason(slot, line) };
                if (choice.blocked == null) choices.Insert(usable++, choice);
                else choices.Add(choice);
            }
            choices.Sort(first, usable - first, ByRarityThenName);
            choices.Sort(usable, choices.Count - usable, ByRarityThenName);
        }

        private static readonly IComparer<Choice> ByRarityThenName = Comparer<Choice>.Create((a, b) =>
        {
            int rarity = ((int)a.item.rarity).CompareTo((int)b.item.rarity);
            return rarity != 0 ? rarity : string.CompareOrdinal(a.name, b.name);
        });

        private void ListColourways(ItemDefinition shown)
        {
            GearThemeLibrary colourways = equipment.Colourways;
            if (shown == null || colourways == null) return;
            string blocked = WeaveRules.DyeBlockedReason(slot, shown, 0, colourways);
            choices.Add(new Choice { key = 0, name = "Its own colours", blocked = blocked });
            foreach (GearThemeLibrary.Theme theme in colourways.Themes)
            {
                if (!theme.IsValid || theme.id == shown.colorTheme) continue; // the colourway it was made in is the first cell
                choices.Add(new Choice { key = theme.id, name = ColourwayName(theme.id), blocked = blocked });
            }
        }

        /// <summary>"ELVN_Sunlord" reads "Sunlord": the pack code in front is for the gear table, not for players.</summary>
        private string ColourwayName(ushort id)
        {
            GearThemeLibrary colourways = equipment != null ? equipment.Colourways : null;
            return (colourways != null ? colourways.DisplayNameOf(id) : null) ?? "Colourway";
        }

        // ------------------------------------------------------------------ cells

        private VisualElement CreateCell(int index)
        {
            var cell = new VisualElement();
            grid.Add(cell);
            cell.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(cell, index));
            cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                GearUIController.Instance?.SuppressWorldInput();
                if (evt.button == 0) Choose(index);
            });
            return cell;
        }

        /// <summary>A cell is rebuilt for what it now stands for: a look is an item cell, a colourway a strip of its colours.</summary>
        private void Dress(VisualElement cell, int index)
        {
            Choice choice = choices[index];
            cell.Clear();
            cell.ClearClassList();
            bool on = mode == Mode.Weave ? IsOn(choice) : equipment.Looks.GetDye(slot) == choice.key;
            if (mode == Mode.Weave)
            {
                cell.AddToClassList("item-cell");
                cell.AddToClassList("weave-cell");
                GearUIController.CreateItemVisual(cell, false);
                GearUIController.SetItemVisual(cell, choice.item, 1, null, choice.blocked != null);
                if (choice.key == 0) AddLabel(cell, "OWN", "weave-own");
            }
            else
            {
                cell.AddToClassList("weave-chip");
                var colours = new VisualElement { pickingMode = PickingMode.Ignore };
                colours.AddToClassList("weave-chip-colours");
                if (choice.key != 0 && equipment.Colourways.TryGet(choice.key, out GearThemeLibrary.Theme theme))
                    foreach (Color32 colour in new[] { theme.primary, theme.secondary, theme.under, theme.accent })
                    {
                        var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                        swatch.AddToClassList("weave-chip-colour");
                        swatch.style.backgroundColor = (Color)new Color32(colour.r, colour.g, colour.b, 255);
                        colours.Add(swatch);
                    }
                cell.Add(colours);
                AddLabel(cell, choice.name, "weave-chip-name");
                cell.style.opacity = choice.blocked != null ? 0.4f : 1f;
            }
            if (on) AddLabel(cell, "ON", "weave-on");
            cell.EnableInClassList(ChosenClass, index == chosen);
        }

        private static void AddLabel(VisualElement parent, string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
        }

        /// <summary>True for the look the slot shows now.</summary>
        private bool IsOn(in Choice choice)
        {
            ushort weave = equipment.Looks.GetWeave(slot);
            ItemDefinition shown = equipment.GetShownDefinition(slot);
            if (choice.key == 0) return weave == 0 || shown == equipment.GetDefinition(slot);
            return shown != null && shown.itemId == WeaveRules.ItemOf(choice.key) && weave == shown.itemId;
        }

        private void ShowTooltip(VisualElement cell, int index)
        {
            GearUIController gear = GearUIController.Instance;
            if (gear == null || index >= choices.Count || mode != Mode.Weave) return;
            Choice choice = choices[index];
            if (choice.item == null) return;
            gear.ShowItemTooltip(cell, choice.item, new ItemStack(choice.item.itemId, 1), choice.blocked ?? "Click to see it on your character");
        }

        // ------------------------------------------------------------------ picking and paying

        private void Choose(int index)
        {
            if ((!open && !previewing) || index >= choices.Count) return;
            chosen = index;
            for (int i = 0; i < choices.Count && i < cells.Count; i++) cells[i].EnableInClassList(ChosenClass, i == chosen);
            ShowOnDoll();
            ShowAction();
        }

        /// <summary>The doll wears what is picked: the look in the slot's dye, or the piece shown in the picked colourway.</summary>
        private void ShowOnDoll()
        {
            GearUIController gear = GearUIController.Instance;
            if (gear == null || !open || chosen < 0) return;
            Choice choice = choices[chosen];
            if (mode == Mode.Weave)
            {
                if (choice.item != null) gear.PreviewPiece(choice.item, equipment.GetDyeCells(slot), $"Weave: {choice.name}", overWorn: true);
                return;
            }
            ItemDefinition shown = equipment.GetShownDefinition(slot);
            if (shown == null) return;
            Color32[] cells = choice.key != 0 && equipment.Colourways.TryGet(choice.key, out GearThemeLibrary.Theme theme) ? theme.cells : null;
            gear.PreviewPiece(shown, cells, $"Dye: {choice.name}", colourway: choice.key);
        }

        /// <summary>The button, its price, and why it would not work.</summary>
        private void ShowAction()
        {
            if (weaves == null || equipment == null) return;
            shownCreds = weaves.Creds;
            credsLabel.text = WalletRules.Format(shownCreds);

            bool weaveOn = mode == Mode.Weave && equipment.Looks.GetWeave(slot) != 0 && equipment.GetShownDefinition(slot) != equipment.GetDefinition(slot);
            bool dyeOn = mode == Mode.Dye && equipment.Looks.GetDye(slot) != 0;
            clear.text = mode == Mode.Weave
                ? weaves.WeaveClearFee > 0 ? $"REMOVE WEAVE   ·   {WalletRules.FormatCreds(weaves.WeaveClearFee)}" : "REMOVE WEAVE"
                : "REMOVE DYE";
            clear.style.display = weaveOn || dyeOn ? DisplayStyle.Flex : DisplayStyle.None;

            // A Weave costs Creds; a dye uses up one cell (a Prism Cell for a weapon), wherever the player keeps them.
            long fee = mode == Mode.Weave ? weaves.WeaveFee : 0;
            shownDyeItems = weaves.DyeItemsHeldFor(slot);
            bool dyeCosts = mode == Mode.Dye && weaves.DyeItemIdFor(slot) != 0;
            string dyeItemName = weaves.DyeItemNameFor(slot);
            act.text = mode == Mode.Weave ? $"WEAVE   ·   {WalletRules.FormatCreds(fee)}"
                : dyeCosts ? $"DYE   ·   1 {dyeItemName} (you have {shownDyeItems})" : "DYE";

            string reason = null;
            bool pickedOwn = chosen >= 0 && choices[chosen].key == 0;
            if (chosen < 0)
            {
                choiceLabel.text = choices.Count > 1 ? (mode == Mode.Weave ? "Pick a look to see it on your character." : "Pick a colourway to see it on your character.")
                    : mode == Mode.Weave && equipment.GetDefinition(slot) != null ? "No other look for this slot is in your Weave Library yet." : string.Empty;
            }
            else
            {
                Choice choice = choices[chosen];
                choiceLabel.text = choice.name;
                reason = choice.blocked;
                bool on = mode == Mode.Weave ? IsOn(choice) : equipment.Looks.GetDye(slot) == choice.key;
                if (reason == null && on) reason = mode == Mode.Weave ? "That is what the slot shows now." : "It is that colour already.";
                if (reason == null && !pickedOwn && shownCreds < fee) reason = "You don't have enough Creds.";
                if (reason == null && !pickedOwn && dyeCosts && shownDyeItems < 1) reason = $"You have no {dyeItemName}.";
            }
            // The piece's own look or colours are what the remove button gives back: the main button is for the rest.
            act.SetEnabled(chosen >= 0 && !pickedOwn && reason == null);
            reasonLabel.text = reason ?? string.Empty;
            reasonLabel.style.display = string.IsNullOrEmpty(reason) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Act()
        {
            if (!open || weaves == null || chosen < 0 || chosen >= choices.Count) return;
            Choice choice = choices[chosen];
            if (choice.key == 0 || choice.blocked != null) return;
            if (mode == Mode.Weave) weaves.RequestWeave(slot, choice.key);
            else weaves.RequestDye(slot, choice.key);
        }

        private void Clear()
        {
            if (!open || weaves == null) return;
            if (mode == Mode.Weave) weaves.RequestClearWeave(slot);
            else weaves.RequestDye(slot, 0);
        }

        private static void Notify(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
