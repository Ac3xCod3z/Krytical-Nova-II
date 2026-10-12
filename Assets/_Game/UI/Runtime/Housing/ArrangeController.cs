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
    /// Arrange: the Furnishing Crate window and the tool that sets furnishings down at a Hall. The window lists what
    /// the Crate holds; a click on a furnishing puts a preview of it under the cursor, green where it may stand and
    /// red with the reason where it may not, and a click on the ground sets it down. A piece already standing is
    /// picked up by a click, turned with Q and E, put away with Delete. Away from the Hall the window is the Crate
    /// alone: furnishings go in from the Pack and come out to it.
    /// The tool decides nothing. It asks the Hall's authority for every edit (<see cref="IHomeAuthority"/>) and
    /// draws what comes back, with the same rules the authority uses to say beforehand what will be refused.
    /// While it is open at the Hall the left button and the hotbar are the tool's; the camera stays the player's.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ArrangeController : MonoBehaviour, IPackTarget
    {
        private const string WindowName = "arrange-window";
        private const string PackWindowName = "inventory-window";
        private const float WindowWidth = 372f, WindowRight = 24f, WindowTop = 64f, WindowMargin = 20f;
        private const float RayDistance = 400f;
        private const float RugLift = 0.01f;
        private const uint PreviewUid = uint.MaxValue;
        private const int LeftButton = 0, RightButton = 1;
        private const string AllCategories = "All";
        private const string GroundLayerName = "Ground";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private enum PendingKind { Edit, Undo, Redo }
        private struct Pending { public PendingKind kind; public List<HomeOp> ops; }

        private VisualElement root, window, grid, categoryRow, budgetFill;
        private Label budgetLabel, statusLabel, keyLabel;
        private Button travelButton;
        private TextField search;
        private FloatingFit windowFit;
        private readonly List<VisualElement> cells = new List<VisualElement>(64);
        private readonly List<CrateEntry> shownEntries = new List<CrateEntry>(64);
        private readonly List<string> categories = new List<string>(12);
        private string category = AllCategories;

        private PlayerHousing housing;
        private PlayerCrate crate;
        private IHomeAuthority authority;
        private ArrangeHistory history;
        private readonly Queue<Pending> pending = new Queue<Pending>();

        // What is under the cursor, and what is being carried.
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly List<PlacedPiece> scratch = new List<PlacedPiece>(256);
        private readonly List<HomeOp> oneOp = new List<HomeOp>(1);
        private FurnishingPiece hovered;
        private FurnishingDefinition carried;
        private uint carriedUid;
        private PlacedPiece carriedBefore, candidate;
        private bool hasCandidate;
        private string refusal;
        private float yaw;
        private GameObject ghost;
        private MaterialPropertyBlock ghostBlock;
        private bool parked;
        private bool open;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            BuildWindow();
            PlayerInventory.LocalChanged += OnLocalChanged;
        }

        private void OnDisable()
        {
            PlayerInventory.LocalChanged -= OnLocalChanged;
            SetOpen(false);
            Bind(null);
            if (GearUIController.Instance != null) GearUIController.Instance.UnregisterPackTarget(this);
            window?.RemoveFromHierarchy();
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
        }

        private void BindAuthority(IHomeAuthority next)
        {
            StopCarrying();
            if (authority != null)
            {
                authority.Changed -= OnEdited;
                authority.Refused -= OnRefused;
            }
            authority = next;
            pending.Clear();
            history = null;
            if (authority == null) return;
            authority.Changed += OnEdited;
            authority.Refused += OnRefused;
            history = new ArrangeHistory(authority.Settings != null ? authority.Settings.undoSteps : 50);
        }

        private void OnHomeChanged()
        {
            BindAuthority(housing != null ? housing.Authority : null);
            if (open) Refresh();
        }

        private void OnCrateChanged()
        {
            if (open) Refresh();
        }

        private bool AtHall => housing != null && housing.InHome && authority != null;

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (window == null) return;
            if (housing == null)
            {
                PlayerHousing local = PlayerHousing.Local;
                if (local != null) Bind(local);
            }

            bool typing = HudTyping.IsTyping(root);
            if (housing != null && !typing && KeyBindings.Pressed(KeyBindings.Arrange)) SetOpen(!open);

            bool tool = open && AtHall;
            Park(tool);
            if (!open) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !typing)
            {
                if (carried != null) StopCarrying();
                else SetOpen(false);
                return;
            }
            if (tool) Tool(keyboard, typing);
            else if (carried != null) StopCarrying();
        }

        // While the tool is out, a click is the tool's and the hotbar keys turn pieces: both are handed back after.
        private void Park(bool on)
        {
            if (!on && !parked) return;
            PlayerInventory local = PlayerInventory.Local;
            if (local != null)
            {
                if (local.TryGetComponent(out PointAndClickPlayer mover)) mover.WorldClicksParked = on;
                // Asked for again every frame: a text field that loses focus hands the hotbar back by itself.
                if (local.TryGetComponent(out PlayerSkillHotbar hotbar) && (on || parked)) hotbar.InputSuppressed = on;
            }
            parked = on;
        }

        private void Tool(Keyboard keyboard, bool typing)
        {
            Mouse mouse = Mouse.current;
            Camera view = ViewCamera();
            if (mouse == null || view == null) return;

            Vector2 screen = mouse.position.ReadValue();
            bool overUi = !view.pixelRect.Contains(screen) || ClientPresentationRegistry.BlocksWorldPointer(screen) || OverWindow(screen);
            bool turningCamera = mouse.rightButton.isPressed;
            bool free = keyboard != null && keyboard.altKey.isPressed;
            bool fine = keyboard != null && keyboard.ctrlKey.isPressed;
            bool again = keyboard != null && keyboard.shiftKey.isPressed;

            Look(view.ScreenPointToRay(screen), overUi || turningCamera, free, fine);

            if (keyboard != null && !typing) Keys(keyboard);
            if (overUi || turningCamera || !mouse.leftButton.wasPressedThisFrame) return;

            if (carried != null) SetDown(again);
            else if (hovered != null) PickUp(hovered);
        }

        private void Keys(Keyboard keyboard)
        {
            HousingSettings settings = authority.Settings;
            float step = settings != null ? settings.rotateStep : 15f;
            int turn = (keyboard.eKey.wasPressedThisFrame ? 1 : 0) - (keyboard.qKey.wasPressedThisFrame ? 1 : 0);
            if (turn != 0)
            {
                if (carried != null) yaw = HousingPlacement.StepYaw(yaw, step, turn);
                else if (hovered != null) TurnWhereItStands(hovered, step, turn);
            }

            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
            {
                if (carried != null && carriedUid != 0) { uint uid = carriedUid; StopCarrying(); PutAway(uid); }
                else if (carried != null) StopCarrying();
                else if (hovered != null) PutAway(hovered.Uid);
            }

            if (keyboard.ctrlKey.isPressed && carried == null)
            {
                if (keyboard.zKey.wasPressedThisFrame && !keyboard.shiftKey.isPressed) Undo();
                else if (keyboard.yKey.wasPressedThisFrame || (keyboard.zKey.wasPressedThisFrame && keyboard.shiftKey.isPressed)) Redo();
            }
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
            Vector2 panel = RuntimePanelUtils.ScreenToPanel(window.panel, new Vector2(screen.x, Screen.height - screen.y));
            return window.worldBound.Contains(panel);
        }

        // ------------------------------------------------------------------ what is under the cursor

        private void Look(Ray ray, bool blocked, bool free, bool fine)
        {
            hovered = null;
            hasCandidate = false;
            if (blocked)
            {
                if (ghost != null) ghost.SetActive(false);
                return;
            }

            int furnishingLayer = HomeView.FurnishingLayer;
            int groundLayer = LayerMask.NameToLayer(GroundLayerName);
            int mask = (1 << furnishingLayer) | (1 << groundLayer) | 1; // and Default: the Hall itself hides what is behind it
            int count = Physics.RaycastNonAlloc(ray, hits, RayDistance, mask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, 0, count, HitDistance.Instance);

            FurnishingPiece under = null;
            RaycastHit ground = default;
            bool hasGround = false;
            for (int n = 0; n < count; n++)
            {
                GameObject hit = hits[n].collider.gameObject;
                if (hit.layer == furnishingLayer)
                {
                    FurnishingPiece piece = hit.GetComponentInParent<FurnishingPiece>();
                    if (piece == null || piece.Uid == carriedUid) continue;
                    if (under == null) under = piece;
                    if (carried == null) break;
                    // Carrying: a piece with a top takes what is small enough to stand on it; otherwise look past it.
                    if (CanStandOn(piece, hits[n].normal, free)) { ground = hits[n]; hasGround = true; break; }
                    continue;
                }
                if (hit.layer == groundLayer) { ground = hits[n]; hasGround = true; under = carried != null ? null : under; }
                break; // the ground, or something solid that is neither: nothing behind it counts
            }

            if (carried == null)
            {
                hovered = under;
                SetStatus(hovered != null ? $"{hovered.Furnishing.DisplayName}: click to move  ·  Q / E turn  ·  Del puts it away" : null);
                return;
            }

            if (!hasGround)
            {
                if (ghost != null) ghost.SetActive(false);
                SetStatus(HousingRules.ReasonBlocked);
                return;
            }
            FurnishingPiece on = ground.collider.gameObject.layer == furnishingLayer ? ground.collider.GetComponentInParent<FurnishingPiece>() : null;
            Propose(ground.point, on, free, fine);
        }

        private bool CanStandOn(FurnishingPiece piece, Vector3 normal, bool free)
        {
            if (piece.Furnishing == null || !piece.Furnishing.hasTop || normal.y < 0.7f) return false;
            return free || carried.mount == FurnishingMount.Surface;
        }

        // Works out where the carried piece would stand, asks the rules, and shows it.
        private void Propose(Vector3 point, FurnishingPiece on, bool free, bool fine)
        {
            HousingSettings settings = authority.Settings;
            List<PlacedPiece> pieces = authority.Home.Area(HomeArea.Lawn);
            Transform pieceRoot = housing.Lot.PieceRoot;
            float step = settings == null ? 0.5f : fine ? settings.fineGridStep : settings.gridStep;

            PlacedPiece piece = carriedUid != 0 ? carriedBefore : new PlacedPiece { i = carried.itemId, s = PlacedPiece.NormalScale };
            piece.u = carriedUid != 0 ? carriedUid : PreviewUid;
            piece.f = (byte)(free ? PieceFlags.Free : PieceFlags.None);
            Vector3 areaPoint = pieceRoot.InverseTransformPoint(point);
            Quaternion areaTurn = Quaternion.Euler(0f, yaw, 0f);

            if (on != null && HousingLayout.TryFind(pieces, on.Uid, out PlacedPiece under))
            {
                // On a piece's top: a finer grid, in that piece's own space, and never over its edge.
                HousingLayout.Pose(pieces, under, out Vector3 underPosition, out Quaternion underTurn);
                Vector3 local = Quaternion.Inverse(underTurn) * (areaPoint - underPosition) / under.Scale;
                float topStep = settings != null ? settings.fineGridStep : 0.125f;
                if (!free) local = HousingPlacement.SnapToGrid(local, topStep);
                Bounds top = on.Furnishing.LocalBounds;
                local.x = Mathf.Clamp(local.x, top.min.x, top.max.x);
                local.z = Mathf.Clamp(local.z, top.min.z, top.max.z);
                local.y = on.Furnishing.Top - carried.Bottom;
                piece.p = under.u;
                piece.Position = local;
                piece.Rotation = Quaternion.Inverse(underTurn) * areaTurn;
            }
            else
            {
                if (!free) areaPoint = HousingPlacement.SnapToGrid(areaPoint, step);
                areaPoint.y = -carried.Bottom + (carried.mount == FurnishingMount.Rug ? RugLift : 0f);
                piece.p = 0;
                piece.Position = areaPoint;
                piece.Rotation = areaTurn;
            }

            candidate = piece;
            hasCandidate = true;
            refusal = Judge(pieces, piece, free);
            ShowGhost(pieces, piece, pieceRoot, refusal == null);
            SetStatus(refusal ?? Hint());
        }

        // The same rules the authority will use, and one courtesy of the tool's own: pieces do not overlap unless
        // the player holds Alt.
        private string Judge(List<PlacedPiece> pieces, in PlacedPiece piece, bool free)
        {
            scratch.Clear();
            scratch.AddRange(pieces);
            int index = HousingLayout.IndexOf(scratch, piece.u);
            if (index >= 0) scratch[index] = piece; else scratch.Add(piece);

            var context = new EditContext
            {
                catalog = authority.Catalog,
                area = HomeArea.Lawn,
                shape = authority.Shape(HomeArea.Lawn),
                faction = housing.Faction,
                budget = authority.Budget(HomeArea.Lawn),
                maxStackDepth = authority.Settings != null ? authority.Settings.maxStackDepth : 3,
            };
            if (!HousingRules.MayHave(carried, context.faction)) return HousingRules.ReasonOtherFaction;
            string reason = HousingRules.PoseBlockedReason(scratch, piece, carried, context);
            if (reason != null) return reason;

            if (carriedUid == 0)
            {
                if (crate == null || crate.Count(carried.itemId) < 1) return HousingRules.ReasonNoneLeft;
                if (authority.BudgetUsed(HomeArea.Lawn) + carried.budgetCost > context.budget) return HousingRules.ReasonBudget;
            }
            if (free || piece.p != 0 || carried.mount == FurnishingMount.Rug) return null;

            HousingLayout.Pose(scratch, piece, out Vector3 position, out Quaternion turn);
            Vector2 middle = HousingPlacement.FootprintCenter(position, turn.eulerAngles.y, carried.FootprintCenter, piece.Scale);
            Vector2 half = carried.FootprintHalf * piece.Scale;
            for (int n = 0; n < scratch.Count; n++)
            {
                PlacedPiece other = scratch[n];
                if (other.u == piece.u || other.p != 0) continue;
                FurnishingDefinition furnishing = authority.Catalog.Get(other.i);
                if (furnishing == null || furnishing.mount == FurnishingMount.Rug) continue;
                Vector2 otherMiddle = HousingPlacement.FootprintCenter(other.Position, other.Yaw, furnishing.FootprintCenter, other.Scale);
                if (HousingPlacement.FootprintsOverlap(middle, half, turn.eulerAngles.y, otherMiddle, furnishing.FootprintHalf * other.Scale, other.Yaw))
                    return HousingRules.ReasonInTheWay;
            }
            return null;
        }

        private string Hint() => carriedUid != 0
            ? "Click to set it down  ·  Q / E turn  ·  Alt: off the grid  ·  Ctrl: fine steps  ·  Esc: leave it where it was"
            : "Click to set it down  ·  Shift-click: and another  ·  Q / E turn  ·  Alt: off the grid  ·  Ctrl: fine steps";

        // ------------------------------------------------------------------ the preview

        private void ShowGhost(List<PlacedPiece> pieces, in PlacedPiece piece, Transform pieceRoot, bool allowed)
        {
            if (ghost == null) return;
            // Drawn where the piece would really stand: through the same sums as a piece that is set down.
            int index = HousingLayout.IndexOf(scratch, piece.u);
            HousingLayout.Pose(scratch, index >= 0 ? scratch[index] : piece, out Vector3 position, out Quaternion turn);
            ghost.transform.SetPositionAndRotation(pieceRoot.TransformPoint(position), pieceRoot.rotation * turn);
            ghost.transform.localScale = Vector3.one * piece.Scale;
            ghost.SetActive(true);

            HousingSettings settings = authority.Settings;
            Color colour = settings == null ? (allowed ? Color.green : Color.red) : allowed ? settings.ghostAllowed : settings.ghostRefused;
            ghostBlock ??= new MaterialPropertyBlock();
            ghostBlock.SetColor(BaseColorId, colour);
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true)) renderer.SetPropertyBlock(ghostBlock);
        }

        private void MakeGhost(FurnishingDefinition furnishing)
        {
            if (ghost != null) Destroy(ghost);
            ghost = HomeView.Spawn(furnishing, null, HomeView.FurnishingLayer, pickable: false);
            if (ghost == null) return;
            ghost.name = "Arrange Preview";
            Material tint = authority.Settings != null ? authority.Settings.ghostMaterial : null;
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (tint == null) continue;
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int n = 0; n < materials.Length; n++) materials[n] = tint;
                renderer.sharedMaterials = materials;
            }
            ghost.SetActive(false);
        }

        // ------------------------------------------------------------------ carrying

        /// <summary>Takes a furnishing out of the Crate to be set down.</summary>
        private void CarryNew(FurnishingDefinition furnishing)
        {
            if (furnishing == null) return;
            if (!AtHall)
            {
                Notify("Furnishings are set down at your Hall.");
                return;
            }
            if (!HousingRules.MayHave(furnishing, housing.Faction)) { Notify(HousingRules.ReasonOtherFaction); return; }
            StopCarrying();
            carried = furnishing;
            carriedUid = 0;
            yaw = 0f;
            MakeGhost(furnishing);
            GearUIController.Instance?.HideItemTooltip();
        }

        private void PickUp(FurnishingPiece piece)
        {
            List<PlacedPiece> pieces = authority.Home.Area(HomeArea.Lawn);
            if (!HousingLayout.TryFind(pieces, piece.Uid, out PlacedPiece standing)) return;
            StopCarrying();
            carried = piece.Furnishing;
            carriedUid = piece.Uid;
            carriedBefore = standing;
            HousingLayout.Pose(pieces, standing, out _, out Quaternion turn);
            yaw = turn.eulerAngles.y;
            MakeGhost(carried);
            housing.LawnView?.SetVisible(carriedUid, false);
            hovered = null;
        }

        private void StopCarrying()
        {
            if (carriedUid != 0 && housing != null && housing.LawnView != null) housing.LawnView.SetVisible(carriedUid, true);
            carried = null;
            carriedUid = 0;
            hasCandidate = false;
            if (ghost != null) Destroy(ghost);
            ghost = null;
        }

        private void SetDown(bool again)
        {
            if (!hasCandidate) return;
            if (refusal != null) { Notify(refusal); return; }

            PlacedPiece piece = candidate;
            FurnishingDefinition furnishing = carried;
            bool fresh = carriedUid == 0;
            if (fresh) piece.u = 0;
            else if (piece.SamePose(carriedBefore)) { StopCarrying(); return; } // put back where it stood: nothing to ask for
            Ask(PendingKind.Edit, fresh ? HomeOp.Place(piece) : HomeOp.Move(piece));

            if (fresh && again && crate != null && crate.Count(furnishing.itemId) > 0) return; // still in hand
            StopCarrying();
        }

        private void TurnWhereItStands(FurnishingPiece piece, float step, int direction)
        {
            List<PlacedPiece> pieces = authority.Home.Area(HomeArea.Lawn);
            if (!HousingLayout.TryFind(pieces, piece.Uid, out PlacedPiece standing)) return;
            standing.Yaw = HousingPlacement.StepYaw(standing.Yaw, step, direction);
            Ask(PendingKind.Edit, HomeOp.Move(standing));
        }

        private void PutAway(uint uid) => Ask(PendingKind.Edit, HomeOp.Remove(uid));

        // ------------------------------------------------------------------ asking the authority

        private void Ask(PendingKind kind, HomeOp op)
        {
            oneOp.Clear();
            oneOp.Add(op);
            Ask(kind, new List<HomeOp>(oneOp));
        }

        private void Ask(PendingKind kind, List<HomeOp> ops)
        {
            if (authority == null || ops == null || ops.Count == 0) return;
            // Answers come back in the order they were asked for (at once, in the sandbox).
            pending.Enqueue(new Pending { kind = kind, ops = ops });
            authority.Submit(HomeArea.Lawn, ops);
        }

        private void OnEdited(HomeArea area, ushort request, IReadOnlyList<HomeChange> changes)
        {
            if (request != 0 && pending.Count > 0 && history != null)
            {
                Pending asked = pending.Dequeue();
                if (asked.kind == PendingKind.Edit) history.Record(changes);
                else history.Settle(asked.kind == PendingKind.Undo, asked.ops, changes);
            }
            if (carriedUid != 0 && housing != null && housing.LawnView != null) housing.LawnView.SetVisible(carriedUid, false);
            if (open) ShowBudget();
        }

        private void OnRefused(ushort request, string reason)
        {
            if (pending.Count > 0)
            {
                Pending asked = pending.Dequeue();
                if (asked.kind != PendingKind.Edit) history?.Discard(asked.kind == PendingKind.Undo);
            }
            Notify(reason);
        }

        private void Undo()
        {
            if (history == null || !history.CanUndo) { Notify("Nothing to undo."); return; }
            List<HomeOp> ops = history.BeginUndo(authority.Home.Area(HomeArea.Lawn));
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
            if (history == null || !history.CanRedo) { Notify("Nothing to redo."); return; }
            List<HomeOp> ops = history.BeginRedo(authority.Home.Area(HomeArea.Lawn));
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
                StopCarrying();
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

            categoryRow = new VisualElement();
            categoryRow.AddToClassList("row");
            categoryRow.style.flexWrap = Wrap.Wrap;
            categoryRow.style.marginTop = 4f;
            window.Add(categoryRow);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.maxHeight = 300f;
            scroll.style.minHeight = 120f;
            scroll.style.marginTop = 4f;
            grid = new VisualElement();
            grid.AddToClassList("inventory-grid");
            scroll.Add(grid);
            window.Add(scroll);

            var budgetRow = new VisualElement();
            budgetRow.AddToClassList("row");
            budgetRow.style.marginTop = 6f;
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
            statusLabel.style.minHeight = 34f;
            window.Add(statusLabel);

            travelButton = new Button(Travel);
            travelButton.AddToClassList("kn-button-primary");
            travelButton.style.marginTop = 4f;
            window.Add(travelButton);

            // Beside the Pack, inside the part of the HUD the item styles belong to: its cells are item cells, and a
            // furnishing dragged from the Pack is drawn over it.
            VisualElement pack = root.Q(PackWindowName);
            (pack != null && pack.parent != null ? pack.parent : root).Add(window);
        }

        private void Travel()
        {
            if (housing == null) return;
            GearUIController.Instance?.SuppressWorldInput();
            string refusal = housing.InHome ? housing.Leave() : housing.Enter();
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
            shownEntries.Clear();
            foreach (CrateEntry entry in crate.Entries)
            {
                FurnishingDefinition furnishing = catalog != null ? catalog.Get(entry.itemId) : null;
                if (furnishing == null) continue;
                if (!categories.Contains(furnishing.category)) categories.Add(furnishing.category);
                if (category != AllCategories && furnishing.category != category) continue;
                if (find.Length > 0 && furnishing.DisplayName.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shownEntries.Add(entry);
            }
            if (!categories.Contains(category)) category = AllCategories;

            categoryRow.Clear();
            foreach (string name in categories)
            {
                string picked = name;
                var chip = new Button(() => { category = picked; Refresh(); }) { text = name.ToUpperInvariant() };

                chip.style.fontSize = 10f;
                chip.style.marginRight = 3f;
                chip.style.marginBottom = 3f;
                chip.style.paddingLeft = chip.style.paddingRight = 6f;
                chip.style.opacity = name == category ? 1f : 0.6f;
                categoryRow.Add(chip);
            }

            while (cells.Count < shownEntries.Count) cells.Add(CreateCell(cells.Count));
            for (int n = 0; n < cells.Count; n++)
            {
                bool used = n < shownEntries.Count;
                cells[n].style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                FurnishingDefinition furnishing = catalog.Get(shownEntries[n].itemId);
                GearUIController.SetItemVisual(cells[n], furnishing.item, Mathf.Min(shownEntries[n].count, ushort.MaxValue));
            }

            travelButton.text = housing != null && housing.InHome ? "RETURN TO TOWN" : "GO TO MY HALL (SANDBOX)";
            ShowBudget();
            if (!AtHall)
                SetStatus(crate.Entries.Count == 0
                    ? "The Crate is empty. Right-click a furnishing in your Pack to store it here."
                    : "Furnishings are set down at your Hall. Right-click one here to take it into your Pack.");
            else if (carried == null) SetStatus("Click a furnishing, then click the lawn to set it down. Click a piece that stands to move it.");
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
            int used = authority.BudgetUsed(HomeArea.Lawn), most = Mathf.Max(1, authority.Budget(HomeArea.Lawn));
            budgetLabel.text = $"LAWN  {used} / {most}";
            float part = Mathf.Clamp01(used / (float)most);
            budgetFill.style.width = Length.Percent(part * 100f);
            budgetFill.style.backgroundColor = part >= 1f ? new Color(0.9f, 0.3f, 0.25f) : part >= 0.8f ? new Color(0.95f, 0.7f, 0.2f) : new Color(0.35f, 0.8f, 0.5f);
        }

        private VisualElement CreateCell(int index)
        {
            VisualElement cell = GearUIController.CreateItemVisual(new VisualElement(), true);
            cell.AddToClassList("item-cell");
            grid.Add(cell);
            cell.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (index >= shownEntries.Count || crate == null || GearUIController.Instance == null) return;
                FurnishingDefinition furnishing = crate.Catalog.Get(shownEntries[index].itemId);
                if (furnishing == null || furnishing.item == null) return;
                string hint = AtHall ? "Click: set it down  ·  Right-click: to the Pack" : "Right-click: to the Pack (Shift: all of them)";
                GearUIController.Instance.ShowItemTooltip(cell, furnishing.item, new ItemStack(furnishing.itemId, 1), hint);
            });
            cell.RegisterCallback<PointerLeaveEvent>(_ => GearUIController.Instance?.HideItemTooltip());
            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                GearUIController.Instance?.SuppressWorldInput();
                if (index >= shownEntries.Count || crate == null) return;
                CrateEntry entry = shownEntries[index];
                if (evt.button == RightButton) crate.RequestWithdraw(entry.itemId, evt.shiftKey ? entry.count : 1);
                else if (evt.button == LeftButton) CarryNew(crate.Catalog.Get(entry.itemId));
            });
            return cell;
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
            if (stack.IsEmpty || !crate.Catalog.IsFurnishing(stack.itemId)) return false; // not ours: the Pack does what it would have
            crate.RequestStore(from.index, stack.count);
            return true;
        }

        private sealed class HitDistance : IComparer<RaycastHit>
        {
            public static readonly HitDistance Instance = new HitDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
