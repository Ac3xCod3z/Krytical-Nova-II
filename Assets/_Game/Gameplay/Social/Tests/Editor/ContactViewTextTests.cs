using KryticalNova.Social;
using NUnit.Framework;
using Unity.Collections;

namespace KryticalNova.Tests
{
    /// <summary>M8 8.4.2: the Contacts window as text (the entity netcode's pages) comes back as NGO's ViewRpc sent it.</summary>
    public sealed class ContactViewTextTests
    {
        static ContactView Line(ContactKind kind, string id, string name, string note, bool online, bool favourite)
        {
            var line = new ContactView { kind = kind, online = online, level = 42, faction = 2, weaponClass = 5, favourite = favourite, lastSeenUtc = 638_000_000_000_000_000 };
            line.id.CopyFromTruncated(id);
            line.name.CopyFromTruncated(name);
            line.note.CopyFromTruncated(note);
            return line;
        }

        [Test]
        public void EveryKindAndField_ComesBack_EvenATabOrABackslashInANote()
        {
            var view = new[]
            {
                Line(ContactKind.Contact, "c1", "Talwyn", "met\tat the docks \\ west", true, true),
                Line(ContactKind.Incoming, "c2", "Brannoc", "", false, false),
                Line(ContactKind.Outgoing, "c3", "Corvane", "€ café", false, true),
                Line(ContactKind.Blocked, "c4", "Dravik", "", false, false),
            };
            Assert.IsTrue(ContactViewText.TryDecode(ContactViewText.Encode(view), out ContactView[] back));
            Assert.AreEqual(view.Length, back.Length);
            for (int i = 0; i < view.Length; i++)
            {
                Assert.AreEqual(view[i].kind, back[i].kind);
                Assert.AreEqual(view[i].id, back[i].id);
                Assert.AreEqual(view[i].name, back[i].name);
                Assert.AreEqual(view[i].note, back[i].note);
                Assert.AreEqual(view[i].online, back[i].online);
                Assert.AreEqual(view[i].favourite, back[i].favourite);
                Assert.AreEqual(view[i].level, back[i].level);
                Assert.AreEqual(view[i].faction, back[i].faction);
                Assert.AreEqual(view[i].weaponClass, back[i].weaponClass);
                Assert.AreEqual(view[i].lastSeenUtc, back[i].lastSeenUtc);
            }
        }

        [Test]
        public void AnEmptyList_IsEmpty_AndBrokenTextIsRefused()
        {
            Assert.IsTrue(ContactViewText.TryDecode(ContactViewText.Encode(new ContactView[0]), out ContactView[] none));
            Assert.AreEqual(0, none.Length);
            Assert.IsFalse(ContactViewText.TryDecode("0\tc1\tTalwyn", out _), "too few fields");
            Assert.IsFalse(ContactViewText.TryDecode("9\tc1\tTalwyn\t1\t1\t1\t1\t1\t0\t", out _), "no such kind");
        }
    }
}
