using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Housing;
using KryticalNova.Items;
using KryticalNova.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    // The tool itself: what is under the cursor, the piece in hand and its preview, and what a click does.
    public sealed partial class ArrangeController
    {
        private const float RayDistance = 400f;
        private const float RugLift = 0.01f;
        private const uint PreviewUid = uint.MaxValue;
        private const string GroundLayerName = "Ground";
        private const float DragPixels = 8f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // What a ray from the cursor meets: the first piece, and what the piece in hand would be set down on.
        private struct Under
        {
            public FurnishingPiece piece;
            public bool hasGround;
            public RaycastHit ground;
            public FurnishingPiece standsOn;
            public bool isWall;
            public Vector3 wallMiddle;
            public Quaternion wallTurn;
        }

        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly HashSet<uint> seen = new HashSet<uint>();
        private readonly List<PlacedPiece> scratch = new List<PlacedPiece>(256);
        private FurnishingPiece hovered;
        private FurnishingDefinition carried;
        private uint carriedUid;
        private PlacedPiece carriedBefore, candidate;
        private bool hasCandidate;
        private string refusal;
        // The piece in hand beyond where the cursor puts it: its turn, and what Advanced adds.
        private float yaw, tilt, lean, lift, size = 1f;
        private GameObject ghost;
        private MaterialPropertyBlock ghostBlock;
        private bool pressed, boxing;
        private Vector2 pressAt;

        private void Tool(Keyboard keyboard, bool typing)
        {
            Mouse mouse = Mouse.current;
            Camera view = ViewCamera();
            if (mouse == null || view == null) return;

            Vector2 screen = mouse.position.ReadValue();
            bool overUi = !view.pixelRect.Contains(screen) || ClientPresentationRegistry.BlocksWorldPointer(screen) || OverWindow(screen);
            bool away = overUi || mouse.rightButton.isPressed; // on a window, or turning the camera
            bool free = advanced || (keyboard != null && keyboard.altKey.isPressed);
            bool fine = keyboard != null && keyboard.ctrlKey.isPressed;
            bool shift = keyboard != null && keyboard.shiftKey.isPressed;

            if (overhead.Active) MoveOverhead(keyboard, mouse, overUi, typing);

            Ray ray = view.ScreenPointToRay(screen);
            if (groupCarried) LookForGroup(ray, away, free, fine);
            else Look(ray, away, free, fine);

            if (keyboard != null && !typing) Keys(keyboard, fine);
            Pointer(mouse, view, screen, away, shift);
            DrawFrames();
        }

        // ------------------------------------------------------------------ keys

        private void Keys(Keyboard keyboard, bool fine)
        {
            HousingSettings settings = authority.Settings;
            float step = settings != null ? settings.rotateStep : 15f;
            int turn = (keyboard.eKey.wasPressedThisFrame ? 1 : 0) - (keyboard.qKey.wasPressedThisFrame ? 1 : 0);
            if (turn != 0)
            {
                if (carried != null) yaw = HousingPlacement.StepYaw(yaw, step, turn);
                else if (groupCarried) groupYaw = HousingPlacement.StepYaw(groupYaw, step, turn);
                else if (hovered != null) TurnWhereItStands(hovered.Uid, step, turn);
                else if (picked.Count == 1) TurnWhereItStands(picked[0], step, turn);
            }

            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
            {
                if (carried != null && carriedUid != 0) { uint uid = carriedUid; StopCarrying(); PutAway(uid); }
                else if (carried != null) StopCarrying();
                else if (groupCarried) { StopCarryingGroup(); PutAwayPicked(); }
                else if (picked.Count > 0) PutAwayPicked();
                else if (hovered != null) PutAway(hovered.Uid);
            }

            if (keyboard.ctrlKey.isPressed && carried == null && !groupCarried)
            {
                if (keyboard.zKey.wasPressedThisFrame && !keyboard.shiftKey.isPressed) Undo();
                else if (keyboard.yKey.wasPressedThisFrame || (keyboard.zKey.wasPressedThisFrame && keyboard.shiftKey.isPressed)) Redo();
                return;
            }

            if (keyboard.tabKey.wasPressedThisFrame) SetAdvanced(!advanced);
            if (keyboard.vKey.wasPressedThisFrame) SetOverhead(!overhead.Active);
            if (keyboard.dKey.wasPressedThisFrame && carried == null && !groupCarried && hovered != null) CopyOf(hovered);

            if (!advanced) return;
            int raise = (keyboard.pageUpKey.wasPressedThisFrame ? 1 : 0) - (keyboard.pageDownKey.wasPressedThisFrame ? 1 : 0);
            if (raise != 0) Adjust(AdjustKind.Lift, raise, fine);
            int grow = (keyboard.rightBracketKey.wasPressedThisFrame ? 1 : 0) - (keyboard.leftBracketKey.wasPressedThisFrame ? 1 : 0);
            if (grow != 0) Adjust(AdjustKind.Size, grow, fine);
        }

        // ------------------------------------------------------------------ the pointer

        // A press and a release in one place is a click; a press on bare ground that is dragged draws a box round
        // the pieces to pick.
        private void Pointer(Mouse mouse, Camera view, Vector2 screen, bool away, bool shift)
        {
            if (mouse.leftButton.wasPressedThisFrame && !away)
            {
                pressed = true;
                pressAt = screen;
            }
            if (!pressed) return;

            bool mayBox = carried == null && !groupCarried && (hovered == null || boxing);
            if (!boxing && mayBox && (screen - pressAt).sqrMagnitude > DragPixels * DragPixels) boxing = true;
            if (boxing) DrawBox(pressAt, screen);

            if (!mouse.leftButton.wasReleasedThisFrame) return;
            pressed = false;
            if (boxing)
            {
                boxing = false;
                boxRect.style.display = DisplayStyle.None;
                PickInBox(view, pressAt, screen, shift);
                return;
            }
            if (away) return;
            Click(screen, shift);
        }

        private void Click(Vector2 screen, bool shift)
        {
            if (carried != null) { SetDown(shift); return; }
            if (groupCarried) { SetDownGroup(); return; }

            if (hovered != null)
            {
                uint uid = hovered.Uid;
                if (shift) TogglePicked(uid);
                else if (picked.Count > 1 && picked.Contains(uid)) CarryGroup();
                // Advanced: the first click picks a piece to be worked on where it stands, the second takes it up.
                else if (advanced && !(picked.Count == 1 && picked[0] == uid)) { ClearPicked(); picked.Add(uid); ShowAdjust(); }
                else { ClearPicked(); PickUp(hovered); }
                return;
            }

            if (picked.Count > 0) { ClearPicked(); return; }
            // Bare ground: the player walks there, as if the tool were not out.
            PlayerInventory local = PlayerInventory.Local;
            if (local != null && local.TryGetComponent(out PointAndClickPlayer mover)) mover.ClickWorld(screen);
        }

        // ------------------------------------------------------------------ what is under the cursor

        private Under Cast(Ray ray, bool carrying, bool free, bool groundOnly = false)
        {
            var under = new Under();
            int furnishingLayer = HomeView.FurnishingLayer;
            int groundLayer = LayerMask.NameToLayer(GroundLayerName);
            int mask = (1 << furnishingLayer) | (1 << groundLayer) | 1; // and Default: the Hall and the room's walls hide what is behind them
            // Pieces are picked by a trigger box, which the player's own clicks pass through.
            int count = Physics.RaycastNonAlloc(ray, hits, RayDistance, mask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, 0, count, HitDistance.Instance);
            seen.Clear();
            HallRoom room = housing.Room;

            for (int n = 0; n < count; n++)
            {
                Collider met = hits[n].collider;
                GameObject hit = met.gameObject;
                if (hit.layer == furnishingLayer)
                {
                    if (groundOnly) continue;
                    FurnishingPiece piece = hit.GetComponentInParent<FurnishingPiece>();
                    if (piece == null || piece.Uid == carriedUid || (groupCarried && picked.Contains(piece.Uid))) continue;
                    if (!seen.Add(piece.Uid)) continue; // a piece has several boxes: the nearest speaks for it
                    if (under.piece == null) under.piece = piece;
                    if (!carrying) return under;
                    // In hand: a piece with a top takes what is small enough to stand on it; otherwise look past it.
                    if (CanStandOn(piece, hits[n].normal, free))
                    {
                        under.hasGround = true;
                        under.ground = hits[n];
                        under.standsOn = piece;
                        return under;
                    }
                    continue;
                }
                if (met.isTrigger) continue; // some other system's zone
                if (hit.layer == groundLayer)
                {
                    under.hasGround = true;
                    under.ground = hits[n];
                    return under;
                }
                if (room != null && room.TryGetWall(met, out under.wallMiddle, out under.wallTurn))
                {
                    under.hasGround = under.isWall = true;
                    under.ground = hits[n];
                }
                return under; // a wall, or something solid that is neither: nothing behind it counts
            }
            return under;
        }

        private void Look(Ray ray, bool away, bool free, bool fine)
        {
            hovered = null;
            hasCandidate = false;
            if (away)
            {
                if (ghost != null) ghost.SetActive(false);
                return;
            }

            Under under = Cast(ray, carried != null, free);
            if (carried == null)
            {
                hovered = boxing ? null : under.piece;
                if (hovered != null)
                    SetStatus($"{hovered.Furnishing.DisplayName}: click to move  ·  Q / E turn  ·  D another like it  ·  Del puts it away  ·  Shift-click picks it with others");
                else if (picked.Count > 0) SetStatus(PickedHint());
                else SetStatus(IdleHint());
                return;
            }

            if (!under.hasGround)
            {
                if (ghost != null) ghost.SetActive(false);
                SetStatus(HousingRules.ReasonBlocked);
                return;
            }
            Propose(under, free, fine);
        }

        private bool CanStandOn(FurnishingPiece piece, Vector3 normal, bool free)
        {
            if (piece.Furnishing == null || !piece.Furnishing.hasTop || normal.y < 0.7f) return false;
            return free || carried.mount == FurnishingMount.Surface;
        }

        // Works out where the piece in hand would stand, asks the rules, and shows it.
        private void Propose(in Under under, bool free, bool fine)
        {
            HousingSettings settings = authority.Settings;
            List<PlacedPiece> pieces = Pieces;
            Transform pieceRoot = PieceRoot;
            float step = settings == null ? 0.5f : fine ? settings.fineGridStep : settings.gridStep;
            float fineStep = settings != null ? settings.fineGridStep : 0.125f;

            PlacedPiece piece = carriedUid != 0 ? carriedBefore : new PlacedPiece { i = carried.itemId };
            piece.u = carriedUid != 0 ? carriedUid : PreviewUid;
            piece.f = (byte)(free ? PieceFlags.Free : PieceFlags.None);
            piece.Scale = size;
            float scale = piece.Scale;
            Vector3 areaPoint = pieceRoot.InverseTransformPoint(under.ground.point);
            HallRoom room = housing.Room;

            if (under.standsOn != null && HousingLayout.TryFind(pieces, under.standsOn.Uid, out PlacedPiece on))
            {
                // On a piece's top: a finer grid, in that piece's own space, and never over its edge.
                HousingLayout.Pose(pieces, on, out Vector3 onPosition, out Quaternion onTurn);
                Vector3 local = Quaternion.Inverse(onTurn) * (areaPoint - onPosition) / on.Scale;
                if (!free) local = HousingPlacement.SnapToGrid(local, fineStep);
                Bounds top = under.standsOn.Furnishing.LocalBounds;
                local.x = Mathf.Clamp(local.x, top.min.x, top.max.x);
                local.z = Mathf.Clamp(local.z, top.min.z, top.max.z);
                local.y = under.standsOn.Furnishing.Top + (lift - carried.Bottom * scale) / on.Scale;
                piece.p = on.u;
                piece.Position = local;
                piece.Rotation = Quaternion.Inverse(onTurn) * Quaternion.Euler(tilt, yaw, lean);
            }
            else if (carried.mount == FurnishingMount.Wall && under.isWall)
            {
                // On a wall: along it and up it on the grid, its back against the wall's face, turned to look out
                // from it. Q and E set it askew.
                Vector3 onWall = Quaternion.Inverse(under.wallTurn) * (areaPoint - under.wallMiddle);
                if (!free)
                {
                    onWall.x = HousingPlacement.Snap(onWall.x, step);
                    onWall.y = HousingPlacement.Snap(onWall.y, step);
                }
                onWall.y = Mathf.Max(onWall.y, -carried.Bottom * scale);
                onWall.z = -carried.Back * scale + lift;
                piece.p = 0;
                piece.Position = under.wallMiddle + under.wallTurn * onWall;
                piece.Rotation = under.wallTurn * Quaternion.Euler(tilt, lean, yaw);
            }
            else
            {
                if (!free) areaPoint = HousingPlacement.SnapToGrid(areaPoint, step);
                if (carried.mount == FurnishingMount.Ceiling && room != null && !under.isWall)
                    areaPoint.y = room.Height - carried.Top * scale - lift; // hung from the ceiling above the place pointed at
                else
                    areaPoint.y = -carried.Bottom * scale + lift + (carried.mount == FurnishingMount.Rug ? RugLift : 0f);
                piece.p = 0;
                piece.Position = areaPoint;
                piece.Rotation = Quaternion.Euler(tilt, yaw, lean);
            }

            candidate = piece;
            hasCandidate = true;
            refusal = Judge(pieces, piece, carried, free, carriedUid == 0);
            ShowGhost(piece, pieceRoot, refusal == null);
            SetStatus(refusal ?? Hint());
        }

        private EditContext Context() => new EditContext
        {
            catalog = authority.Catalog,
            area = Area,
            shape = authority.Shape(Area),
            faction = housing.Faction,
            budget = authority.Budget(Area),
            maxStackDepth = authority.Settings != null ? authority.Settings.maxStackDepth : 3,
            minScale = authority.Settings != null ? authority.Settings.minPieceScale : 0f,
            maxScale = authority.Settings != null ? authority.Settings.maxPieceScale : 0f,
        };

        // The same rules the authority will use, and two courtesies of the tool's own: pieces do not overlap unless
        // the player sets them down freely, and nobody is built into a wardrobe.
        // On return <see cref="scratch"/> holds the area as it would be with the piece where it is asked to be.
        private string Judge(List<PlacedPiece> pieces, in PlacedPiece piece, FurnishingDefinition furnishing, bool free, bool fresh)
        {
            scratch.Clear();
            scratch.AddRange(pieces);
            int index = HousingLayout.IndexOf(scratch, piece.u);
            if (index >= 0) scratch[index] = piece; else scratch.Add(piece);

            EditContext context = Context();
            if (!HousingRules.MayHave(furnishing, context.faction)) return HousingRules.ReasonOtherFaction;
            string reason = HousingRules.PoseBlockedReason(scratch, piece, furnishing, context);
            if (reason != null) return reason;

            if (fresh)
            {
                if (crate == null || crate.Count(furnishing.itemId) < 1) return HousingRules.ReasonNoneLeft;
                if (authority.BudgetUsed(Area) + furnishing.budgetCost > context.budget) return HousingRules.ReasonBudget;
            }
            return Courtesy(scratch, piece, furnishing, free);
        }

        private string Courtesy(List<PlacedPiece> all, in PlacedPiece piece, FurnishingDefinition furnishing, bool free)
        {
            bool stands = piece.p == 0 && (furnishing.mount == FurnishingMount.Floor || furnishing.mount == FurnishingMount.Surface);
            if (!stands) return null;
            HousingLayout.Pose(all, piece, out Vector3 position, out Quaternion turn);
            float facing = turn.eulerAngles.y;
            Vector2 middle = HousingPlacement.FootprintCenter(position, facing, furnishing.FootprintCenter, piece.Scale);
            Vector2 half = furnishing.FootprintHalf * piece.Scale;

            if (furnishing.blocks)
            {
                // Where the player stands, with room for their shoulders.
                Vector3 feet = PieceRoot.InverseTransformPoint(housing.Feet);
                float clear = authority.Settings != null ? authority.Settings.standClear : 0.45f;
                if (HousingPlacement.FootprintsOverlap(middle, half, facing, new Vector2(feet.x, feet.z), Vector2.one * clear, 0f))
                    return HousingRules.ReasonStandingThere;
            }
            if (free) return null;

            for (int n = 0; n < all.Count; n++)
            {
                PlacedPiece other = all[n];
                if (other.u == piece.u || other.p != 0) continue;
                if (groupCarried && picked.Contains(other.u)) continue; // moved together: they keep their places among themselves
                FurnishingDefinition theirs = authority.Catalog.Get(other.i);
                if (theirs == null || (theirs.mount != FurnishingMount.Floor && theirs.mount != FurnishingMount.Surface)) continue;
                Vector2 otherMiddle = HousingPlacement.FootprintCenter(other.Position, other.Yaw, theirs.FootprintCenter, other.Scale);
                if (HousingPlacement.FootprintsOverlap(middle, half, facing, otherMiddle, theirs.FootprintHalf * other.Scale, other.Yaw))
                    return HousingRules.ReasonInTheWay;
            }
            return null;
        }

        private string Hint()
        {
            string tail = advanced ? "  ·  PgUp / PgDn lift  ·  [ ] size" : "  ·  Alt: off the grid  ·  Ctrl: fine steps";
            return carriedUid != 0
                ? "Click to set it down  ·  Q / E turn" + tail + "  ·  Esc: leave it where it was"
                : "Click to set it down  ·  Shift-click: and another  ·  Q / E turn" + tail;
        }

        // ------------------------------------------------------------------ the preview

        private void ShowGhost(in PlacedPiece piece, Transform pieceRoot, bool allowed)
        {
            if (ghost == null) return;
            // Drawn where the piece would really stand: through the same sums as a piece that is set down.
            HousingLayout.Pose(scratch, piece, out Vector3 position, out Quaternion turn);
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

        // ------------------------------------------------------------------ carrying one piece

        /// <summary>Takes a furnishing out of the Crate to be set down.</summary>
        private void CarryNew(FurnishingDefinition furnishing, float turn = 0f, float scale = 1f)
        {
            if (furnishing == null) return;
            if (!AtHall)
            {
                Notify("Furnishings are set down at your Hall.");
                return;
            }
            if (!HousingRules.MayHave(furnishing, housing.Faction)) { Notify(HousingRules.ReasonOtherFaction); return; }
            if (crate == null || crate.Count(furnishing.itemId) < 1) { Notify(HousingRules.ReasonNoneLeft); return; }
            StopCarrying();
            if (groupCarried) StopCarryingGroup();
            ClearPicked();
            carried = furnishing;
            carriedUid = 0;
            yaw = turn;
            tilt = lean = lift = 0f;
            size = scale;
            MakeGhost(furnishing);
            ShowAdjust();
            GearUIController.Instance?.HideItemTooltip();
        }

        // Another of what the cursor is on, turned and sized the same.
        private void CopyOf(FurnishingPiece piece)
        {
            if (!HousingLayout.TryFind(Pieces, piece.Uid, out PlacedPiece standing)) return;
            HousingLayout.Pose(Pieces, standing, out _, out Quaternion turn);
            CarryNew(piece.Furnishing, turn.eulerAngles.y, standing.Scale);
        }

        private void PickUp(FurnishingPiece piece)
        {
            List<PlacedPiece> pieces = Pieces;
            if (!HousingLayout.TryFind(pieces, piece.Uid, out PlacedPiece standing)) return;
            StopCarrying();
            carried = piece.Furnishing;
            carriedUid = piece.Uid;
            carriedBefore = standing;
            HousingLayout.Pose(pieces, standing, out _, out Quaternion turn);
            Vector3 euler = turn.eulerAngles;
            // What hangs on a wall takes its facing from the wall: only its skew is its own.
            bool onWall = carried.mount == FurnishingMount.Wall && HomeAreas.IsInside(Area) && !standing.Free;
            yaw = onWall ? Mathf.DeltaAngle(0f, standing.Rotation.eulerAngles.z) : euler.y;
            tilt = onWall ? 0f : Mathf.DeltaAngle(0f, euler.x);
            lean = onWall ? 0f : Mathf.DeltaAngle(0f, euler.z);
            lift = 0f;
            size = standing.Scale;
            MakeGhost(carried);
            View?.SetVisible(carriedUid, false);
            hovered = null;
            ShowAdjust();
        }

        private void StopCarrying()
        {
            if (carriedUid != 0 && View != null) View.SetVisible(carriedUid, true);
            carried = null;
            carriedUid = 0;
            hasCandidate = false;
            if (ghost != null) Destroy(ghost);
            ghost = null;
            ShowAdjust();
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
            if (fresh) Remember(furnishing.itemId);

            if (fresh && again && crate != null && crate.Count(furnishing.itemId) > 0) return; // still in hand
            StopCarrying();
        }

        private void TurnWhereItStands(uint uid, float step, int direction)
        {
            if (!HousingLayout.TryFind(Pieces, uid, out PlacedPiece standing)) return;
            FurnishingDefinition furnishing = authority.Catalog.Get(standing.i);
            // What hangs on a wall faces out from it: a turn sets it askew, it does not turn it off the wall.
            if (furnishing != null && furnishing.mount == FurnishingMount.Wall && !standing.Free)
                standing.c = PlacedPiece.Angle(HousingPlacement.StepYaw(standing.c / 100f, step, direction));
            else standing.Yaw = HousingPlacement.StepYaw(standing.Yaw, step, direction);
            Ask(PendingKind.Edit, HomeOp.Move(standing));
        }

        private void PutAway(uint uid) => Ask(PendingKind.Edit, HomeOp.Remove(uid));

        private sealed class HitDistance : IComparer<RaycastHit>
        {
            public static readonly HitDistance Instance = new HitDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
