using System.Collections.Generic;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// The slots of a Hostile Zone that wait to be filled, soonest first (a binary min-heap on the time). A zone
    /// only ever looks at the top entry, so what a check costs does not grow with the number of dead Hostiles.
    /// </summary>
    public sealed class RespawnQueue
    {
        private readonly List<(float Time, int Slot)> heap = new List<(float, int)>();

        public int Count => heap.Count;

        /// <summary>When the soonest slot is due. Only valid while <see cref="Count"/> is above zero.</summary>
        public float PeekTime => heap[0].Time;

        public void Clear() => heap.Clear();

        public void Push(float time, int slot)
        {
            heap.Add((time, slot));
            int child = heap.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (heap[parent].Time <= heap[child].Time) break;
                (heap[parent], heap[child]) = (heap[child], heap[parent]);
                child = parent;
            }
        }

        /// <summary>Takes the soonest slot out and returns it.</summary>
        public int Pop()
        {
            int slot = heap[0].Slot;
            RemoveAt(0);
            return slot;
        }

        /// <summary>When a slot is due, if it is waiting. For tools: it looks at every entry.</summary>
        public bool TryGetTime(int slot, out float time)
        {
            for (int i = 0; i < heap.Count; i++)
                if (heap[i].Slot == slot) { time = heap[i].Time; return true; }
            time = 0f;
            return false;
        }

        /// <summary>Takes a waiting slot out, wherever it is. For tools: it looks at every entry.</summary>
        public bool Remove(int slot)
        {
            for (int i = 0; i < heap.Count; i++)
                if (heap[i].Slot == slot) { RemoveAt(i); return true; }
            return false;
        }

        private void RemoveAt(int index)
        {
            int last = heap.Count - 1;
            heap[index] = heap[last];
            heap.RemoveAt(last);
            if (index >= heap.Count) return;
            // The entry moved in may belong higher or lower than the hole it filled.
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (heap[parent].Time <= heap[index].Time) break;
                (heap[parent], heap[index]) = (heap[index], heap[parent]);
                index = parent;
            }
            while (true)
            {
                int left = index * 2 + 1, right = left + 1, smallest = index;
                if (left < heap.Count && heap[left].Time < heap[smallest].Time) smallest = left;
                if (right < heap.Count && heap[right].Time < heap[smallest].Time) smallest = right;
                if (smallest == index) break;
                (heap[smallest], heap[index]) = (heap[index], heap[smallest]);
                index = smallest;
            }
        }
    }
}
