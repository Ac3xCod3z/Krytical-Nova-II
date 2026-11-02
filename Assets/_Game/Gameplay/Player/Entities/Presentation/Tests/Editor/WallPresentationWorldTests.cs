using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Tests;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>
    /// Plan 55 M3c "For all five" (`m3c-wallstride-design.md` section 8): what another player sees of a wall move, and a forced rollback in
    /// the middle of one. A server and two clients with a body per ghost and a recording <see cref="IGhostMotionView"/>; the wall is
    /// <see cref="WallWorldTests"/>' stub. NGO sent the phase to everyone as one NetworkVariable and the body through an interpolated
    /// NetworkTransform (rules O1-O4); the entity netcode sends <see cref="PoseTilt"/> and <see cref="PlayerPose"/>.
    /// </summary>
    public class WallPresentationWorldTests
    {
        /// <summary>Records the wall phases the follow tells the body to play (each call begins a clip).</summary>
        sealed class WallPhaseRecorder : MonoBehaviour, IGhostMotionView
        {
            public readonly List<byte> Phases = new List<byte>();

            public void WallPhase(byte phase, float seconds) => Phases.Add(phase);
            public void BeginJump(float clipSeconds) { }
            public void JumpTookOff() { }
            public void JumpLanded() { }
            public void JumpCancelled() { }
            public void BeginVault(byte heightClass, float elapsedSeconds, float airSeconds) { }
            public void EndVault(float speedMps) { }
            public void WaterStriding(bool onWater, bool splash) { }
        }

        sealed class RecordingBodyFactory : IPlayerBodyFactory
        {
            public GameObject Create(in PlayerBodyRequest request)
            {
                var go = new GameObject("TestBody " + request.NetworkId);
                go.AddComponent<WallPhaseRecorder>();
                go.transform.position = request.Position;
                return go;
            }

            public void Destroy(GameObject body)
            {
                if (body != null) UnityEngine.Object.DestroyImmediate(body);
            }
        }

        sealed class WallRig : IDisposable
        {
            public PlayerRig Rig;
            public readonly WallWorldTests.StubWallPlanner Planner = new WallWorldTests.StubWallPlanner();
            public readonly WallWorldTests.StubGrip Grip = new WallWorldTests.StubGrip { Points = 1000 };

            public static WallRig Create(int delayMs)
            {
                GhostBodyRegistry.Clear();
                LocalPlayer.Set(null);
                var w = new WallRig();
                w.Rig = WallWorldTests.Rig(delayMs, w.Planner, w.Grip, clients: 2);
                var factory = new RecordingBodyFactory();
                PlayerPresentationSetup.Install(w.Rig.Clients[0], factory, null, registerLocalPlayer: true);
                PlayerPresentationSetup.Install(w.Rig.Clients[1], factory, null, registerLocalPlayer: false);
                w.Rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>().RunCost = new WallWorldTests.PaidRuns();
                w.Rig.Server.GetExistingSystemManaged<WallGripSystem>().JumpCost = new WallWorldTests.PaidJumps();
                w.Rig.Tick(3);
                return w;
            }

            public Transform BodyOf(int viewer, int owner)
            {
                Assert.IsTrue(GhostBodyRegistry.TryGetByGhost(Rig.Clients[viewer], Rig.GhostIn(viewer, owner), out var e),
                    $"client {viewer} has no body for the ghost of client {owner}");
                return e.Body.transform;
            }

            public WallPhaseRecorder ViewOf(int viewer, int owner) => BodyOf(viewer, owner).GetComponent<WallPhaseRecorder>();

            public void Dispose()
            {
                Rig?.Dispose();
                GhostBodyRegistry.Clear();
                LocalPlayer.Set(null);
            }
        }

        public enum WallMove { StraightClimb, RunOverAWallByItself, RoofEdgeJump }

        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();
        [TearDown] public void TearDown() { GhostBodyRegistry.Clear(); LocalPlayer.Set(null); }

        static void Log(string line) => Debug.Log("[PLAYER-M3C-ALL5] " + line);

        /// <summary>The phase a body plays for this tilt (the follow's own rule: upright = 0).</summary>
        static byte Shown(in PoseTilt tilt) => tilt.Tilted ? tilt.Phase : (byte)0;

        static void Start(WallRig w, WallMove move)
        {
            var source = w.Rig.Sources[0];
            switch (move)
            {
                case WallMove.StraightClimb: source.Wall(new int3(77777, 3000, 0), WallWorldTests.Wall); break;
                case WallMove.RunOverAWallByItself: source.Click(new int3(WallWorldTests.AutoClick, 5000, 0), run: true); break;
                default: source.Click(new int3(WallWorldTests.LedgeClick, 0, 0)); break;
            }
        }

        static int3 Goal(WallMove move) => move switch
        {
            WallMove.StraightClimb => new int3(6500, 5000, 0),
            WallMove.RunOverAWallByItself => new int3(WallWorldTests.AutoClick, 5000, 0),
            _ => new int3(WallWorldTests.LedgeClick, 0, 0),
        };

        /// <summary>
        /// T1 (design 8.2; NGO O1-O3): another player plays the same phases, each begun where the server began it (the same start tick, seen
        /// through the interpolation delay), shows only poses the server had, in its order (the same curve), and ends where it landed.
        /// </summary>
        [Test]
        public void AnotherPlayer_SeesTheSameWallPhases_TheSameCurve_AndTheSameLanding(
            [Values] WallMove move, [Values(0, 100)] int delayMs)
        {
            using var w = WallRig.Create(delayMs);
            var rig = w.Rig;
            var observerGhost = rig.GhostIn(1, 0);
            var observerView = w.ViewOf(1, 0);

            // the server's every tick: its pose and the phase it shows; and where each phase began
            var serverTicks = new List<(uint tick, int3 pose)>();
            var serverStarts = new List<(byte phase, uint tick, int3 pose)>();
            byte serverShown = 0;
            int skippedTicks = 0;
            // what the observer shows: its ghost's pose after every tick, and its pose when a phase began
            var seen = new List<int3>();
            var observerStarts = new List<int3>();

            serverTicks.Add((rig.ServerTick, rig.ServerPose().Mm));
            Start(w, move);
            for (int i = 0; i < 260; i++)
            {
                uint before = rig.ServerTick;
                rig.Tick();
                if (rig.ServerTick != before + 1) skippedTicks++;
                var pose = rig.ServerPose().Mm;
                serverTicks.Add((rig.ServerTick, pose));
                byte shown = Shown(rig.Get<PoseTilt>(rig.Server, rig.ServerGhosts[0]));
                if (shown != serverShown) { serverShown = shown; serverStarts.Add((shown, rig.ServerTick, pose)); }

                var observed = rig.Get<PlayerPose>(rig.Clients[1], observerGhost).Mm;
                if (seen.Count == 0 || !seen[seen.Count - 1].Equals(observed)) seen.Add(observed);
                while (observerStarts.Count < observerView.Phases.Count) observerStarts.Add(observed);
            }

            var serverPhases = serverStarts.ConvertAll(s => s.phase);
            Log($"{move} at {delayMs} ms: server phases [{string.Join(",", serverPhases)}], the observer's [{string.Join(",", observerView.Phases)}]; " +
                $"{serverTicks.Count} server ticks ({skippedTicks} skipped), {seen.Count} distinct observer poses");
            Assert.AreEqual(Goal(move), rig.ServerPose().Mm, "the server finished the move");
            Assert.Greater(serverPhases.Count, 1, "the move showed wall phases: the test proves nothing without them");
            Assert.AreEqual(0, skippedTicks, "a tick passed unrecorded: the curve check would miss its pose");

            // the same phases, none lost, none twice (NGO O3: a phase plays once)
            CollectionAssert.AreEqual(serverPhases, observerView.Phases, "the observer plays the server's phases, in order, each once");

            // the same start: the observer began each phase on the server's pose of the tick that phase began
            int worstStartTicks = 0;
            for (int i = 0; i < serverStarts.Count; i++)
            {
                int at = serverTicks.FindIndex(t => t.pose.Equals(observerStarts[i]) && t.tick <= serverStarts[i].tick + 2 && t.tick + 2 >= serverStarts[i].tick);
                Assert.GreaterOrEqual(at, 0, $"phase {serverStarts[i].phase}: the observer began it at {observerStarts[i]}, a pose the server did not have near tick " +
                    serverStarts[i].tick + $" ({serverStarts[i].pose})");
                worstStartTicks = math.max(worstStartTicks, math.abs((int)serverTicks[at].tick - (int)serverStarts[i].tick));
            }
            Log($"{move} at {delayMs} ms: the observer began every phase within {worstStartTicks} tick(s) of the server's start");
            Assert.AreEqual(0, worstStartTicks, "every phase begins on the server's own start tick");

            // the same curve: every pose the observer showed is one of the server's, in the server's order
            int j = 0;
            foreach (var p in seen)
            {
                while (j < serverTicks.Count && !serverTicks[j].pose.Equals(p)) j++;
                Assert.Less(j, serverTicks.Count, $"the observer showed {p}, which is not on the server's curve after tick {serverTicks[math.max(0, j - 1)].tick}");
            }

            // the same landing
            Assert.AreEqual(rig.ServerPose().Mm, seen[seen.Count - 1], "the observer ends where the server landed");
            Assert.AreEqual(Goal(move).x * 0.001f, w.BodyOf(1, 0).position.x, 0.05f, "and so does the body it draws");
        }

        /// <summary>
        /// T2 (design 8.2; ghosts-design 3.2.7): mid-climb the owner's ghost is pushed off in its own world and the next snapshot takes it back.
        /// The clip is not begun again (the owner's phases are the server's, each once) and the drawn body slides back through the follow's
        /// offset instead of popping, ending on the ghost.
        /// </summary>
        [Test]
        public void AForcedRollbackMidClimb_NeitherRestartsTheClip_NorPopsTheBody()
        {
            const float Push = 0.6f;
            using var w = WallRig.Create(100);
            var rig = w.Rig;
            var em = rig.Clients[0].EntityManager;
            var ghost = rig.GhostIn(0, 0);
            var body = w.BodyOf(0, 0);
            var ownerView = w.ViewOf(0, 0);

            var serverPhases = new List<byte>();
            byte serverShown = 0;
            void RecordServer()
            {
                byte shown = Shown(rig.Get<PoseTilt>(rig.Server, rig.ServerGhosts[0]));
                if (shown != serverShown) { serverShown = shown; serverPhases.Add(shown); }
            }

            Start(w, WallMove.StraightClimb);
            int toClimb = 0;
            while (rig.Get<PoseTilt>(rig.Clients[0], ghost).Phase != WallPhase.Climb && toClimb++ < 120) { rig.Tick(); RecordServer(); }
            Assert.Less(toClimb, 120, "the owner never reached the climb");
            rig.Tick(2); RecordServer();   // a little way up the face

            // the forced misprediction: the owner's ghost 0.6 m off the face's climb, in its own world only
            var pose = em.GetComponentData<PlayerPose>(ghost); pose.YMm += (int)(Push * 1000); em.SetComponentData(ghost, pose);
            var lt = em.GetComponentData<LocalTransform>(ghost); lt.Position.y += Push; em.SetComponentData(ghost, lt);

            Vector3 lastGhost = lt.Position, lastBody = body.position;
            float maxGhostStep = 0f, maxBodyStep = 0f, ownStep = 0f;
            for (int i = 0; i < 90; i++)
            {
                rig.Tick(); RecordServer();
                Vector3 g = rig.Get<LocalTransform>(rig.Clients[0], ghost).Position;
                float ghostStep = Vector3.Distance(g, lastGhost), bodyStep = Vector3.Distance(body.position, lastBody);
                maxGhostStep = Mathf.Max(maxGhostStep, ghostStep);
                if (ghostStep < Push * 0.5f) ownStep = Mathf.Max(ownStep, ghostStep);   // the climb's own step, the pop left out
                maxBodyStep = Mathf.Max(maxBodyStep, bodyStep);
                lastGhost = g; lastBody = body.position;
            }

            Log($"rollback mid-climb: the ghost's largest frame step {maxGhostStep:F2} m (its own climb {ownStep:F2} m), the body's {maxBodyStep:F2} m; " +
                $"server phases [{string.Join(",", serverPhases)}], the owner's body played [{string.Join(",", ownerView.Phases)}]");
            if (maxGhostStep < Push * 0.5f) Assert.Inconclusive($"the ghost never popped ({maxGhostStep:F2} m): the injected error did not survive; the test proves nothing");
            CollectionAssert.AreEqual(serverPhases, ownerView.Phases, "the rollback began no clip again: the owner played the server's phases, each once");
            Assert.Less(maxBodyStep, ownStep + Push * 0.5f, "the body slides back through the follow's offset instead of popping");
            Assert.AreEqual(new int3(6500, 5000, 0), rig.ServerPose().Mm, "the climb finished (server)");
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose(), "the owner's ghost is the server's again");
            Assert.AreEqual(lastGhost.y, body.position.y, 0.05f, "and the body ends on it");
        }
    }
}
