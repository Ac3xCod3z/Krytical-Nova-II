using System;
using System.Diagnostics;

namespace KryticalNova.Diagnostics
{
    /// <summary>
    /// The last markers of the performance log, in the order they were made. A fixed ring: adding one never
    /// allocates, and the oldest is written over when it is full. Markers are numbered from 0, the first ever made.
    /// </summary>
    public sealed class MarkerRing
    {
        public struct Marker
        {
            /// <summary>Stopwatch ticks when it was made (a timed one: when its work ended).</summary>
            public long Timestamp;
            /// <summary>How long the work took; negative for a marker that only says "this happened".</summary>
            public float Milliseconds;
            public string Name, Detail;
            /// <summary><see cref="PerformanceLog.NoNumber"/> when there is none.</summary>
            public long Number;
            /// <summary>A change of settings or session: numbers before and after it are not compared.</summary>
            public bool IsChange;
        }

        private readonly Marker[] ring;
        private long made;

        public MarkerRing(int capacity)
        {
            ring = new Marker[Math.Max(1, capacity)];
        }

        public int Capacity => ring.Length;

        /// <summary>How many markers were ever made.</summary>
        public long Made { get { lock (ring) return made; } }

        public void Add(string name, string detail, long number, float milliseconds, bool isChange) =>
            Add(name, detail, number, milliseconds, isChange, Stopwatch.GetTimestamp());

        public void Add(string name, string detail, long number, float milliseconds, bool isChange, long timestamp)
        {
            // Nearly every marker is made on the main thread; one made elsewhere must not tear the ring.
            lock (ring)
            {
                ref Marker marker = ref ring[made % ring.Length];
                marker.Timestamp = timestamp;
                marker.Milliseconds = milliseconds;
                marker.Name = name;
                marker.Detail = detail;
                marker.Number = number;
                marker.IsChange = isChange;
                made++;
            }
        }

        /// <summary>Marker number <paramref name="index"/>. False for one not made yet, or one the ring has written over.</summary>
        public bool TryGet(long index, out Marker marker)
        {
            lock (ring)
            {
                if (index < 0 || index >= made || index < made - ring.Length)
                {
                    marker = default;
                    return false;
                }
                marker = ring[index % ring.Length];
                return true;
            }
        }

        /// <summary>The number of the oldest marker still held.</summary>
        public long Oldest { get { lock (ring) return Math.Max(0, made - ring.Length); } }

        public void Clear()
        {
            lock (ring)
            {
                Array.Clear(ring, 0, ring.Length);
                made = 0;
            }
        }
    }
}
