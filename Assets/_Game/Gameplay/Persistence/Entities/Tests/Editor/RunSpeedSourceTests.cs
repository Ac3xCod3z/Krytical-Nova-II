using System.Reflection;
using KryticalNova.Player.Entities;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// The ghost's speeds come from the same assets and the same function as the NGO game's (owner, 30 Oct 2026: Run tops out at
    /// 28 m/s at the top rank, the lower ranks worked back from it; mounts top out at 34 m/s; the speed Stance stacks on top on foot).
    /// </summary>
    public class RunSpeedSourceTests
    {
        const string Folder = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/MovementSkills/";
        const string MountSettingsPath = "Assets/_Game/Gameplay/Mounts/Data/Assets/MountSettings.asset";

        GameObject m_Body;
        PlayerMovementSkills m_Skills;
        MovementSkillData m_Run;
        MovementSettings m_Settings;

        static void Log(string line) => Debug.Log("[KnSpeed] " + line);

        [SetUp]
        public void SetUp()
        {
            m_Settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(Folder + "Movement.asset");
            m_Run = AssetDatabase.LoadAssetAtPath<MovementSkillData>(Folder + "Run.asset");
            var jump = AssetDatabase.LoadAssetAtPath<MovementSkillData>(Folder + "Jump.asset");
            Assert.IsNotNull(m_Settings, "the game's Movement.asset");
            Assert.IsNotNull(m_Run, "the game's Run.asset");
            m_Body = new GameObject("speed body");
            m_Skills = m_Body.AddComponent<PlayerMovementSkills>();
            var so = new SerializedObject(m_Skills);
            so.FindProperty("movementSettings").objectReferenceValue = m_Settings;
            so.FindProperty("runSkill").objectReferenceValue = m_Run;
            so.FindProperty("jumpSkill").objectReferenceValue = jump;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Body);

        void SetRunRank(int rank)
        {
            typeof(PlayerMovementSkills).GetField("runRank", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(m_Skills, rank);
            Assert.AreEqual(rank, m_Skills.RunRank);
        }

        static float Mps(int mmps) => mmps * 0.001f;

        [Test]
        public void RunRanks_FromTheGamesRunAsset_TopRankIs28()
        {
            int max = m_Run.MaxRank;
            int mid = (max + 1) / 2;
            foreach (int rank in new[] { 1, mid, max })
            {
                SetRunRank(rank);
                MoveStats? stats = PlayerMoveFromSkills.StatsOf(m_Body);
                Assert.IsTrue(stats.HasValue);
                Assert.AreEqual(m_Skills.SpeedFor(true), Mps(stats.Value.RunMmps), 0.001f, "the ghost runs at exactly the NGO game's speed");
                Assert.AreEqual(m_Skills.SpeedFor(false), Mps(stats.Value.WalkMmps), 0.001f);
                Log($"Run rank {rank} of {max}: run {Mps(stats.Value.RunMmps):F2} m/s, walk {Mps(stats.Value.WalkMmps):F2} m/s");
            }
            SetRunRank(1);
            Assert.AreEqual(10f, Mps(PlayerMoveFromSkills.StatsOf(m_Body).Value.RunMmps), 0.001f, "rank 1");
            SetRunRank(max);
            Assert.AreEqual(28f, Mps(PlayerMoveFromSkills.StatsOf(m_Body).Value.RunMmps), 0.001f, "the top rank (owner: 28 m/s)");
            Assert.AreEqual(m_Settings.walkSpeed, Mps(PlayerMoveFromSkills.StatsOf(m_Body).Value.WalkMmps), 0.001f, "walking is the settings' walk");
        }

        [Test]
        public void Mounted_TopSpeedIs34_FromTheMountSettings()
        {
            var mount = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MountSettingsPath);
            Assert.IsNotNull(mount, "the game's MountSettings.asset");
            var so = new SerializedObject(mount);
            Vector2 walk = so.FindProperty("walkSpeed").vector2Value, run = so.FindProperty("runSpeed").vector2Value;
            Assert.AreEqual(34f, run.y, 0.001f, "owner: a mount's top speed is 34 m/s");
            SetRunRank(m_Run.MaxRank);
            m_Skills.SetRideSpeeds(walk.y, run.y);     // what PlayerMounts sets for a fully grown, fed mount
            MoveStats stats = PlayerMoveFromSkills.StatsOf(m_Body).Value;
            Assert.AreEqual(34f, Mps(stats.RunMmps), 0.001f);
            Assert.AreEqual(walk.y, Mps(stats.WalkMmps), 0.001f);
            Log($"mounted top: run {Mps(stats.RunMmps):F2} m/s, walk {Mps(stats.WalkMmps):F2} m/s");
        }

        [Test]
        public void TheSpeedStance_StacksOnTop_OnFootOnly_WithinTheSettingsCap()
        {
            // SpeedFor = base x StatSpeedMultiplier (Boots, the speed Stance: the Move Speed stat) on foot; a mount ignores it
            Assert.AreEqual(1f, m_Settings.StatSpeedMultiplier(100f), 0.0001f, "no bonus: x1");
            Assert.AreEqual(1.2f, m_Settings.StatSpeedMultiplier(120f), 0.0001f, "a 20 % Stance: x1.2");
            Assert.AreEqual(m_Settings.maxStatSpeedMultiplier, m_Settings.StatSpeedMultiplier(400f), 0.0001f, "capped");
            Log($"top Run with a 20 % Stance: {28f * m_Settings.StatSpeedMultiplier(120f):F2} m/s; cap x{m_Settings.maxStatSpeedMultiplier}");
            // the ghost takes SpeedFor whole, so whatever the Stance adds reaches it (StatsOf == SpeedFor above)
        }
    }
}
