using System.Collections.Generic;

namespace KryticalNova.Housing
{
    /// <summary>
    /// One player's undo and redo while arranging. It remembers what each edit did (<see cref="HomeChange"/>) and
    /// hands back the edit that reverses it; it never changes a Hall itself. A piece that is put away and brought
    /// back by an undo comes back under a new number, so the history keeps track of which number became which.
    /// A step is skipped when a piece is no longer as this player left it (someone else has since moved it).
    /// </summary>
    public sealed class ArrangeHistory
    {
        private readonly List<List<HomeChange>> done = new List<List<HomeChange>>();
        private readonly List<List<HomeChange>> undone = new List<List<HomeChange>>();
        private readonly Dictionary<uint, uint> renumbered = new Dictionary<uint, uint>();
        private readonly int capacity;

        public ArrangeHistory(int capacity) => this.capacity = capacity < 1 ? 1 : capacity;

        public bool CanUndo => done.Count > 0;
        public bool CanRedo => undone.Count > 0;

        public void Clear()
        {
            done.Clear();
            undone.Clear();
            renumbered.Clear();
        }

        /// <summary>A fresh edit by the player: it can be undone, and what was undone before it is forgotten.</summary>
        public void Record(IReadOnlyList<HomeChange> changes)
        {
            if (changes == null || changes.Count == 0) return;
            done.Add(new List<HomeChange>(changes));
            if (done.Count > capacity) done.RemoveAt(0);
            undone.Clear();
        }

        /// <summary>The number a piece goes by now.</summary>
        public uint Current(uint uid)
        {
            int guard = 0;
            while (uid != 0 && renumbered.TryGetValue(uid, out uint next) && guard++ < 64) uid = next;
            return uid;
        }

        /// <summary>The edit that takes back the last one, or null. Call <see cref="Settle"/> with what it did.</summary>
        public List<HomeOp> BeginUndo(List<PlacedPiece> pieces) => Reverse(done, pieces, backwards: true);

        /// <summary>The edit that makes the last undone one again, or null. Call <see cref="Settle"/> with what it did.</summary>
        public List<HomeOp> BeginRedo(List<PlacedPiece> pieces) => Reverse(undone, pieces, backwards: false);

        /// <summary>
        /// Finishes an undo or a redo once the authority has made the edit: the step moves to the other pile, and
        /// pieces that came back are followed under their new numbers. <paramref name="asked"/> is the edit that
        /// was sent, <paramref name="changes"/> what it did.
        /// </summary>
        public void Settle(bool wasUndo, IReadOnlyList<HomeOp> asked, IReadOnlyList<HomeChange> changes)
        {
            List<List<HomeChange>> from = wasUndo ? done : undone;
            List<List<HomeChange>> to = wasUndo ? undone : done;
            if (from.Count == 0) return;
            List<HomeChange> step = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(step);

            // Place steps come back in the order they were asked for, each with the number it was asked under.
            int placed = 0;
            for (int n = 0; n < asked.Count; n++)
            {
                if (asked[n].kind != HomeOpKind.Place) continue;
                for (; placed < changes.Count; placed++)
                {
                    if (!changes[placed].hasAfter || changes[placed].hadBefore) continue;
                    uint old = asked[n].piece.u, now = changes[placed].after.u;
                    if (old != 0 && old != now) renumbered[old] = now;
                    placed++;
                    break;
                }
            }
        }

        /// <summary>Drops the newest step of a pile: it could not be reversed at all.</summary>
        public void Discard(bool wasUndo)
        {
            List<List<HomeChange>> from = wasUndo ? done : undone;
            if (from.Count > 0) from.RemoveAt(from.Count - 1);
        }

        private List<HomeOp> Reverse(List<List<HomeChange>> pile, List<PlacedPiece> pieces, bool backwards)
        {
            if (pile.Count == 0) return null;
            List<HomeChange> step = pile[pile.Count - 1];
            var ops = new List<HomeOp>(step.Count);

            // The pieces this edit brings back, by the numbers they are remembered under: what stood on one of them
            // names it by that number, and the authority joins the two up again.
            var returning = new HashSet<uint>();
            foreach (HomeChange change in step)
            {
                bool had = backwards ? change.hadBefore : change.hasAfter;
                bool has = backwards ? change.hasAfter : change.hadBefore;
                if (had && !has) returning.Add(backwards ? change.before.u : change.after.u);
            }

            // Undo puts things back in the order they were taken (a table before its plates) and takes away what
            // was set down; redo does the step again as it was.
            foreach (HomeChange change in step)
            {
                bool had = backwards ? change.hadBefore : change.hasAfter;   // the state to bring back
                bool has = backwards ? change.hasAfter : change.hadBefore;   // the state it is in now
                PlacedPiece want = backwards ? change.before : change.after;
                PlacedPiece now = backwards ? change.after : change.before;

                if (had && has)
                {
                    uint uid = Current(now.u);
                    if (!HousingLayout.TryFind(pieces, uid, out PlacedPiece live) || !live.SamePose(Renumber(now))) continue;
                    want = Renumber(want);
                    want.u = uid;
                    ops.Add(HomeOp.Move(want));
                }
                else if (had)
                {
                    // Bring a piece back. It keeps its old number as the name the rest of this edit knows it by.
                    PlacedPiece back = want;
                    if (!returning.Contains(back.p)) back.p = Current(back.p);
                    ops.Add(HomeOp.Place(back));
                }
                else if (has)
                {
                    uint uid = Current(now.u);
                    if (HousingLayout.IndexOf(pieces, uid) >= 0) ops.Add(HomeOp.Remove(uid));
                }
            }
            return ops;
        }

        private PlacedPiece Renumber(PlacedPiece piece)
        {
            piece.u = Current(piece.u);
            piece.p = Current(piece.p);
            return piece;
        }
    }
}
