using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Inventory + Equipment windows (ItemWindows.uxml, instanced in the HUD document). Pure presentation: every change
    /// is a request to <see cref="PlayerInventory"/>, which the server validates; the UI redraws from replicated state.
    /// Interactions (modern ARPG conventions):
    ///   right-click  bag item → equip (swapping the current piece back into that cell); equipped slot → unequip
    ///   left-click   opens a context menu (Equip / Try On / Unequip / Drop)
    ///   drag         between cells and slots; dropping outside any window asks to drop (destroy) the item
    /// The equipment page shows a live model (<see cref="CharacterPreviewStage"/>) the player can rotate and zoom.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GearUIController : MonoBehaviour, IPointerBlockQuery
    {
        private const int LeftButton = 0;
        private const int RightButton = 1;
        private const int MiddleButton = 2;
        private const float DragThresholdSquared = 36f;
        private const float GhostHalfSize = 27f;
        private const float RotateDegreesPerPixel = 0.45f;
        private const float PitchDegreesPerPixel = 0.2f;
        private const float ZoomPerWheelStep = 0.3f;
        private const string BlockingClass = "blocks-world";
        private const string GlyphClass = "item-glyph";
        private static readonly string[] GlyphToneClasses = { "glyph-health", "glyph-chi", "glyph-dual" };
        private static readonly string[] RarityClasses = { "rarity-0", "rarity-1", "rarity-2", "rarity-3", "rarity-4" };
        private static readonly string[] RarityTextClasses = { "rarity-text-0", "rarity-text-1", "rarity-text-2", "rarity-text-3", "rarity-text-4" };

        private VisualElement root;
        private VisualElement equipmentWindow, inventoryWindow, grid, viewport, tryOnChip;
        private VisualElement tooltip, menu, menuActions, confirm, ghost;
        private Label countLabel, tryOnLabel, menuTitle, confirmText;
        private Label tipName, tipType, tipDesc, tipStats, tipHint;
        private readonly VisualElement[] cells = new VisualElement[PlayerInventory.Capacity];
        private readonly Button[] pageTabs = new Button[PlayerInventory.PageCount];
        private int currentPage;
        private Label ghostGlyph;
        private readonly Dictionary<EquipSlot, VisualElement> slots = new Dictionary<EquipSlot, VisualElement>();
        private readonly Dictionary<EquipSlot, Sprite> silhouettes = new Dictionary<EquipSlot, Sprite>();
        private readonly List<Action> unbind = new List<Action>();
        private bool initialized;

        private PlayerInventory inventory;
        private PlayerEquipment equipment;
        private CharacterPreviewStage stage;

        // Press / drag state
        private VisualElement pressElement;
        private ItemLocation pressLocation;
        private int pressPointerId = -1;
        private Vector2 pressPosition;
        private bool dragging;
        private VisualElement hoverTarget;
        private VisualElement lastActionElement; // flashed red if the server rejects the request

        // Viewport orbit state
        private int orbitPointerId = -1;
        private int orbitButton;
        private Vector2 orbitLast;

        private ItemLocation menuLocation;
        private ItemLocation confirmLocation;
        private int suppressWorldInputFrame = -1;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            equipmentWindow = root?.Q("equipment-window");
            inventoryWindow = root?.Q("inventory-window");
            if (equipmentWindow == null || inventoryWindow == null)
            {
                Debug.LogError("[GearUI] ItemWindows.uxml is not instanced in this UI document.", this);
                return;
            }

            grid = root.Q("inventory-grid");
            viewport = root.Q("preview-viewport");
            tryOnChip = root.Q("try-on-chip");
            tryOnLabel = root.Q<Label>("try-on-label");
            countLabel = root.Q<Label>("inventory-count");
            tooltip = root.Q("item-tooltip");
            tipName = root.Q<Label>("tooltip-name");
            tipType = root.Q<Label>("tooltip-type");
            tipDesc = root.Q<Label>("tooltip-desc");
            tipStats = root.Q<Label>("tooltip-stats");
            tipHint = root.Q<Label>("tooltip-hint");
            menu = root.Q("item-menu");
            menuActions = root.Q("item-menu-actions");
            menuTitle = root.Q<Label>("item-menu-title");
            confirm = root.Q("item-confirm");
            confirmText = root.Q<Label>("item-confirm-text");
            ghost = root.Q("item-drag-ghost");

            if (ghostGlyph == null)
            {
                ghostGlyph = new Label { pickingMode = PickingMode.Ignore };
                ghostGlyph.AddToClassList(GlyphClass);
                ghost.Add(ghostGlyph);
            }

            BuildGrid();
            BuildPageTabs();
            BuildEquipSlots();
            BindViewport();
            BindButton("close-equipment", () => SetEquipmentOpen(false));
            BindButton("close-inventory", () => SetInventoryOpen(false));
            BindButton("inventory-sort", () => inventory?.RequestSort());
            BindButton("try-on-clear", ClearTryOn);
            BindButton("preview-reset", () => stage?.ResetView());
            BindButton("item-confirm-yes", ConfirmDrop);
            BindButton("item-confirm-no", HideConfirm);
            BindButton("open-inventory", () => SetInventoryOpen(!IsOpen(inventoryWindow)));
            BindButton("open-equipment", () => SetEquipmentOpen(!IsOpen(equipmentWindow)));

            ClientPresentationRegistry.RegisterPointerBlock(this);
            PlayerInventory.LocalChanged += OnLocalChanged;
            initialized = true;
            OnLocalChanged();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            PlayerInventory.LocalChanged -= OnLocalChanged;
            ClientPresentationRegistry.UnregisterPointerBlock(this);
            BindPlayer(null);
            foreach (var undo in unbind) undo();
            unbind.Clear();
            if (stage != null) stage.Close();
        }

        private void OnDestroy()
        {
            if (stage != null) Destroy(stage.gameObject);
        }

        private void OnLocalChanged() => BindPlayer(PlayerInventory.Local);

        private void BindPlayer(PlayerInventory next)
        {
            if (inventory == next && next != null) return;
            if (inventory != null)
            {
                inventory.Changed -= RefreshBag;
                inventory.RequestFailed -= OnRequestFailed;
                if (equipment != null) equipment.SlotChanged -= OnSlotChanged;
            }
            CancelDrag();
            HideMenu();
            HideConfirm();
            inventory = next;
            equipment = next != null ? next.Equipment : null;
            if (inventory != null)
            {
                inventory.Changed += RefreshBag;
                inventory.RequestFailed += OnRequestFailed;
                if (equipment != null) equipment.SlotChanged += OnSlotChanged;
                BuildSilhouettes();
            }
            if (stage != null && inventory != null) BindStage();
            RefreshBag();
            RefreshEquipment();
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && !IsTyping())
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (IsOpen(confirm)) HideConfirm();
                    else if (IsOpen(menu)) HideMenu();
                    else if (dragging) CancelDrag();
                    else { SetEquipmentOpen(false); SetInventoryOpen(false); }
                }
                if (keyboard.iKey.wasPressedThisFrame) SetInventoryOpen(!IsOpen(inventoryWindow));
                if (keyboard.uKey.wasPressedThisFrame) SetEquipmentOpen(!IsOpen(equipmentWindow)); // E is a skill slot
            }

            // Click anywhere outside the context menu closes it (the world never receives that click).
            var mouse = Mouse.current;
            if (mouse != null && IsOpen(menu) &&
                (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) &&
                !menu.worldBound.Contains(ScreenToPanel(mouse.position.ReadValue())))
            {
                HideMenu();
            }

            if (tryOnChip != null && IsOpen(tryOnChip) && (stage == null || stage.TryOnItem == null)) SetDisplay(tryOnChip, false);
        }

        public bool BlocksWorldPointer(Vector2 screenPosition)
        {
            if (!initialized) return false;
            return dragging || orbitPointerId >= 0 || IsOpen(menu) || IsOpen(confirm) || suppressWorldInputFrame == Time.frameCount;
        }

        private bool IsTyping()
        {
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            for (VisualElement e = focused; e != null; e = e.parent)
                if (e is TextField) return true;
            return false;
        }

        // ------------------------------------------------------------------ windows

        private static bool IsOpen(VisualElement e) => e != null && e.resolvedStyle.display != DisplayStyle.None;

        private static void SetDisplay(VisualElement e, bool visible)
        {
            if (e == null) return;
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            e.EnableInClassList("visible", visible);
        }

        public void SetInventoryOpen(bool open)
        {
            if (open == IsOpen(inventoryWindow)) return;
            SetDisplay(inventoryWindow, open);
            if (open) { inventoryWindow.BringToFront(); RefreshBag(); }
            else { CancelDrag(); HideMenu(); HideTooltip(); }
        }

        public void SetEquipmentOpen(bool open)
        {
            if (open == IsOpen(equipmentWindow)) return;
            SetDisplay(equipmentWindow, open);
            if (open)
            {
                equipmentWindow.BringToFront();
                RefreshEquipment();
                if (!IsOpen(inventoryWindow))
                {
                    SetInventoryOpen(true);
                    DockAfterLayout();
                }
                EnsureStage();
                // The viewport's GeometryChangedEvent sizes and opens the stage once layout is known.
                viewport.schedule.Execute(OpenStageNow);
            }
            else
            {
                HideMenu();
                HideTooltip();
                if (stage != null) stage.Close();
                if (viewport != null) viewport.style.backgroundImage = StyleKeyword.None;
                SetDisplay(tryOnChip, false);
            }
        }

        /// <summary>Docks once the equipment page has been laid out (its bounds are meaningless the frame it opens).</summary>
        private void DockAfterLayout()
        {
            EventCallback<GeometryChangedEvent> once = null;
            once = _ =>
            {
                equipmentWindow.UnregisterCallback(once);
                DockInventory();
            };
            equipmentWindow.RegisterCallback(once);
        }

        /// <summary>Place the inventory flush to the right of the equipment page (or left, if there's no room).</summary>
        private void DockInventory()
        {
            Rect eq = equipmentWindow.worldBound;
            Rect parent = inventoryWindow.parent.worldBound;
            float width = inventoryWindow.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) width = 372f;
            float x = eq.xMax + 10f;
            if (x + width > root.layout.width) x = Mathf.Max(0f, eq.xMin - 10f - width);
            // style.left/top are pre-translate; subtract any HUD-layout offset the player gave this window.
            Vector3 offset = inventoryWindow.resolvedStyle.translate;
            inventoryWindow.style.left = x - parent.x - offset.x;
            inventoryWindow.style.top = eq.yMin - parent.y - offset.y;
        }

        // ------------------------------------------------------------------ grid & slots

        private void BuildGrid()
        {
            grid.Clear();
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = CreateItemVisual(new VisualElement(), true);
                cell.AddToClassList("item-cell");
                cell.userData = ItemLocation.Bag(i);
                grid.Add(cell);
                cells[i] = cell;
                BindItemElement(cell);
            }
            ShowPage(currentPage);
        }

        private void BuildPageTabs()
        {
            for (int i = 0; i < pageTabs.Length; i++)
            {
                int page = i;
                pageTabs[i] = root.Q<Button>($"inventory-page-{i}");
                BindButton($"inventory-page-{i}", () => ShowPage(page));
            }
            ShowPage(currentPage);
        }

        /// <summary>Only the current page's cells are laid out; the bag itself is one continuous array.</summary>
        private void ShowPage(int page)
        {
            currentPage = Mathf.Clamp(page, 0, PlayerInventory.PageCount - 1);
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != null) cells[i].style.display = PageOf(i) == currentPage ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < pageTabs.Length; i++)
                pageTabs[i]?.EnableInClassList("selected", i == currentPage);
        }

        private static int PageOf(int cellIndex) => cellIndex / PlayerInventory.PageSize;

        private int PageTabAt(Vector2 panelPosition)
        {
            if (!IsOpen(inventoryWindow)) return -1;
            for (int i = 0; i < pageTabs.Length; i++)
                if (pageTabs[i] != null && pageTabs[i].worldBound.Contains(panelPosition)) return i;
            return -1;
        }

        private void BuildEquipSlots()
        {
            foreach (var slot in EquipSlots.All)
            {
                var element = root.Q($"equip-{slot}");
                if (element == null) continue;
                CreateItemVisual(element, false);
                element.userData = ItemLocation.Equip(slot);
                slots[slot] = element;
                BindItemElement(element);
            }
        }

        /// <summary>Adds icon / stack count / frame children. Frame goes last so it draws over the icon.</summary>
        private static VisualElement CreateItemVisual(VisualElement host, bool withCount)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("item-icon");
            host.Insert(0, icon);
            var glyph = new Label { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList(GlyphClass);
            host.Insert(1, glyph);
            if (withCount)
            {
                var count = new Label { pickingMode = PickingMode.Ignore };
                count.AddToClassList("item-count");
                host.Add(count);
            }
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("item-frame");
            host.Add(frame);
            return host;
        }

        private void BuildSilhouettes()
        {
            silhouettes.Clear();
            var catalog = inventory != null ? inventory.Catalog : null;
            if (catalog == null) return;
            foreach (var def in catalog.Items) // catalog is ID-sorted, so the starter look of each slot comes first
                if (def != null && def.icon != null && def.IsEquippable && !silhouettes.ContainsKey(def.equipSlot))
                    silhouettes[def.equipSlot] = def.icon;
        }

        private void RefreshBag()
        {
            if (!initialized) return;
            int used = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                var stack = inventory != null ? inventory.Get(i) : ItemStack.Empty;
                var def = inventory != null && !stack.IsEmpty ? inventory.GetDefinition(i) : null;
                if (def != null) used++;
                SetItemVisual(cells[i], def, stack.count, null);
            }
            if (countLabel != null) countLabel.text = $"{used} / {PlayerInventory.Capacity}";
        }

        private void RefreshEquipment()
        {
            if (!initialized) return;
            foreach (var kv in slots)
            {
                var def = equipment != null ? equipment.GetDefinition(kv.Key) : null;
                silhouettes.TryGetValue(kv.Key, out var silhouette);
                SetItemVisual(kv.Value, def, 1, silhouette);
            }
        }

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next) => RefreshEquipment();

        private static void SetItemVisual(VisualElement host, ItemDefinition def, int count, Sprite silhouette)
        {
            var icon = host.Q(className: "item-icon");
            foreach (var c in RarityClasses) host.RemoveFromClassList(c);
            if (def != null)
            {
                icon.style.backgroundImage = def.icon != null ? new StyleBackground(def.icon) : StyleKeyword.None;
                icon.RemoveFromClassList("silhouette");
                host.AddToClassList(RarityClasses[Mathf.Clamp((int)def.rarity, 0, RarityClasses.Length - 1)]);
            }
            else
            {
                icon.style.backgroundImage = silhouette != null ? new StyleBackground(silhouette) : StyleKeyword.None;
                icon.EnableInClassList("silhouette", silhouette != null);
            }
            SetGlyph(host.Q<Label>(className: GlyphClass), def);
            var countLabel = host.Q<Label>(className: "item-count");
            if (countLabel != null) countLabel.text = def != null && count > 1 ? count.ToString() : string.Empty;
        }

        /// <summary>Items without a sprite show their placeholder text, tinted by what they restore.</summary>
        private static void SetGlyph(Label glyph, ItemDefinition def)
        {
            if (glyph == null) return;
            bool show = def != null && def.icon == null && !string.IsNullOrEmpty(def.placeholderGlyph);
            glyph.text = show ? def.placeholderGlyph : string.Empty;
            foreach (var c in GlyphToneClasses) glyph.RemoveFromClassList(c);
            if (!show || !def.IsConsumable) return;
            int tone = def.restoreHealth > 0f && def.restoreChi > 0f ? 2 : def.restoreChi > 0f ? 1 : 0;
            glyph.AddToClassList(GlyphToneClasses[tone]);
        }

        private ItemDefinition DefinitionAt(ItemLocation loc)
        {
            if (inventory == null) return null;
            return loc.IsBag ? inventory.GetDefinition(loc.index) : equipment?.GetDefinition(loc.Slot);
        }

        // ------------------------------------------------------------------ item pointer handling

        private void BindItemElement(VisualElement element)
        {
            Register<PointerDownEvent>(element, OnItemPointerDown);
            Register<PointerMoveEvent>(element, OnItemPointerMove);
            Register<PointerUpEvent>(element, OnItemPointerUp);
            Register<PointerCaptureOutEvent>(element, _ => { if (pressElement == element && !dragging) ResetPress(); });
            Register<PointerEnterEvent>(element, evt => { if (!dragging && !IsOpen(menu)) ShowTooltip(element); });
            Register<PointerLeaveEvent>(element, evt => HideTooltip());
        }

        private void OnItemPointerDown(PointerDownEvent evt)
        {
            if (!(evt.currentTarget is VisualElement element) || !(element.userData is ItemLocation loc)) return;
            evt.StopPropagation();
            HideMenu();
            if (DefinitionAt(loc) == null) return;

            if (evt.button == RightButton)
            {
                HideTooltip();
                lastActionElement = element;
                QuickAction(loc);
                return;
            }
            if (evt.button != LeftButton) return;
            pressElement = element;
            pressLocation = loc;
            pressPointerId = evt.pointerId;
            pressPosition = evt.position;
            dragging = false;
            element.CapturePointer(evt.pointerId);
        }

        private void OnItemPointerMove(PointerMoveEvent evt)
        {
            if (pressElement == null || evt.pointerId != pressPointerId) return;
            Vector2 pos = evt.position;
            if (!dragging && (pos - pressPosition).sqrMagnitude >= DragThresholdSquared) BeginDrag();
            if (!dragging) return;
            ghost.style.left = pos.x - GhostHalfSize;
            ghost.style.top = pos.y - GhostHalfSize;
            int tab = PageTabAt(pos);
            if (tab >= 0 && tab != currentPage) ShowPage(tab);
            SetHoverTarget(tab >= 0 ? pageTabs[tab] : FindItemElement(pos));
        }

        private void OnItemPointerUp(PointerUpEvent evt)
        {
            if (pressElement == null || evt.pointerId != pressPointerId) return;
            evt.StopPropagation();
            var source = pressElement;
            var from = pressLocation;
            bool wasDragging = dragging;
            Vector2 pos = evt.position;
            if (source.HasPointerCapture(evt.pointerId)) source.ReleasePointer(evt.pointerId);
            EndDragVisuals();
            ResetPress();
            suppressWorldInputFrame = Time.frameCount;

            if (!wasDragging)
            {
                OpenMenu(from, pos);
                return;
            }

            var target = FindItemElement(pos);
            if (target != null && target.userData is ItemLocation to)
            {
                lastActionElement = target;
                if (!to.Equals(from)) inventory?.RequestMove(from, to);
                return;
            }
            if (!IsOverBlockingUi(pos)) ShowConfirm(from); // dropped onto the world
        }

        private void BeginDrag()
        {
            var def = DefinitionAt(pressLocation);
            if (def == null) { ResetPress(); return; }
            dragging = true;
            HideTooltip();
            pressElement.AddToClassList("drag-source");
            ghost.style.backgroundImage = def.icon != null ? new StyleBackground(def.icon) : StyleKeyword.None;
            SetGlyph(ghostGlyph, def);
            SetDisplay(ghost, true);
            ghost.BringToFront();

            // Highlight where this item can go: its equip slot glows, other equip slots dim.
            foreach (var kv in slots)
            {
                bool valid = def.equipSlot == kv.Key || (!pressLocation.IsBag && pressLocation.Slot == kv.Key);
                kv.Value.EnableInClassList("drop-valid", valid && kv.Key != (pressLocation.IsBag ? EquipSlot.None : pressLocation.Slot));
                kv.Value.EnableInClassList("drop-invalid", !valid);
            }
        }

        private void EndDragVisuals()
        {
            if (pressElement != null) pressElement.RemoveFromClassList("drag-source");
            foreach (var kv in slots)
            {
                kv.Value.RemoveFromClassList("drop-valid");
                kv.Value.RemoveFromClassList("drop-invalid");
            }
            SetHoverTarget(null);
            SetDisplay(ghost, false);
        }

        private void CancelDrag()
        {
            if (pressElement != null && pressPointerId >= 0 && pressElement.HasPointerCapture(pressPointerId))
                pressElement.ReleasePointer(pressPointerId);
            EndDragVisuals();
            ResetPress();
        }

        private void ResetPress()
        {
            pressElement = null;
            pressPointerId = -1;
            dragging = false;
        }

        private void SetHoverTarget(VisualElement target)
        {
            if (hoverTarget == target) return;
            hoverTarget?.RemoveFromClassList("drop-hover");
            hoverTarget = target;
            hoverTarget?.AddToClassList("drop-hover");
        }

        private VisualElement FindItemElement(Vector2 panelPosition)
        {
            if (IsOpen(inventoryWindow))
            {
                // Cells on other pages are not laid out; their stale bounds must not catch the pointer.
                int first = currentPage * PlayerInventory.PageSize;
                for (int i = first; i < first + PlayerInventory.PageSize; i++)
                    if (cells[i].worldBound.Contains(panelPosition)) return cells[i];
            }
            foreach (var kv in slots)
                if (IsOpen(equipmentWindow) && kv.Value.worldBound.Contains(panelPosition)) return kv.Value;
            return null;
        }

        private bool IsOverBlockingUi(Vector2 panelPosition)
        {
            for (VisualElement e = root.panel?.Pick(panelPosition); e != null; e = e.parent)
                if (e.ClassListContains(BlockingClass)) return true;
            return false;
        }

        private void QuickAction(ItemLocation loc)
        {
            if (inventory == null) return;
            suppressWorldInputFrame = Time.frameCount;
            if (!loc.IsBag) inventory.RequestQuickUnequip(loc.Slot);
            else if (IsUsable(DefinitionAt(loc))) inventory.RequestUse(loc.index);
            else inventory.RequestQuickEquip(loc.index);
        }

        private void OnRequestFailed(string reason)
        {
            // Messages sink (if any) shows the text; flash the last drop target red for immediate feedback.
            var target = lastActionElement;
            if (target == null) return;
            target.AddToClassList("reject-flash");
            target.schedule.Execute(() => target.RemoveFromClassList("reject-flash")).StartingIn(350);
        }

        // ------------------------------------------------------------------ context menu (extensible action list)

        private readonly struct MenuAction
        {
            public readonly string Label;
            public readonly Action Run;
            public readonly bool Danger;
            public MenuAction(string label, Action run, bool danger = false) { Label = label; Run = run; Danger = danger; }
        }

        private List<MenuAction> BuildActions(ItemLocation loc, ItemDefinition def)
        {
            var actions = new List<MenuAction>();
            if (loc.IsBag)
            {
                if (IsUsable(def)) actions.Add(new MenuAction("Use", () => inventory.RequestUse(loc.index)));
                if (def.IsEquippable) actions.Add(new MenuAction("Equip", () => inventory.RequestQuickEquip(loc.index)));
                if (CanTryOn(def)) actions.Add(new MenuAction("Try On", () => TryOn(def)));
            }
            else
            {
                actions.Add(new MenuAction("Unequip", () => inventory.RequestQuickUnequip(loc.Slot)));
            }
            actions.Add(new MenuAction("Drop", () => ShowConfirm(loc), danger: true));
            return actions;
        }

        private void OpenMenu(ItemLocation loc, Vector2 panelPosition)
        {
            var def = DefinitionAt(loc);
            if (def == null) return;
            HideTooltip();
            menuLocation = loc;
            menuTitle.text = def.displayName;
            ApplyRarityText(menuTitle, def.rarity);
            menuActions.Clear();
            foreach (var action in BuildActions(loc, def))
            {
                var run = action.Run;
                var button = new Button(() => { HideMenu(); run(); }) { text = action.Label.ToUpperInvariant() };
                if (action.Danger) button.AddToClassList("kn-button-danger");
                menuActions.Add(button);
            }
            SetDisplay(menu, true);
            menu.BringToFront();
            menu.style.left = panelPosition.x + 6f;
            menu.style.top = panelPosition.y + 6f;
            menu.schedule.Execute(() => ClampIntoView(menu));
        }

        private void HideMenu() => SetDisplay(menu, false);

        private void ShowConfirm(ItemLocation loc)
        {
            var def = DefinitionAt(loc);
            if (def == null) return;
            confirmLocation = loc;
            confirmText.text = $"Drop {def.displayName}?";
            SetDisplay(confirm, true);
            confirm.BringToFront();
        }

        private void ConfirmDrop()
        {
            HideConfirm();
            suppressWorldInputFrame = Time.frameCount;
            inventory?.RequestDestroy(confirmLocation); // TODO(loot): spawn a world drop instead once ground loot exists
        }

        private void HideConfirm() => SetDisplay(confirm, false);

        // ------------------------------------------------------------------ tooltip

        private void ShowTooltip(VisualElement element)
        {
            if (!(element.userData is ItemLocation loc)) return;
            var def = DefinitionAt(loc);
            if (def == null) { HideTooltip(); return; }

            tipName.text = def.displayName;
            ApplyRarityText(tipName, def.rarity);
            tipType.text = def.IsEquippable ? $"{def.equipSlot.ToString().ToUpperInvariant()}  ·  {def.rarity.ToString().ToUpperInvariant()}"
                                            : def.rarity.ToString().ToUpperInvariant();
            tipDesc.text = def.description;
            tipStats.text = FormatStats(def);
            tipHint.text = !loc.IsBag ? "Right-click: unequip   ·   Left-click: options"
                : IsUsable(def) ? "Right-click: use   ·   Left-click: options"
                : def.IsEquippable ? "Right-click: equip   ·   Left-click: options"
                : "Left-click: options";

            SetDisplay(tooltip, true);
            tooltip.BringToFront();
            Rect r = element.worldBound;
            tooltip.style.left = r.xMax + 8f;
            tooltip.style.top = r.yMin;
            tooltip.schedule.Execute(() =>
            {
                // Flip to the left side when it would leave the screen.
                if (tooltip.worldBound.xMax > root.layout.width) tooltip.style.left = r.xMin - 8f - tooltip.layout.width;
                ClampIntoView(tooltip);
            });
        }

        private void HideTooltip() => SetDisplay(tooltip, false);

        /// <summary>Consumables that aren't gear are used, not equipped, on right-click.</summary>
        private static bool IsUsable(ItemDefinition def) => def != null && def.IsConsumable && !def.IsEquippable;

        private static string FormatStats(ItemDefinition def)
        {
            var sb = new System.Text.StringBuilder();
            if (def.IsConsumable)
            {
                if (def.restoreHealth > 0f) sb.AppendLine($"Restores {def.restoreHealth:0} HP");
                if (def.restoreChi > 0f) sb.AppendLine($"Restores {def.restoreChi:0} Chi");
                return sb.ToString().TrimEnd();
            }
            if (def.statModifiers == null || def.statModifiers.Count == 0) return "No stats yet";
            foreach (var m in def.statModifiers)
            {
                string value = m.modType == Progression.ModifierType.Flat ? $"{m.value:+0;-0}" : $"{m.value:+0;-0}%";
                sb.Append(value).Append(' ').AppendLine(m.stat.ToString());
            }
            return sb.ToString().TrimEnd();
        }

        private static void ApplyRarityText(Label label, ItemRarity rarity)
        {
            foreach (var c in RarityTextClasses) label.RemoveFromClassList(c);
            label.AddToClassList(RarityTextClasses[Mathf.Clamp((int)rarity, 0, RarityTextClasses.Length - 1)]);
        }

        private void ClampIntoView(VisualElement e)
        {
            Rect r = e.worldBound;
            float maxX = root.layout.width - r.width - 4f;
            float maxY = root.layout.height - r.height - 4f;
            if (r.xMin > maxX) e.style.left = e.layout.x - (r.xMin - maxX);
            if (r.yMin > maxY) e.style.top = e.layout.y - (r.yMin - maxY);
        }

        // ------------------------------------------------------------------ 3D preview

        private static bool CanTryOn(ItemDefinition def) =>
            def.equipSlot == EquipSlot.Weapon || def.equipSlot == EquipSlot.Armor || def.equipSlot == EquipSlot.Gloves ||
            def.equipSlot == EquipSlot.Boots || def.equipSlot == EquipSlot.Cape;

        private void TryOn(ItemDefinition def)
        {
            SetEquipmentOpen(true);
            // The stage opens after layout; defer the try-on until it has a doll.
            viewport.schedule.Execute(() =>
            {
                if (stage == null || !stage.TryOn(def)) return;
                tryOnLabel.text = $"Trying on: {def.displayName}";
                SetDisplay(tryOnChip, true);
            }).StartingIn(30);
        }

        private void ClearTryOn()
        {
            stage?.ClearTryOn();
            SetDisplay(tryOnChip, false);
        }

        private void EnsureStage()
        {
            if (stage != null) return;
            stage = CharacterPreviewStage.Create();
            BindStage();
        }

        private void BindStage()
        {
            if (stage == null || inventory == null) return;
            var binder = inventory.GetComponent<EquipmentVisualBinder>();
            var motor = inventory.GetComponent<PlayerAnimancerMotor>();
            if (binder == null || binder.Appearance == null) return;
            stage.Bind(binder.Appearance, equipment, motor != null ? motor.IdleClip : null);
        }

        private void OpenStageNow()
        {
            if (stage == null || !IsOpen(equipmentWindow)) return;
            Vector2Int size = ViewportPixelSize();
            stage.Open(size.x, size.y);
            AssignViewportTexture();
        }

        private Vector2Int ViewportPixelSize()
        {
            float scale = root.layout.width > 0f ? Screen.width / root.layout.width : 1f;
            return new Vector2Int(Mathf.RoundToInt(viewport.layout.width * scale), Mathf.RoundToInt(viewport.layout.height * scale));
        }

        private void AssignViewportTexture()
        {
            if (stage != null && stage.Texture != null)
                viewport.style.backgroundImage = Background.FromRenderTexture(stage.Texture);
        }

        private void BindViewport()
        {
            Register<GeometryChangedEvent>(viewport, _ =>
            {
                if (stage == null || !stage.IsOpen) return;
                Vector2Int size = ViewportPixelSize();
                stage.Resize(size.x, size.y);
                AssignViewportTexture();
            });
            Register<PointerDownEvent>(viewport, evt =>
            {
                if (stage == null) return;
                evt.StopPropagation();
                HideMenu();
                if (evt.clickCount >= 2) { stage.ResetView(); return; }
                orbitPointerId = evt.pointerId;
                orbitButton = evt.button;
                orbitLast = evt.position;
                viewport.CapturePointer(evt.pointerId);
            });
            Register<PointerMoveEvent>(viewport, evt =>
            {
                if (evt.pointerId != orbitPointerId || stage == null) return;
                Vector2 delta = (Vector2)evt.position - orbitLast;
                orbitLast = evt.position;
                if (orbitButton == MiddleButton) stage.Pan(delta, viewport.layout.height);
                else stage.Rotate(-delta.x * RotateDegreesPerPixel, delta.y * PitchDegreesPerPixel);
            });
            Register<PointerUpEvent>(viewport, evt =>
            {
                if (evt.pointerId != orbitPointerId) return;
                if (viewport.HasPointerCapture(evt.pointerId)) viewport.ReleasePointer(evt.pointerId);
                orbitPointerId = -1;
                suppressWorldInputFrame = Time.frameCount;
            });
            Register<PointerCaptureOutEvent>(viewport, _ => orbitPointerId = -1);
            Register<WheelEvent>(viewport, evt =>
            {
                if (stage == null) return;
                evt.StopPropagation();
                stage.Zoom(Mathf.Sign(evt.delta.y) * ZoomPerWheelStep);
            });
        }

        // ------------------------------------------------------------------ helpers

        private Vector2 ScreenToPanel(Vector2 screen) =>
            RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));

        private void BindButton(string name, Action callback)
        {
            var button = root.Q<Button>(name);
            if (button == null) return;
            button.clicked += callback;
            unbind.Add(() => button.clicked -= callback);
        }

        private void Register<TEvent>(VisualElement element, EventCallback<TEvent> callback) where TEvent : EventBase<TEvent>, new()
        {
            element.RegisterCallback(callback);
            unbind.Add(() => element.UnregisterCallback(callback));
        }
    }
}
