using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Crafting;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Fabricator's bench. Opened from the Fabricator's dialogue, beside the Pack: a list of what can be made,
    /// and for the picked recipe what it uses up with how many of each the player has, carried or stored. One
    /// button makes one, the other as many as the materials allow. It sends requests to
    /// <see cref="PlayerCrafting"/>; the server decides, by the same rules the window shows
    /// (<see cref="CraftRules"/>). Closed when the player walks away.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CraftWindowController : MonoBehaviour
    {
        private const float RangeCheckSeconds = 0.25f;
        private const string WindowName = "craft-window";
        private const string PackWindowName = "inventory-window";
        private const string ChosenClass = "chosen";
        private const string ShortClass = "short";

        private VisualElement root, window, list, needs;
        private Label nameLabel, noteLabel, credsLabel, reasonLabel;
        private Button act, all;
        // The rows are kept here, not read back from the list: a skinned panel has children of its own.
        private readonly List<VisualElement> rows = new List<VisualElement>(16);
        private readonly List<CraftRecipe> recipes = new List<CraftRecipe>(16);
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private PlayerCrafting crafting;
        private int chosen = -1;
        private bool initialized;
        // Open at the Fabricator, as against merely shown: the layout editor shows the window to have it placed.
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
                Debug.LogError("[Crafting] CraftWindow.uxml is not instanced in this UI document.", this);
                return;
            }
            list = window.Q("craft-list");
            needs = window.Q("craft-needs");
            nameLabel = window.Q<Label>("craft-name");
            noteLabel = window.Q<Label>("craft-note");
            credsLabel = window.Q<Label>("craft-creds");
            reasonLabel = window.Q<Label>("craft-reason");
            act = window.Q<Button>("craft-act");
            all = window.Q<Button>("craft-all");
            window.Q<Button>("close-craft").clicked += Close;
            act.clicked += () => Craft(1);
            all.clicked += () => Craft(int.MaxValue);
            SetOpen(false);

            NpcServices.Register(ServiceIds.Craft, NpcOffer.Craft, "CRAFT", Open);
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
            NpcServices.Unregister(ServiceIds.Craft);
            Bind(null);
        }

        private void Bind(PlayerCrafting next)
        {
            if (crafting != null) crafting.Sources.Changed -= Refresh;
            crafting = next;
            if (crafting != null) crafting.Sources.Changed += Refresh;
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && open;

        public void Open()
        {
            if (!initialized) return;
            previewing = false;
            Bind(PlayerCrafting.Local);
            if (crafting == null || crafting.Settings == null)
            {
                Notify("Crafting isn't available for this character.");
                return;
            }
            Fill();
            SetOpen(true);
            window.parent?.BringToFront();
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;

            // What is made lands in the Pack: it opens with the bench, and the bench sits beside it.
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                openedPack = !gear.IsInventoryOpen;
                if (openedPack) gear.SetInventoryOpen(true);
                gear.SuppressWorldInput();
            }
            placement.Open(window, root.Q(PackWindowName));
        }

        public void Close()
        {
            if (!initialized || !open) return;
            SetOpen(false);
            placement.Close();
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.HideItemTooltip();
                if (openedPack) gear.SetInventoryOpen(false);
                gear.SuppressWorldInput();
            }
            openedPack = false;
            Bind(null);
        }

        private void SetOpen(bool shown)
        {
            open = shown;
            window.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", shown);
        }

        /// <summary>The layout editor has the window out to be placed, away from any Fabricator: it shows the recipes so it has its real size.</summary>
        private void Preview(bool on)
        {
            if (!initialized || open) return; // open at the Fabricator: it is its own likeness
            previewing = on;
            if (!on) { Bind(null); return; }
            Bind(PlayerCrafting.Local);
            if (crafting != null && crafting.Settings != null) Fill();
        }

        private void Update()
        {
            if (!open) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            if (crafting == null) { Close(); return; }
            placement.Tick();
            if (crafting.Creds != shownCreds) Refresh();
            if (Time.unscaledTime < nextRangeCheck) return;
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;
            if (crafting.InReach) return;
            Close();
            Notify("You walked away from the Fabricator.");
        }

        // ------------------------------------------------------------------ the recipes

        /// <summary>Builds the list: the recipes in the order the settings hold them, a heading where the kind changes.</summary>
        private void Fill()
        {
            list.Clear();
            rows.Clear();
            recipes.Clear();
            ItemCatalog catalog = crafting.Inventory.Catalog;
            string group = null;
            foreach (CraftRecipe recipe in crafting.Settings.recipes)
            {
                ItemDefinition result = recipe.IsValid && catalog != null ? catalog.Get(recipe.resultItemId) : null;
                if (result == null) continue;
                if (recipe.group != group)
                {
                    group = recipe.group;
                    var heading = new Label(group) { pickingMode = PickingMode.Ignore };
                    heading.AddToClassList("craft-group");
                    list.Add(heading);
                }
                int index = recipes.Count;
                recipes.Add(recipe);

                var row = new VisualElement();
                row.AddToClassList("craft-row");
                var icon = new VisualElement();
                icon.AddToClassList("item-cell");
                icon.AddToClassList("craft-row-icon");
                GearUIController.CreateItemVisual(icon, false);
                GearUIController.SetItemVisual(icon, result, 1);
                icon.pickingMode = PickingMode.Ignore;
                row.Add(icon);
                var name = new Label(result.displayName) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("craft-row-name");
                GearUIController.ApplyRarityText(name, result.rarity);
                row.Add(name);
                var can = new Label { pickingMode = PickingMode.Ignore };
                can.AddToClassList("craft-row-can");
                row.Add(can);
                row.RegisterCallback<PointerDownEvent>(evt =>
                {
                    evt.StopPropagation();
                    GearUIController.Instance?.SuppressWorldInput();
                    if (evt.button == 0) Choose(index);
                });
                list.Add(row);
                rows.Add(row);
            }
            if (chosen >= recipes.Count) chosen = -1;
            if (chosen < 0 && recipes.Count > 0) chosen = 0;
            Refresh();
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= recipes.Count) return;
            chosen = index;
            Refresh();
        }

        /// <summary>How many each recipe would make right now, and the picked recipe in full.</summary>
        private void Refresh()
        {
            if (crafting == null || (!open && !previewing)) return;
            shownCreds = crafting.Creds;
            credsLabel.text = WalletRules.Format(shownCreds);
            for (int i = 0; i < rows.Count; i++)
            {
                int can = crafting.Level >= recipes[i].requiredLevel ? crafting.MaxCrafts(recipes[i]) : 0;
                rows[i].EnableInClassList(ChosenClass, i == chosen);
                rows[i].EnableInClassList(ShortClass, can < 1);
                rows[i].Q<Label>(className: "craft-row-can").text = can > 0 ? $"x{can * recipes[i].resultCount}" : string.Empty;
            }
            ShowChosen();
        }

        private void ShowChosen()
        {
            needs.Clear();
            if (chosen < 0 || chosen >= recipes.Count)
            {
                nameLabel.text = "Nothing can be made here yet.";
                noteLabel.text = string.Empty;
                reasonLabel.style.display = DisplayStyle.None;
                act.SetEnabled(false);
                all.style.display = DisplayStyle.None;
                return;
            }
            CraftRecipe recipe = recipes[chosen];
            ItemCatalog catalog = crafting.Inventory.Catalog;
            ItemDefinition result = catalog.Get(recipe.resultItemId);
            nameLabel.text = recipe.resultCount > 1 ? $"{result.displayName} x{recipe.resultCount}" : result.displayName;

            var seen = new HashSet<ushort>();
            foreach (CraftIngredient ingredient in recipe.ingredients)
                if (ingredient.itemId != 0 && seen.Add(ingredient.itemId)) Need(catalog.Get(ingredient.itemId), (int)CraftRules.Needed(recipe, ingredient.itemId));

            string note = recipe.requiredLevel > 0 ? $"Requires level {recipe.requiredLevel}" : string.Empty;
            if (recipe.creds > 0) note += (note.Length > 0 ? "   ·   " : string.Empty) + $"{WalletRules.FormatCreds(recipe.creds)} a craft";
            noteLabel.text = note;
            noteLabel.style.display = note.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            string reason = crafting.BlockedReason(recipe, 1);
            reasonLabel.text = reason ?? string.Empty;
            reasonLabel.style.display = string.IsNullOrEmpty(reason) ? DisplayStyle.None : DisplayStyle.Flex;
            act.SetEnabled(reason == null);
            int most = reason == null ? crafting.MaxCrafts(recipe) : 0;
            all.text = $"CRAFT ALL ({most})";
            all.style.display = most > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>One material as an icon: how many a craft uses, how many the player has, and where they are.</summary>
        private void Need(ItemDefinition item, int count)
        {
            if (item == null) return;
            int have = crafting.Sources.Count(item.itemId);
            var pick = new VisualElement();
            pick.AddToClassList("item-cell");
            pick.AddToClassList("forge-pick");
            GearUIController.CreateItemVisual(pick, true);
            GearUIController.SetItemVisual(pick, item, have);
            pick.EnableInClassList("out", have < count);
            var tag = new Label($"x{count}") { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("forge-pick-tag");
            pick.Add(tag);
            string where = crafting.Sources.CountIn(ItemSourceKind.Pack, item.itemId) == have ? string.Empty : $" ({crafting.Sources.Describe(item.itemId)})";
            pick.RegisterCallback<PointerEnterEvent>(_ => GearUIController.Instance?.ShowItemTooltip(pick, item, new ItemStack(item.itemId, 1),
                $"A craft uses {count}   ·   {(have > 0 ? $"You have {have}{where}" : "You have none")}"));
            pick.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
            needs.Add(pick);
        }

        private void Craft(int times)
        {
            if (!open || crafting == null || chosen < 0 || chosen >= recipes.Count) return;
            CraftRecipe recipe = recipes[chosen];
            times = Mathf.Min(times, crafting.MaxCrafts(recipe));
            if (times < 1 || crafting.BlockedReason(recipe, times) != null) return;
            crafting.RequestCraft(recipe.id, times);
        }

        private static void Notify(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
