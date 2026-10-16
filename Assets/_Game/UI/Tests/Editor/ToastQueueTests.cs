using KryticalNova.Core;
using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Toasts come up one at a time, in order, and never two at once.</summary>
    public sealed class ToastQueueTests
    {
        [Test]
        public void TheTickerRunsTheLineAcrossTheBandAsManyTimesAsAsked()
        {
            // A 1000 px band and a 500 px line at 150 px/s: one pass is 10 s, three passes 30 s.
            Assert.AreEqual(10f, TickerRules.PassSeconds(1000f, 500f, 150f), 0.001f);
            Assert.AreEqual(30f, TickerRules.ShowSeconds(1000f, 500f, 150f, 3), 0.001f);
            Assert.AreEqual(1000f, TickerRules.Left(0f, 1000f, 500f, 150f), 0.001f, "starts at the right edge");
            Assert.AreEqual(250f, TickerRules.Left(5f, 1000f, 500f, 150f), 0.001f, "half way through a pass");
            Assert.AreEqual(1000f, TickerRules.Left(10f, 1000f, 500f, 150f), 0.001f, "comes round for the next pass");
            Assert.AreEqual(-500f, TickerRules.Left(9.99999f, 1000f, 500f, 150f), 0.01f, "wholly past the left edge at the end of a pass");
        }

        [Test]
        public void ARarePieceTemperedToTheTopIsOrange_AndAnEpicOneRed()
        {
            const string text = "Arinis tempered Sunlord Blade to 120%.";
            var rare = new Toast { Kind = AnnouncementKind.Temper, Text = text, Highlight = "Arinis|Sunlord Blade", Rarity = 2 };
            string line = ToastHudController.Line(rare);
            StringAssert.StartsWith("<color=#FF851F>TEMPERING</color>", line);
            StringAssert.Contains("Arinis <color=#FF851F>tempered</color> <color=#FF851F>Sunlord Blade</color> to 120%.", line);

            var epic = new Toast { Kind = AnnouncementKind.Temper, Text = text, Highlight = "Arinis|Sunlord Blade", Rarity = 3 };
            line = ToastHudController.Line(epic);
            StringAssert.StartsWith("<color=#FF4A4A>TEMPERING</color>", line);
            StringAssert.Contains("Arinis <color=#FF4A4A>tempered</color> <color=#FF4A4A>Sunlord Blade</color> to 120%.", line);
        }

        [Test]
        public void AnEpicFindIsTaggedAndItsNameIsPurple_ThePlayerIsLeftPlain()
        {
            var find = new Toast { Kind = AnnouncementKind.Drop, Text = "Arinis found Sunlord Blade.", Highlight = "Arinis|Sunlord Blade", Rarity = 3 };
            string line = ToastHudController.Line(find);
            StringAssert.StartsWith("<color=#C880FF>EPIC FIND</color>", line);
            StringAssert.Contains("Arinis found <color=#C880FF>Sunlord Blade</color>.", line);
            var legend = new Toast { Kind = AnnouncementKind.Drop, Text = "Arinis found Dawn Edge.", Highlight = "Arinis|Dawn Edge", Rarity = 4 };
            StringAssert.StartsWith("<color=#FF9640>LEGENDARY FIND</color>", ToastHudController.Line(legend));
        }

        [Test]
        public void ALineWithNothingToLightIsJustTagged_AndAGmLineLightsEveryName()
        {
            var gm = new Toast { Kind = AnnouncementKind.Gm, Sender = "GM", Text = "Server restart in 10 minutes." };
            StringAssert.StartsWith("<color=#FFB83C>GM</color>", ToastHudController.Line(gm));
            var named = new Toast { Kind = AnnouncementKind.Gm, Sender = "Tolan", Text = "Tolan: hello Arinis", Highlight = "Tolan|Arinis" };
            StringAssert.Contains("<color=#FFB83C>Tolan</color>: hello <color=#FFB83C>Arinis</color>", ToastHudController.Line(named));
        }

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
