using KryticalNova.Networking.Entities.Tests;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Build 17, item 11.1 (PR 238, owner pick C): the two-client proof that nobody stands inside another player, in a server and two
    /// clients (<see cref="PlayerRig"/>): two players on one spot, the one who stopped last slides out to touching and both games see it;
    /// one walks straight through the other unhindered; against a wall (the walkable surface's edge, as the NavMesh's is at a building)
    /// the slide never goes into the wall: the one at the wall cannot slide into it, so the other steps aside over the open ground.
    /// </summary>
    public sealed class PlayerSeparationWorldTests
    {
        const int Radius = 400, SlideMmps = 1500;

        /// <summary>A straight-line walk with a wall: nothing is walkable north of <see cref="WallZ"/> (mm), like a NavMesh edge.</summary>
        sealed class WallSurface : IPathPlanner, IWalkableSurface
        {
            public int WallZ = 1000;
            public int Clamped;
            readonly StraightLinePlanner m_Line = new StraightLinePlanner();

            public bool TryPlan(int3 startMm, int3 goalMm, NativeList<int3> cornersMm) => m_Line.TryPlan(startMm, goalMm, cornersMm);

            public bool TryClampStep(int3 fromMm, int3 toMm, out int3 reachedMm)
            {
                reachedMm = fromMm;
                if (fromMm.z > WallZ) return false;   // not on the walkable surface
                if (toMm.z <= WallZ) { reachedMm = new int3(toMm.x, fromMm.y, toMm.z); return true; }
                Clamped++;
                double t = (WallZ - fromMm.z) / (double)(toMm.z - fromMm.z);
                reachedMm = new int3(fromMm.x + (int)math.round((toMm.x - fromMm.x) * t), fromMm.y, WallZ);
                return true;
            }
        }

        static PlayerRig Rig(IPathPlanner serverPlanner = null)
        {
            PlayerRig rig = PlayerRig.Create(2, starts: new[] { new int3(0, 0, -20000), new int3(0, 0, -30000) });
            if (serverPlanner != null) rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>().Planner = serverPlanner;
            // the server's move settings with the separation on (Movement.asset's PROPOSED numbers)
            EntityManager em = rig.Server.EntityManager;
            using EntityQuery q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerMoveSettings>());
            PlayerMoveSettings cfg = q.IsEmpty ? PlayerMoveSettings.Default : q.GetSingleton<PlayerMoveSettings>();
            cfg.PersonalRadiusMm = Radius;
            cfg.SlideOutMmps = SlideMmps;
            if (q.IsEmpty) em.AddComponentData(em.CreateEntity(), cfg);
            else q.SetSingleton(cfg);
            return rig;
        }

        static float Apart(PlayerPose a, PlayerPose b) => math.distance(new float2(a.XMm, a.ZMm), new float2(b.XMm, b.ZMm));

        static PlayerSeparationSystem Slides(PlayerRig rig)
        {
            var s = rig.Server.GetExistingSystemManaged<PlayerSeparationSystem>();
            Assert.IsNotNull(s, "the server world has the separation system");
            return s;
        }

        /// <summary>Both clients' games show both players where the server has them (owner prediction and the other's ghost).</summary>
        static void AssertBothGamesSee(PlayerRig rig, string when)
        {
            for (int viewer = 0; viewer < 2; viewer++)
            for (int owner = 0; owner < 2; owner++)
            {
                PlayerPose seen = rig.Get<PlayerPose>(rig.Clients[viewer], rig.GhostIn(viewer, owner));
                PlayerPose server = rig.ServerPose(owner);
                Assert.LessOrEqual(Apart(seen, server), 5f, $"{when}: client {viewer}'s view of player {owner} is where the server has it");
            }
        }

        /// <summary>
        /// The player walks onto <paramref name="at"/> from 1 m west and stops there (a teleport alone keeps the tick it first stood on, so
        /// "who stopped last" is set by a real walk, as in the game).
        /// </summary>
        static void Arrive(PlayerRig rig, int owner, int3 at)
        {
            rig.Teleport(owner, at - new int3(1000, 0, 0));
            rig.Tick(5);
            rig.IssueOrder(owner, at);
            int used = rig.Tw.TickUntil(() => (rig.ServerMove(owner).Flags & MoveFlags.Moving) == 0
                && math.distance(new float2(rig.ServerPose(owner).XMm, rig.ServerPose(owner).ZMm), new float2(at.x, at.z)) <= 850f, 120);   // a slide may start the tick it stops
            Assert.GreaterOrEqual(used, 0, $"player {owner} did not arrive at {at}");
        }

        [Test]
        public void TwoOnOneSpot_TheOneWhoStoppedLast_SlidesOutToTouching_AndBothGamesSeeIt()
        {
            using PlayerRig rig = Rig();
            PlayerSeparationSystem s = Slides(rig);
            Arrive(rig, 0, new int3(0, 0, 0));
            rig.Tick(30);
            Arrive(rig, 1, new int3(50, 0, 20));   // stops last, almost on top (it slides as soon as it stands)
            rig.Tick(90);                            // 0.75 m at 1.5 m/s is 15 ticks; plenty, and the views settle
            PlayerPose a = rig.ServerPose(0), b = rig.ServerPose(1);
            Assert.AreEqual(new int3(0, 0, 0), a.Mm, "the one who stood first is not moved");
            Assert.AreEqual(2 * Radius, Apart(a, b), 2f, "the later one slid out to touching, no further");
            Assert.Greater(s.Slides, 0);
            AssertBothGamesSee(rig, "after the slide");
            int slides = s.Slides;
            rig.Tick(30);
            Assert.AreEqual(slides, s.Slides, "touching: nothing more to do");
        }

        [Test]
        public void AWalk_GoesStraightThroughAStandingPlayer_Unhindered()
        {
            using PlayerRig rig = Rig();
            rig.Teleport(0, new int3(0, 0, 0));
            rig.Teleport(1, new int3(-6000, 0, 0));
            rig.Tick(30);
            rig.IssueOrder(1, new int3(6000, 0, 0));
            int worst = 0;
            for (int i = 0; i < 150; i++)
            {
                rig.Tick(1);
                PlayerPose a = rig.ServerPose(0);
                worst = math.max(worst, math.abs(a.XMm) + math.abs(a.ZMm));
            }
            Assert.AreEqual(new int3(6000, 0, 0), rig.ServerPose(1).Mm, "the walker arrived where it was sent, straight through");
            Assert.AreEqual(0, worst, "the standing player was never pushed while the other walked through");
            AssertBothGamesSee(rig, "after the walk through");
        }

        [Test]
        public void AtAWall_TheSlideNeverGoesIntoIt_TheOtherStepsAside()
        {
            var wall = new WallSurface { WallZ = 1000 };
            using PlayerRig rig = Rig(wall);
            PlayerSeparationSystem s = Slides(rig);

            // 1) the one who stopped last stands against the wall, the other a little in front: its way out is into the wall
            Arrive(rig, 0, new int3(0, 0, 800));
            rig.Tick(30);
            Arrive(rig, 1, new int3(0, 0, 1000));
            rig.Tick(90);
            PlayerPose a = rig.ServerPose(0), b = rig.ServerPose(1);
            Assert.LessOrEqual(b.ZMm, wall.WallZ, "the one at the wall never went into it");
            Assert.LessOrEqual(a.ZMm, wall.WallZ);
            Assert.AreEqual(2 * Radius, Apart(a, b), 3f, "they still end up touching: the other stepped aside over the open ground");
            Assert.Less(a.ZMm, 800, "the one in front moved away from the wall");
            Assert.Greater(wall.Clamped, 0, "the wall was asked and held");
            AssertBothGamesSee(rig, "at the wall");

            // 2) exactly on top of each other at the wall (a fixed direction per pair; it may point into the wall)
            Arrive(rig, 0, new int3(3000, 0, 1000));
            rig.Tick(30);
            Arrive(rig, 1, new int3(3000, 0, 1000));
            rig.Tick(90);
            a = rig.ServerPose(0); b = rig.ServerPose(1);
            Assert.LessOrEqual(a.ZMm, wall.WallZ, "exactly on top: nobody into the wall");
            Assert.LessOrEqual(b.ZMm, wall.WallZ);
            Assert.GreaterOrEqual(Apart(a, b), 2 * Radius - 3f, "exactly on top: still apart in the end");
            AssertBothGamesSee(rig, "on top at the wall");
            TestContext.WriteLine($"wall: slides {s.Slides}, blocked {s.Blocked}, clamped {wall.Clamped}; on top -> {a.Mm} / {b.Mm}");
        }
    }
}
