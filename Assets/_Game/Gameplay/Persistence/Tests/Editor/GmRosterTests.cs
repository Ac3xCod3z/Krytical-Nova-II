using System.Collections.Generic;
using System.IO;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The Game Master list (16 Oct 2026): three at most, a password each, five wrong tries, removal ends GM.</summary>
    public sealed class GmRosterTests
    {
        private static readonly GmEntry Ash = new GmEntry { name = "Ash", password = "ember-lantern-7" };
        private static readonly GmEntry Bo = new GmEntry { name = "Bo", password = "tide-river-42" };

        [SetUp]
        public void Clear()
        {
            GmRoster.SetAll(new GmEntry[0]);
            for (ulong id = 0; id < 10; id++) GmRoster.Forget(id);
        }

        [Test]
        public void TheRightPasswordMakesThatPlayerGmByName()
        {
            GmRoster.SetAll(new[] { Ash, Bo });
            Assert.IsTrue(GmRoster.TryLogin(4, "tide-river-42", out string name, out _, out bool kick));
            Assert.AreEqual("Bo", name);
            Assert.IsFalse(kick);
            Assert.IsTrue(GmRoster.IsGm(4));
            Assert.AreEqual("Bo", GmRoster.NameOf(4));
            Assert.IsTrue(GmRoster.IsSignedIn("bo"));
            Assert.IsFalse(GmRoster.IsGm(5));
        }

        [Test]
        public void FiveWrongPasswordsSendThePlayerAway()
        {
            GmRoster.SetAll(new[] { Ash });
            for (int i = 1; i < GmRoster.WrongTriesBeforeKick; i++)
            {
                Assert.IsFalse(GmRoster.TryLogin(3, "guess" + i, out _, out string message, out bool kick));
                Assert.IsFalse(kick, message);
                StringAssert.Contains($"{GmRoster.WrongTriesBeforeKick - i} tr", message);
            }
            Assert.IsFalse(GmRoster.TryLogin(3, "last guess", out _, out _, out bool sentAway));
            Assert.IsTrue(sentAway);
            Assert.IsFalse(GmRoster.IsGm(3));
        }

        [Test]
        public void ARightPasswordClearsTheWrongTries()
        {
            GmRoster.SetAll(new[] { Ash });
            GmRoster.TryLogin(2, "nope", out _, out _, out _);
            Assert.IsTrue(GmRoster.TryLogin(2, "ember-lantern-7", out _, out _, out _));
            for (int i = 1; i < GmRoster.WrongTriesBeforeKick; i++)
            {
                GmRoster.TryLogin(2, "nope", out _, out _, out bool kick);
                Assert.IsFalse(kick, "the earlier wrong try still counted after a right one");
            }
        }

        [Test]
        public void NoPasswordIsNeverAMatch()
        {
            var empty = new GmEntry { name = "Blank", password = "" };
            Assert.IsNull(GmRoster.Match(new[] { empty }, ""));
            Assert.IsNull(GmRoster.Match(new[] { Ash }, null));
            Assert.IsNull(GmRoster.Match(new[] { Ash }, "ember-lantern"));
            Assert.AreEqual("Ash", GmRoster.Match(new[] { Bo, Ash }, "ember-lantern-7"));
        }

        [Test]
        public void RemovingASignedInGmEndsTheirRightsAndSaysSo()
        {
            GmRoster.SetAll(new[] { Ash, Bo });
            GmRoster.TryLogin(7, "ember-lantern-7", out _, out _, out _);
            var revoked = new List<ulong>();
            GmRoster.Revoked += revoked.Add;
            try
            {
                GmRoster.SetAll(new[] { Bo });
            }
            finally
            {
                GmRoster.Revoked -= revoked.Add;
            }
            Assert.IsFalse(GmRoster.IsGm(7));
            CollectionAssert.AreEqual(new ulong[] { 7 }, revoked);
        }

        [Test]
        public void ANewGmNeedsANameAPasswordOfTheirOwnAndAFreePlace()
        {
            var two = new[] { Ash, Bo };
            Assert.IsNull(GmRoster.ValidateNew(two, "Cy", "storm-crown-9"));
            StringAssert.Contains("name", GmRoster.ValidateNew(two, "  ", "storm-crown-9"));
            StringAssert.Contains("already a GM", GmRoster.ValidateNew(two, "ash", "storm-crown-9"));
            StringAssert.Contains("own", GmRoster.ValidateNew(two, "Cy", "tide-river-42"));
            StringAssert.Contains("8", GmRoster.ValidateNew(two, "Cy", "short"));
            StringAssert.Contains("letters", GmRoster.ValidateNew(two, "Cy<b>", "storm-crown-9"));
            StringAssert.Contains("remove one", GmRoster.ValidateNew(new[] { Ash, Bo, new GmEntry { name = "Cy", password = "storm-crown-9" } }, "Di", "night-shade-3"));
        }

        [Test]
        public void TheListHoldsThreeAtMost()
        {
            GmRoster.SetAll(new[] { Ash, Bo, new GmEntry { name = "Cy", password = "a1234567" }, new GmEntry { name = "Di", password = "b1234567" } });
            Assert.AreEqual(GmRoster.MaxGameMasters, GmRoster.All.Count);
        }

        [Test]
        public void SavingTheGmsChangesOnlyThatListInTheFile()
        {
            string folder = Path.Combine(Path.GetTempPath(), "kn-gm-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                ServerLaunchOptions options = ServerLaunchOptions.Load(folder, new[] { "-port", "7790" }, out _);
                Assert.IsTrue(options.SaveGameMasters(new[] { Ash }, out string error), error);
                ServerLaunchOptions again = ServerLaunchOptions.Load(folder, new string[0], out _);
                Assert.AreEqual(7777, again.port, "a command-line port of one run is not saved");
                Assert.AreEqual(1, again.gameMasters.Length);
                Assert.AreEqual("Ash", again.gameMasters[0].name);
                Assert.AreEqual("ember-lantern-7", again.gameMasters[0].password);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
