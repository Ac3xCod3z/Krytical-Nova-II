using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Tests;
using KryticalNova.World;
using KryticalNova.World.Portals;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.Crossfire.Entities.Tests
{
    /// <summary>
    /// Owner, 16 Nov 2026: while Crossfire is called, a bracket X player (100-112) who goes through the Transit Row port is teleported onto
    /// their OWN faction's Rally Point on the same server; a lower bracket is told it has no zone and stays where it is. Real player ghosts on
    /// a server and clients (<see cref="PlayerRig"/>), a clock of the test's own, three test Rally Points (placeholders until the owner's map).
    /// </summary>
    public sealed class CrossfirePortWorldTests
    {
        const double Origin = 1000;   // the clock starts at 1005: the Call runs 1005-1065, the Rally to 1070, the War to 1130, the End to 1132
        CrossfireSettings m_Settings;
        double m_Now;
        readonly List<GameObject> m_Made = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<CrossfireSettings>();
            m_Settings.hideFlags = HideFlags.DontUnloadUnusedAsset;   // the rig's subscene load unloads unused assets, which took this copy
            m_Settings.anchorToNightfall = false;
            m_Settings.callMinutes = 1f;
            m_Settings.joinWindowMinutes = 1f;   // the whole Call takes players in
            m_Settings.musterSeconds = 5f;
            m_Settings.warMinutes = 1f;
            m_Settings.endSeconds = 2f;
            m_Now = Origin;
            CrossfireClockServerSystem.ClockSource = () => (false, default(WorldEnvironmentState));
        }

        [TearDown]
        public void TearDown()
        {
            CrossfireClockServerSystem.ClockSource = null;
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
            foreach (CrossfireArea a in new List<CrossfireArea>(CrossfireArea.All)) CrossfireArea.Untrack(a);
            Object.DestroyImmediate(m_Settings);
        }

        /// <summary>A faction's Rally Point: a Muster Yard box round <paramref name="centre"/> (metres) with a landing spot on its ground.</summary>
        CrossfireArea RallyPoint(FactionId faction, Vector3 centre, Vector3 landingAt, float yaw)
        {
            var go = new GameObject("test Rally Point " + faction);
            m_Made.Add(go);
            go.transform.position = centre;
            var yard = go.AddComponent<CrossfireArea>();
            yard.kind = CrossfireArea.AreaKind.MusterYard;
            yard.faction = faction;
            yard.size = new Vector3(20, 10, 20);
            var landing = new GameObject("landing");
            landing.transform.SetParent(go.transform, false);
            landing.transform.SetPositionAndRotation(landingAt, Quaternion.Euler(0, yaw, 0));
            yard.landing = landing.transform;
            CrossfireArea.Track(yard);
            return yard;
        }

        /// <summary>
        /// The worlds and players, then <paramref name="makeYards"/> (after the rig: loading its subscene replaces the scene, which would
        /// take objects made before it), then the test's clock in the Call; the players are seen standing outside every yard first.
        /// </summary>
        (PlayerRig rig, CrossfireClockServerSystem clock, CrossfireEntryServerSystem entry) Start(int players, System.Action makeYards)
        {
            var starts = new int3[players];
            for (int i = 0; i < players; i++) starts[i] = new int3(-40000 + 5000 * i, 0, -40000);   // well outside every yard
            PlayerRig rig = PlayerRig.Create(players, starts: starts);
            makeYards();
            var clock = rig.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
            var entry = rig.Server.GetExistingSystemManaged<CrossfireEntryServerSystem>();
            Assert.IsNotNull(clock, "the server world has Crossfire's clock");
            Assert.IsNotNull(entry, "the server world has Crossfire's entry system");
            clock.Settings = m_Settings;
            clock.Clock = () => m_Now;
            clock.Interval = 0f;
            m_Now = Origin + 5;
            Settle(rig);
            Assert.AreEqual(CrossfirePhase.Call, clock.Current.Now.Phase, "the war is called");
            return (rig, clock, entry);
        }

        /// <summary>Ticks past the clock's next look (it was booked a quarter second ahead before the test set Interval 0).</summary>
        static void Settle(PlayerRig rig) => rig.Tick(30);

        static int3 Mm(Vector3 m) => new int3((int)math.round(m.x * 1000f), (int)math.round(m.y * 1000f), (int)math.round(m.z * 1000f));

        [Test]
        public void BracketX_ThroughThePort_LandsOnItsOwnFactionsRallyPoint_AndIsSentBackAfterTheWar()
        {
            var factions = new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant };
            var landings = new[] { new Vector3(100, 0, 0), new Vector3(-100, 0, 0), new Vector3(0, 0, 100) };
            var levels = new[] { 112, 100, 105 };

            var (rig, clock, entry) = Start(3, () =>
            {
                for (int i = 0; i < 3; i++) RallyPoint(factions[i], landings[i] + new Vector3(0, 2, 0), landings[i], 90f * i);
            });
            using (rig)
            {
                var before = new PlayerPose[3];
                for (int i = 0; i < 3; i++) before[i] = rig.ServerPose(i);
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(entry.Port(rig.ServerGhosts[i], levels[i], factions[i], m_Settings.portPortalId, out ZoneTransferResult r),
                        $"player {i}: Crossfire answers its port");
                    Assert.IsTrue(r.Transferred, $"player {i} (level {levels[i]}, {factions[i]}) is moved; the port said {entry.LastPortAnswer} ({r.Message}), phase {clock.Current.Now.Phase}");
                    Assert.AreEqual(m_Settings.portArrivedText, r.Message);
                }
                rig.Tick(10);
                for (int i = 0; i < 3; i++)
                {
                    PlayerPose p = rig.ServerPose(i);
                    Assert.AreEqual(Mm(landings[i]), p.Mm, $"player {i} ({factions[i]}) stands on its own faction's Rally Point");
                    Assert.IsTrue(CrossfireArea.TryRallyPoint(factions[i], out CrossfireArea own) && own.Contains(RigM(p)), $"player {i} is inside its own yard");
                    for (int j = 0; j < 3; j++)
                        if (j != i) Assert.IsFalse(CrossfireArea.TryRallyPoint(factions[j], out CrossfireArea other) && other.Contains(RigM(p)), $"player {i} is not in {factions[j]}'s yard");
                }
                Assert.AreEqual(3, entry.Ported);
                Assert.AreEqual(3, entry.Joined, $"landed in their yard while it takes players in: all three joined the war (seen outside {entry.SeenOutside}, "
                    + $"join open {entry.LastJoinOpen}, looks {entry.Looks}, now {clock.Now}, joins from {clock.Current.JoinOpensAt}, war {clock.Current.Now.WarStart}; "
                    + $"settings alive {m_Settings != null}, the clock's are the test's {ReferenceEquals(clock.Settings, m_Settings)}, clock's null {clock.Settings == null}, "
                    + $"call min {(clock.ActiveSettings != null ? clock.ActiveSettings.callMinutes : -1f)})");

                // the war runs out: everyone the port brought is sent back where they stood before
                m_Now = Origin + 200;
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.Gap, clock.Current.Now.Phase);
                Assert.AreEqual(3, entry.SentBack);
                for (int i = 0; i < 3; i++)
                    Assert.LessOrEqual(math.distance(new float2(rig.ServerPose(i).XMm, rig.ServerPose(i).ZMm), new float2(before[i].XMm, before[i].ZMm)), 5f,
                        $"player {i} is back where it went through the port");
            }
        }

        static Vector3 RigM(PlayerPose p) => new Vector3(p.XMm, p.YMm, p.ZMm) * 0.001f;

        [Test]
        public void LowerBrackets_AreToldTheyHaveNoZone_AndAreNotMoved()
        {
            var (rig, _, entry) = Start(3, () => RallyPoint(FactionId.Hegemony, new Vector3(100, 2, 0), new Vector3(100, 0, 0), 0f));
            using (rig)
            {
                var levels = new[] { 99, 55, 10 };
                var before = new PlayerPose[3];
                for (int i = 0; i < 3; i++) before[i] = rig.ServerPose(i);
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(entry.Port(rig.ServerGhosts[i], levels[i], FactionId.Hegemony, m_Settings.portPortalId, out ZoneTransferResult r), $"level {levels[i]}: Crossfire answers");
                    Assert.IsFalse(r.Transferred, $"level {levels[i]} is not moved");
                    StringAssert.Contains("has no zone yet", r.Message);
                    StringAssert.Contains("bracket " + CrossfireRules.Roman(m_Settings.BracketOf(levels[i])), r.Message);
                }
                Assert.IsFalse(entry.Port(rig.ServerGhosts[0], 9, FactionId.Hegemony, m_Settings.portPortalId, out _), "below level 10: the portal as always");
                Assert.IsFalse(entry.Port(rig.ServerGhosts[0], 112, FactionId.Hegemony, "another-portal", out _), "not the Transit Row port: the portal as always");
                rig.Tick(10);
                for (int i = 0; i < 3; i++) Assert.AreEqual(before[i].Mm, rig.ServerPose(i).Mm, $"level {levels[i]} stayed where it was");
                Assert.AreEqual(0, entry.Ported);
            }
        }

        [Test]
        public void OutsideTheCall_ThePortIsThePortalAsAlways_AndOtherPortalsAreUntouched()
        {
            var (rig, clock, entry) = Start(1, () => RallyPoint(FactionId.Hegemony, new Vector3(100, 2, 0), new Vector3(100, 0, 0), 0f));
            using (rig)
            {
                Assert.IsNotNull(ZonePortal.TransferHandler);
                Assert.IsNotInstanceOf<SealedPortalHandler>(ZonePortal.TransferHandler, "Crossfire stands in front of the portals' handler on this server");
                m_Now = Origin + 90;   // the War: the gates are open, the port no longer takes anyone
                Settle(rig);
                Assert.AreEqual(CrossfirePhase.War, clock.Current.Now.Phase);
                Assert.IsFalse(entry.Port(rig.ServerGhosts[0], 112, FactionId.Hegemony, m_Settings.portPortalId, out _), "during the War: the portal as always");

                // a body the server does not know falls through to the sealed portal, message and all
                var portalGo = new GameObject("test portal");
                m_Made.Add(portalGo);
                portalGo.AddComponent<SphereCollider>();
                ZonePortal portal = portalGo.AddComponent<ZonePortal>();
                var stranger = new GameObject("stranger");
                m_Made.Add(stranger);
                ZoneTransferResult sealedResult = ZonePortal.TransferHandler.TryTransfer(portal, stranger);
                Assert.IsFalse(sealedResult.Transferred);
                Assert.AreEqual(portal.SealedMessage, sealedResult.Message);
            }
        }
    }
}
