using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 9: <see cref="GhostBodyEntry.TryGetPart{T}"/> answers exactly as <c>Body.TryGetComponent</c> did:
    /// the same part, none when it is missing, none when it was destroyed (Unity-null), the new one when it is added later or
    /// replaced, none when the body is gone. The timing test prints 27 per-player passes (the server systems' pattern) old vs new at
    /// N = 50 / 200 / 1000.
    /// </summary>
    public class GhostBodyPartTests
    {
        World m_World;
        readonly List<GameObject> m_Bodies = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            GhostBodyRegistry.Clear();
            m_World = new World("chunk 9 parts");
        }

        [TearDown]
        public void TearDown()
        {
            GhostBodyRegistry.Clear();
            foreach (GameObject body in m_Bodies) if (body != null) Object.DestroyImmediate(body);
            m_Bodies.Clear();
            m_World.Dispose();
        }

        GhostBodyEntry Entry(int i, out GameObject body)
        {
            body = new GameObject("body " + i) { hideFlags = HideFlags.HideAndDontSave };
            m_Bodies.Add(body);
            Entity ghost = m_World.EntityManager.CreateEntity();
            return GhostBodyRegistry.Register(m_World, ghost, i + 1, PlayerKey.None, body, false);
        }

        // Old and new must agree on found / not found and on which part.
        static void Same<T>(GhostBodyEntry entry, GameObject body, string when) where T : class
        {
            T old = null;
            bool had = body != null && body.TryGetComponent(out old);
            if (!had) old = null;
            bool has = entry.TryGetPart(out T now);
            Assert.AreEqual(had, has, when + ": found");
            Assert.AreSame(old, now, when + ": the same part");
        }

        [Test]
        public void TryGetPart_AnswersAsTryGetComponent_ThroughAddDestroyReplace()
        {
            GhostBodyEntry entry = Entry(0, out GameObject body);
            Same<BoxCollider>(entry, body, "missing");
            BoxCollider first = body.AddComponent<BoxCollider>();
            Same<BoxCollider>(entry, body, "added later");
            Same<BoxCollider>(entry, body, "asked again (kept)");
            Assert.IsTrue(entry.TryGetPart(out Collider asBase) && ReferenceEquals(asBase, first), "a base type finds it too, as GetComponent does");
            Object.DestroyImmediate(first);
            Same<BoxCollider>(entry, body, "destroyed");
            Same<Collider>(entry, body, "destroyed, base type");
            BoxCollider second = body.AddComponent<BoxCollider>();
            Same<BoxCollider>(entry, body, "replaced");
            Assert.AreSame(second, entry.TryGetPart(out BoxCollider again) ? again : null);
        }

        [Test]
        public void TryGetPart_IsFalse_WhenTheBodyIsGone()
        {
            GhostBodyEntry entry = Entry(0, out GameObject body);
            body.AddComponent<BoxCollider>();
            Assert.IsTrue(entry.TryGetPart(out BoxCollider _));
            Object.DestroyImmediate(body);
            Assert.IsFalse(entry.TryGetPart(out BoxCollider gone));
            Assert.IsNull(gone);
        }

        [Test]
        public void ManyPartTypes_GetTheirOwnSlots()
        {
            GhostBodyEntry entry = Entry(0, out GameObject body);
            var box = body.AddComponent<BoxCollider>();
            var source = body.AddComponent<AudioSource>();
            var rb = body.AddComponent<Rigidbody>();
            Assert.IsTrue(entry.TryGetPart(out Rigidbody r) && r == rb);
            Assert.IsTrue(entry.TryGetPart(out AudioSource a) && a == source);
            Assert.IsTrue(entry.TryGetPart(out BoxCollider b) && b == box);
            Assert.IsFalse(entry.TryGetPart(out Light _));
            Assert.IsTrue(entry.TryGetPart(out Transform t) && t == body.transform);
        }

        [Test]
        public void Timing_27PerPlayerPasses_OldAndNew_AtN50_200_1000()
        {
            foreach (int n in new[] { 50, 200, 1000 })
            {
                GhostBodyRegistry.Clear();
                var entries = new List<GhostBodyEntry>(n);
                for (int i = 0; i < n; i++)
                {
                    GhostBodyEntry e = Entry(i, out GameObject body);
                    body.AddComponent<BoxCollider>(); body.AddComponent<AudioSource>(); body.AddComponent<Rigidbody>();
                    entries.Add(e);
                }
                const int passes = 27;
                int found = 0;
                foreach (GhostBodyEntry e in entries) { e.TryGetPart(out BoxCollider _); e.TryGetPart(out AudioSource _); e.TryGetPart(out Rigidbody _); }   // registered, first asked

                var sw = Stopwatch.StartNew();
                for (int p = 0; p < passes; p++)
                    foreach (GhostBodyEntry e in entries)
                        switch (p % 3)
                        {
                            case 0: if (e.Body != null && e.Body.TryGetComponent(out BoxCollider _)) found++; break;
                            case 1: if (e.Body != null && e.Body.TryGetComponent(out AudioSource _)) found++; break;
                            default: if (e.Body != null && e.Body.TryGetComponent(out Rigidbody _)) found++; break;
                        }
                double oldMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                for (int p = 0; p < passes; p++)
                    foreach (GhostBodyEntry e in entries)
                        switch (p % 3)
                        {
                            case 0: if (e.Body != null && e.TryGetPart(out BoxCollider _)) found--; break;
                            case 1: if (e.Body != null && e.TryGetPart(out AudioSource _)) found--; break;
                            default: if (e.Body != null && e.TryGetPart(out Rigidbody _)) found--; break;
                        }
                double newMs = sw.Elapsed.TotalMilliseconds;

                UnityEngine.Debug.Log($"[perf chunk 9] {passes} per-player passes over N={n} bodies (one server tick): TryGetComponent {oldMs:F3} ms, " +
                    $"TryGetPart {newMs:F3} ms (x{oldMs / System.Math.Max(newMs, 1e-6):F1})");
                Assert.AreEqual(0, found, "both ways found every part");
            }
        }
    }
}
