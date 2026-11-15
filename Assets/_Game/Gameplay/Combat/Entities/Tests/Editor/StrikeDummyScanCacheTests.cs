using System.Collections.Generic;
using System.Diagnostics;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 12: <see cref="StrikeDummyServerSystem"/> searches the whole scene only when the loaded scenes
    /// change (and every <see cref="StrikeDummyServerSystem.FullScanEveryUpdates"/> updates); between, it checks the dummies it found.
    /// The scene-change test reports no change when nothing loaded; the time of one scan with 200 CombatEntity objects is printed.
    /// </summary>
    public sealed class StrikeDummyScanCacheTests
    {
        [Test]
        public void ScenesChanged_IsTrueOnce_ThenFalseUntilAScenesLoads()
        {
            var last = new List<UnityEngine.SceneManagement.Scene>(); var scratch = new List<UnityEngine.SceneManagement.Scene>();
            Assert.IsTrue(StrikeDummyServerSystem.ScenesChanged(last, scratch), "the first look sees the open scenes");
            Assert.IsFalse(StrikeDummyServerSystem.ScenesChanged(last, scratch), "nothing loaded since");
            last.Add(default);
            Assert.IsTrue(StrikeDummyServerSystem.ScenesChanged(last, scratch), "a different set is a change");
            Assert.IsFalse(StrikeDummyServerSystem.ScenesChanged(last, scratch));
        }

        [Test]
        public void Timing_OneScan_With200CombatEntities_OldAndNew()
        {
            var made = new List<GameObject>();
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    var go = new GameObject("chunk 12 body " + i);
                    go.AddComponent<CombatEntity>();
                    made.Add(go);
                }
                // the old scan: a scene-wide search, then the dummy test on each
                var cache = new List<CombatEntity>();
                const int scans = 50;
                int oldFound = 0, newFound = 0;
                var sw = Stopwatch.StartNew();
                for (int s = 0; s < scans; s++)
                    foreach (CombatEntity e in Object.FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        if (StrikeDummies.IsSceneDummy(e)) oldFound++;
                double oldMs = sw.Elapsed.TotalMilliseconds / scans;
                foreach (CombatEntity e in Object.FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (StrikeDummies.IsSceneDummy(e)) cache.Add(e);   // the one whole-scene search
                sw.Restart();
                for (int s = 0; s < scans; s++)
                    foreach (CombatEntity e in cache) if (StrikeDummies.IsSceneDummy(e)) newFound++;
                double newMs = sw.Elapsed.TotalMilliseconds / scans;
                UnityEngine.Debug.Log($"[perf chunk 12] one dummy scan with 200 CombatEntity objects ({cache.Count} dummies): old {oldMs:F4} ms, " +
                    $"new {newMs:F4} ms between whole-scene searches");
                Assert.AreEqual(oldFound, newFound, "the same dummies found");
            }
            finally
            {
                foreach (GameObject go in made) Object.DestroyImmediate(go);
            }
        }
    }
}
