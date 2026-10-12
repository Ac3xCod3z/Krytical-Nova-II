using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>What an edit is checked against: whose it is, which area it is in, and how much room is left.</summary>
    public struct EditContext
    {
        public FurnishingCatalog catalog;
        public HomeArea area;
        public AreaShape shape;
        /// <summary>The faction of the player making the edit.</summary>
        public FactionId faction;
        /// <summary>The player's index in the Hall's owner table.</summary>
        public byte owner;
        public int budget;
        public int maxStackDepth;
        /// <summary>The smallest and largest a piece may be made (0 for either: no limit).</summary>
        public float minScale, maxScale;
        /// <summary>How many of an item the player's Furnishing Crate holds.</summary>
        public Func<ushort, int> crateCount;
    }

    /// <summary>
    /// The rules of furnishing a Hall, as plain functions: the same ones answer the player's own game while a piece
    /// is being moved and the authority when the edit is made. Two pieces overlapping is not among them: that is a
    /// courtesy of the Arrange tool's Basic mode, so decorating stays forgiving and no two machines ever disagree
    /// about a hair's breadth.
    /// </summary>
    public static class HousingRules
    {
        public const string ReasonUnknown = "That is not a furnishing.";
        public const string ReasonOtherFaction = "That belongs to another faction.";
        public const string ReasonNoneLeft = "There is none of that left in your Furnishing Crate.";
        public const string ReasonOutside = "That is outside the Lot.";
        public const string ReasonBlocked = "Nothing can stand there.";
        public const string ReasonBudget = "There is no room left for that here.";
        public const string ReasonGone = "That piece is no longer there.";
        public const string ReasonNoTop = "Nothing can stand on that.";
        public const string ReasonTooHigh = "Pieces cannot be stacked that high.";
        public const string ReasonOutdoors = "That belongs outdoors.";
        public const string ReasonIndoors = "That belongs indoors.";
        public const string ReasonInTheWay = "Something is in the way.";
        public const string ReasonNeedsWall = "That hangs on a wall.";
        public const string ReasonNeedsCeiling = "That hangs from a ceiling.";
        public const string ReasonSize = "It cannot be made that size.";
        public const string ReasonStandingThere = "You are standing there.";
        public const string ReasonNotALook = "That is not a Facade or a Finish.";
        public const string ReasonWornAlready = "The Hall already wears that.";

        // How near a wall or the ceiling a piece that hangs must be, in metres.
        private const float HangReach = 0.35f;

        /// <summary>
        /// Whether a character of a faction may have a furnishing. Furnishings are anyone's (owner, 12 Oct 2026: a
        /// Hall is furnished in any style its players like, indoors and on its lawns; only its Facade is its
        /// faction's). A furnishing's faction is the style it is filed under, not a lock; one whose item is bound to
        /// a faction would still be refused here.
        /// </summary>
        public static bool MayHave(FurnishingDefinition furnishing, FactionId faction) =>
            furnishing != null && KryticalNova.Items.ItemFactionRules.MayHave(furnishing.item, faction);

        /// <summary>
        /// Whether a Hall of a faction may wear a look. A Facade is its faction's own, so that every Hall fits the
        /// Crew Quarter it stands in; a Finish is anyone's.
        /// </summary>
        public static bool MayWear(HallLookDefinition look, FactionId faction) =>
            look != null && (look.slot != HomeLook.Facade || look.faction == FactionId.None || look.faction == faction);

        /// <summary>Why a piece may not be where it is, or null. <paramref name="pieces"/> already holds it.</summary>
        public static string PoseBlockedReason(List<PlacedPiece> pieces, in PlacedPiece piece, FurnishingDefinition furnishing, in EditContext context)
        {
            if (furnishing == null) return ReasonUnknown;
            if (furnishing.place == FurnishingPlace.LawnOnly && context.area != HomeArea.Lawn) return ReasonOutdoors;
            if (furnishing.place == FurnishingPlace.IndoorOnly && !HomeAreas.IsInside(context.area)) return ReasonIndoors;

            if (piece.p != 0)
            {
                if (!HousingLayout.TryFind(pieces, piece.p, out PlacedPiece on)) return ReasonGone;
                FurnishingDefinition under = context.catalog != null ? context.catalog.Get(on.i) : null;
                if (under == null || !under.hasTop) return ReasonNoTop;
                if (HousingLayout.Depth(pieces, piece.u) > context.maxStackDepth) return ReasonTooHigh;
            }

            float scale = piece.Scale;
            if ((context.minScale > 0f && scale < context.minScale - 0.001f) || (context.maxScale > 0f && scale > context.maxScale + 0.001f))
                return ReasonSize;

            HousingLayout.Pose(pieces, piece, out Vector3 position, out _);
            if (!context.shape.Contains(position)) return ReasonOutside;
            // What hangs has no footing to be in the way of a door: only what stands is kept off the blocked ground.
            bool hangs = furnishing.mount == FurnishingMount.Wall || furnishing.mount == FurnishingMount.Ceiling;
            if (!hangs && context.shape.IsBlocked(position)) return ReasonBlocked;
            // Set down freely (Alt, or Advanced), a banner may hang from a beam: the wall and the ceiling are Basic's rule.
            if (hangs && !piece.Free && piece.p == 0)
            {
                if (furnishing.mount == FurnishingMount.Wall && (!context.shape.hasWalls || context.shape.WallDistance(position) > HangReach))
                    return ReasonNeedsWall;
                if (furnishing.mount == FurnishingMount.Ceiling &&
                    (!context.shape.hasWalls || position.y + furnishing.Top * scale < context.shape.height - HangReach))
                    return ReasonNeedsCeiling;
            }
            return null;
        }

        /// <summary>
        /// Changes one of a Hall's four looks, or puts its faction's standard one back (<paramref name="itemId"/> 0).
        /// The look comes out of the Furnishing Crate and the one it replaces goes back in. On a refusal the Hall is
        /// untouched and the reason is returned.
        /// </summary>
        public static string TryWear(HomeSave home, FurnishingCatalog catalog, FactionId faction, Func<ushort, int> crateCount,
            HomeLook slot, ushort itemId, Dictionary<ushort, int> crateDelta)
        {
            crateDelta.Clear();
            if (home == null) return ReasonGone;
            ushort worn = home.Look(slot);
            HallLookDefinition wornLook = catalog != null ? catalog.GetLook(worn) : null;
            if (wornLook == null || wornLook.standard) worn = 0; // the standard look is no item

            if (itemId != 0)
            {
                HallLookDefinition look = catalog != null ? catalog.GetLook(itemId) : null;
                if (look == null || look.slot != slot) return ReasonNotALook;
                if (!MayWear(look, faction)) return ReasonOtherFaction;
                if (look.standard) itemId = 0;
                else if (itemId == worn) return ReasonWornAlready;
                else if (crateCount == null || crateCount(itemId) < 1) return ReasonNoneLeft;
            }
            if (itemId == worn) return ReasonWornAlready;

            if (itemId != 0) crateDelta[itemId] = -1;
            if (worn != 0) crateDelta[worn] = 1;
            home.SetLook(slot, itemId);
            return null;
        }

        /// <summary>
        /// Makes an edit of several steps, all of it or none of it. On success <paramref name="changes"/> says what
        /// became of each piece and <paramref name="crateDelta"/> what the Furnishing Crate gains and loses; on a
        /// refusal the Hall is untouched and the reason is returned.
        /// A Place step may carry a number of the caller's own in <c>piece.u</c>: a later step of the same edit that
        /// names it as a parent gets the piece's real number (this is how an undone "put away" stands the plates
        /// back on the table).
        /// </summary>
        public static string TryApply(HomeSave home, in EditContext context, IReadOnlyList<HomeOp> ops,
            List<HomeChange> changes, Dictionary<ushort, int> crateDelta)
        {
            changes.Clear();
            crateDelta.Clear();
            if (home == null || ops == null || ops.Count == 0) return ReasonGone;

            List<PlacedPiece> live = home.Area(context.area);
            var scratch = new List<PlacedPiece>(live);
            uint nextUid = home.nextUid;
            var renumbered = new Dictionary<uint, uint>();
            var touched = new List<uint>();
            var removed = new HashSet<uint>();
            var family = new List<PlacedPiece>();

            for (int n = 0; n < ops.Count; n++)
            {
                HomeOp op = ops[n];
                switch (op.kind)
                {
                    case HomeOpKind.Place:
                    {
                        FurnishingDefinition furnishing = context.catalog != null ? context.catalog.Get(op.piece.i) : null;
                        if (furnishing == null) return ReasonUnknown;
                        if (!MayHave(furnishing, context.faction)) return ReasonOtherFaction;
                        crateDelta.TryGetValue(op.piece.i, out int delta);
                        int held = context.crateCount != null ? context.crateCount(op.piece.i) : 0;
                        if (held + delta < 1) return ReasonNoneLeft;
                        crateDelta[op.piece.i] = delta - 1;

                        uint asked = op.piece.u;
                        PlacedPiece piece = op.piece;
                        if (piece.p != 0 && renumbered.TryGetValue(piece.p, out uint parent)) piece.p = parent;
                        piece.o = context.owner;
                        piece = HousingLayout.Add(scratch, ref nextUid, piece);
                        if (asked != 0) renumbered[asked] = piece.u;
                        string reason = PoseBlockedReason(scratch, piece, furnishing, context);
                        if (reason != null) return reason;
                        changes.Add(HomeChange.Placed(piece));
                        touched.Add(piece.u);
                        break;
                    }
                    case HomeOpKind.Move:
                    {
                        int index = HousingLayout.IndexOf(scratch, op.piece.u);
                        if (index < 0) return ReasonGone;
                        PlacedPiece before = scratch[index];
                        PlacedPiece after = before;
                        after.p = op.piece.p;
                        after.x = op.piece.x; after.y = op.piece.y; after.z = op.piece.z;
                        after.a = op.piece.a; after.b = op.piece.b; after.c = op.piece.c;
                        after.s = op.piece.s == 0 ? before.s : op.piece.s;
                        after.v = op.piece.v;
                        after.f = op.piece.f;
                        if (after.p != 0 && HousingLayout.WouldCycle(scratch, after.u, after.p)) return ReasonNoTop;
                        scratch[index] = after;
                        FurnishingDefinition furnishing = context.catalog != null ? context.catalog.Get(after.i) : null;
                        string reason = PoseBlockedReason(scratch, after, furnishing, context);
                        if (reason != null) return reason;
                        changes.Add(HomeChange.Moved(before, after));
                        touched.Add(after.u);
                        break;
                    }
                    case HomeOpKind.Remove:
                    {
                        family.Clear();
                        HousingLayout.WithChildren(scratch, op.piece.u, family);
                        if (family.Count == 0)
                        {
                            // Already gone with the piece it stood on, earlier in this same edit.
                            if (removed.Contains(op.piece.u)) break;
                            return ReasonGone;
                        }
                        foreach (PlacedPiece gone in family)
                        {
                            removed.Add(gone.u);
                            HousingLayout.Remove(scratch, gone.u);
                            crateDelta.TryGetValue(gone.i, out int delta);
                            crateDelta[gone.i] = delta + 1;
                            changes.Add(HomeChange.Removed(gone));
                        }
                        break;
                    }
                    default:
                        return ReasonGone;
                }
            }

            // What stands on a moved piece moved with it: none of it may have left the area.
            foreach (uint uid in touched)
            {
                family.Clear();
                HousingLayout.WithChildren(scratch, uid, family);
                for (int n = 1; n < family.Count; n++)
                {
                    HousingLayout.Pose(scratch, family[n], out Vector3 position, out _);
                    if (!context.shape.Contains(position)) return ReasonOutside;
                    if (context.shape.IsBlocked(position)) return ReasonBlocked;
                }
            }

            FurnishingCatalog known = context.catalog;
            Func<ushort, int> costOf = id =>
            {
                FurnishingDefinition furnishing = known != null ? known.Get(id) : null;
                return furnishing != null ? furnishing.budgetCost : 1;
            };
            int usedAfter = HousingLayout.BudgetUsed(scratch, costOf);
            // An area already over its budget (a number was lowered) can still be thinned out and rearranged.
            if (usedAfter > context.budget && usedAfter > HousingLayout.BudgetUsed(live, costOf)) return ReasonBudget;

            live.Clear();
            live.AddRange(scratch);
            home.nextUid = nextUid;
            return null;
        }
    }
}
