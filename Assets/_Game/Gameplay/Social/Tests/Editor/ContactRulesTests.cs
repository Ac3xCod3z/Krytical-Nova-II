using System;
using KryticalNova.Social;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class ContactRulesTests
    {
        private const long Now = 1000;
        private ContactBook tolan, vex, kael;

        [SetUp]
        public void SetUp()
        {
            tolan = new ContactBook { characterId = "a", name = "Tolan", faction = 1 };
            vex = new ContactBook { characterId = "b", name = "Vex", faction = 1 };
            kael = new ContactBook { characterId = "c", name = "Kael", faction = 2 };
        }

        private bool Request(ContactBook from, ContactBook to, out bool became) =>
            ContactRules.TryRequest(from, to, Now, false, out _, out became);

        [Test]
        public void ARequest_WaitsOnBothSides_UntilItIsAccepted()
        {
            Assert.IsTrue(Request(tolan, vex, out bool became));
            Assert.IsFalse(became);
            Assert.IsNotNull(ContactRules.Find(tolan.outgoing, "b"));
            Assert.IsNotNull(ContactRules.Find(vex.incoming, "a"));
            Assert.IsFalse(ContactRules.AreContacts(tolan, vex));

            Assert.IsTrue(ContactRules.TryAccept(vex, tolan, Now, out _));
            Assert.IsTrue(ContactRules.AreContacts(tolan, vex));
            Assert.IsTrue(ContactRules.AreContacts(vex, tolan), "contacts are mutual");
            Assert.AreEqual(0, tolan.outgoing.Count);
            Assert.AreEqual(0, vex.incoming.Count);
        }

        [Test]
        public void AskingSomeoneWhoAlreadyAsked_MakesContactsAtOnce()
        {
            Request(tolan, vex, out _);
            Assert.IsTrue(Request(vex, tolan, out bool became));
            Assert.IsTrue(became);
            Assert.IsTrue(ContactRules.AreContacts(tolan, vex));
            Assert.AreEqual(0, tolan.outgoing.Count + tolan.incoming.Count + vex.outgoing.Count + vex.incoming.Count);
        }

        [Test]
        public void ARequest_IsNotSentTwice_NorToAContact_NorToYourself()
        {
            Request(tolan, vex, out _);
            Assert.IsNotNull(ContactRules.RequestBlockedReason(tolan, vex, false));
            ContactRules.TryAccept(vex, tolan, Now, out _);
            Assert.IsNotNull(ContactRules.RequestBlockedReason(tolan, vex, false));
            Assert.AreEqual(ContactRules.ReasonSelf, ContactRules.RequestBlockedReason(tolan, tolan, false));
        }

        [Test]
        public void AnotherFaction_IsRefused_UnlessAllowed()
        {
            Assert.AreEqual(ContactRules.ReasonOtherFaction, ContactRules.RequestBlockedReason(tolan, kael, false));
            Assert.IsNull(ContactRules.RequestBlockedReason(tolan, kael, true));
        }

        [Test]
        public void Declining_OrTakingARequestBack_LeavesNothingBehind()
        {
            Request(tolan, vex, out _);
            Assert.IsTrue(ContactRules.DropRequests(vex, tolan));
            Assert.AreEqual(0, tolan.outgoing.Count + vex.incoming.Count);
            Assert.IsFalse(ContactRules.TryAccept(vex, tolan, Now, out string reason));
            Assert.AreEqual(ContactRules.ReasonNoRequest, reason);
        }

        [Test]
        public void Removing_TakesBothOut()
        {
            Request(tolan, vex, out _);
            ContactRules.TryAccept(vex, tolan, Now, out _);
            Assert.IsTrue(ContactRules.RemoveContact(vex, tolan));
            Assert.IsFalse(ContactRules.AreContacts(tolan, vex));
            Assert.IsFalse(ContactRules.AreContacts(vex, tolan));
            Assert.IsFalse(ContactRules.RemoveContact(vex, tolan));
        }

        [Test]
        public void Blocking_EndsTheContact_AndStopsRequestsBothWays_WithoutSayingWhy()
        {
            Request(tolan, vex, out _);
            ContactRules.TryAccept(vex, tolan, Now, out _);
            Assert.IsTrue(ContactRules.TryBlock(vex, tolan, Now, out _));
            Assert.IsFalse(ContactRules.AreContacts(tolan, vex));
            Assert.IsTrue(ContactRules.Blocks(tolan, vex));
            Assert.IsTrue(ContactRules.Blocks(vex, tolan), "a block cuts both directions");
            Assert.AreEqual(ContactRules.ReasonUnavailable, ContactRules.RequestBlockedReason(tolan, vex, false), "the blocked side is not told it was blocked");
            StringAssert.Contains("Unblock", ContactRules.RequestBlockedReason(vex, tolan, false));

            Assert.IsTrue(ContactRules.Unblock(vex, "a"));
            Assert.IsFalse(ContactRules.Blocks(tolan, vex));
            Assert.IsNull(ContactRules.RequestBlockedReason(tolan, vex, false));
        }

        [Test]
        public void Blocking_DropsAPendingRequest()
        {
            Request(tolan, vex, out _);
            ContactRules.TryBlock(vex, tolan, Now, out _);
            Assert.AreEqual(0, tolan.outgoing.Count + vex.incoming.Count);
        }

        [Test]
        public void AFullList_TakesNoMore()
        {
            for (int i = 0; i < ContactRules.MaxContacts; i++) tolan.contacts.Add(new ContactEntry { characterId = "x" + i });
            Assert.AreEqual(ContactRules.ReasonListFull, ContactRules.RequestBlockedReason(tolan, vex, false));
            Assert.AreEqual(ContactRules.ReasonTheirListFull, ContactRules.RequestBlockedReason(vex, tolan, false));
        }

        [Test]
        public void FavouritesAndNotes_BelongToOneSideOnly()
        {
            Request(tolan, vex, out _);
            ContactRules.TryAccept(vex, tolan, Now, out _);
            Assert.IsTrue(ContactRules.SetFavourite(tolan, "b", true));
            Assert.IsTrue(ContactRules.SetNote(tolan, "b", "  sells <b>stims</b>\nat the plaza  "));
            Assert.IsTrue(ContactRules.Find(tolan.contacts, "b").favourite);
            Assert.IsFalse(ContactRules.Find(tolan.contacts, "b").note.Contains("<"));
            Assert.IsFalse(ContactRules.Find(tolan.contacts, "b").note.Contains("\n"));
            Assert.IsFalse(ContactRules.Find(vex.contacts, "a").favourite);
            Assert.AreEqual(string.Empty, ContactRules.Find(vex.contacts, "a").note);
            Assert.AreEqual(ContactRules.MaxNoteLength, ContactRules.CleanNote(new string('n', 200)).Length);
        }

        [Test]
        public void LastSeen_ReadsAsTimeAgo()
        {
            long now = TimeSpan.FromDays(10).Ticks;
            Assert.AreEqual("Online", ContactRules.LastSeen(true, now, 0));
            Assert.AreEqual("Just now", ContactRules.LastSeen(false, now, now - TimeSpan.FromSeconds(20).Ticks));
            Assert.AreEqual("5m ago", ContactRules.LastSeen(false, now, now - TimeSpan.FromMinutes(5.5).Ticks));
            Assert.AreEqual("3h ago", ContactRules.LastSeen(false, now, now - TimeSpan.FromHours(3.2).Ticks));
            Assert.AreEqual("2d ago", ContactRules.LastSeen(false, now, now - TimeSpan.FromDays(2.5).Ticks));
            Assert.AreEqual("Offline", ContactRules.LastSeen(false, now, 0));
        }
    }
}
