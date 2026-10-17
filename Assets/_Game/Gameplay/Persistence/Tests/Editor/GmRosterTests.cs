using System.Collections.Generic;
using System.IO;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Game Master list: three at most, each bound to a proven account on the control page (online phase 2, owner
    /// 18 Oct 2026: no password in the game); a bound account is GM on joining; unbinding or removal ends GM.
    /// </summary>
    public sealed class GmRosterTests
    {
        private const string AshAccount = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string BoAccount = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private static GmEntry Ash() => new GmEntry { name = "Ash", account = AshAccount };
        private static GmEntry Bo() => new GmEntry { name = "Bo" };

        [SetUp]
        public void Clear()
        {
            GmRoster.SetAll(new GmEntry[0]);
            for (ulong id = 0; id < 10; id++)
            {
                GmRoster.Forget(id);
                ServerAccounts.Forget(id);
            }
        }

        [Test]
        public void ABoundAccountIsGmWhenItJoins()
        {
            GmRoster.SetAll(new[] { Ash(), Bo() });
            ServerAccounts.Remember(4, AshAccount, "");
            Assert.IsTrue(GmRoster.SignInBound(4, out string name));
            Assert.AreEqual("Ash", name);
            Assert.IsTrue(GmRoster.IsGm(4));
            Assert.AreEqual("Ash", GmRoster.NameOf(4));
            Assert.IsTrue(GmRoster.IsSignedIn("ash"));
        }

        [Test]
        public void AnyOtherAccountIsNot()
        {
            GmRoster.SetAll(new[] { Ash(), Bo() });
            ServerAccounts.Remember(5, BoAccount, "");
            Assert.IsFalse(GmRoster.SignInBound(5, out _), "Bo is not bound yet");
            Assert.IsFalse(GmRoster.SignInBound(6, out _), "a connection with no proven account");
            Assert.IsFalse(GmRoster.IsGm(5));
            Assert.IsNull(GmRoster.FindByAccount(""));
        }

        [Test]
        public void UnbindingOrRemovingAGmEndsTheirRightsAndSaysSo()
        {
            GmRoster.SetAll(new[] { Ash(), Bo() });
            ServerAccounts.Remember(7, AshAccount, "");
            GmRoster.SignInBound(7, out _);
            var revoked = new List<ulong>();
            GmRoster.Revoked += revoked.Add;
            try
            {
                GmRoster.SetAll(new[] { new GmEntry { name = "Ash" }, Bo() }); // unbound on the page
            }
            finally
            {
                GmRoster.Revoked -= revoked.Add;
            }
            Assert.IsFalse(GmRoster.IsGm(7));
            CollectionAssert.AreEqual(new ulong[] { 7 }, revoked);

            GmRoster.SetAll(new[] { Ash() });
            GmRoster.SignInBound(7, out _);
            GmRoster.SetAll(new GmEntry[0]); // removed
            Assert.IsFalse(GmRoster.IsGm(7));
        }

        [Test]
        public void ANewGmNeedsANameAndAFreePlace()
        {
            var two = new[] { Ash(), Bo() };
            Assert.IsNull(GmRoster.ValidateNew(two, "Cy"));
            StringAssert.Contains("name", GmRoster.ValidateNew(two, "  "));
            StringAssert.Contains("already a GM", GmRoster.ValidateNew(two, "ash"));
            StringAssert.Contains("letters", GmRoster.ValidateNew(two, "Cy<b>"));
            StringAssert.Contains("remove one", GmRoster.ValidateNew(new[] { Ash(), Bo(), new GmEntry { name = "Cy" } }, "Di"));
        }

        [Test]
        public void TheListHoldsThreeAtMost()
        {
            GmRoster.SetAll(new[] { Ash(), Bo(), new GmEntry { name = "Cy" }, new GmEntry { name = "Di" } });
            Assert.AreEqual(GmRoster.MaxGameMasters, GmRoster.All.Count);
        }

        [Test]
        public void SavingTheGmsKeepsTheirAccountsAndChangesOnlyThatList()
        {
            string folder = Path.Combine(Path.GetTempPath(), "kn-gm-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                ServerLaunchOptions options = ServerLaunchOptions.Load(folder, new[] { "-port", "7790" }, out _);
                Assert.IsTrue(options.SaveGameMasters(new[] { Ash() }, out string error), error);
                ServerLaunchOptions again = ServerLaunchOptions.Load(folder, new string[0], out _);
                Assert.AreEqual(7777, again.port, "a command-line port of one run is not saved");
                Assert.AreEqual(1, again.gameMasters.Length);
                Assert.AreEqual("Ash", again.gameMasters[0].name);
                Assert.AreEqual(AshAccount, again.gameMasters[0].account);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
