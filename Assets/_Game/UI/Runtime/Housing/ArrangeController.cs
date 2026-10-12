using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Housing;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Arrange: the Furnishing Crate window and the tool that furnishes a Hall, on its lawns and in the room
    /// inside. The window lists what the Crate holds; a click on a furnishing puts a preview of it under the
    /// cursor, green where it may stand and red with the reason where it may not, and a click sets it down. A piece
    /// already standing is picked up by a click, turned with Q and E, put away with Delete; a click on bare ground
    /// walks there. A Facade or a Finish in the Crate is worn by the Hall at a click. Away from the Hall the window
    /// is the Crate alone: furnishings go in from the Pack and come out to it.
    /// <para>The comfort set is in the two other parts of this class: several pieces picked and moved together,
    /// Advanced mode with its lift, tilt and size, copying a piece, the view from above.</para>
    /// The tool decides nothing. It asks the Hall's authority for every edit (<see cref="IHomeAuthority"/>) and
    /// draws what comes back, with the same rules the authority uses to say beforehand what will be refused.
    /// While it is open at the Hall the left button and the hotbar are the tool's; the camera stays the player's.
    /// It also draws the curtain of a trip to the Hall or through its door.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class ArrangeController : MonoBehaviour, IPackTarget
    {
        private const string WindowName = "arrange-window";
        private const string PackWindowName = "inventory-window";
        private const float WindowWidth = 372f, WindowRight = 24f, WindowTop = 64f, WindowMargin = 20f;
        private const int LeftButton = 0, RightButton = 1, MiddleButton = 2;
        private const string AllCategories = "All", Favourites = "★", Recent = "Recent", HallCategory = "Hall";
        private const string FavouritesKeyPrefix = "kn.housing.favourites.";
        private const int MostRecent = 16;
        private const float AwayConfirmSeconds = 3f;
        private static readonly HomeLook[] LookSlots = { HomeLook.Facade, HomeLook.Wall, HomeLook.Floor, HomeLook.Ceiling };
        private static readonly string[] LookNames = { "FACADE", "WALLS", "FLOOR", "CEILING" };

        private enum PendingKind { Edit, Undo, Redo }
        private struct Pending { public PendingKind kind; public List<HomeOp> ops; public HomeArea area; }

        private VisualElement root, window, grid, categoryRow, budgetFill, toolRow, adjustPanel, looksRow, boxRect, curtain;
        private Label budgetLabel, statusLabel, keyLabel, adjustLabel, curtainLabel;
        private Button travelButton, doorButton, backButton, upButton, downButton, modeButton, overheadButton, awayButton;
        private readonly Button[] lookButtons = new Button[4];
        private TextField search;
        private FloatingFit windowFit;
        private readonly List<VisualElement> cells = new List<VisualElement>(64);
        private readonly List<Label> cellStars = new List<Label>(64);
        private readonly List<CrateEntry> shownEntries = new List<CrateEntry>(64);
        private readonly List<string> categories = new List<string>(12);
        private string category = AllCategories;
        private readonly HashSet<ushort> favourites = new HashSet<ushort>();
        private readonly List<ushort> recents = new List<ushort>(MostRecent);
        private string favouritesKey;
        private float awayArmedUntil;

        private PlayerHousing housing;
        private PlayerCrate crate;
        private IHomeAuthority authority;
        private readonly ArrangeHistory[] histories = new ArrangeHistory[HomeAreas.Count];
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private bool parked;
        private bool open;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            BuildWindow();
            BuildOverlays();
            PlayerInventory.LocalChanged += OnLocalChanged;
        }

        private void OnDisable()
        {
            PlayerInventory.LocalChanged -= OnLocalChanged;
            SetOpen(false);
            Bind(null);
            if (GearUIController.Instance != null) GearUIController.Instance.UnregisterPackTarget(this);
            window?.RemoveFromHierarchy();
            boxRect?.RemoveFromHierarchy();
            curtain?.RemoveFromHierarchy();
            window = null;
        }

        private void OnLocalChanged() => Bind(null);

        private void Bind(PlayerHousing next)
        {
            if (housing != null) housing.Changed -= OnHomeChanged;
            if (crate != null) crate.Changed -= OnCrateChanged;
            BindAuthority(null);
            housing = next;
            crate = housing != null ? housing.Crate : null;
            if (housing != null) housing.Changed += OnHomeChanged;
            if (crate != null) crate.Changed += OnCrateChanged;
            if (housing != null) BindAuthority(housing.Authority);
            LoadFavourites();
        }

        private void BindAuthority(IHomeAuthority next)
        {
            if (next == authority && next != null) return;
            DropEverything();
            if (authority != null)
            {
                authority.Changed -= OnEdited;
                authority.Refused -= OnRefused;
                authority.LooksChanged -= OnLooksChanged;
            }
            authority = next;
            pending.Clear();
            for (int n = 0; n < histories.Length; n++) histories[n] = null;
            if (authority == null) return;
            authority.Changed += OnEdited;
            authority.Refused += OnRefused;
            authority.LooksChanged += OnLooksChanged;
            int steps = authority.Settings != null ? authority.Settings.undoSteps : 50;
            for (int n = 0; n < histories.Length; n++) histories[n] = new ArrangeHistory(steps);
        }

        // The player arrived, left, or went through the door: whatever was in hand belongs to where they were.
        private void OnHomeChanged()
        {
            DropEverything();
            BindAuthority(housing != null ? housing.Authority : null);
            if (open) Refresh();
        }

        private void OnCrateChanged()
        {
            if (open) Refresh();
        }

        private void OnLooksChanged(HomeLook slot)
        {
            if (open) Refresh();
        }

        private bool AtHall => housing != null && housing.InHome && authority != null && !housing.Travelling;
        /// <summary>The part of the Hall the player is in: the one the tool works on.</summary>
        private HomeArea Area => housing != null ? housing.Area : HomeArea.Lawn;
        private ArrangeHistory History => histories[(int)Area];
        private HomeView View => housing != null ? housing.View(Area) : null;
        private Transform PieceRoot => housing != null ? housing.PieceRoot(Area) : null;
        private List<PlacedPiece> Pieces => authority.Home.Area(Area);

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (window == null) return;
            if (housing == null)
            {
                PlayerHousing local = PlayerHousing.Local;
                if (local != null) Bind(local);
            }
            DrawCurtain();

            bool typing = HudTyping.IsTyping(root);
            if (housing != null && !typing && KeyBindings.Pressed(KeyBindings.Arrange)) SetOpen(!open);

            bool tool = open && AtHall && PieceRoot != null && View != null;
            Park(tool);
            if (!tool) DropEverything();
            if (!open) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !typing)
            {
                // One thing at a time is let go of, the last taken up first.
                if (carried != null) StopCarrying();
                else if (groupCarried) StopCarryingGroup();
                else if (picked.Count > 0) ClearPicked();
                else if (overhead.Active) SetOverhead(false);
                else SetOpen(false);
                return;
            }
            if (tool) Tool(keyboard, typing);
        }

        // While the tool is out, a click is the tool's and the hotbar keys turn pieces: both are handed back after.
        private void Park(bool on)
        {
            if (!on && !parked) return;
            PlayerInventory local = PlayerInventory.Local;
            if (local != null)
            {
                // A trip parks the clicks for itself and hands them back when it ends.
                if (local.TryGetComponent(out PointAndClickPlayer mover) && (housing == null || !housing.Travelling)) mover.WorldClicksParked = on;
                // Asked for again every frame: a text field that loses focus hands the hotbar back by itself.
                if (local.TryGetComponent(out PlayerSkillHotbar hotbar) && (on || parked)) hotbar.InputSuppressed = on;
            }
            parked = on;
        }

        // Lets go of everything the tool holds: the piece in hand, the pieces picked, the box, the view from above.
        private void DropEverything()
        {
            if (carried != null) StopCarrying();
            if (groupCarried) StopCarryingGroup();
            if (picked.Count > 0) ClearPicked();
            ClearFrames();
            pressed = boxing = false;
            if (boxRect != null) boxRect.style.display = DisplayStyle.None;
            if (overhead.Active) SetOverhead(false);
        }

        private Camera ViewCamera()
        {
            PlayerInventory local = PlayerInventory.Local;
            Camera own = local != null && local.TryGetComponent(out PointAndClickPlayer mover) ? mover.GameplayCamera : null;
            return own != null ? own : Camera.main;
        }

        private bool OverWindow(Vector2 screen)
        {
            if (window == null || window.panel == null) return false;
            return window.worldBound.Contains(ToPanel(screen));
        }

        private Vector2 ToPanel(Vector2 screen) =>
            RuntimePanelUtils.ScreenToPanel(window.panel, new Vector2(screen.x, Screen.height - screen.y));

        // ------------------------------------------------------------------ asking the authority

        private void Ask(PendingKind kind, HomeOp op) => Ask(kind, new List<HomeOp>(1) { op });

        private void Ask(PendingKind kind, List<HomeOp> ops)
        {
            if (authority == null || ops == null || ops.Count == 0) return;
            // Answers come back in the order they were asked for (at once, in the sandbox).
            pending.Enqueue(new Pending { kind = kind, ops = ops, area = Area });
            authority.Submit(Area, ops);
        }

        private void OnEdited(HomeArea area, ushort request, IReadOnlyList<HomeChange> changes)
        {
            if (request != 0 && pending.Count > 0)
            {
                Pending asked = pending.Dequeue();
                ArrangeHistory history = histories[(int)asked.area];
                if (history != null)
                {
                    if (asked.kind == PendingKind.Edit) history.Record(changes);
                    else history.Settle(asked.kind == PendingKind.Undo, asked.ops, changes);
                }
            }
            // Pieces that were picked and are gone are picked no longer.
            if (picked.Count > 0)
            {
                List<PlacedPiece> pieces = authority.Home.Area(area);
                picked.RemoveAll(uid => HousingLayout.IndexOf(pieces, uid) < 0);
            }
            if (carriedUid != 0 && View != null) View.SetVisible(carriedUid, false);
            if (open) ShowBudget();
        }

        private void OnRefused(ushort request, string reason)
        {
            if (pending.Count > 0)
            {
                Pending asked = pending.Dequeue();
                if (asked.kind != PendingKind.Edit) histories[(int)asked.area]?.Discard(asked.kind == PendingKind.Undo);
            }
            Notify(reason);
        }

        private void Undo()
        {
            ArrangeHistory history = History;
            if (history == null || !history.CanUndo) { Notify("Nothing to undo."); return; }
            List<HomeOp> ops = history.BeginUndo(Pieces);
            if (ops == null || ops.Count == 0)
            {
                history.Discard(true);
                Notify("That step was skipped: the pieces have been changed since.");
                return;
            }
            Ask(PendingKind.Undo, ops);
        }

        private void Redo()
        {
            ArrangeHistory history = History;
            if (history == null || !history.CanRedo) { Notify("Nothing to redo."); return; }
            List<HomeOp> ops = history.BeginRedo(Pieces);
            if (ops == null || ops.Count == 0)
            {
                history.Discard(false);
                Notify("That step was skipped: the pieces have been changed since.");
                return;
            }
            Ask(PendingKind.Redo, ops);
        }

        // ------------------------------------------------------------------ the window

        public bool IsOpen => open;

        public void SetOpen(bool show)
        {
            if (window == null) return;
            if (show && housing == null) return;
            open = show;
            window.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", show);
            GearUIController gear = GearUIController.Instance;
            if (!show)
            {
                DropEverything();
                Park(false);
                if (gear != null) gear.UnregisterPackTarget(this);
                return;
            }

            window.BringToFront();
            VisualElement screen = window.panel != null ? window.panel.visualTree : null;
            HudLayoutEditor layout = HudLayoutEditor.Current;
            if (screen != null && (layout == null || !layout.HasSavedPosition(WindowName)))
                windowFit.At(new Vector2(screen.worldBound.xMax - WindowRight - WindowWidth, screen.worldBound.yMin + WindowTop));
            if (gear != null)
            {
                gear.UnregisterPackTarget(this); // last opened is asked first
                gear.RegisterPackTarget(this);
            }
            keyLabel.text = $"[{KeyBindings.Label(KeyBindings.Arrange)}]";
            Refresh();
        }

        private void BuildWindow()
        {
            window = new VisualElement { name = WindowName };
            foreach (string c in new[] { "panel", "window", "blocks-world", "hud-movable" }) window.AddToClassList(c);
            window.style.width = WindowWidth;
            window.style.display = DisplayStyle.None;
            windowFit = new FloatingFit(window, 0f, WindowMargin);

            var header = new VisualElement();
            header.AddToClassList("row");
            header.AddToClassList("kn-header");
            var title = new Label("FURNISHING CRATE");
            title.AddToClassList("heading");
            header.Add(title);
            keyLabel = new Label();
            keyLabel.AddToClassList("muted");
            keyLabel.AddToClassList("header-key");
            header.Add(keyLabel);
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.AddToClassList("header-spacer");
            header.Add(spacer);
            var close = new Button(() => SetOpen(false)) { name = "close-arrange" };
            close.AddToClassList("close");
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("kn-close-glyph");
            close.Add(glyph);
            header.Add(close);
            window.Add(header);

            search = new TextField { name = "arrange-search" };
            search.textEdition.placeholder = "Find a furnishing";
            search.style.marginTop = 6f;
            HudTyping.Guard(search);
            search.RegisterValueChangedCallback(_ => Refresh());
            window.Add(search);

            categoryRow = Row(4f, wrap: true);
            window.Add(categoryRow);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.maxHeight = 260f;
            scroll.style.minHeight = 120f;
            scroll.style.marginTop = 4f;
            grid = new VisualElement();
            grid.AddToClassList("inventory-grid");
            scroll.Add(grid);
            window.Add(scroll);

            // What the Hall wears, each put back to its faction's standard at a press.
            looksRow = Row(4f, wrap: true);
            for (int n = 0; n < LookSlots.Length; n++)
            {
                HomeLook slot = LookSlots[n];
                lookButtons[n] = Chip(LookNames[n], () => { if (AtHall) authority.SubmitLook(slot, 0); });
                looksRow.Add(lookButtons[n]);
            }
            window.Add(looksRow);

            toolRow = Row(6f, wrap: true);
            modeButton = Chip("BASIC", () => SetAdvanced(!advanced));
            overheadButton = Chip("FROM ABOVE", () => SetOverhead(!overhead.Active));
            toolRow.Add(modeButton);
            toolRow.Add(overheadButton);
            toolRow.Add(Chip("UNDO", Undo));
            toolRow.Add(Chip("REDO", Redo));
            awayButton = Chip("PUT ALL AWAY", PutAllAway);
            toolRow.Add(awayButton);
            window.Add(toolRow);

            BuildAdjustPanel();

            var budgetRow = Row(6f, wrap: false);
            budgetRow.style.alignItems = Align.Center;
            budgetLabel = new Label("LAWN");
            budgetLabel.AddToClassList("muted");
            budgetLabel.style.minWidth = 118f;
            budgetRow.Add(budgetLabel);
            var bar = new VisualElement();
            bar.style.flexGrow = 1f;
            bar.style.height = 8f;
            bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            budgetFill = new VisualElement();
            budgetFill.style.height = Length.Percent(100f);
            budgetFill.style.width = Length.Percent(0f);
            bar.Add(budgetFill);
            budgetRow.Add(bar);
            window.Add(budgetRow);

            statusLabel = new Label();
            statusLabel.AddToClassList("help");
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.minHeight = 46f;
            window.Add(statusLabel);

            var doors = Row(4f, wrap: false);
            doorButton = new Button(ThroughDoor);
            doorButton.style.flexGrow = 1f;
            backButton = new Button(() => Do(housing != null ? housing.BackToDoor() : null)) { text = "BACK TO THE DOOR" };
            backButton.style.flexGrow = 1f;
            doors.Add(doorButton);
            doors.Add(backButton);
            window.Add(doors);

            // The stairs, for whoever would sooner press than walk.
            var floors = Row(2f, wrap: false);
            upButton = new Button(() => TakeStair(up: true)) { text = "UP A FLOOR" };
            upButton.style.flexGrow = 1f;
            downButton = new Button(() => TakeStair(up: false)) { text = "DOWN A FLOOR" };
            downButton.style.flexGrow = 1f;
            floors.Add(upButton);
            floors.Add(downButton);
            window.Add(floors);

            travelButton = new Button(Travel);
            travelButton.AddToClassList("kn-button-primary");
            travelButton.style.marginTop = 4f;
            window.Add(travelButton);

            // Beside the Pack, inside the part of the HUD the item styles belong to: its cells are item cells, and a
            // furnishing dragged from the Pack is drawn over it.
            VisualElement pack = root.Q(PackWindowName);
            (pack != null && pack.parent != null ? pack.parent : root).Add(window);
        }

        private static VisualElement Row(float marginTop, bool wrap)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            row.style.marginTop = marginTop;
            if (wrap) row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        private static Button Chip(string text, Action clicked)
        {
            var chip = new Button(clicked) { text = text };
            chip.style.fontSize = 10f;
            chip.style.marginRight = 3f;
            chip.style.marginBottom = 3f;
            chip.style.paddingLeft = chip.style.paddingRight = 6f;
            return chip;
        }

        // The box drawn while several pieces are picked, and the curtain of a trip: over the whole screen, under no window.
        private void BuildOverlays()
        {
            boxRect = new VisualElement { name = "arrange-box", pickingMode = PickingMode.Ignore };
            boxRect.style.position = Position.Absolute;
            boxRect.style.display = DisplayStyle.None;
            boxRect.style.backgroundColor = new Color(0.4f, 0.85f, 1f, 0.12f);
            Color edge = new Color(0.4f, 0.85f, 1f, 0.9f);
            boxRect.style.borderLeftColor = boxRect.style.borderRightColor = boxRect.style.borderTopColor = boxRect.style.borderBottomColor = edge;
            boxRect.style.borderLeftWidth = boxRect.style.borderRightWidth = boxRect.style.borderTopWidth = boxRect.style.borderBottomWidth = 1f;
            root.Add(boxRect);

            curtain = new VisualElement { name = "hall-curtain", pickingMode = PickingMode.Ignore };
            curtain.style.position = Position.Absolute;
            curtain.style.left = curtain.style.right = curtain.style.top = curtain.style.bottom = 0f;
            curtain.style.backgroundColor = Color.black;
            curtain.style.alignItems = Align.Center;
            curtain.style.justifyContent = Justify.Center;
            curtain.style.display = DisplayStyle.None;
            curtainLabel = new Label { pickingMode = PickingMode.Ignore };
            curtainLabel.AddToClassList("heading");
            curtainLabel.style.fontSize = 22f;
            curtainLabel.style.color = new Color(1f, 1f, 1f, 0.85f);
            curtain.Add(curtainLabel);
            root.Add(curtain);
        }

        private void DrawCurtain()
        {
            if (curtain == null) return;
            float closed = housing != null ? housing.Curtain : 0f;
            bool show = closed > 0.001f;
            if (show != (curtain.style.display == DisplayStyle.Flex))
            {
                curtain.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                // A trip takes the click that began it: the curtain stops what is under it being pressed meanwhile.
                curtain.pickingMode = show ? PickingMode.Position : PickingMode.Ignore;
                if (show) curtain.BringToFront();
            }
            if (!show) return;
            curtain.style.opacity = closed;
            string label = housing.TravelLabel ?? string.Empty;
            if (curtainLabel.text != label) curtainLabel.text = label;
        }

        private void Travel()
        {
            if (housing == null) return;
            GearUIController.Instance?.SuppressWorldInput();
            Do(housing.InHome ? housing.Leave() : housing.Enter());
        }

        private void ThroughDoor()
        {
            if (housing == null || !housing.InHome) return;
            GearUIController.Instance?.SuppressWorldInput();
            Do(housing.Indoors ? housing.GoOutside() : housing.GoInside());
        }

        private void TakeStair(bool up)
        {
            if (housing == null || !housing.Indoors) return;
            GearUIController.Instance?.SuppressWorldInput();
            HomeArea to = up ? (housing.Area == HomeArea.Basement ? HomeArea.Interior : HomeArea.Upper)
                : (housing.Area == HomeArea.Upper ? HomeArea.Interior : HomeArea.Basement);
            Do(housing.GoToFloor(to));
        }

        private void Do(string refusal)
        {
            if (refusal != null) Notify(refusal);
            Refresh();
        }

        private void Refresh()
        {
            if (window == null || crate == null) return;
            FurnishingCatalog catalog = crate.Catalog;
            string find = search.value != null ? search.value.Trim() : string.Empty;

            categories.Clear();
            categories.Add(AllCategories);
            if (favourites.Count > 0) categories.Add(Favourites);
            if (recents.Count > 0) categories.Add(Recent);
            shownEntries.Clear();
            foreach (CrateEntry entry in crate.Entries)
            {
                if (!Describe(catalog, entry.itemId, out string name, out string filedUnder, out _)) continue;
                if (!categories.Contains(filedUnder)) categories.Add(filedUnder);
                if (category == Favourites ? !favourites.Contains(entry.itemId)
                    : category == Recent ? !recents.Contains(entry.itemId)
                    : category != AllCategories && filedUnder != category) continue;
                if (find.Length > 0 && name.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shownEntries.Add(entry);
            }
            if (!categories.Contains(category)) category = AllCategories;
            // The last used come first among the recent.
            if (category == Recent) shownEntries.Sort((a, b) => recents.IndexOf(a.itemId).CompareTo(recents.IndexOf(b.itemId)));

            categoryRow.Clear();
            foreach (string name in categories)
            {
                string chosen = name;
                Button chip = Chip(name.ToUpperInvariant(), () => { category = chosen; Refresh(); });
                chip.style.opacity = name == category ? 1f : 0.6f;
                categoryRow.Add(chip);
            }

            while (cells.Count < shownEntries.Count) cells.Add(CreateCell(cells.Count));
            for (int n = 0; n < cells.Count; n++)
            {
                bool used = n < shownEntries.Count;
                cells[n].style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                Describe(catalog, shownEntries[n].itemId, out _, out _, out ItemDefinition item);
                GearUIController.SetItemVisual(cells[n], item, Mathf.Min(shownEntries[n].count, ushort.MaxValue));
                cellStars[n].style.display = favourites.Contains(shownEntries[n].itemId) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool atHall = AtHall;
            toolRow.style.display = atHall ? DisplayStyle.Flex : DisplayStyle.None;
            looksRow.style.display = atHall ? DisplayStyle.Flex : DisplayStyle.None;
            doorButton.parent.style.display = atHall ? DisplayStyle.Flex : DisplayStyle.None;
            upButton.parent.style.display = atHall && housing.Indoors ? DisplayStyle.Flex : DisplayStyle.None;
            if (atHall)
            {
                doorButton.text = housing.Indoors ? "GO OUTSIDE" : "GO INSIDE";
                upButton.SetEnabled(housing.Area != HomeArea.Upper);
                downButton.SetEnabled(housing.Area != HomeArea.Basement);
                for (int n = 0; n < LookSlots.Length; n++)
                {
                    HallLookDefinition worn = catalog != null ? catalog.GetLook(authority.Home.Look(LookSlots[n])) : null;
                    bool own = worn != null && !worn.standard;
                    lookButtons[n].text = own ? $"{LookNames[n]}: {worn.DisplayName.ToUpperInvariant()}  ↺" : $"{LookNames[n]}: STANDARD";
                    lookButtons[n].SetEnabled(own);
                }
            }
            modeButton.text = advanced ? "ADVANCED" : "BASIC";
            overheadButton.style.opacity = overhead.Active ? 1f : 0.6f;
            awayButton.text = "PUT ALL AWAY";
            awayArmedUntil = 0f;
            travelButton.text = housing != null && housing.InHome ? "RETURN TO TOWN" : "GO TO MY HALL (SANDBOX)";
            ShowBudget();
            ShowAdjust();
            if (!atHall)
                SetStatus(crate.Entries.Count == 0
                    ? "The Crate is empty. Right-click a furnishing in your Pack to store it here."
                    : "Furnishings are set down at your Hall. Right-click one here to take it into your Pack.");
            else if (carried == null) SetStatus(IdleHint());
        }

        private string IdleHint() => housing.Indoors
            ? $"The {HomeAreas.Name(housing.Area)}. Click a furnishing, then the floor or a wall. Click a piece to move it; drag across the floor to pick several. Step onto a marked stair to change floor. Tab: Advanced  ·  V: from above."
            : "Click a furnishing, then the lawn. Click a piece to move it; drag across the lawn to pick several. Walk into the Hall's door to go inside.";

        // A Crate item is a furnishing, or a look the Hall is not wearing: what it is called, where it is filed, its item.
        private static bool Describe(FurnishingCatalog catalog, ushort itemId, out string name, out string filedUnder, out ItemDefinition item)
        {
            name = filedUnder = null;
            item = null;
            if (catalog == null) return false;
            FurnishingDefinition furnishing = catalog.Get(itemId);
            if (furnishing != null)
            {
                name = furnishing.DisplayName;
                filedUnder = furnishing.category;
                item = furnishing.item;
                return item != null;
            }
            HallLookDefinition look = catalog.GetLook(itemId);
            if (look == null) return false;
            name = look.DisplayName;
            filedUnder = HallCategory;
            item = look.item;
            return item != null;
        }

        private void ShowBudget()
        {
            if (budgetLabel == null) return;
            if (!AtHall)
            {
                budgetLabel.text = "NOT AT YOUR HALL";
                budgetFill.style.width = Length.Percent(0f);
                return;
            }
            int used = authority.BudgetUsed(Area), most = Mathf.Max(1, authority.Budget(Area));
            budgetLabel.text = $"{(HomeAreas.IsInside(Area) ? "INSIDE" : "LAWN")}  {used} / {most}";
            float part = Mathf.Clamp01(used / (float)most);
            budgetFill.style.width = Length.Percent(part * 100f);
            budgetFill.style.backgroundColor = part >= 1f ? new Color(0.9f, 0.3f, 0.25f) : part >= 0.8f ? new Color(0.95f, 0.7f, 0.2f) : new Color(0.35f, 0.8f, 0.5f);
        }

        private VisualElement CreateCell(int index)
        {
            VisualElement cell = GearUIController.CreateItemVisual(new VisualElement(), true);
            cell.AddToClassList("item-cell");
            grid.Add(cell);
            var star = new Label(Favourites) { pickingMode = PickingMode.Ignore };
            star.style.position = Position.Absolute;
            star.style.left = 2f;
            star.style.top = 0f;
            star.style.fontSize = 11f;
            star.style.color = new Color(1f, 0.85f, 0.3f);
            star.style.display = DisplayStyle.None;
            cell.Add(star);
            cellStars.Add(star);

            cell.RegisterCallback<PointerEnterEvent>(entered =>
            {
                if (index >= shownEntries.Count || crate == null || GearUIController.Instance == null) return;
                ushort id = shownEntries[index].itemId;
                if (!Describe(crate.Catalog, id, out _, out _, out ItemDefinition item)) return;
                bool look = crate.Catalog.GetLook(id) != null;
                string hint = !AtHall ? "Right-click: to the Pack (Shift: all of them)"
                    : look ? "Click: the Hall wears it  ·  Right-click: to the Pack"
                    : "Click: set it down  ·  Right-click: to the Pack  ·  Middle-click: favourite";
                GearUIController.Instance.ShowItemTooltip(cell, item, new ItemStack(id, 1), hint);
            });
            cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                GearUIController.Instance?.SuppressWorldInput();
                if (index >= shownEntries.Count || crate == null) return;
                CrateEntry entry = shownEntries[index];
                if (evt.button == RightButton) crate.RequestWithdraw(entry.itemId, evt.shiftKey ? entry.count : 1);
                else if (evt.button == MiddleButton) ToggleFavourite(entry.itemId);
                else if (evt.button == LeftButton) Use(entry.itemId);
            });
            return cell;
        }

        // A click on what the Crate holds: a furnishing is taken in hand, a Facade or a Finish is worn by the Hall.
        private void Use(ushort itemId)
        {
            HallLookDefinition look = crate.Catalog != null ? crate.Catalog.GetLook(itemId) : null;
            if (look == null)
            {
                CarryNew(crate.Catalog != null ? crate.Catalog.Get(itemId) : null);
                return;
            }
            if (!AtHall) { Notify("A Hall's looks are changed at the Hall."); return; }
            GearUIController.Instance?.HideItemTooltip();
            authority.SubmitLook(look.slot, itemId);
        }

        private void PutAllAway()
        {
            if (!AtHall) return;
            List<PlacedPiece> pieces = Pieces;
            if (pieces.Count == 0) { Notify("Nothing stands here."); return; }
            if (Time.unscaledTime > awayArmedUntil)
            {
                // Asked twice: it is one press from an empty lawn.
                awayArmedUntil = Time.unscaledTime + AwayConfirmSeconds;
                awayButton.text = "PRESS AGAIN: ALL OF IT";
                return;
            }
            awayArmedUntil = 0f;
            awayButton.text = "PUT ALL AWAY";
            DropEverything();
            var ops = new List<HomeOp>(pieces.Count);
            foreach (PlacedPiece piece in pieces)
                if (piece.p == 0) ops.Add(HomeOp.Remove(piece.u)); // what stands on it goes with it
            Ask(PendingKind.Edit, ops);
        }

        // ------------------------------------------------------------------ favourites and the last used

        // A player's own marks, kept with their other interface settings on this machine.
        private void LoadFavourites()
        {
            favourites.Clear();
            recents.Clear();
            favouritesKey = null;
            if (housing == null) return;
            var identity = housing.GetComponent<PlayerIdentity>();
            favouritesKey = FavouritesKeyPrefix + (identity != null && !string.IsNullOrEmpty(identity.CharacterId) ? identity.CharacterId : "offline");
            foreach (string part in PlayerPrefs.GetString(favouritesKey, string.Empty).Split(','))
                if (ushort.TryParse(part, out ushort id) && id != 0) favourites.Add(id);
        }

        private void ToggleFavourite(ushort itemId)
        {
            if (favouritesKey == null) return;
            if (!favourites.Remove(itemId)) favourites.Add(itemId);
            PlayerPrefs.SetString(favouritesKey, string.Join(",", favourites));
            Refresh();
        }

        private void Remember(ushort itemId)
        {
            recents.Remove(itemId);
            recents.Insert(0, itemId);
            if (recents.Count > MostRecent) recents.RemoveAt(recents.Count - 1);
        }

        private void SetStatus(string text)
        {
            if (statusLabel != null && text != null && statusLabel.text != text) statusLabel.text = text;
        }

        private static void Notify(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ the Pack hands furnishings over

        public string QuickHint => "store in the Furnishing Crate";

        public bool TryQuick(ItemLocation from) => Store(from);

        public bool TryDrop(ItemLocation from, Vector2 panelPosition) =>
            window != null && window.worldBound.Contains(panelPosition) && Store(from);

        public bool LocksCell(int cell) => false;

        private bool Store(ItemLocation from)
        {
            PlayerInventory pack = PlayerInventory.Local;
            if (!from.IsBag || pack == null || crate == null || crate.Catalog == null) return false;
            ItemStack stack = pack.Get(from.index);
            if (stack.IsEmpty || !crate.Catalog.HeldInCrate(stack.itemId)) return false; // not ours: the Pack does what it would have
            crate.RequestStore(from.index, stack.count);
            return true;
        }
    }
}
