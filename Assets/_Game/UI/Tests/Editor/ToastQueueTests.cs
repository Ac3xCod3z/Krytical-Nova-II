using KryticalNova.Core;
using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Toasts come up one at a time, in order, and never two at once.</summary>
    public sealed class ToastQueueTests
    {
        private const float Show = 5f, Fade = 0.35f;

        private static Toast Of(string text, AnnouncementKind kind = AnnouncementKind.Gm) => new Toast { Kind = kind, Sender = "GM", Text = text };

        [Test]
        public void AToastComesUp_StaysItsTime_AndGoes()
        {
            var queue = new ToastQueue();
            Assert.IsFalse(queue.Advance(0f, Show, Fade), "nothing to show");
            queue.Enqueue(Of("one"), 8);
            Assert.IsTrue(queue.Advance(1f, Show, Fade));
            Assert.IsTrue(queue.Showing);
            Assert.AreEqual("one", queue.Current.Text);
            Assert.IsFalse(queue.Advance(5.9f, Show, Fade), "still its time");
            Assert.IsTrue(queue.Showing);
            Assert.IsTrue(queue.Advance(6f, Show, Fade));
            Assert.IsFalse(queue.Showing);
        }

        [Test]
        public void TwoCloseTogetherAreShownOneAfterTheOther_NeverAtOnce()
        {
            var queue = new ToastQueue();
            queue.Enqueue(Of("gm message"), 8);
            queue.Enqueue(Of("temper", AnnouncementKind.Temper), 8);
            Assert.IsTrue(queue.Advance(0f, Show, Fade));
            Assert.AreEqual("gm message", queue.Current.Text);
            Assert.AreEqual(1, queue.Waiting);

            Assert.IsFalse(queue.Advance(3f, Show, Fade), "the second waits");
            Assert.AreEqual("gm message", queue.Current.Text);

            Assert.IsTrue(queue.Advance(5f, Show, Fade), "the first goes");
            Assert.IsFalse(queue.Showing);
            Assert.IsFalse(queue.Advance(5.2f, Show, Fade), "and has to have faded before the next comes up");
            Assert.IsTrue(queue.Advance(5.35f, Show, Fade));
            Assert.IsTrue(queue.Showing);
            Assert.AreEqual("temper", queue.Current.Text);
            Assert.AreEqual(AnnouncementKind.Temper, queue.Current.Kind);
            Assert.AreEqual(0, queue.Waiting);
        }

        [Test]
        public void TheyComeInTheOrderTheyArrived()
        {
            var queue = new ToastQueue();
            for (int i = 1; i <= 4; i++) queue.Enqueue(Of(i.ToString()), 8);
            float now = 0f;
            for (int i = 1; i <= 4; i++)
            {
                Assert.IsTrue(queue.Advance(now, Show, Fade));
                Assert.AreEqual(i.ToString(), queue.Current.Text);
                now += Show;
                Assert.IsTrue(queue.Advance(now, Show, Fade));
                now += Fade;
            }
            Assert.IsFalse(queue.Advance(now, Show, Fade));
        }

        [Test]
        public void WhenTooManyWait_TheOldestWaitingGivesWay_NotTheOneOnScreen()
        {
            var queue = new ToastQueue();
            queue.Enqueue(Of("on screen"), 2);
            queue.Advance(0f, Show, Fade);
            Assert.IsTrue(queue.Enqueue(Of("a"), 2));
            Assert.IsTrue(queue.Enqueue(Of("b"), 2));
            Assert.IsFalse(queue.Enqueue(Of("c"), 2), "a is dropped");
            Assert.AreEqual(2, queue.Waiting);
            Assert.AreEqual("on screen", queue.Current.Text);

            queue.Advance(Show, Show, Fade);
            queue.Advance(Show + Fade, Show, Fade);
            Assert.AreEqual("b", queue.Current.Text);
        }
    }
}
