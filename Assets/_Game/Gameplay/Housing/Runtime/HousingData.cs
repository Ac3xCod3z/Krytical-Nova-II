using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>What a furnishing stands on or hangs from.</summary>
    public enum FurnishingMount : byte
    {
        /// <summary>Stands on the ground or the floor (a bed, a table, a tree).</summary>
        Floor = 0,
        /// <summary>Small enough to stand on another piece's top as well as on the floor (a vase, a candle).</summary>
        Surface = 1,
        /// <summary>Lies flat on the floor and is walked over; other pieces may stand on it.</summary>
        Rug = 2,
        /// <summary>Hangs on a wall (a painting, a banner).</summary>
        Wall = 3,
        /// <summary>Hangs from a ceiling (a chandelier).</summary>
        Ceiling = 4,
    }

    /// <summary>Where a furnishing may be put.</summary>
    public enum FurnishingPlace : byte { Anywhere = 0, LawnOnly = 1, IndoorOnly = 2 }

    /// <summary>Where players get a furnishing. A tag for now: nothing sells or drops them yet.</summary>
    public enum FurnishingSource : byte { Vendor = 0, Drop = 1, NovaStore = 2 }

    /// <summary>
    /// The parts of a Hall that are furnished apart from each other: its lawns, and the three floors inside (owner,
    /// 12 Oct 2026: the floor walked into from outside, the upstairs and a basement). <c>Interior</c> is the
    /// ground floor.
    /// </summary>
    public enum HomeArea : byte { Lawn = 0, Interior = 1, Upper = 2, Basement = 3 }

    public static class HomeAreas
    {
        public const int Count = 4;
        public static readonly HomeArea[] All = { HomeArea.Lawn, HomeArea.Interior, HomeArea.Upper, HomeArea.Basement };
        /// <summary>The floors inside, the one walked into first.</summary>
        public static readonly HomeArea[] Inside = { HomeArea.Interior, HomeArea.Upper, HomeArea.Basement };

        public static bool IsInside(HomeArea area) => area != HomeArea.Lawn;

        public static string Name(HomeArea area)
        {
            switch (area)
            {
                case HomeArea.Interior: return "ground floor";
                case HomeArea.Upper: return "upstairs";
                case HomeArea.Basement: return "basement";
                default: return "lawns";
            }
        }
    }

    /// <summary>
    /// The four looks a Hall wears, each changed whole by one item: the building on the Lot (a Facade) and the
    /// walls, the floor and the ceiling of the room inside (Finishes).
    /// </summary>
    public enum HomeLook : byte { Facade = 0, Wall = 1, Floor = 2, Ceiling = 3 }

    /// <summary>
    /// One furnishing set down in a Hall. Kept small because a Hall holds hundreds: a position to 1/64 m from the
    /// corner of its Lot or room, angles to a hundredth of a degree. A piece that stands on another names it as its
    /// parent and keeps its place relative to it, so moving a table moves what is on it.
    /// </summary>
    [Serializable]
    public struct PlacedPiece : INetworkSerializable, IEquatable<PlacedPiece>
    {
        /// <summary>This piece's number in its Hall: never 0, never used twice.</summary>
        public uint u;
        /// <summary>The piece this one stands on, or 0.</summary>
        public uint p;
        /// <summary>The furnishing's item id.</summary>
        public ushort i;
        public short x, y, z;
        /// <summary>Yaw, pitch and roll in hundredths of a degree, each within -180 to 180.</summary>
        public short a, b, c;
        /// <summary>Size, 64 = as made.</summary>
        public byte s;
        /// <summary>Which of the furnishing's colour variants it wears.</summary>
        public byte v;
        /// <summary><see cref="PieceFlags"/>.</summary>
        public byte f;
        /// <summary>Index of its owner in the Hall's owner table.</summary>
        public byte o;

        public const float UnitsPerMetre = 64f;
        public const byte NormalScale = 64;

        public Vector3 Position
        {
            readonly get => new Vector3(x, y, z) / UnitsPerMetre;
            set { x = Quantize(value.x); y = Quantize(value.y); z = Quantize(value.z); }
        }

        public float Yaw
        {
            readonly get => a / 100f;
            set => a = Angle(value);
        }

        public Quaternion Rotation
        {
            readonly get => Quaternion.Euler(b / 100f, a / 100f, c / 100f);
            set
            {
                Vector3 euler = value.eulerAngles;
                b = Angle(euler.x); a = Angle(euler.y); c = Angle(euler.z);
            }
        }

        public float Scale
        {
            readonly get => (s == 0 ? NormalScale : s) / (float)NormalScale;
            set => s = (byte)Mathf.Clamp(Mathf.RoundToInt(value * NormalScale), 1, 255);
        }

        public readonly bool Free => (f & (byte)PieceFlags.Free) != 0;

        public static short Quantize(float metres) =>
            (short)Mathf.Clamp(Mathf.RoundToInt(metres * UnitsPerMetre), short.MinValue, short.MaxValue);

        public static short Angle(float degrees) => (short)Mathf.RoundToInt(Mathf.DeltaAngle(0f, degrees) * 100f);

        /// <summary>The same place and turn, whatever the piece's number or owner.</summary>
        public readonly bool SamePose(in PlacedPiece other) =>
            p == other.p && x == other.x && y == other.y && z == other.z && a == other.a && b == other.b && c == other.c && s == other.s;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref u);
            serializer.SerializeValue(ref p);
            serializer.SerializeValue(ref i);
            serializer.SerializeValue(ref x);
            serializer.SerializeValue(ref y);
            serializer.SerializeValue(ref z);
            serializer.SerializeValue(ref a);
            serializer.SerializeValue(ref b);
            serializer.SerializeValue(ref c);
            serializer.SerializeValue(ref s);
            serializer.SerializeValue(ref v);
            serializer.SerializeValue(ref f);
            serializer.SerializeValue(ref o);
        }

        public readonly bool Equals(PlacedPiece other) =>
            u == other.u && i == other.i && v == other.v && f == other.f && o == other.o && SamePose(other);

        public override readonly bool Equals(object obj) => obj is PlacedPiece other && Equals(other);
        public override readonly int GetHashCode() => (int)u;
    }

    [Flags]
    public enum PieceFlags : byte
    {
        None = 0,
        /// <summary>Set down off the grid and without the collision rule (Alt, or Advanced mode).</summary>
        Free = 1,
    }

    /// <summary>A Hall as it is saved: its looks and everything set down in it. One document per Hall.</summary>
    [Serializable]
    public sealed class HomeSave
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        /// <summary>The number the next piece set down gets.</summary>
        public uint nextUid = 1;
        public ushort facade;
        public ushort wallFinish, floorFinish, ceilingFinish;
        /// <summary>Character ids of the players whose pieces stand here; a piece names its owner by index.</summary>
        public List<string> owners = new List<string>();
        public List<PlacedPiece> lawn = new List<PlacedPiece>();
        /// <summary>The ground floor inside.</summary>
        public List<PlacedPiece> interior = new List<PlacedPiece>();
        public List<PlacedPiece> upper = new List<PlacedPiece>();
        public List<PlacedPiece> basement = new List<PlacedPiece>();

        public List<PlacedPiece> Area(HomeArea area)
        {
            switch (area)
            {
                case HomeArea.Interior: return interior;
                case HomeArea.Upper: return upper;
                case HomeArea.Basement: return basement;
                default: return lawn;
            }
        }

        /// <summary>The item id of the look worn in one of the four, or 0 for the faction's standard one.</summary>
        public ushort Look(HomeLook slot)
        {
            switch (slot)
            {
                case HomeLook.Facade: return facade;
                case HomeLook.Wall: return wallFinish;
                case HomeLook.Floor: return floorFinish;
                default: return ceilingFinish;
            }
        }

        public void SetLook(HomeLook slot, ushort itemId)
        {
            switch (slot)
            {
                case HomeLook.Facade: facade = itemId; break;
                case HomeLook.Wall: wallFinish = itemId; break;
                case HomeLook.Floor: floorFinish = itemId; break;
                default: ceilingFinish = itemId; break;
            }
        }

        /// <summary>The index of a character in the owner table, added when new. 255 owners at most.</summary>
        public byte OwnerIndex(string characterId)
        {
            characterId ??= string.Empty;
            int index = owners.IndexOf(characterId);
            if (index < 0 && owners.Count < byte.MaxValue)
            {
                owners.Add(characterId);
                index = owners.Count - 1;
            }
            return (byte)Mathf.Max(0, index);
        }
    }

    /// <summary>How many of one furnishing a Furnishing Crate holds.</summary>
    [Serializable]
    public struct CrateEntry : INetworkSerializable
    {
        public ushort itemId;
        public int count;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemId);
            serializer.SerializeValue(ref count);
        }
    }

    public enum HomeOpKind : byte { Place = 0, Move = 1, Remove = 2 }

    /// <summary>One step of an edit: set a piece down, move one, or put one away.</summary>
    [Serializable]
    public struct HomeOp : INetworkSerializable
    {
        public HomeOpKind kind;
        public PlacedPiece piece;

        public static HomeOp Place(in PlacedPiece piece) => new HomeOp { kind = HomeOpKind.Place, piece = piece };
        public static HomeOp Move(in PlacedPiece piece) => new HomeOp { kind = HomeOpKind.Move, piece = piece };
        public static HomeOp Remove(uint uid) => new HomeOp { kind = HomeOpKind.Remove, piece = new PlacedPiece { u = uid } };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref kind);
            serializer.SerializeValue(ref piece);
        }
    }

    /// <summary>What an edit did to one piece: how it was before, and how it is now. Either side may be missing.</summary>
    public struct HomeChange
    {
        public bool hadBefore, hasAfter;
        public PlacedPiece before, after;

        public static HomeChange Placed(in PlacedPiece after) => new HomeChange { hasAfter = true, after = after };
        public static HomeChange Removed(in PlacedPiece before) => new HomeChange { hadBefore = true, before = before };
        public static HomeChange Moved(in PlacedPiece before, in PlacedPiece after) =>
            new HomeChange { hadBefore = true, hasAfter = true, before = before, after = after };
    }

    /// <summary>
    /// The outline of a Lot's lawns or of a room, in its own space (x across, z towards the back, y up, the origin
    /// in the middle of the floor). Plain numbers, so the rules that read it run in a test.
    /// </summary>
    [Serializable]
    public struct AreaShape
    {
        public Vector2 halfSize;
        public float height;
        /// <summary>Rectangles nothing may be set down in: the Hall's pad, the space kept clear at a door.</summary>
        public Rect[] blocked;
        /// <summary>A room: it has walls at its edges and a ceiling at its height, for what hangs on them.</summary>
        public bool hasWalls;

        public readonly bool Contains(Vector3 point) =>
            Mathf.Abs(point.x) <= halfSize.x && Mathf.Abs(point.z) <= halfSize.y && point.y >= -0.01f && point.y <= height;

        /// <summary>How far a point is from the nearest wall, across the floor. Only a room has walls.</summary>
        public readonly float WallDistance(Vector3 point) =>
            Mathf.Min(halfSize.x - Mathf.Abs(point.x), halfSize.y - Mathf.Abs(point.z));

        public readonly bool IsBlocked(Vector3 point)
        {
            if (blocked == null) return false;
            var flat = new Vector2(point.x, point.z);
            for (int i = 0; i < blocked.Length; i++)
                if (blocked[i].Contains(flat)) return true;
            return false;
        }
    }
}
