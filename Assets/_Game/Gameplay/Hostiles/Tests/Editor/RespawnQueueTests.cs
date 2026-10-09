using System.Collections.Generic;
using NUnit.Framework;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>The queue a Hostile Zone keeps its empty slots in: soonest first, whatever order they died in.</summary>
    public sealed class RespawnQueueTests
    {
        [Test]
        public void SlotsComeOutSoonestFirst()
        {
            var queue = new RespawnQueue();
            float[] times = { 9f, 2f, 7f, 2.5f, 30f, 0f, 11f, 4f };
            for (int slot = 0; slot < times.Length; slot++) queue.Push(times[slot], slot);

            var order = new List<float>();
            while (queue.Count > 0)
            {
                float due = queue.PeekTime;
                Assert.AreEqual(due, times[queue.Pop()]);
                order.Add(due);
            }
            Assert.That(order, Is.Ordered);
            Assert.AreEqual(times.Length, order.Count);
        }

        [Test]
        public void AWaitingSlotCanBeFoundAndTakenOut()
        {
            var queue = new RespawnQueue();
            for (int slot = 0; slot < 10; slot++) queue.Push(100f - slot * 3f, slot);

            Assert.IsTrue(queue.TryGetTime(4, out float time));
            Assert.AreEqual(88f, time);
            Assert.IsTrue(queue.Remove(4));
            Assert.IsFalse(queue.Remove(4));
            Assert.IsFalse(queue.TryGetTime(4, out _));

            float previous = float.MinValue;
            int left = 0;
            while (queue.Count > 0)
            {
                Assert.GreaterOrEqual(queue.PeekTime, previous);
                previous = queue.PeekTime;
                Assert.AreNotEqual(4, queue.Pop());
                left++;
            }
            Assert.AreEqual(9, left);
        }

        [Test]
        public void ClearEmptiesIt()
        {
            var queue = new RespawnQueue();
            queue.Push(1f, 0);
            queue.Push(2f, 1);
            queue.Clear();
            Assert.AreEqual(0, queue.Count);
        }
    }
}
