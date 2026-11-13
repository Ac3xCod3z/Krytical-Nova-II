using System.Collections.Generic;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// Wave 4 relevancy, the one thing a client was told only once about another player (the lead's Change 1): the Recall aura. The
    /// server remembers who channels and until when, so a connection that gets the ghost later is told the seconds left; the client
    /// holds a "channel begun" whose body is not there yet until the body is.
    /// </summary>
    public class RecallRelevancyTests
    {
        [Test]
        public void TheServer_KnowsTheSecondsLeft_UntilTheChannelEnds()
        {
            var channels = new RecallChannels();
            Assert.IsFalse(channels.TryLeft(4, 10.0, out _), "nobody channels");

            channels.Begin(4, now: 10.0, seconds: 8f);
            Assert.IsTrue(channels.TryLeft(4, 13.0, out float left));
            Assert.AreEqual(5f, left, 0.001f, "a client that gets the ghost 3 s in is shown 5 s of aura");
            Assert.IsFalse(channels.TryLeft(5, 13.0, out _), "another player does not channel");

            channels.End(4);   // broken off, or arrived
            Assert.IsFalse(channels.TryLeft(4, 13.5, out _));
            Assert.AreEqual(0, channels.Count);
        }

        [Test]
        public void TheServer_ForgetsAChannelWhoseTimeIsOver()
        {
            var channels = new RecallChannels();
            channels.Begin(4, now: 10.0, seconds: 8f);
            Assert.IsFalse(channels.TryLeft(4, 18.0, out _), "the end tick itself is over");
            Assert.AreEqual(0, channels.Count, "so the server system goes back to doing nothing");
        }

        [Test]
        public void TheClient_HoldsABegunShow_UntilTheBodyIsThere_WithTheSecondsLeft()
        {
            var pending = new PendingRecallShows();
            var here = new HashSet<int>();
            var played = new List<(int id, float seconds)>();

            pending.Hold(4, now: 20.0, seconds: 5f);
            pending.Step(20.1, here.Contains, (id, s) => played.Add((id, s)));
            Assert.AreEqual(0, played.Count, "no body yet: it waits");
            Assert.AreEqual(1, pending.Count);

            here.Add(4);   // the ghost arrived and its body was made
            pending.Step(21.0, here.Contains, (id, s) => played.Add((id, s)));
            Assert.AreEqual(1, played.Count);
            Assert.AreEqual(4, played[0].id);
            Assert.AreEqual(4f, played[0].seconds, 0.001f);
            Assert.AreEqual(0, pending.Count, "played once");
        }

        [Test]
        public void TheClient_DropsAHeldShow_WhenTheChannelEndsOrItsTimeIsOver()
        {
            var pending = new PendingRecallShows();
            var played = new List<int>();

            pending.Hold(4, now: 20.0, seconds: 5f);
            pending.Drop(4);   // Ended or Arrived came for that player
            pending.Step(20.5, id => true, (id, s) => played.Add(id));
            Assert.AreEqual(0, played.Count);

            pending.Hold(6, now: 20.0, seconds: 5f);
            pending.Step(25.0, id => true, (id, s) => played.Add(id));
            Assert.AreEqual(0, played.Count, "the channel is over before the body came: no aura on nothing");
            Assert.AreEqual(0, pending.Count);

            pending.Hold(7, now: 30.0, seconds: 5f);
            pending.Hold(7, now: 31.0, seconds: 5f);   // told again: one entry, the newest
            Assert.AreEqual(1, pending.Count);
        }
    }
}
