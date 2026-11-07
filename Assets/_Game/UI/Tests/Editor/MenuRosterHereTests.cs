using KryticalNova.CharacterCreation;
using KryticalNova.Networking;
using KryticalNova.UI.Menus;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A2 ("Play DOTS here (test saves)"): character select's "here" list comes from <see cref="CharacterSession.HereStore"/> only while it
    /// is set; unset (every other session) it is this PC's own characters, as before; a server's list never takes it.
    /// </summary>
    public sealed class MenuRosterHereTests
    {
        sealed class ListOnly : ICharacterStore
        {
            public CharacterRoster Load() => new CharacterRoster();
            public void Save(CharacterRoster roster) { }
            public void DeleteAll() { }
        }

        [Test]
        public void TheHereList_IsThePcsOwn_UnlessTheTestSavesStoreIsSet_AndAServersListNeverTakesIt()
        {
            string keyBefore = MenuRoster.Key;
            ICharacterStore storeBefore = CharacterSession.Store;
            ICharacterStore hereBefore = CharacterSession.HereStore;
            try
            {
                CharacterSession.HereStore = null;
                MenuRoster.Use("unit-test-server:1");
                MenuRoster.Use(ServerDirectory.HereKey);
                Assert.AreSame(MenuRoster.Local, CharacterSession.Store, "unset: this PC's own characters, as before");

                var test = new ListOnly();
                CharacterSession.HereStore = test;
                MenuRoster.Use("unit-test-server:1");
                Assert.AreNotSame(test, CharacterSession.Store, "a server's list never takes the test store");
                MenuRoster.Use(ServerDirectory.HereKey);
                Assert.AreSame(test, CharacterSession.Store, "set: the test account's characters");
            }
            finally
            {
                CharacterSession.HereStore = hereBefore;
                MenuRoster.Use(keyBefore);
                CharacterSession.Store = storeBefore;
            }
        }

        [Test]
        public void EnterInTestSaves_GoesToPlayHere_NeverTheLastRealServer()
        {
            var nova = new ServerEntry { Listing = new ServerListing { name = "Nova" }, Name = "Nova", Status = ServerEntry.State.Off };
            var here = new ServerEntry { Name = "Play here (no server)", Status = ServerEntry.State.Here };
            var entries = new[] { nova, here };
            string lastBefore = ServerDirectory.LastKeyChosen;

            Assert.AreSame(nova, CharacterSelectController.EnterTarget(false, entries, nova), "every other session: the server played last (NGO)");
            ServerEntry target = CharacterSelectController.EnterTarget(true, entries, nova);
            Assert.AreSame(here, target, "test saves: Play here, not Nova (owner's r9)");
            Assert.IsTrue(target.CanJoin, "Play here can always be entered: Enter goes in, the Servers window never opens");
            Assert.IsNull(CharacterSelectController.EnterTarget(true, new[] { nova }, nova), "no Play here entry: nothing, never the real server");
            Assert.AreEqual(lastBefore, ServerDirectory.LastKeyChosen, "the live front end's last server is never changed by it");
        }
    }
}
