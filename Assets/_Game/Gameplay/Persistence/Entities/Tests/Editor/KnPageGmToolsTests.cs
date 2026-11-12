using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// M7.4 the page's GM buttons on the entity netcode, NGO's words and rules (GmRosterEdits): add, bind to the account of a player picked by
    /// key, unbind, remove; the list is written first and the roster changes only when it was; an unproven account or a gone player is refused.
    /// </summary>
    public class KnPageGmToolsTests
    {
        readonly List<GmEntry[]> m_Saved = new List<GmEntry[]>();
        string m_SaveError;
        KnPageGmTools m_Tools;

        [SetUp]
        public void SetUp()
        {
            PlayerKeys.Reset();
            GmRoster.SetAll(Array.Empty<GmEntry>());
            GmRoster.SetHost(null);   // static, shared with GmLineTests: no host left from an earlier test
            m_Saved.Clear();
            m_SaveError = null;
            m_Tools = new KnPageGmTools { Save = list => { m_Saved.Add(list); return m_SaveError; } };
        }

        [TearDown]
        public void TearDown()
        {
            GmRoster.SetAll(Array.Empty<GmEntry>());
            GmRoster.SetHost(null);
            PlayerKeys.Reset();
        }

        [Test]
        public void AddBindUnbindRemove_WriteTheListFirst_AndBindTheAccountOfThePickedPlayer()
        {
            StringAssert.StartsWith("Ace is a GM.", m_Tools.GmAdd(" Ace "));
            Assert.AreEqual(1, m_Saved.Count, "written to the settings file");
            Assert.AreEqual("[{\"name\":\"Ace\",\"bound\":false,\"signedIn\":false}]", m_Tools.GmListJson());

            Assert.AreEqual("That player is no longer here.", m_Tools.GmBind("Ace", 999), "no such player key");
            PlayerKey key = PlayerKeys.Mint(7);
            Assert.AreEqual("That player's account is not proven (they joined without Unity sign-in).", m_Tools.GmBind("Ace", key.Value));
            ServerAccounts.Remember(key, "acct-ace", string.Empty);
            try
            {
                StringAssert.StartsWith("Ace is now connection 7's account", m_Tools.GmBind("Ace", key.Value));
                Assert.IsTrue(GmRoster.IsGm(7), "GM at once");
                Assert.AreEqual("[{\"name\":\"Ace\",\"bound\":true,\"signedIn\":true}]", m_Tools.GmListJson());

                StringAssert.StartsWith("Ace is bound to no one now", m_Tools.GmUnbind("Ace"));
                Assert.IsFalse(GmRoster.IsGm(7), "their GM rights ended");

                m_SaveError = "The settings file is read-only.";
                Assert.AreEqual("The settings file is read-only.", m_Tools.GmRemove("Ace"));
                Assert.IsNotNull(GmRoster.Find("Ace"), "the file was not written: the roster is unchanged");
                m_SaveError = null;
                StringAssert.StartsWith("Ace is no longer a GM.", m_Tools.GmRemove("Ace"));
                Assert.AreEqual("[]", m_Tools.GmListJson());
                Assert.AreEqual("There is no GM called Bea.", m_Tools.GmBind("Bea", key.Value));
            }
            finally
            {
                ServerAccounts.Forget(key);
            }
        }
    }
}
