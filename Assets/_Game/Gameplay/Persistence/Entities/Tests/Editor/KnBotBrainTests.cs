using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Rules = KryticalNova.Persistence.Entities.KnBotBrainRules;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>The test-lane bot brain's decisions (perf round 3, 15 Nov 2026).</summary>
    public class KnBotBrainRulesTests
    {
        static readonly int3 Home = new int3(100_000, 0, 100_000);
        const int Radius = 30_000;

        static Rules.View Hunting(int3 me) => new Rules.View
        {
            Alive = true, Enrolled = true, AutopilotOn = true, HasHome = true, MeMm = me, HomeMm = Home, HomeRadiusMm = Radius,
            MayAskAutopilot = true,
        };

        static Rules.Candidate Mob(int id, int x, int z, bool alive = true) =>
            new Rules.Candidate { GhostId = id, FeetMm = new int3(x, 0, z), Alive = alive };

        static readonly Dictionary<int, int> NoOne = new Dictionary<int, int>();

        [Test]
        public void AttacksTheNearestLivingHostileOfItsZone()
        {
            var mobs = new List<Rules.Candidate>
            {
                Mob(1, 110_000, 100_000, alive: false),   // nearest, but dead
                Mob(2, 115_000, 100_000),
                Mob(3, 125_000, 100_000),
                Mob(4, 102_000, 140_000),                 // near the bot, outside the zone
            };
            Rules.Plan plan = Rules.Decide(Hunting(new int3(102_000, 0, 120_000)), mobs, NoOne);
            Assert.AreEqual(Rules.Act.Attack, plan.Act);
            Assert.AreEqual(2, plan.TargetGhostId);
        }

        [Test]
        public void AGroupSpreadsOverTheHostiles()
        {
            var mobs = new List<Rules.Candidate> { Mob(7, 105_000, 100_000), Mob(8, 109_000, 100_000) };
            var crowd = new Dictionary<int, int> { { 7, 2 } };   // two bots already on 7
            Assert.AreEqual(8, Rules.Decide(Hunting(Home), mobs, crowd).TargetGhostId);
            Assert.AreEqual(7, Rules.Decide(Hunting(Home), mobs, NoOne).TargetGhostId);
        }

        [Test]
        public void ACombo_UnderWay_GoesOn()
        {
            Rules.View v = Hunting(Home);
            v.CurrentTargetGhostId = 3;
            v.CurrentTargetValid = true;
            Assert.AreEqual(Rules.Act.Wait, Rules.Decide(v, new List<Rules.Candidate> { Mob(2, 100_500, 100_000) }, NoOne).Act);
        }

        [Test]
        public void NothingToFight_WalksBackWhenStrayed_ElseWaits()
        {
            var none = new List<Rules.Candidate>();
            Assert.AreEqual(Rules.Act.WalkHome, Rules.Decide(Hunting(new int3(120_000, 0, 100_000)), none, NoOne).Act);
            Assert.AreEqual(Rules.Act.Wait, Rules.Decide(Hunting(new int3(103_000, 0, 100_000)), none, NoOne).Act);
            Rules.View walking = Hunting(new int3(120_000, 0, 100_000));
            walking.Walking = true;
            Assert.AreEqual(Rules.Act.Wait, Rules.Decide(walking, none, NoOne).Act);
        }

        [Test]
        public void FarAway_AfterARespawnInTown_IsPutBackHome()
        {
            Rules.View v = Hunting(new int3(100_000 + Rules.TeleportBeyondMm + 1_000, 0, 100_000));
            v.AutopilotOn = false;   // a death switched it off
            Assert.AreEqual(Rules.Act.TeleportHome, Rules.Decide(v, new List<Rules.Candidate>(), NoOne).Act);
        }

        [Test]
        public void BackInTheZone_AutopilotIsAskedAgain_OnlyWhenItsTimerAllows()
        {
            Rules.View v = Hunting(Home);
            v.AutopilotOn = false;
            Assert.AreEqual(Rules.Act.AutopilotOn, Rules.Decide(v, new List<Rules.Candidate> { Mob(2, 101_000, 100_000) }, NoOne).Act);
            v.MayAskAutopilot = false;
            Assert.AreEqual(Rules.Act.Wait, Rules.Decide(v, new List<Rules.Candidate>(), NoOne).Act);
            v.MeMm = new int3(140_000, 0, 100_000);   // outside the zone with Autopilot off: walk in first
            Assert.AreEqual(Rules.Act.WalkHome, Rules.Decide(v, new List<Rules.Candidate>(), NoOne).Act);
        }

        [Test]
        public void TheDead_TheUnenrolled_AndTheHomeless_DoNothing()
        {
            var mobs = new List<Rules.Candidate> { Mob(2, 101_000, 100_000) };
            Rules.View dead = Hunting(Home); dead.Alive = false;
            Rules.View idle = Hunting(Home); idle.Enrolled = false;
            Rules.View homeless = Hunting(Home); homeless.HasHome = false;
            Assert.AreEqual(Rules.Act.Wait, Rules.Decide(dead, mobs, NoOne).Act);
            Assert.AreEqual(Rules.Act.Wait, Rules.Decide(idle, mobs, NoOne).Act);
            Assert.AreEqual(Rules.Act.Wait, Rules.Decide(homeless, mobs, NoOne).Act);
        }

        [Test]
        public void WalkedDistance_IgnoresHeightAndTeleports()
        {
            Assert.AreEqual(5_000, Rules.WalkedMm(new int3(0, 0, 0), new int3(3_000, 900, 4_000)));
            Assert.AreEqual(0, Rules.WalkedMm(new int3(0, 0, 0), new int3(Rules.TeleportBeyondMm + 1, 0, 0)));
        }

        [Test]
        public void SkillGaps_AreFourToSevenSeconds_AndDifferBetweenBots()
        {
            var gaps = new HashSet<float>();
            for (int bot = 1; bot <= 20; bot++)
            {
                float g = Rules.SkillGapSeconds(bot, 0);
                Assert.That(g, Is.InRange(4f, 7.01f));
                gaps.Add(g);
            }
            Assert.Greater(gaps.Count, 5);
        }
    }

    /// <summary>The bot brain never ships: every file of it is compiled only into the Editor and <c>KN_DEV_BOTS</c> builds.</summary>
    public class KnBotBrainGuardTests
    {
        const string Guard = "#if KN_DEV_BOTS || UNITY_EDITOR";

        static string GameFolder => Path.Combine(Application.dataPath, "_Game");

        [Test]
        public void EveryBotBrainSourceFile_IsInsideTheDevBotsGuard()
        {
            string[] files = Directory.GetFiles(Path.Combine(GameFolder, "Gameplay", "Persistence", "Entities", "Runtime"), "KnBotBrain*.cs");
            Assert.AreEqual(2, files.Length, "KnBotBrainRules.cs and KnBotBrainServerSystem.cs");
            foreach (string file in files)
            {
                string[] lines = File.ReadAllLines(file).Where(l => l.Trim().Length > 0).ToArray();
                StringAssert.StartsWith(Guard, lines[0], Path.GetFileName(file) + " must open with the dev-bots guard");
                Assert.AreEqual("#endif", lines[lines.Length - 1].Trim(), Path.GetFileName(file) + " must close the guard on its last line");
            }
        }

        [Test]
        public void NoShippedCodeNamesTheBotBrain()
        {
            // only the brain's own (guarded) files and the Editor-only tests may name it, so a release build compiles without it
            foreach (string file in Directory.GetFiles(GameFolder, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("KnBotBrain") || file.Replace('\\', '/').Contains("/Tests/")) continue;
                Assert.IsFalse(File.ReadAllText(file).Contains("KnBotBrain"), name + " names the bot brain outside its guarded files");
            }
        }
    }
}
