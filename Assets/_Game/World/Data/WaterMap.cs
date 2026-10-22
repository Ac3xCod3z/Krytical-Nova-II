using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Where a zone's water can be run on (Waterstride, owner 22 Oct 2026: "water mapped in a more efficient way than the
    /// walls"). Water is flat, so it needs no graph of places and steps: the water's height, and a grid of one-metre cells
    /// over the zone with one bit each, set where the water is deep enough and nothing stands in it. Baked in the Editor
    /// (<c>WaterMapBuilder</c>) from the terrain and the zone's water, compressed into the asset itself: a few kilobytes
    /// for a whole zone. Routes over it are found by <see cref="Water.WaterRoute"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Water Map", fileName = "WaterMap")]
    public sealed class WaterMap : ScriptableObject
    {
        [Tooltip("Height of the water's surface (metres).")]
        public float waterLevel;
        [Tooltip("World X and Z of the grid's first cell's corner.")]
        public Vector2 origin;
        [Min(0.25f)] public float cellSize = 1f;
        [Min(1)] public int width = 1, depth = 1;
        [Tooltip("The cells, one bit each, row by row along X, compressed (written by the builder).")]
        [SerializeField, HideInInspector] private byte[] packed;
        [SerializeField] private int waterCells;
        [Tooltip("What was baked, for the record.")]
        [TextArea] public string bakeNote;

        [NonSerialized] private bool[] cells;

        /// <summary>How many cells are water.</summary>
        public int WaterCells => waterCells;

        /// <summary>The cell a world point falls in; false outside the grid.</summary>
        public bool TryCell(Vector3 point, out Vector2Int cell)
        {
            cell = new Vector2Int(Mathf.FloorToInt((point.x - origin.x) / cellSize), Mathf.FloorToInt((point.z - origin.y) / cellSize));
            return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < depth;
        }

        /// <summary>Is this cell water that can be run on?</summary>
        public bool IsWater(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= depth) return false;
            EnsureLoaded();
            return cells != null && cells[cell.y * width + cell.x];
        }

        /// <summary>Is the place under this point water that can be run on?</summary>
        public bool IsWater(Vector3 point) => TryCell(point, out Vector2Int cell) && IsWater(cell);

        /// <summary>The middle of a cell, on the water's surface.</summary>
        public Vector3 Centre(Vector2Int cell) =>
            new Vector3(origin.x + (cell.x + 0.5f) * cellSize, waterLevel, origin.y + (cell.y + 0.5f) * cellSize);

        /// <summary>The builder's cells (row by row along X, <see cref="width"/> by <see cref="depth"/>).</summary>
        public void Set(bool[] water, string note)
        {
            if (water == null || water.Length != width * depth) throw new ArgumentException("the cells do not fill the grid");
            cells = water;
            waterCells = 0;
            foreach (bool w in water) if (w) waterCells++;
            packed = Pack(water);
            bakeNote = note;
        }

        /// <summary>The packed size, for the builder's report.</summary>
        public int PackedBytes => packed != null ? packed.Length : 0;

        private void EnsureLoaded()
        {
            if (cells != null || packed == null || packed.Length == 0) return;
            try { cells = Unpack(packed, width * depth); }
            catch (Exception e)
            {
                Debug.LogError($"[Water map] {name} could not be read: {e.Message}");
                cells = new bool[width * depth];
            }
        }

        private static byte[] Pack(bool[] water)
        {
            var bits = new byte[(water.Length + 7) / 8];
            for (int i = 0; i < water.Length; i++) if (water[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
            using var memory = new MemoryStream();
            using (var zip = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, true)) zip.Write(bits, 0, bits.Length);
            return memory.ToArray();
        }

        private static bool[] Unpack(byte[] bytes, int count)
        {
            var bits = new byte[(count + 7) / 8];
            using (var memory = new MemoryStream(bytes))
            using (var zip = new GZipStream(memory, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < bits.Length)
                {
                    int got = zip.Read(bits, read, bits.Length - read);
                    if (got <= 0) throw new InvalidDataException("the water map is short");
                    read += got;
                }
            }
            var water = new bool[count];
            for (int i = 0; i < count; i++) water[i] = (bits[i >> 3] & (1 << (i & 7))) != 0;
            return water;
        }

        private void OnValidate() => cells = null; // baked again: read it again
    }
}
