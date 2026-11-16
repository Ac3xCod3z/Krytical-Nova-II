using System.Collections.Generic;
using KryticalNova.Factions;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Crossfire.Tests
{
    /// <summary>
    /// Owner, 16 Nov 2026: every player gets the Crossfire call for THEIR OWN bracket (10-19 ... 100-112), and only bracket X has a zone for
    /// now: its players go through the Transit Row port onto their own faction's Rally Point; the others are told plainly they have none yet.
    /// The shipped words and numbers (a fresh <see cref="CrossfireSettings"/> has the asset's defaults).
    /// </summary>
    public sealed class CrossfireBracketTests
    {
        CrossfireSettings m_Settings;
        readonly List<GameObject> m_Made = new List<GameObject>();
        static readonly CrossfireState Call = new CrossfireState { Known = true, MinLevel = 10, Now = new CrossfireNow { Phase = CrossfirePhase.Call, WarStart = 600 } };

        [SetUp]
        public void SetUp() => m_Settings = ScriptableObject.CreateInstance<CrossfireSettings>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
            foreach (CrossfireArea a in new List<CrossfireArea>(CrossfireArea.All)) CrossfireArea.Untrack(a);
            Object.DestroyImmediate(m_Settings);
        }

        string CallFor(int level) =>
            CrossfireCaller.Text(new CrossfireCaller.Line { Kind = CrossfireCaller.LineKind.Call, Seconds = 600 }, m_Settings, m_Settings.BracketOf(level), Call);

        [Test]
        public void EachLevel_GetsItsOwnBracketsCall_AndBelow10_NoCall()
        {
            const string zone = "Go through the Transit Row port to reach your Rally Point.";
            const string none = "Your bracket has no Crossfire zone yet, so there is no war for it this time.";
            Assert.AreEqual("Crossfire! Bracket I (levels 10-19): the war starts in 10 minutes. " + none, CallFor(10));
            Assert.AreEqual("Crossfire! Bracket V (levels 50-59): the war starts in 10 minutes. " + none, CallFor(55));
            Assert.AreEqual("Crossfire! Bracket IX (levels 90-99): the war starts in 10 minutes. " + none, CallFor(99));
            Assert.AreEqual("Crossfire! Bracket X (levels 100-112): the war starts in 10 minutes. " + zone, CallFor(100));
            Assert.AreEqual("Crossfire! Bracket X (levels 100-112): the war starts in 10 minutes. " + zone, CallFor(112), "112 is bracket X, not a bracket of its own");
            Assert.IsNull(CallFor(9), "below level 10: no call");
            Assert.IsNull(CallFor(1));
        }

        [Test]
        public void TheCountdownAndTheGatesClosed_GoOnlyToBracketsWithAZone()
        {
            var count = new CrossfireCaller.Line { Kind = CrossfireCaller.LineKind.Count, Seconds = 7 };
            var closed = new CrossfireCaller.Line { Kind = CrossfireCaller.LineKind.Closed };
            Assert.AreEqual("Crossfire: the gates open in 7", CrossfireCaller.Text(count, m_Settings, m_Settings.BracketOf(112), Call));
            Assert.AreEqual(m_Settings.gatesClosedText, CrossfireCaller.Text(closed, m_Settings, m_Settings.BracketOf(100), Call));
            foreach (int level in new[] { 9, 10, 55, 99 })
            {
                Assert.IsNull(CrossfireCaller.Text(count, m_Settings, m_Settings.BracketOf(level), Call), $"level {level}: no gates of theirs open");
                Assert.IsNull(CrossfireCaller.Text(closed, m_Settings, m_Settings.BracketOf(level), Call), $"level {level}");
            }
        }

        [Test]
        public void TheWords_AndTheZonedBrackets_ComeFromTheSettings()
        {
            m_Settings.callText = "{bracket}|{levels}|{level}|{time}|{where}";
            m_Settings.zoneBrackets = new[] { 5 };
            m_Settings.whereText = "GO";
            m_Settings.noZoneText = "NONE";
            Assert.AreEqual("V|50-59|50|10 minutes|GO", CallFor(55));
            Assert.AreEqual("X|100-112|100|10 minutes|NONE", CallFor(112));
            m_Settings.maxLevel = 120;
            Assert.AreEqual("100-120", m_Settings.LevelsOf(10), "the last bracket runs to the highest level");
            Assert.AreEqual("10-19", m_Settings.LevelsOf(1));
        }

        CrossfireArea Yard(FactionId faction, Vector3 at)
        {
            var go = new GameObject("test Rally Point " + faction);
            m_Made.Add(go);
            go.transform.position = at;
            CrossfireArea yard = go.AddComponent<CrossfireArea>();
            yard.kind = CrossfireArea.AreaKind.MusterYard;
            yard.faction = faction;
            CrossfireArea.Track(yard);
            return yard;
        }

        [Test]
        public void ThePort_LandsBracketX_OnItsOwnFactionsRallyPoint_AndNobodyElse()
        {
            CrossfireArea heg = Yard(FactionId.Hegemony, new Vector3(100, 5, 0));
            CrossfireArea nul = Yard(FactionId.Null, new Vector3(-100, 5, 0));
            CrossfireArea cov = Yard(FactionId.Covenant, new Vector3(0, 5, 100));
            string port = m_Settings.portPortalId;

            foreach ((FactionId faction, CrossfireArea expected) in new[] { (FactionId.Hegemony, heg), (FactionId.Null, nul), (FactionId.Covenant, cov) })
            foreach (int level in new[] { 100, 112 })
            {
                Assert.AreEqual(CrossfirePortAnswer.Land, CrossfirePort.Decide(m_Settings, Call, port, level, faction, out CrossfireArea yard), $"{faction} {level}");
                Assert.AreSame(expected, yard, $"{faction} level {level} lands on its own faction's Rally Point");
            }

            foreach (int level in new[] { 10, 55, 99 })
            {
                Assert.AreEqual(CrossfirePortAnswer.NoZone, CrossfirePort.Decide(m_Settings, Call, port, level, FactionId.Hegemony, out CrossfireArea yard), $"level {level}");
                Assert.IsNull(yard);
                StringAssert.Contains("bracket " + CrossfireRules.Roman(m_Settings.BracketOf(level)) + " has no zone yet",
                    CrossfirePort.Message(m_Settings, CrossfirePortAnswer.NoZone, level));
            }
            Assert.AreEqual(CrossfirePortAnswer.NotCalled, CrossfirePort.Decide(m_Settings, Call, port, 9, FactionId.Hegemony, out _), "below the war's level: the portal as always");
            Assert.AreEqual(CrossfirePortAnswer.NotThisPort, CrossfirePort.Decide(m_Settings, Call, "some-other-portal", 112, FactionId.Hegemony, out _));
            Assert.AreEqual(CrossfirePortAnswer.NoRally, CrossfirePort.Decide(m_Settings, Call, port, 112, FactionId.None, out _), "no faction, no Rally Point");

            // the Rally takes players too; the war, the end and the gap do not
            var rally = Call; rally.Now.Phase = CrossfirePhase.Muster;
            Assert.AreEqual(CrossfirePortAnswer.Land, CrossfirePort.Decide(m_Settings, rally, port, 112, FactionId.Null, out _));
            foreach (CrossfirePhase phase in new[] { CrossfirePhase.Gap, CrossfirePhase.War, CrossfirePhase.End })
            {
                var at = Call; at.Now.Phase = phase;
                Assert.AreEqual(CrossfirePortAnswer.NotCalled, CrossfirePort.Decide(m_Settings, at, port, 112, FactionId.Null, out _), phase.ToString());
            }

            // a faction whose yard is not built yet
            CrossfireArea.Untrack(cov);
            Assert.AreEqual(CrossfirePortAnswer.NoRally, CrossfirePort.Decide(m_Settings, Call, port, 112, FactionId.Covenant, out _));
        }

        [Test]
        public void ALanding_IsTheYardsLandingSpot_OrItsCentre()
        {
            CrossfireArea yard = Yard(FactionId.Hegemony, new Vector3(10, 5, 20));
            yard.size = new Vector3(50, 10, 40);
            yard.Landing(out Vector3 at, out _);
            Assert.AreEqual(new Vector3(10, 5, 20), at, "no landing spot: the box's centre");
            Assert.IsTrue(yard.Contains(at), "inside the yard, so the player counts as in it");
            var spot = new GameObject("landing");
            m_Made.Add(spot);
            spot.transform.SetPositionAndRotation(new Vector3(12, 0.5f, 18), Quaternion.Euler(0, 90, 0));
            yard.landing = spot.transform;
            yard.Landing(out at, out float yaw);
            Assert.AreEqual(new Vector3(12, 0.5f, 18), at);
            Assert.AreEqual(90f, yaw, 1e-3f);
            Assert.IsTrue(yard.Contains(at));
        }
    }
}
