using System.Reflection;
using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KryticalNova.Autopilot.Tests
{
    /// <summary>
    /// Autopilot on the entity netcode (wave 2, 12 Nov 2026): the server alone decides on/off, the time and the Pass; a view obeys; a save that cannot be
    /// read never resets what is there. Written without a Unity run: not yet run.
    /// </summary>
    public class AutopilotStateTests
    {
        GameObject m_Body;
        PlayerAutopilot m_Autopilot;
        AutopilotSettings m_Settings;

        [SetUp]
        public void Make()
        {
            m_Settings = ScriptableObject.CreateInstance<AutopilotSettings>();
            m_Body = new GameObject("autopilot test body");
            m_Autopilot = m_Body.AddComponent<PlayerAutopilot>();
            typeof(PlayerAutopilot).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(m_Autopilot, m_Settings);
        }

        [TearDown]
        public void Clear()
        {
            Object.DestroyImmediate(m_Body);
            Object.DestroyImmediate(m_Settings);
            PlayerAutopilot.Link = null;
        }

        void Role(NetRoleKind kind, bool local)
        {
            m_Body.AddComponent<BodyRoleTag>().Assign(kind, default, 5, local);
        }

        sealed class FakeLink : PlayerAutopilot.IOwnerLink
        {
            public int SetOns, Prefs;
            public bool LastOn;
            public void SetOn(bool value) { SetOns++; LastOn = value; }
            public void SendPreferences(string json) { Prefs++; }
        }

        // ------------------------------------------------------------------ the server decides

        [Test]
        public void TheServerBodyRefusesWithoutTime_ThenWithoutAZone_ThenStopsWhenTheTimeEnds()
        {
            Role(NetRoleKind.ServerBody, false);
            StringAssert.Contains("No Autopilot time", m_Autopilot.StartBlockedReason());
            m_Autopilot.ServerSetOn(true);
            Assert.IsFalse(m_Autopilot.IsOn, "no time: refused");
            m_Autopilot.ServerSetDays(3);
            Assert.IsTrue(m_Autopilot.HasTime);
            StringAssert.Contains("Hostile Zone", m_Autopilot.StartBlockedReason(), "default: only inside a Hostile Zone");
            m_Autopilot.ServerSetFree(true);
            m_Autopilot.ServerSetDays(0);
            Assert.IsFalse(m_Autopilot.HasTime);
        }

        [Test]
        public void AViewNeverDecides_ItOnlyShowsWhatTheServerSaid()
        {
            Role(NetRoleKind.ClientView, true);
            m_Autopilot.ServerSetDays(30);
            Assert.IsFalse(m_Autopilot.HasTime, "a view cannot give itself time");
            m_Autopilot.ServerSetFree(true);
            Assert.IsFalse(m_Autopilot.Free);
            m_Autopilot.ServerSetOn(true);
            Assert.IsFalse(m_Autopilot.IsOn, "a view cannot switch itself on");

            long expires = System.DateTime.UtcNow.AddDays(2).Ticks;
            m_Autopilot.ApplyServerState(true, new Vector3(1f, 2f, 3f), expires, new AutopilotPreferences { huntRadius = 12f }.ToJson());
            Assert.IsTrue(m_Autopilot.IsOn);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), m_Autopilot.Anchor);
            Assert.IsTrue(m_Autopilot.HasTime);
            Assert.AreEqual(12f, m_Autopilot.Preferences.huntRadius);

            // the server said off: whatever the view does next, it is off and a request goes to the server, not to itself
            m_Autopilot.ApplyServerState(false, Vector3.zero, expires, null);
            Assert.IsFalse(m_Autopilot.IsOn);
            m_Autopilot.ServerSetOn(true);
            Assert.IsFalse(m_Autopilot.IsOn, "steering on after the server said off gains nothing: the view cannot turn itself on");
        }

        [Test]
        public void TheLocalViewAsksTheServerThroughTheLink_AnotherPlayersViewDoesNot()
        {
            Role(NetRoleKind.ClientView, true);
            var link = new FakeLink();
            PlayerAutopilot.Link = link;
            m_Autopilot.RequestSetOn(true);
            Assert.AreEqual(1, link.SetOns);
            Assert.IsTrue(link.LastOn);
            Assert.IsFalse(m_Autopilot.IsOn, "asking is not switching on");
            m_Autopilot.SavePreferences(new AutopilotPreferences());
            Assert.AreEqual(1, link.Prefs);
        }

        [Test]
        public void ServerSetPreferences_TakesGoodTextWithinLimits_AndIgnoresBadText()
        {
            Role(NetRoleKind.ServerBody, false);
            Assert.IsTrue(m_Autopilot.ServerSetPreferences(new AutopilotPreferences { huntRadius = 999f, gatherCount = 99 }.ToJson()));
            Assert.AreEqual(40f, m_Autopilot.Preferences.huntRadius, "brought inside the limits of the settings");
            Assert.AreEqual(8, m_Autopilot.Preferences.gatherCount);
            Assert.IsFalse(m_Autopilot.ServerSetPreferences("{ not json"));
            Assert.IsFalse(m_Autopilot.ServerSetPreferences(null));
            Assert.IsFalse(m_Autopilot.ServerSetPreferences(new string('x', 5000)));
            Assert.AreEqual(40f, m_Autopilot.Preferences.huntRadius, "bad text changed nothing");
        }

        [Test]
        public void AnotherPlayersAutoTagIsKeptPerBody_ForTheNamecard()
        {
            var other = new GameObject("other player's view");
            try
            {
                other.AddComponent<BodyRoleTag>().Assign(NetRoleKind.ClientView, default, 9, false);
                Assert.IsFalse(PlayerAutopilot.RemoteAuto(other.GetComponent<BodyRoleTag>()));
                PlayerAutopilot.SetRemoteAuto(other, true);
                Assert.IsTrue(PlayerAutopilot.RemoteAuto(other.GetComponent<BodyRoleTag>()));
                PlayerAutopilot.SetRemoteAuto(other, false);
                Assert.IsFalse(PlayerAutopilot.RemoteAuto(other.GetComponent<BodyRoleTag>()));
                PlayerAutopilot.SetRemoteAuto(other, true);
                PlayerAutopilot.ClearRemoteAuto();
                Assert.IsFalse(PlayerAutopilot.RemoteAuto(other.GetComponent<BodyRoleTag>()));
            }
            finally { Object.DestroyImmediate(other); }
        }

        // ------------------------------------------------------------------ the Pass

        [Test]
        public void APassAddsItsDaysOnTheServerBody_AndTheTimeStaysUnderTheLimit()
        {
            Role(NetRoleKind.ServerBody, false);
            PassTier tier = m_Settings.autopilotTiers[0];
            Assert.AreEqual(ItemUseEffect.AutopilotTime, m_Autopilot.Effect);
            m_Autopilot.ServerSetDays(m_Settings.autopilotMaxDays - 1);
            var pass = new UsedItem(tier.passItemId, "pass");
            StringAssert.Contains("cannot hold more", m_Autopilot.UseBlockedReason(pass));
            m_Autopilot.ServerSetDays(0);
            Assert.IsNull(m_Autopilot.UseBlockedReason(pass));
            m_Autopilot.ServerUse(pass);
            Assert.IsTrue(m_Autopilot.HasTime);
            Assert.AreEqual("It does nothing here.", m_Autopilot.UseBlockedReason(new UsedItem(12345, "x")));
        }

        // ------------------------------------------------------------------ saves

        [Test]
        public void SaveRoundTrip_KeepsTheTimeAndTheChoices()
        {
            Role(NetRoleKind.ServerBody, false);
            m_Autopilot.ServerSetDays(5);
            Assert.IsTrue(m_Autopilot.ServerSetPreferences(new AutopilotPreferences { attackSlot = 2, takeCommon = true }.ToJson()));
            string text = m_Autopilot.CaptureSave();
            long saved = m_Autopilot.StateForOwner().expiresUtc;

            var other = new GameObject("second");
            try
            {
                var again = other.AddComponent<PlayerAutopilot>();
                typeof(PlayerAutopilot).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(again, m_Settings);
                again.RestoreSave(text);
                Assert.AreEqual(saved, again.ExpiresUtc);
                Assert.AreEqual(2, again.Preferences.attackSlot);
                Assert.IsTrue(again.Preferences.takeCommon);
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void ASaveThatCannotBeReadKeepsTheCurrentStateAndLogsOnce()
        {
            Role(NetRoleKind.ServerBody, false);
            m_Autopilot.ServerSetDays(7);
            Assert.IsTrue(m_Autopilot.ServerSetPreferences(new AutopilotPreferences { attackSlot = 3 }.ToJson()));
            long before = m_Autopilot.ExpiresUtc;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("could not be read"));
            m_Autopilot.RestoreSave("{ this is not json");
            m_Autopilot.RestoreSave("[1,2,3]");   // a second bad one: no second line
            Assert.AreEqual(before, m_Autopilot.ExpiresUtc, "never reset by a load it cannot read");
            Assert.AreEqual(3, m_Autopilot.Preferences.attackSlot);

            // a good part with bad choices keeps the current choices but takes the time
            m_Autopilot.RestoreSave("{\"expiresUtc\":" + (before + 1000) + ",\"preferences\":\"nonsense\"}");
            Assert.AreEqual(before + 1000, m_Autopilot.ExpiresUtc);
            Assert.AreEqual(3, m_Autopilot.Preferences.attackSlot);
        }

        [Test]
        public void TryParseSave_SaysNoToEmptyAndGarbage_YesToTheOldShape()
        {
            Assert.IsFalse(PlayerAutopilot.TryParseSave(null, out _, out _));
            Assert.IsFalse(PlayerAutopilot.TryParseSave("   ", out _, out _));
            Assert.IsFalse(PlayerAutopilot.TryParseSave("{ nope", out _, out _));
            Assert.IsTrue(PlayerAutopilot.TryParseSave("{\"expiresUtc\":42,\"preferences\":\"{}\"}", out long expires, out string prefs));
            Assert.AreEqual(42, expires);
            Assert.AreEqual("{}", prefs);
            Assert.IsFalse(AutopilotPreferences.TryFromJson("{ nope", m_Settings, out _));
            Assert.IsTrue(AutopilotPreferences.TryFromJson("{\"attackSlot\":4}", m_Settings, out AutopilotPreferences p));
            Assert.AreEqual(4, p.attackSlot);
        }
    }
}
