using System.Collections.Generic;
using System.Diagnostics;
using KryticalNova.Core;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using World = Unity.Entities.World;
using Entity = Unity.Entities.Entity;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 9 part 3: <see cref="OwnerProgressBridge"/> read and mirrored through the registry entry's kept
    /// parts gives exactly what the body reads (GetComponent) gave: the same progress, none for a body without the parts, the same
    /// mirrored state. The timing test prints one server pass (Read for every player) old vs new at N = 50 / 200 / 1000.
    /// </summary>
    public sealed class OwnerProgressEntryReadTests
    {
        World m_World;
        CharacterProgressionData m_Data;
        CharacterStatData m_Stats;
        readonly List<GameObject> m_Bodies = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            GhostBodyRegistry.Clear();
            m_World = new World("chunk 9 part 3");
            m_Data = ScriptableObject.CreateInstance<CharacterProgressionData>();
            m_Data.startingStatPoints = 5;
            m_Data.startingSkillPoints = 1;
            m_Data.levels = new CharacterLevelStep[20];
            for (int i = 0; i < m_Data.levels.Length; i++)
                m_Data.levels[i] = new CharacterLevelStep { experienceRequired = 10, statPointReward = 5, skillPointReward = 2 };
            m_Stats = ScriptableObject.CreateInstance<CharacterStatData>();
            m_Stats.allocationCost = 1;
        }

        [TearDown]
        public void TearDown()
        {
            GhostBodyRegistry.Clear();
            foreach (GameObject body in m_Bodies) if (body != null) Object.DestroyImmediate(body);
            m_Bodies.Clear();
            Object.DestroyImmediate(m_Data);
            Object.DestroyImmediate(m_Stats);
            m_World.Dispose();
        }

        GhostBodyEntry Entry(int i, bool withProgression, out GameObject body)
        {
            body = new GameObject("body " + i) { hideFlags = HideFlags.HideAndDontSave };
            m_Bodies.Add(body);
            if (withProgression)
            {
                body.SetActive(false);
                var progression = body.AddComponent<PlayerCharacterProgression>();
                var serialized = new SerializedObject(progression);
                serialized.FindProperty("progressionData").objectReferenceValue = m_Data;
                serialized.FindProperty("statData").objectReferenceValue = m_Stats;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                body.SetActive(true);
                // An EditMode test runs no Awake: called by hand, as the progression tests do.
                typeof(PlayerCharacterProgression).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(progression, null);
            }
            Entity ghost = m_World.EntityManager.CreateEntity();
            return GhostBodyRegistry.Register(m_World, ghost, i + 1, PlayerKey.None, body, false);
        }

        [Test]
        public void Read_FromTheEntry_IsTheBodysRead()
        {
            GhostBodyEntry with = Entry(0, true, out GameObject withBody);
            GhostBodyEntry without = Entry(1, false, out GameObject withoutBody);
            OwnerProgress? old = OwnerProgressBridge.Read(withBody), now = OwnerProgressBridge.Read(with);
            Assert.IsTrue(old.HasValue && now.HasValue, "a body with a progression reads");
            Assert.IsTrue(OwnerProgressBridge.Same(old.Value, now.Value), "the same progress");
            Assert.IsTrue(OwnerProgressBridge.Read(with).HasValue, "asked again (kept part)");
            Assert.IsFalse(OwnerProgressBridge.Read(withoutBody).HasValue);
            Assert.IsFalse(OwnerProgressBridge.Read(without).HasValue, "no parts: none, as before");
            Assert.AreEqual(OwnerProgressBridge.ReadSerums(withoutBody).HasValue, OwnerProgressBridge.ReadSerums(without).HasValue, "serums: same answer");
            Object.DestroyImmediate(withBody);
            Assert.IsFalse(OwnerProgressBridge.Read(with).HasValue, "a destroyed body reads none");
        }

        [Test]
        public void Mirror_ThroughTheEntry_ShowsWhatTheBodyMirrorShows()
        {
            GhostBodyEntry a = Entry(0, true, out GameObject aBody);
            Entry(1, true, out GameObject bBody);
            var p = new OwnerProgress { Level = 4, Experience = 7, StatPoints = 3, SkillPoints = 2, Renown = 1, Strength = 9, Vitality = 8, Agility = 6, Spirit = 5 };
            OwnerProgressBridge.Mirror(a, p);
            OwnerProgressBridge.Mirror(bBody, p);
            Assert.IsTrue(OwnerProgressBridge.Same(OwnerProgressBridge.Read(aBody).Value, OwnerProgressBridge.Read(bBody).Value), "the same state shown");
            OwnerProgress before = OwnerProgressBridge.Read(aBody).Value;
            OwnerProgressBridge.Mirror(a, new OwnerProgress { Level = 0 });
            Assert.IsTrue(OwnerProgressBridge.Same(before, OwnerProgressBridge.Read(aBody).Value), "level 0 is left as it is");
        }

        [Test]
        public void Timing_OneServerPass_OldVsNew()
        {
            foreach (int n in new[] { 50, 200, 1000 })
            {
                GhostBodyRegistry.Clear();
                var entries = new List<GhostBodyEntry>(n);
                for (int i = 0; i < n; i++) entries.Add(Entry(i, true, out _));
                const int passes = 30;
                long sink = 0;
                foreach (GhostBodyEntry e in entries) sink += OwnerProgressBridge.Read(e)?.Level ?? 0;   // warm: parts kept
                var watch = Stopwatch.StartNew();
                for (int k = 0; k < passes; k++) foreach (GhostBodyEntry e in entries) sink += OwnerProgressBridge.Read(e.Body)?.Level ?? 0;
                double oldMs = watch.Elapsed.TotalMilliseconds / passes;
                watch.Restart();
                for (int k = 0; k < passes; k++) foreach (GhostBodyEntry e in entries) sink += OwnerProgressBridge.Read(e)?.Level ?? 0;
                double newMs = watch.Elapsed.TotalMilliseconds / passes;
                Debug.Log($"[perf chunk 9 part 3] OwnerProgress server pass N={n}: {oldMs:0.000} -> {newMs:0.000} ms (sink {sink})");
                Assert.Greater(sink, 0);
            }
        }
    }
}
