using System.Collections.Generic;
using KryticalNova.Housing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    // The comfort set: several pieces picked and moved together, Advanced mode (set down freely, and lifted,
    // tilted and sized), the frames that show what the cursor is on, and the view from above.
    public sealed partial class ArrangeController
    {
        private enum AdjustKind { Lift, Tilt, Lean, Size, Level }

        private readonly List<uint> picked = new List<uint>(64);
        private readonly List<PlacedPiece> groupBefore = new List<PlacedPiece>(64);
        private readonly List<PlacedPiece> groupAfter = new List<PlacedPiece>(64);
        private readonly List<uint> framed = new List<uint>(64);
        private readonly ArrangeOverhead overhead = new ArrangeOverhead();
        private HomeView framedView;
        private bool groupCarried, groupHasCandidate;
        private Vector3 groupAnchor;
        private float groupYaw;
        private string groupRefusal;
        private bool advanced;

        // ------------------------------------------------------------------ picking several

        private void ClearPicked()
        {
            picked.Clear();
            ShowAdjust();
        }

        // What stands on a piece moves with it, so it is the piece underneath that is picked.
        private uint TopOf(uint uid)
        {
            List<PlacedPiece> pieces = Pieces;
            int guard = 0;
            while (guard++ < 8 && HousingLayout.TryFind(pieces, uid, out PlacedPiece piece) && piece.p != 0) uid = piece.p;
            return uid;
        }

        private void TogglePicked(uint uid)
        {
            uid = TopOf(uid);
            if (!picked.Remove(uid))
            {
                int most = authority.Settings != null ? authority.Settings.maxGroup : 60;
                if (picked.Count >= most) { Notify($"No more than {most} pieces are picked up together."); return; }
                picked.Add(uid);
            }
            ShowAdjust();
        }

        private void DrawBox(Vector2 from, Vector2 to)
        {
            Vector2 a = ToPanel(from), b = ToPanel(to);
            boxRect.style.display = DisplayStyle.Flex;
            boxRect.style.left = Mathf.Min(a.x, b.x);
            boxRect.style.top = Mathf.Min(a.y, b.y);
            boxRect.style.width = Mathf.Abs(a.x - b.x);
            boxRect.style.height = Mathf.Abs(a.y - b.y);
            boxRect.BringToFront();
            window.BringToFront();
        }

        private void PickInBox(Camera view, Vector2 from, Vector2 to, bool adding)
        {
            if (!adding) picked.Clear();
            var box = Rect.MinMaxRect(Mathf.Min(from.x, to.x), Mathf.Min(from.y, to.y), Mathf.Max(from.x, to.x), Mathf.Max(from.y, to.y));
            int most = authority.Settings != null ? authority.Settings.maxGroup : 60;
            HomeView shown = View;
            foreach (PlacedPiece piece in Pieces)
            {
                if (piece.p != 0 || picked.Contains(piece.u)) continue;
                FurnishingPiece drawn = shown.Find(piece.u);
                if (drawn == null || drawn.Furnishing == null || !drawn.gameObject.activeInHierarchy) continue;
                // What hangs keeps to its wall: it is moved with the rest only where pieces are set down freely.
                FurnishingMount mount = drawn.Furnishing.mount;
                if (!advanced && (mount == FurnishingMount.Wall || mount == FurnishingMount.Ceiling)) continue;
                Vector3 at = view.WorldToScreenPoint(drawn.transform.TransformPoint(drawn.Furnishing.boundsCenter));
                if (at.z <= 0f || !box.Contains(new Vector2(at.x, at.y))) continue;
                if (picked.Count >= most) { Notify($"No more than {most} pieces are picked up together."); break; }
                picked.Add(piece.u);
            }
            ShowAdjust();
        }

        private string PickedHint() => picked.Count == 1
            ? "One piece picked: click it to take it up  ·  Q / E turn  ·  Del puts it away  ·  Shift-click adds more"
            : $"{picked.Count} pieces picked: click one to move them all  ·  Del puts them away  ·  Esc lets them go";

        // ------------------------------------------------------------------ moving them together

        private void CarryGroup()
        {
            List<PlacedPiece> pieces = Pieces;
            groupBefore.Clear();
            for (int n = picked.Count - 1; n >= 0; n--)
            {
                uint top = TopOf(picked[n]);
                if (top != picked[n])
                {
                    picked.RemoveAt(n);
                    if (!picked.Contains(top)) picked.Add(top);
                }
            }
            Vector3 sum = Vector3.zero;
            foreach (uint uid in picked)
            {
                if (!HousingLayout.TryFind(pieces, uid, out PlacedPiece piece)) continue;
                groupBefore.Add(piece);
                sum += piece.Position;
            }
            if (groupBefore.Count == 0) { ClearPicked(); return; }
            groupAnchor = sum / groupBefore.Count;
            groupAnchor.y = 0f;
            groupYaw = 0f;
            groupCarried = true;
            groupHasCandidate = false;
            hovered = null;
        }

        private void StopCarryingGroup()
        {
            groupCarried = groupHasCandidate = false;
            View?.RefreshPoses(); // back where the Hall has them
        }

        // Where the pieces picked would stand, moved as one to the cursor: asked of the rules, and shown by moving
        // the pieces themselves.
        private void LookForGroup(Ray ray, bool away, bool free, bool fine)
        {
            hovered = null;
            if (away) return;
            Under under = Cast(ray, carrying: false, free, groundOnly: true);
            if (!under.hasGround || under.isWall)
            {
                groupHasCandidate = false;
                SetStatus(HousingRules.ReasonBlocked);
                return;
            }

            HousingSettings settings = authority.Settings;
            float step = settings == null ? 0.5f : fine ? settings.fineGridStep : settings.gridStep;
            Vector3 target = PieceRoot.InverseTransformPoint(under.ground.point);
            if (!free) target = HousingPlacement.SnapToGrid(target, step);
            target.y = 0f;
            Quaternion spin = Quaternion.Euler(0f, groupYaw, 0f);

            scratch.Clear();
            scratch.AddRange(Pieces);
            groupAfter.Clear();
            foreach (PlacedPiece before in groupBefore)
            {
                PlacedPiece after = before;
                Vector3 place = target + spin * (before.Position - groupAnchor);
                place.y = before.Position.y;
                after.Position = place;
                after.Rotation = spin * before.Rotation;
                after.f = (byte)(free ? PieceFlags.Free : PieceFlags.None);
                int index = HousingLayout.IndexOf(scratch, after.u);
                if (index >= 0) scratch[index] = after;
                groupAfter.Add(after);
            }

            EditContext context = Context();
            groupRefusal = null;
            foreach (PlacedPiece after in groupAfter)
            {
                FurnishingDefinition furnishing = authority.Catalog.Get(after.i);
                groupRefusal = HousingRules.PoseBlockedReason(scratch, after, furnishing, context) ?? Courtesy(scratch, after, furnishing, free);
                if (groupRefusal != null) break;
            }
            View.ShowPoses(scratch);
            groupHasCandidate = true;
            SetStatus(groupRefusal ?? "Click to set them down  ·  Q / E turn them  ·  Esc: leave them where they were");
        }

        private void SetDownGroup()
        {
            if (!groupHasCandidate) return;
            if (groupRefusal != null) { Notify(groupRefusal); return; }
            var ops = new List<HomeOp>(groupAfter.Count);
            for (int n = 0; n < groupAfter.Count; n++)
                if (!groupAfter[n].SamePose(groupBefore[n])) ops.Add(HomeOp.Move(groupAfter[n]));
            groupCarried = groupHasCandidate = false;
            if (ops.Count == 0) View.RefreshPoses();
            else Ask(PendingKind.Edit, ops); // one edit, undone as one
        }

        private void PutAwayPicked()
        {
            if (picked.Count == 0) return;
            var ops = new List<HomeOp>(picked.Count);
            foreach (uint uid in picked) ops.Add(HomeOp.Remove(uid));
            picked.Clear();
            Ask(PendingKind.Edit, ops);
            ShowAdjust();
        }

        // ------------------------------------------------------------------ frames

        private void DrawFrames()
        {
            HomeView shown = View;
            if (shown != framedView) ClearFrames();
            framedView = shown;
            if (shown == null) return;
            HousingSettings settings = authority.Settings;
            Color hover = settings != null ? settings.frameHover : Color.white;
            Color pick = settings != null ? settings.framePicked : Color.cyan;
            if (groupCarried && groupHasCandidate && settings != null) pick = groupRefusal == null ? settings.ghostAllowed : settings.ghostRefused;
            pick.a = 1f;

            uint over = hovered != null && carried == null ? hovered.Uid : 0;
            for (int n = framed.Count - 1; n >= 0; n--)
            {
                if (framed[n] == over || picked.Contains(framed[n])) continue;
                shown.SetFrame(framed[n], false, default);
                framed.RemoveAt(n);
            }
            foreach (uint uid in picked)
            {
                shown.SetFrame(uid, true, pick);
                if (!framed.Contains(uid)) framed.Add(uid);
            }
            if (over != 0 && !picked.Contains(over))
            {
                shown.SetFrame(over, true, hover);
                if (!framed.Contains(over)) framed.Add(over);
            }
        }

        private void ClearFrames()
        {
            if (framedView != null)
                foreach (uint uid in framed) framedView.SetFrame(uid, false, default);
            framed.Clear();
            framedView = null;
        }

        // ------------------------------------------------------------------ Advanced

        private void SetAdvanced(bool on)
        {
            advanced = on;
            if (!on) tilt = lean = lift = 0f; // Basic keeps what is in hand upright and on the ground
            Refresh();
        }

        private void BuildAdjustPanel()
        {
            adjustPanel = new VisualElement();
            adjustPanel.style.marginTop = 4f;
            adjustLabel = new Label();
            adjustLabel.AddToClassList("muted");
            adjustLabel.style.whiteSpace = WhiteSpace.Normal;
            adjustPanel.Add(adjustLabel);
            VisualElement row = Row(2f, wrap: true);
            row.Add(Chip("LIFT −", () => Adjust(AdjustKind.Lift, -1, FineHeld())));
            row.Add(Chip("LIFT +", () => Adjust(AdjustKind.Lift, 1, FineHeld())));
            row.Add(Chip("TILT −", () => Adjust(AdjustKind.Tilt, -1, FineHeld())));
            row.Add(Chip("TILT +", () => Adjust(AdjustKind.Tilt, 1, FineHeld())));
            row.Add(Chip("LEAN −", () => Adjust(AdjustKind.Lean, -1, FineHeld())));
            row.Add(Chip("LEAN +", () => Adjust(AdjustKind.Lean, 1, FineHeld())));
            row.Add(Chip("SIZE −", () => Adjust(AdjustKind.Size, -1, FineHeld())));
            row.Add(Chip("SIZE +", () => Adjust(AdjustKind.Size, 1, FineHeld())));
            row.Add(Chip("LEVEL", () => Adjust(AdjustKind.Level, 0, false)));
            adjustPanel.Add(row);
            window.Add(adjustPanel);
        }

        private static bool FineHeld() => Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;

        private void ShowAdjust()
        {
            if (adjustPanel == null) return;
            bool show = advanced && AtHall;
            adjustPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            if (carried != null)
                adjustLabel.text = $"IN HAND: {carried.DisplayName}  ·  lift {lift:0.##} m  ·  tilt {tilt:0}°  ·  lean {lean:0}°  ·  size {size * 100f:0} %";
            else if (picked.Count == 1 && HousingLayout.TryFind(Pieces, picked[0], out PlacedPiece piece))
            {
                FurnishingDefinition furnishing = authority.Catalog.Get(piece.i);
                adjustLabel.text = $"PICKED: {(furnishing != null ? furnishing.DisplayName : "a piece")}  ·  height {piece.Position.y:0.##} m  ·  tilt {piece.b / 100f:0}°  ·  lean {piece.c / 100f:0}°  ·  size {piece.Scale * 100f:0} %";
            }
            else adjustLabel.text = "Advanced: pieces are set down freely. Click a piece to work on it where it stands (Ctrl: fine steps).";
        }

        // Lifts, tilts, leans or sizes the piece in hand, or the one piece picked where it stands.
        private void Adjust(AdjustKind kind, int direction, bool fine)
        {
            if (!advanced || !AtHall) return;
            HousingSettings settings = authority.Settings;
            float liftStep = settings == null ? 0.25f : fine ? settings.fineLiftStep : settings.liftStep;
            float tiltStep = settings == null ? 15f : fine ? settings.fineTiltStep : settings.tiltStep;
            float sizeStep = settings == null ? 0.125f : fine ? settings.scaleStep * 0.5f : settings.scaleStep;
            float smallest = settings != null ? settings.minPieceScale : 0.5f, largest = settings != null ? settings.maxPieceScale : 2f;

            if (carried != null)
            {
                switch (kind)
                {
                    case AdjustKind.Lift: lift += direction * liftStep; break;
                    case AdjustKind.Tilt: tilt = Mathf.DeltaAngle(0f, tilt + direction * tiltStep); break;
                    case AdjustKind.Lean: lean = Mathf.DeltaAngle(0f, lean + direction * tiltStep); break;
                    case AdjustKind.Size: size = Mathf.Clamp(size + direction * sizeStep, smallest, largest); break;
                    case AdjustKind.Level: tilt = lean = lift = 0f; size = 1f; break;
                }
                ShowAdjust();
                return;
            }

            if (picked.Count != 1 || !HousingLayout.TryFind(Pieces, picked[0], out PlacedPiece piece))
            {
                Notify("Take a piece in hand, or click one to pick it, first.");
                return;
            }
            FurnishingDefinition furnishing = authority.Catalog.Get(piece.i);
            Vector3 position = piece.Position;
            switch (kind)
            {
                case AdjustKind.Lift: position.y += direction * liftStep; break;
                case AdjustKind.Tilt: piece.b = PlacedPiece.Angle(piece.b / 100f + direction * tiltStep); break;
                case AdjustKind.Lean: piece.c = PlacedPiece.Angle(piece.c / 100f + direction * tiltStep); break;
                case AdjustKind.Size: piece.Scale = Mathf.Clamp(piece.Scale + direction * sizeStep, smallest, largest); break;
                case AdjustKind.Level:
                    piece.b = piece.c = 0;
                    piece.Scale = 1f;
                    // Back on the ground, when the ground is what it stands on.
                    if (piece.p == 0 && furnishing != null && (furnishing.mount == FurnishingMount.Floor || furnishing.mount == FurnishingMount.Surface))
                        position.y = -furnishing.Bottom;
                    break;
            }
            piece.Position = position;
            piece.f = (byte)PieceFlags.Free;
            Ask(PendingKind.Edit, HomeOp.Move(piece));
            ShowAdjust();
        }

        // ------------------------------------------------------------------ the view from above

        private void SetOverhead(bool on)
        {
            if (on && AtHall && PieceRoot != null)
            {
                AreaShape shape = authority.Shape(Area);
                overhead.Begin(PieceRoot.position, shape.halfSize, authority.Settings, ViewCamera());
            }
            else overhead.End();
            if (overheadButton != null) overheadButton.style.opacity = overhead.Active ? 1f : 0.6f;
        }

        // The arrow keys move it across the ground, the wheel brings it nearer.
        private void MoveOverhead(Keyboard keyboard, Mouse mouse, bool overUi, bool typing)
        {
            Vector2 across = Vector2.zero;
            if (keyboard != null && !typing)
            {
                across.x = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
                across.y = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
            }
            float wheel = overUi ? 0f : mouse.scroll.ReadValue().y;
            overhead.Move(across, Mathf.Abs(wheel) > 0.01f ? Mathf.Sign(wheel) : 0f, Time.unscaledDeltaTime);
        }
    }
}
