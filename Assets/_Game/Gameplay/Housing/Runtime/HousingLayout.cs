using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// The pieces of one area of a Hall as a list, and the plain operations on it. No Unity objects and no
    /// networking: the authority that owns a Hall calls these, and so do the tests.
    /// </summary>
    public static class HousingLayout
    {
        public static int IndexOf(List<PlacedPiece> pieces, uint uid)
        {
            if (uid == 0) return -1;
            for (int n = 0; n < pieces.Count; n++)
                if (pieces[n].u == uid) return n;
            return -1;
        }

        public static bool TryFind(List<PlacedPiece> pieces, uint uid, out PlacedPiece piece)
        {
            int index = IndexOf(pieces, uid);
            piece = index >= 0 ? pieces[index] : default;
            return index >= 0;
        }

        /// <summary>How many pieces a piece stands on, one on top of the other. 0 for a piece on the floor.</summary>
        public static int Depth(List<PlacedPiece> pieces, uint uid)
        {
            int depth = 0;
            uint at = uid;
            while (TryFind(pieces, at, out PlacedPiece piece) && piece.p != 0 && depth <= pieces.Count)
            {
                depth++;
                at = piece.p;
            }
            return depth;
        }

        /// <summary>True when standing <paramref name="uid"/> on <paramref name="parent"/> would make a ring.</summary>
        public static bool WouldCycle(List<PlacedPiece> pieces, uint uid, uint parent)
        {
            int guard = 0;
            for (uint at = parent; at != 0 && guard <= pieces.Count; guard++)
            {
                if (at == uid) return true;
                if (!TryFind(pieces, at, out PlacedPiece piece)) return false;
                at = piece.p;
            }
            return false;
        }

        /// <summary>Where a piece is in its area's own space, following what it stands on.</summary>
        public static void Pose(List<PlacedPiece> pieces, in PlacedPiece piece, out Vector3 position, out Quaternion rotation)
        {
            position = piece.Position;
            rotation = piece.Rotation;
            int guard = 0;
            uint parent = piece.p;
            while (parent != 0 && guard++ <= pieces.Count && TryFind(pieces, parent, out PlacedPiece on))
            {
                Quaternion onRotation = on.Rotation;
                position = on.Position + onRotation * (position * on.Scale);
                rotation = onRotation * rotation;
                parent = on.p;
            }
        }

        /// <summary>A pose in the area's space, written as a pose relative to the piece it is to stand on.</summary>
        public static void ToLocal(List<PlacedPiece> pieces, uint parent, Vector3 position, Quaternion rotation,
            out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = position;
            localRotation = rotation;
            if (!TryFind(pieces, parent, out PlacedPiece on)) return;
            Pose(pieces, on, out Vector3 onPosition, out Quaternion onRotation);
            Quaternion inverse = Quaternion.Inverse(onRotation);
            localPosition = inverse * (position - onPosition) / on.Scale;
            localRotation = inverse * rotation;
        }

        /// <summary>Sets a new piece down and gives it its number.</summary>
        public static PlacedPiece Add(List<PlacedPiece> pieces, ref uint nextUid, PlacedPiece piece)
        {
            if (nextUid == 0) nextUid = 1;
            piece.u = nextUid++;
            if (piece.s == 0) piece.s = PlacedPiece.NormalScale;
            pieces.Add(piece);
            return piece;
        }

        /// <summary>The piece and everything standing on it, the piece first, each child after what it stands on.</summary>
        public static void WithChildren(List<PlacedPiece> pieces, uint uid, List<PlacedPiece> into)
        {
            if (!TryFind(pieces, uid, out PlacedPiece root)) return;
            int start = into.Count;
            into.Add(root);
            for (int n = start; n < into.Count; n++)
            {
                uint parent = into[n].u;
                for (int m = 0; m < pieces.Count; m++)
                    if (pieces[m].p == parent) into.Add(pieces[m]);
            }
        }

        public static void Remove(List<PlacedPiece> pieces, uint uid)
        {
            int index = IndexOf(pieces, uid);
            if (index >= 0) pieces.RemoveAt(index);
        }

        /// <summary>Budget points in use. <paramref name="costOf"/> answers 0 for an item it does not know.</summary>
        public static int BudgetUsed(List<PlacedPiece> pieces, System.Func<ushort, int> costOf)
        {
            int used = 0;
            for (int n = 0; n < pieces.Count; n++) used += Mathf.Max(0, costOf(pieces[n].i));
            return used;
        }
    }
}
