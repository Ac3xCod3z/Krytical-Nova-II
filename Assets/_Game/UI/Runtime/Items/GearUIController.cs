using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Pets;
using KryticalNova.Player;
using KryticalNova.Progression;
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
        private const string LevelClass = "item-level";
        private const string TemperClass = "item-temper";
        private const string GlowClass = "item-glow";
        private const string RimClass = "item-rim";
        private const string UnusableClass = "item-unusable";
        private const string UnmetClass = "tooltip-requires--unmet";
        private const string LockedClass = "item-locked";
        private static readonly string[] GlyphToneClasses = { "glyph-health", "glyph-chi", "glyph-dual" };
        private static readonly string[] RarityClasses = { "rarity-0", "rarity-1", "rarity-2", "rarity-3", "rarity-4" };
        private static readonly string[] RarityCardClasses = { "rarity-card-0", "rarity-card-1", "rarity-card-2", "rarity-card-3", "rarity-card-4" };
        private const string EmptyClass = "item-empty";
        private static readonly string[] RarityTextClasses = { "rarity-text-0", "rarity-text-1", "rarity-text-2", "rarity-text-3", "rarity-text-4" };

        private VisualElement root;
        private VisualElement equipmentWindow, inventoryWindow, grid, viewport, tryOnChip;
        private VisualElement tooltip, menu, menuActions, confirm, ghost;
        private FloatingFit tooltipFit, menuFit;
        private Label countLabel, credsLabel, tryOnLabel, menuTitle, confirmText, confirmHelp;
        private Button confirmYes;
        private bool confirmDestroys;
        private Label tipName, tipType, tipRequires, tipDesc, tipStats, tipHint;
        private PetTooltip tipPet;
        private readonly VisualElement[] cells = new VisualElement[PlayerInventory.Capacity];
        private readonly Button[] pageTabs = new Button[PlayerInventory.PageCount];
        private int currentPage;
        private readonly VisualElement[] hotbarSlots = new VisualElement[PlayerSkillHotbar.SlotCount];
        private Label ghostGlyph;
        private readonly Dictionary<EquipSlot, VisualElement> slots = new Dictionary<EquipSlot, VisualElement>();
        private readonly Dictionary<EquipSlot, Sprite> silhouettes = new Dictionary<EquipSlot, Sprite>();
        private readonly List<Action> unbind = new List<Action>();
        private bool initialized;

        private PlayerInventory inventory;
        private PlayerEquipment equipment;
        private PlayerCharacterProgression progression; // a level-up changes what can be worn
        private PlayerWallet wallet;
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

        // Other windows that take items from the Pack while they are open (Vault, Trade, Kiosk). Last opened first.
        private readonly List<IPackTarget> packTargets = new List<IPackTarget>(4);

        /// <summary>The HUD's item windows, or null (no HUD in this scene, dedicated server).</summary>
        public static GearUIController Instance { get; private set; }

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
            credsLabel = root.Q<Label>("inventory-creds");
            tooltip = root.Q("item-tooltip");
            tooltipFit = new FloatingFit(tooltip);
            tipName = root.Q<Label>("tooltip-name");
            tipType = root.Q<Label>("tooltip-type");
            tipRequires = root.Q<Label>("tooltip-requires");
            tipDesc = root.Q<Label>("tooltip-desc");
            tipStats = root.Q<Label>("tooltip-stats");
            tipHint = root.Q<Label>("tooltip-hint");
            tipPet = new PetTooltip(tooltip);
            menu = root.Q("item-menu");
            menuFit = new FloatingFit(menu, 0f);
            menuActions = root.Q("item-menu-actions");
            menuTitle = root.Q<Label>("item-menu-title");
            confirm = root.Q("item-confirm");
            confirmText = root.Q<Label>("item-confirm-text");
            confirmHelp = root.Q<Label>("item-confirm-help");
            confirmYes = root.Q<Button>("item-confirm-yes");
            ghost = root.Q("item-drag-ghost");

            if (ghostGlyph == null)
            {
                ghostGlyph = new Label { pickingMode = PickingMode.Ignore };
                ghostGlyph.AddToClassList(GlyphClass);
                ghost.Add(ghostGlyph);
            }

            Array.Clear(hotbarSlots, 0, hotbarSlots.Length); // looked up again lazily in the current document
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
            Instance = this;
            initialized = true;
            OnLocalChanged();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            if (Instance == this) Instance = null;
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
                if (progression != null)
                {
                    progression.Changed -= RefreshBag;
                    progression.Changed -= RefreshOwner;
                }
                if (equipment != null) equipment.SlotChanged -= OnSlotChanged;
                if (wallet != null) wallet.Changed -= RefreshCreds;
            }
            CancelDrag();
            HideMenu();
            HideConfirm();
            inventory = next;
            equipment = next != null ? next.Equipment : null;
            progression = next != null ? next.GetComponent<PlayerCharacterProgression>() : null;
            if (progression != null) progression.Changed += RefreshBag;
            wallet = next != null ? next.GetComponent<PlayerWallet>() : null;
            if (wallet != null) wallet.Changed += RefreshCreds;
            if (identity != null) identity.Changed -= RefreshOwner;
            if (progression != null) progression.Changed += RefreshOwner;
            identity = next != null ? next.GetComponent<KryticalNova.Player.PlayerIdentity>() : null;
            if (identity != null) identity.Changed += RefreshOwner;
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
            RefreshCreds();
            RefreshOwner();
        }

        // Whose equipment the page shows: the name, and under it the level and the lineage.
        private KryticalNova.Player.PlayerIdentity identity;

        private void RefreshOwner()
        {
            if (root == null) return;
            Label name = root.Q<Label>("equipment-name"), rank = root.Q<Label>("equipment-rank");
            if (name != null) name.text = identity != null ? identity.CharacterName : string.Empty;
            if (rank == null) return;
            string lineage = identity != null && identity.Lineage != null ? identity.Lineage.displayName.ToUpperInvariant() : string.Empty;
            rank.text = progression != null ? $"LEVEL {progression.CurrentLevel}  {lineage}".TrimEnd() : lineage;
        }

        private void RefreshCreds()
        {
            if (credsLabel != null) credsLabel.text = WalletRules.Format(wallet != null ? wallet.Creds : 0);
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
                if (KeyBindings.Pressed(KeyBindings.Pack)) SetInventoryOpen(!IsOpen(inventoryWindow));
                if (KeyBindings.Pressed(KeyBindings.Equipment)) SetEquipmentOpen(!IsOpen(equipmentWindow));
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

        /// <summary>
        /// Adds icon / stack count / rim / frame children. The rim and the frame go last so they draw over the icon:
        /// the rim is the item's own outline (<see cref="SetRim"/>), the frame over it what is happening to the cell.
        /// </summary>
        public static VisualElement CreateItemVisual(VisualElement host, bool withCount)
        {
            // Behind the icon: the glow rim of a tempered weapon (SetTemperGlow). Empty for everything else.
            var glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.AddToClassList(GlowClass);
            glow.style.display = DisplayStyle.None;
            host.Insert(0, glow);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("item-icon");
            host.Insert(1, icon);
            var glyph = new Label { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList(GlyphClass);
            host.Insert(2, glyph);
            var level = new Label { pickingMode = PickingMode.Ignore };
            level.AddToClassList(LevelClass);
            host.Add(level);
            var temper = new Label { pickingMode = PickingMode.Ignore };
            temper.AddToClassList(TemperClass);
            host.Add(temper);
            if (withCount)
            {
                var count = new Label { pickingMode = PickingMode.Ignore };
                count.AddToClassList("item-count");
                host.Add(count);
            }
            var rim = new VisualElement { pickingMode = PickingMode.Ignore };
            rim.AddToClassList(RimClass);
            host.Add(rim);
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
                bool unusable = def != null && def.IsEquippable && !inventory.CanEquip(def, out _);
                SetItemVisual(cells[i], def, stack, null, unusable);
                SetTemperVisual(cells[i], def, stack);
                cells[i].EnableInClassList(LockedClass, def != null && IsLocked(i));
            }
            if (countLabel != null) countLabel.text = $"{used} / {PlayerInventory.Capacity}";
        }

        private void RefreshEquipment()
        {
            if (!initialized) return;
            foreach (var kv in slots)
            {
                var def = equipment != null ? equipment.GetDefinition(kv.Key) : null;
                // An empty slot shows the skin's picture of what it is for (.slot-glyph), not a faint item.
                ItemStack worn = inventory != null ? inventory.GetEquipped(kv.Key) : ItemStack.Empty;
                if (def != null && !worn.IsEmpty) SetItemVisual(kv.Value, def, worn); else SetItemVisual(kv.Value, def, 1);
                SetTemperVisual(kv.Value, def, worn);
            }
        }

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next) => RefreshEquipment();

        /// <summary>
        /// An item's picture where the item itself is at hand: a pet shows the icon of the evolution it has reached
        /// (<see cref="PetIcons"/>), everything else its own, and a tempered piece's rim is its Temper glow's colour
        /// (<see cref="SetRim"/>).
        /// </summary>
        public static void SetItemVisual(VisualElement host, ItemDefinition def, in ItemStack stack, Sprite silhouette = null, bool unusable = false)
        {
            SetItemVisual(host, def, stack.count, silhouette, unusable);
            if (def != null && def.IsLadderGear)
            {
                // Every window draws the local player's items, by that player's settings.
                PlayerInventory local = PlayerInventory.Local;
                PlayerEquipment worn = local != null ? local.Equipment : null;
                SetRim(host, def, GearItemState.Of(stack).Temper, worn != null ? worn.Progression : null);
            }
            if (def == null || def.equipSlot != EquipSlot.Pet) return;
            Sprite now = PetIcons.For(def, stack);
            if (now != null && now != def.icon) host.Q(className: "item-icon").style.backgroundImage = new StyleBackground(now);
        }

        public static void SetItemVisual(VisualElement host, ItemDefinition def, int count, Sprite silhouette = null, bool unusable = false)
        {
            host.EnableInClassList(UnusableClass, def != null && unusable);
            host.EnableInClassList(EmptyClass, def == null);
            var levelLabel = host.Q<Label>(className: LevelClass);
            if (levelLabel != null) levelLabel.text = def != null && def.IsEquippable && def.itemLevel > 0 ? def.itemLevel.ToString() : string.Empty;
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
            SetRim(host, def, 0, null); // its rarity's, until a caller that knows the piece's Temper says otherwise
        }

        /// <summary>
        /// The rim round an item's icon, in every window that shows one. A tempered piece's is the colour of the glow
        /// band its Temper % is in, as its rarity shows it: the colour the piece glows in the world, so the rim
        /// changes when a strike takes the piece into another band, up or down. Everything else, and an untempered
        /// piece, keeps its rarity's colour (the <c>.rarity-</c> rules of Items.uss). The art of the rim is the
        /// faction skin's.
        /// </summary>
        public static void SetRim(VisualElement host, ItemDefinition def, int temper, GearProgressionSettings settings)
        {
            VisualElement rim = host != null ? host.Q(className: RimClass) : null;
            if (rim == null) return;
            if (def != null && def.IsLadderGear && settings != null && GearProgressionRules.TryGetGlow(settings.temper, temper, out TemperGlowBand band))
                rim.style.unityBackgroundImageTintColor = band.ColorFor(def.rarity);
            else
                rim.style.unityBackgroundImageTintColor = StyleKeyword.Null;
        }

        /// <summary>The Temper % of a piece of gear, in the corner of its icon and in its glow colour.</summary>
        private void SetTemperVisual(VisualElement host, ItemDefinition def, in ItemStack stack)
        {
            var label = host.Q<Label>(className: TemperClass);
            if (label == null) return;
            int temper = def != null && def.IsLadderGear ? GearItemState.Of(stack).Temper : 0;
            label.text = temper > 0 ? $"+{temper}" : string.Empty;
            GearProgressionSettings settings = equipment != null ? equipment.Progression : null;
            if (temper > 0 && settings != null && GearProgressionRules.TryGetGlow(settings.temper, temper, out TemperGlowBand band))
                label.style.color = band.ColorFor(def.rarity);
            SetTemperGlow(host, def, temper, settings);
        }

        /// <summary>
        /// The glow rim on a weapon's icon: the outline of the part of the weapon that glows in the world
        /// (<see cref="ItemGlowIcons"/>: the blade, not the hilt), behind the icon, in the colour of the glow band
        /// its Temper % is in. Nothing for an untempered
        /// weapon, for other gear and for an empty cell. The Pack, the Equipment page and the forge all show it, and
        /// it follows the Temper as it changes.
        /// </summary>
        public static void SetTemperGlow(VisualElement host, ItemDefinition def, int temper, GearProgressionSettings settings)
        {
            VisualElement glow = host != null ? host.Q(className: GlowClass) : null;
            if (glow == null) return;
            Sprite rim = def != null && def.equipSlot == EquipSlot.Weapon && temper > 0 && settings != null && settings.iconGlows != null
                ? settings.iconGlows.RimOf(def.icon) : null;
            if (rim == null || !GearProgressionRules.TryGetGlow(settings.temper, temper, out TemperGlowBand band))
            {
                glow.style.display = DisplayStyle.None;
                return;
            }
            glow.style.backgroundImage = new StyleBackground(rim);
            glow.style.unityBackgroundImageTintColor = band.ColorFor(def.rarity);
            glow.style.display = DisplayStyle.Flex;
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
            if (loc.IsBag && IsLocked(loc.index)) return; // promised to a trade or a Kiosk: hands off until that ends

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
            int hotbarSlot = HotbarSlotAt(pos);
            SetHoverTarget(tab >= 0 ? pageTabs[tab] : hotbarSlot >= 0 ? hotbarSlots[hotbarSlot] : FindItemElement(pos));
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

            if (from.IsBag)
            {
                for (int i = packTargets.Count - 1; i >= 0; i--)
                    if (packTargets[i].IsOpen && packTargets[i].TryDrop(from, pos)) return;
            }

            int hotbarSlot = HotbarSlotAt(pos);
            if (hotbarSlot >= 0)
            {
                AssignToHotbar(from, hotbarSlot);
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
            Sprite picture = PetIcons.For(def, inventory != null ? inventory.GetStack(pressLocation) : ItemStack.Empty);
            ghost.style.backgroundImage = picture != null ? new StyleBackground(picture) : StyleKeyword.None;
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

        /// <summary>The hotbar slot (owned by ProgressionHUD, same document) under the pointer, or -1.</summary>
        private int HotbarSlotAt(Vector2 panelPosition)
        {
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                if (hotbarSlots[i] == null) hotbarSlots[i] = root.Q($"slot-{i}");
                if (hotbarSlots[i] != null && hotbarSlots[i].worldBound.Contains(panelPosition)) return i;
            }
            return -1;
        }

        /// <summary>Dropping a bag item on the hotbar links it there; the item itself stays in the bag.</summary>
        private void AssignToHotbar(ItemLocation from, int slot)
        {
            var def = DefinitionAt(from);
            var hotbar = inventory != null ? inventory.GetComponent<PlayerSkillHotbar>() : null;
            if (def == null || hotbar == null) return;
            string key = PlayerSkillHotbar.GetKeyLabel(slot);
            bool assigned = from.IsBag && hotbar.AssignItem(slot, def.itemId);
            ClientPresentationRegistry.Messages?.ShowMessage(assigned
                ? $"{def.displayName} set to key {key}."
                : $"{def.displayName} can't be used from the hotbar.");
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
            if (loc.IsBag)
            {
                IPackTarget target = OpenPackTarget();
                if (target != null && target.TryQuick(loc)) return;
            }
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
            actions.Add(new MenuAction("Drop", () => ShowConfirm(loc)));
            actions.Add(new MenuAction("Destroy", () => ShowConfirm(loc, destroy: true), danger: true));
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
            menuFit.At(new Vector2(panelPosition.x + 6f, panelPosition.y + 6f));
        }

        private void HideMenu() => SetDisplay(menu, false);

        /// <summary>Asks before an item leaves the Pack: onto the ground (others may pick it up), or destroyed for good.</summary>
        private void ShowConfirm(ItemLocation loc, bool destroy = false)
        {
            var def = DefinitionAt(loc);
            if (def == null) return;
            confirmLocation = loc;
            confirmDestroys = destroy;
            confirmText.text = destroy ? $"Destroy {def.displayName}?" : $"Drop {def.displayName}?";
            if (confirmHelp != null)
            {
                PlayerLoot loot = inventory != null ? inventory.GetComponent<PlayerLoot>() : null;
                string after = loot == null || loot.DropsOpenToEveryone ? "it is free for all" : "others of your faction can take it";
                confirmHelp.text = destroy
                    ? "It will be destroyed. This can't be undone."
                    : loot != null && loot.DropReserveSeconds > 0f
                        ? $"It goes on the ground at your feet. It stays yours alone for {loot.DropReserveSeconds:0} seconds, then {after}."
                        : "It goes on the ground at your feet, where anyone can take it.";
            }
            if (confirmYes != null) confirmYes.text = destroy ? "DESTROY" : "DROP";
            SetDisplay(confirm, true);
            confirm.BringToFront();
        }

        private void ConfirmDrop()
        {
            HideConfirm();
            suppressWorldInputFrame = Time.frameCount;
            if (inventory == null) return;
            var loot = confirmDestroys ? null : inventory.GetComponent<PlayerLoot>();
            if (loot != null) loot.RequestDrop(confirmLocation);
            else inventory.RequestDestroy(confirmLocation);
        }

        private void HideConfirm() => SetDisplay(confirm, false);

        // ------------------------------------------------------------------ tooltip

        private void ShowTooltip(VisualElement element)
        {
            if (!(element.userData is ItemLocation loc)) return;
            var def = DefinitionAt(loc);
            if (def == null) { HideTooltip(); return; }

            IPackTarget target = loc.IsBag ? OpenPackTarget() : null;
            string hint = target != null && !string.IsNullOrEmpty(target.QuickHint) ? $"Right-click: {target.QuickHint}   ·   Drag to move"
                : !loc.IsBag ? "Right-click: unequip   ·   Left-click: options"
                : IsUsable(def) ? (def.IsHotbarUsable ? "Right-click: use   ·   Drag to the hotbar to bind a key" : "Right-click: use   ·   Left-click: options")
                : def.IsEquippable ? "Right-click: equip   ·   Left-click: options"
                : "Left-click: options";
            ShowItemTooltip(element, def, inventory != null ? inventory.GetStack(loc) : ItemStack.Empty, hint, !loc.IsBag,
                loc.IsBag ? null : LookLine(loc.Slot));
        }

        /// <summary>
        /// What a worn slot shows in place of the piece in it: "Weave: Commander" (the set the Weave is from), or
        /// "Morph: Kitsune Spirit, Nine Tails" for the pet. Null when the slot shows what is worn.
        /// </summary>
        private string LookLine(EquipSlot slot)
        {
            if (equipment == null) return null;
            if (slot == EquipSlot.Pet)
            {
                PlayerPet pet = PlayerPet.Local;
                EquipmentLooks looks = equipment.Looks;
                if (pet == null || pet.Pet == null || pet.Catalog == null || looks.pet == 0) return null;
                return MorphRules.Shown(pet.Pet, pet.Stage, pet.Catalog.Get(looks.pet), looks.petStage, out PetDefinition seen, out int stage)
                    ? $"Morph: {MorphRules.NameOf(seen, stage)}" : null;
            }
            ItemDefinition worn = equipment.GetDefinition(slot), shown = equipment.GetShownDefinition(slot);
            return shown != null && shown != worn ? $"Weave: {WeaveRules.NameOf(inventory != null ? inventory.Gear : null, shown)}" : null;
        }

        /// <summary>
        /// The item tooltip beside any element: for the Pack's own cells and for other windows that show items
        /// (Vault, Trade, Kiosk). The stack carries the item's own state, so a pet reads the same everywhere.
        /// </summary>
        /// <param name="equipped">The item is being worn: the card says so.</param>
        /// <param name="look">What the slot shows over the piece ("Weave: Commander"), or null. Only a worn piece has one.</param>
        /// <param name="above">Over the element rather than beside it: for the Hotbar, whose slots stand in a row.</param>
        public void ShowItemTooltip(VisualElement element, ItemDefinition def, in ItemStack itemStack, string hint, bool equipped = false, string look = null, bool above = false)
        {
            if (!initialized || element == null || def == null) { HideItemTooltip(); return; }
            ShowCardPicture(def, equipped, PetIcons.For(def, itemStack));

            GearItemState gearState = def.IsLadderGear ? GearItemState.Of(itemStack) : default;
            tipName.text = gearState.IsClean ? def.displayName : $"{def.displayName}  {gearState}";
            ApplyRarityText(tipName, def.rarity);
            tipType.text = TypeLine(def);
            // A dyed piece says so (the colour goes with it into a trade, a Kiosk or the Vault), and so does a slot that
            // shows another look: each on a strip of its own, so it is not lost in the small type line.
            ShowCardLooks(gearState, look);
            string requirement = RequirementLine(def, out bool unmet);
            if (tipRequires != null)
            {
                tipRequires.text = requirement;
                tipRequires.EnableInClassList(UnmetClass, unmet);
                SetDisplay(tipRequires, requirement.Length > 0);
            }
            tipDesc.text = def.description;
            // A booster's numbers are settings, not text: its tooltip reads them, and the time left of it.
            if (def.useEffect == ItemUseEffect.ExperienceBoost && inventory != null
                && inventory.TryGetComponent(out KryticalNova.Progression.PlayerExperienceBoosts boosts))
            {
                string line = boosts.TooltipLine(def);
                if (line != null) tipDesc.text = def.description + "\n" + line;
            }
            // A Flask says how many drinks it has left: they travel with the bottle.
            if (def.useEffect == ItemUseEffect.FluxFlask && inventory != null && inventory.TryGetComponent(out PlayerFlask flask))
                tipDesc.text = def.description + "\n" + flask.TooltipLine(itemStack);
            // A pet's stage, growth, hunger and bonus are a block of their own; the line under it says what it grows.
            string petLine = tipPet.Show(def, itemStack.IsEmpty ? new ItemStack(def.itemId, 1) : itemStack);
            // What the piece gives is a strip a line; how far it has been worked on stays a paragraph under them.
            // A Dossier or Skill Chip says what kind of skill it teaches (area, single, two or three hits, or a ward).
            ShowCardStats(petLine != null ? null : def.useEffect == ItemUseEffect.LearnSkill ? TaughtSkillLines(def) : FormatStats(def));
            tipStats.text = petLine != null ? $"<color={SkillColor}>{petLine}</color>" : FormatProgression(def, itemStack, gearState).TrimStart('\n');
            SetDisplay(tipStats, tipStats.text.Length > 0);
            tipHint.text = hint ?? string.Empty;
            ShowCardHints(hint);

            SetDisplay(tooltip, true);
            tooltip.BringToFront();
            // Beside the cell, on whichever side has room, and never off the screen (see FloatingFit).
            if (above) tooltipFit.Above(element.worldBound); else tooltipFit.Beside(element.worldBound);
        }

        private void HideTooltip() => SetDisplay(tooltip, false);

        // ------------------------------------------------------------------ the item card's parts

        private void ShowCardPicture(ItemDefinition def, bool equipped, Sprite shown)
        {
            VisualElement picture = tooltip.Q("tooltip-picture"), icon = tooltip.Q("tooltip-icon"), flag = tooltip.Q("tooltip-equipped");
            if (flag != null) SetDisplay(flag, equipped);
            if (picture == null || icon == null) return;
            SetDisplay(picture, shown != null);
            icon.style.backgroundImage = shown != null ? new StyleBackground(shown) : StyleKeyword.None;
            foreach (string each in RarityCardClasses) picture.RemoveFromClassList(each);
            picture.AddToClassList(RarityCardClasses[Mathf.Clamp((int)def.rarity, 0, RarityCardClasses.Length - 1)]);
        }

        /// <summary>
        /// The dye and the look strips: the colourway's name beside its two colours, and the Weave or Morph the worn
        /// slot shows ("Weave: Commander" reads as a WEAVE strip saying Commander). A strip is hidden when there is
        /// nothing for it.
        /// </summary>
        private void ShowCardLooks(GearItemState gearState, string look)
        {
            VisualElement dyeRow = tooltip.Q("tooltip-dye"), weaveRow = tooltip.Q("tooltip-weave");
            if (dyeRow == null || weaveRow == null) return;
            bool dyed = false;
            if (gearState.Dye != 0 && inventory != null && inventory.Catalog != null && inventory.Catalog.Themes != null
                && inventory.Catalog.Themes.TryGet(gearState.Dye, out GearThemeLibrary.Theme theme))
            {
                string colourway = inventory.Catalog.Themes.DisplayNameOf(gearState.Dye);
                if (colourway != null)
                {
                    dyed = true;
                    Label value = dyeRow.Q<Label>("tooltip-dye-value");
                    if (value != null) value.text = colourway;
                    VisualElement primary = dyeRow.Q("tooltip-dye-primary"), secondary = dyeRow.Q("tooltip-dye-secondary");
                    if (primary != null) primary.style.backgroundColor = (Color)theme.primary;
                    if (secondary != null) secondary.style.backgroundColor = (Color)theme.secondary;
                }
            }
            SetDisplay(dyeRow, dyed);

            bool shown = !string.IsNullOrEmpty(look);
            if (shown)
            {
                int cut = look.IndexOf(':');
                string tag = cut > 0 ? look.Substring(0, cut).Trim().ToUpperInvariant() : "LOOK";
                string value = cut > 0 ? look.Substring(cut + 1).Trim() : look;
                Label tagLabel = weaveRow.Q<Label>("tooltip-weave-tag"), valueLabel = weaveRow.Q<Label>("tooltip-weave-value");
                if (tagLabel != null) tagLabel.text = tag;
                if (valueLabel != null) valueLabel.text = value;
            }
            SetDisplay(weaveRow, shown);
        }

        /// <summary>
        /// A Dossier's or Skill Chip's strips: the kind of skill it teaches and what the skill does at its first rank.
        /// </summary>
        private static string TaughtSkillLines(ItemDefinition def)
        {
            PlayerCombatSkills skills = PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerCombatSkills>() : null;
            SkillDefinitionSO skill = skills != null ? skills.SkillTaughtBy(def.itemId) : null;
            if (skill == null) return string.Empty;
            return $"<color={PrimaryColor}>{SkillKinds.KindLine(skill, 1)}</color>\n{SkillKinds.Describe(skill, 1)}";
        }

        private void ShowCardStats(string stats)
        {
            VisualElement rows = tooltip.Q("tooltip-stat-rows");
            if (rows == null) return;
            rows.Clear();
            if (string.IsNullOrEmpty(stats)) return;
            foreach (string line in stats.Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("card-stat");
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList("card-stat-pip");
                var text = new Label(line.Trim()) { pickingMode = PickingMode.Ignore };
                text.AddToClassList("card-stat-text");
                row.Add(pip);
                row.Add(text);
                rows.Add(row);
            }
        }

        /// <summary>
        /// The hint as prompts, one a line: "Right-click: use" becomes the mouse with its right button lit and "use".
        /// </summary>
        private void ShowCardHints(string hint)
        {
            VisualElement rows = tooltip.Q("tooltip-hints");
            if (rows == null) return;
            rows.Clear();
            if (string.IsNullOrEmpty(hint)) return;
            foreach (string part in hint.Split(new[] { "·" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string text = part.Trim();
                if (text.Length == 0) continue;
                string mouse = null;
                if (text.StartsWith("Right-click:", StringComparison.OrdinalIgnoreCase)) { mouse = "mouse-right"; text = text.Substring("Right-click:".Length).Trim(); }
                else if (text.StartsWith("Left-click:", StringComparison.OrdinalIgnoreCase)) { mouse = "mouse-left"; text = text.Substring("Left-click:".Length).Trim(); }
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("prompt-row");
                if (mouse != null)
                {
                    var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
                    glyph.AddToClassList("key-mouse");
                    glyph.AddToClassList(mouse);
                    row.Add(glyph);
                }
                if (text.Length > 0) text = char.ToUpperInvariant(text[0]) + text.Substring(1);
                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("prompt-text");
                row.Add(label);
                rows.Add(row);
            }
        }

        public void HideItemTooltip()
        {
            if (initialized) HideTooltip();
        }

        // ------------------------------------------------------------------ other windows that work with the Pack

        /// <summary>A window that takes Pack items while it is open. Register on enable, unregister on disable.</summary>
        public void RegisterPackTarget(IPackTarget target)
        {
            if (target != null && !packTargets.Contains(target)) packTargets.Add(target);
        }

        public void UnregisterPackTarget(IPackTarget target) => packTargets.Remove(target);

        private IPackTarget OpenPackTarget()
        {
            for (int i = packTargets.Count - 1; i >= 0; i--)
                if (packTargets[i].IsOpen) return packTargets[i];
            return null;
        }

        private bool IsLocked(int cell)
        {
            for (int i = 0; i < packTargets.Count; i++)
                if (packTargets[i].IsOpen && packTargets[i].LocksCell(cell)) return true;
            return false;
        }

        /// <summary>Redraws the Pack (a window changed which cells it holds on to).</summary>
        public void RefreshPack() => RefreshBag();

        /// <summary>The Pack cell under a point of the panel, or -1.</summary>
        public int PackCellAt(Vector2 panelPosition)
        {
            if (!initialized || !IsOpen(inventoryWindow)) return -1;
            int first = currentPage * PlayerInventory.PageSize;
            for (int i = first; i < first + PlayerInventory.PageSize; i++)
                if (cells[i].worldBound.Contains(panelPosition)) return i;
            return -1;
        }

        /// <summary>True when the point is anywhere on the Pack window.</summary>
        public bool IsOverPack(Vector2 panelPosition) =>
            initialized && IsOpen(inventoryWindow) && inventoryWindow.worldBound.Contains(panelPosition);

        public bool IsInventoryOpen => initialized && IsOpen(inventoryWindow);

        /// <summary>The drag picture, for a window dragging an item of its own. Hide it when the drag ends.</summary>
        public void ShowGhost(ItemDefinition def, Vector2 panelPosition)
        {
            if (!initialized || def == null) return;
            ghost.style.backgroundImage = def.icon != null ? new StyleBackground(def.icon) : StyleKeyword.None;
            SetGlyph(ghostGlyph, def);
            SetDisplay(ghost, true);
            ghost.BringToFront();
            MoveGhost(panelPosition);
        }

        public void MoveGhost(Vector2 panelPosition)
        {
            if (!initialized) return;
            ghost.style.left = panelPosition.x - GhostHalfSize;
            ghost.style.top = panelPosition.y - GhostHalfSize;
        }

        public void HideGhost()
        {
            if (!initialized) return;
            SetDisplay(ghost, false);
            suppressWorldInputFrame = Time.frameCount;
        }

        /// <summary>Clicks that end on the HUD must not also move the character this frame.</summary>
        public void SuppressWorldInput() => suppressWorldInputFrame = Time.frameCount;

        /// <summary>Consumables that aren't gear are used, not equipped, on right-click.</summary>
        private static bool IsUsable(ItemDefinition def) => def != null && def.IsConsumable && !def.IsEquippable;

        /// <summary>"WEAPON  ·  RARE  ·  LEVEL 45", with the class on a weapon.</summary>
        private static string TypeLine(ItemDefinition def)
        {
            string rarity = def.rarity.ToString().ToUpperInvariant();
            if (!def.IsEquippable) return rarity;
            string slot = def.weaponClass != WeaponClassId.None
                ? WeaponClasses.DisplayName(def.weaponClass).ToUpperInvariant()
                : def.equipSlot.ToString().ToUpperInvariant();
            return def.itemLevel > 0 ? $"{slot}  ·  {rarity}  ·  LEVEL {def.itemLevel}" : $"{slot}  ·  {rarity}";
        }

        /// <summary>Why the local character cannot wear this (level, faction, body), or the level it asks for. Empty for non-gear.</summary>
        private string RequirementLine(ItemDefinition def, out bool unmet)
        {
            unmet = false;
            if (!def.IsEquippable) return string.Empty;
            if (inventory != null && !inventory.CanEquip(def, out string reason))
            {
                unmet = true;
                return reason;
            }
            return def.itemLevel > 1 ? PlayerInventory.RequiresLevel(def.itemLevel) : string.Empty;
        }

        private const string PrimaryColor = "#F0ECDC";
        private const string SkillColor = "#72DEC4";

        /// <summary>A Serum's lines: what one gives for good, and how many of it this character has taken of how many.</summary>
        private static void AppendSerum(System.Text.StringBuilder sb, ItemDefinition def)
        {
            PlayerSerums mine = PlayerSerums.Local;
            SerumSettings settings = mine != null ? mine.Settings : null;
            int kind = SerumRules.KindOf(settings, def.itemId);
            if (kind < 0) return;
            SerumKind serum = settings.serums[kind];
            int taken = mine.State.Taken(kind), cap = settings.Cap;
            sb.Append("<color=").Append(PrimaryColor).Append('>').Append(SerumRules.Gains(serum, 1)).AppendLine(" each, for good</color>");
            sb.Append("Taken ").Append(taken).Append(" / ").Append(cap);
            if (taken > 0) sb.Append("  ·  ").Append(SerumRules.Gains(serum, taken)).Append(" so far");
            sb.AppendLine();
            if (taken >= cap) sb.AppendLine("You can take no more of these.");
        }

        private static string FormatStats(ItemDefinition def)
        {
            var sb = new System.Text.StringBuilder();
            if (def.IsConsumable)
            {
                if (def.restoreHealth > 0f) sb.AppendLine($"Restores {def.restoreHealth:0} HP");
                if (def.restoreChi > 0f) sb.AppendLine($"Restores {def.restoreChi:0} Flux");
                if (def.useEffect == ItemUseEffect.Serum) AppendSerum(sb, def);
                return sb.ToString().TrimEnd();
            }
            if (!def.IsEquippable) return string.Empty;
            if (!def.HasStats && (def.skillModifiers == null || def.skillModifiers.Length == 0))
                return def.equipSlot == EquipSlot.Pet ? string.Empty : "No stats";

            // Primary stats first and brighter: they are what the piece is for.
            if (def.baseStats != null)
                foreach (var m in def.baseStats)
                    sb.Append("<color=").Append(PrimaryColor).Append('>').Append(StatDisplay.FormatModifier(m.stat, m.modType, m.value)).AppendLine("</color>");
            if (def.secondaryStats != null)
                foreach (var m in def.secondaryStats) sb.AppendLine(StatDisplay.FormatModifier(m.stat, m.modType, m.value));
            if (def.statModifiers != null)
                foreach (var m in def.statModifiers) sb.AppendLine(StatDisplay.FormatModifier(m.stat, m.modType, m.value));
            if (def.skillModifiers != null)
            {
                foreach (var m in def.skillModifiers)
                {
                    string line = m.line != WeaponClassId.None ? $"{WeaponClasses.DisplayName(m.line)} skills" : "class skills";
                    sb.Append("<color=").Append(SkillColor).Append(">+").Append(m.levels).Append(" to ").Append(line).AppendLine("</color>");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private readonly List<StatModifier> progressionScratch = new List<StatModifier>(8);

        /// <summary>What the piece's own Temper, Fusion and Tuning add, under its baked stats.</summary>
        private string FormatProgression(ItemDefinition def, in ItemStack stack, GearItemState state)
        {
            GearProgressionSettings settings = equipment != null ? equipment.Progression : null;
            if (settings == null || state.IsClean) return string.Empty;
            progressionScratch.Clear();
            GearProgressionRules.AppendEffects(settings, stack, def, null, progressionScratch);
            if (progressionScratch.Count == 0) return string.Empty;

            string colour = GearProgressionRules.TryGetGlow(settings.temper, state.Temper, out TemperGlowBand band)
                ? "#" + ColorUtility.ToHtmlStringRGB(band.ColorFor(def.rarity)) : SkillColor;
            var sb = new System.Text.StringBuilder();
            sb.Append("\n<color=").Append(colour).Append('>');
            if (state.Temper > 0) sb.Append("Temper +").Append(state.Temper).Append("%  ");
            if (state.Fusion > 0) sb.Append("Fusion FS").Append(state.Fusion).Append("  ");
            if (state.Tune > 0) sb.Append("Tune ").Append(state.Tune);
            sb.Append("</color>");
            // One line per stat, however many of the three systems feed it.
            for (int i = 0; i < progressionScratch.Count; i++)
            {
                StatModifier first = progressionScratch[i];
                bool counted = false;
                for (int j = 0; j < i && !counted; j++)
                    counted = progressionScratch[j].Stat == first.Stat && progressionScratch[j].ModType == first.ModType;
                if (counted) continue;
                float total = 0f;
                for (int j = i; j < progressionScratch.Count; j++)
                    if (progressionScratch[j].Stat == first.Stat && progressionScratch[j].ModType == first.ModType) total += progressionScratch[j].Value;
                sb.Append("\n<color=").Append(SkillColor).Append('>').Append(StatDisplay.FormatModifier(first.Stat, first.ModType, total)).Append("</color>");
            }
            return sb.ToString();
        }

        public static void ApplyRarityText(Label label, ItemRarity rarity)
        {
            foreach (var c in RarityTextClasses) label.RemoveFromClassList(c);
            label.AddToClassList(RarityTextClasses[Mathf.Clamp((int)rarity, 0, RarityTextClasses.Length - 1)]);
        }

        // ------------------------------------------------------------------ 3D preview

        private static bool CanTryOn(ItemDefinition def) =>
            def.equipSlot == EquipSlot.Weapon || def.equipSlot == EquipSlot.Armor || def.equipSlot == EquipSlot.Gloves ||
            def.equipSlot == EquipSlot.Boots || def.equipSlot == EquipSlot.Cape;

        private void TryOn(ItemDefinition def) => PreviewPiece(def);

        public bool IsEquipmentOpen => initialized && IsOpen(equipmentWindow);

        /// <summary>
        /// Shows a piece on the equipment page's doll without wearing it, and opens the page for it: a piece being
        /// tried on, a Weave or a colourway being chosen (<paramref name="dye"/> is then the colourway's colour grid,
        /// and <paramref name="colourway"/> its id, which is what a weapon is repainted by).
        /// </summary>
        public void PreviewPiece(ItemDefinition def, Color32[] dye = null, string label = null, bool overWorn = false, ushort colourway = 0)
        {
            if (!initialized || def == null || !CanTryOn(def)) return;
            bool wasOpen = IsOpen(equipmentWindow) && stage != null && stage.IsOpen;
            SetEquipmentOpen(true);
            // The stage opens after layout; defer the try-on until it has a doll.
            viewport.schedule.Execute(() =>
            {
                if (stage == null || !stage.TryOn(def, dye, overWorn, colourway)) return;
                tryOnLabel.text = label ?? $"Trying on: {def.displayName}";
                SetDisplay(tryOnChip, true);
            }).StartingIn(wasOpen ? 0 : 30);
        }

        /// <summary>
        /// Gives the equipment page's doll a look of its own, and opens the page for it: <paramref name="dress"/>
        /// is handed the doll once it stands there (a restyle being chosen applies its recipe to it).
        /// </summary>
        public void PreviewOnDoll(Action<GameObject> dress, string label)
        {
            if (!initialized || dress == null) return;
            bool wasOpen = IsOpen(equipmentWindow) && stage != null && stage.IsOpen;
            SetEquipmentOpen(true);
            viewport.schedule.Execute(() =>
            {
                GameObject doll = stage != null ? stage.BeginOwnLook() : null;
                if (doll == null) return;
                dress(doll);
                tryOnLabel.text = label;
                SetDisplay(tryOnChip, true);
            }).StartingIn(wasOpen ? 0 : 30);
        }

        /// <summary>The doll wears what the character shows again.</summary>
        public void ClearPreview() => ClearTryOn();

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
