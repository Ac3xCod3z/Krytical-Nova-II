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
    /// Perf night chunk 1: the registry's map lookups give exactly the answers the old prune-and-scan gave. <see cref="ScanRegistry"/>
    /// is the old algorithm copied as it was (list order, prune before every lookup, first match wins); a seeded random script of
    /// registers, re-registers, unregisters, destroyed bodies, disposed worlds and world take-overs runs on both, and every lookup,
    /// <see cref="GhostBodyRegistry.Count"/>, the <see cref="GhostBodyRegistry.Entries"/> order and the <see cref="PlayerBodies"/> order
    /// must match after every step. The timing test prints old vs new for one frame of per-player lookups at N = 50 / 200 / 1000.
    /// </summary>
    public class GhostBodyRegistryLookupTests
    {
        /// <summary>The old registry's lookups, verbatim in behaviour: a list, a prune before every lookup, linear first-match scans.</summary>
        sealed class ScanRegistry
        {
            public sealed class Rec { public World World; public Entity Ghost; public int NetworkId; public PlayerKey Key; public GameObject Body;
                public bool IsDead => World == null || !World.IsCreated || Body == null; }

            readonly List<Rec> m_Entries = new List<Rec>();

            public int Count { get { Prune(); return m_Entries.Count; } }
            public List<Rec> Entries { get { Prune(); return m_Entries; } }

            public void Register(World world, Entity ghost, int networkId, PlayerKey key, GameObject body)
            {
                Prune();
                RemoveAt(IndexOf(world, ghost));
                m_Entries.Add(new Rec { World = world, Ghost = ghost, NetworkId = networkId, Key = key, Body = body });
            }

            public bool Unregister(World world, Entity ghost, out Rec rec)
            {
                int i = IndexOf(world, ghost);
                rec = i >= 0 ? m_Entries[i] : null;
                RemoveAt(i);
                return rec != null;
            }

            public List<Rec> TakeWorld(World world)
            {
                var taken = new List<Rec>();
                for (int i = m_Entries.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(m_Entries[i].World, world)) continue;
                    taken.Add(m_Entries[i]);
                    RemoveAt(i);
                }
                taken.Reverse();
                return taken;
            }

            public bool TryGetByGhost(World world, Entity ghost, out Rec rec)
            {
                Prune();
                int i = IndexOf(world, ghost);
                rec = i >= 0 ? m_Entries[i] : null;
                return rec != null;
            }

            public bool TryGetByNetworkId(World world, int networkId, out Rec rec)
            {
                Prune();
                foreach (var e in m_Entries)
                    if (ReferenceEquals(e.World, world) && e.NetworkId == networkId) { rec = e; return true; }
                rec = null;
                return false;
            }

            public bool TryGetByKey(PlayerKey key, out Rec rec)
            {
                Prune();
                if (key.IsValid)
                    foreach (var e in m_Entries)
                        if (e.Key == key) { rec = e; return true; }
                rec = null;
                return false;
            }

            int IndexOf(World world, Entity ghost)
            {
                for (int i = 0; i < m_Entries.Count; i++)
                    if (ReferenceEquals(m_Entries[i].World, world) && m_Entries[i].Ghost == ghost) return i;
                return -1;
            }

            void RemoveAt(int i) { if (i >= 0) m_Entries.RemoveAt(i); }

            void Prune()
            {
                for (int i = m_Entries.Count - 1; i >= 0; i--)
                    if (m_Entries[i].IsDead) RemoveAt(i);
            }
        }

        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<World> m_Worlds = new List<World>();

        [SetUp] public void SetUp() { GhostBodyRegistry.Clear(); LocalPlayer.Set(null); }

        [TearDown]
        public void TearDown()
        {
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
            foreach (var w in m_Worlds) if (w != null && w.IsCreated) w.Dispose();
            m_Worlds.Clear();
            foreach (var g in m_Made) if (g != null) Object.DestroyImmediate(g);
            m_Made.Clear();
        }

        GameObject Body(string name) { var g = new GameObject(name); g.hideFlags = HideFlags.HideAndDontSave; m_Made.Add(g); return g; }
        World NewWorld(string name) { var w = new World(name); m_Worlds.Add(w); return w; }

        // The registry never reads the entity manager, so plain ids stand in for ghosts.
        static Entity GhostId(int i) => new Entity { Index = i + 1, Version = 1 };

        static GameObject BodyOf(GhostBodyEntry e) => e?.Body;
        static GameObject BodyOf(ScanRegistry.Rec r) => r?.Body;

        [Test]
        public void MapLookups_GiveTheOldScansAnswers_OverARandomScript()
        {
            var rng = new System.Random(20261115);
            var old = new ScanRegistry();
            var worlds = new World[] { NewWorld("lookup-a"), NewWorld("lookup-b"), NewWorld("lookup-c") };
            const int ghosts = 24, ids = 6, keys = 5;   // small pools: re-registers and duplicate ids / keys happen often
            int made = 0;

            for (int step = 0; step < 3000; step++)
            {
                World w = worlds[rng.Next(worlds.Length)];
                Entity g = GhostId(rng.Next(ghosts));
                int roll = rng.Next(100);
                string what;

                if (roll < 40)
                {
                    int id = rng.Next(ids);
                    var key = rng.Next(3) == 0 ? PlayerKey.None : new PlayerKey((ulong)(1 + rng.Next(keys)));
                    var body = Body("b" + made++);
                    old.Register(w, g, id, key, body);
                    var entry = GhostBodyRegistry.Register(w, g, id, key, body, false);
                    Assert.AreSame(body, entry.Body);
                    what = $"register {g.Index} id {id} key {key}";
                }
                else if (roll < 50)
                {
                    bool a = old.Unregister(w, g, out var r);
                    bool b = GhostBodyRegistry.Unregister(w, g, out var e);
                    Assert.AreEqual(a, b, $"step {step}: unregister found");
                    Assert.AreSame(BodyOf(r), BodyOf(e), $"step {step}: unregister entry");
                    what = "unregister";
                }
                else if (roll < 58)
                {
                    // A body destroyed from outside (both registries hold the same GameObject).
                    var alive = old.Entries;
                    if (alive.Count > 0) Object.DestroyImmediate(alive[rng.Next(alive.Count)].Body);
                    what = "destroy a body";
                }
                else if (roll < 60)
                {
                    // A world goes away without TakeWorld (a disposed world must leave no entries), and a new one takes its slot.
                    int s = rng.Next(worlds.Length);
                    worlds[s].Dispose();
                    worlds[s] = NewWorld("lookup-new-" + step);
                    what = "dispose a world";
                }
                else if (roll < 62)
                {
                    var a = old.TakeWorld(w);
                    var b = GhostBodyRegistry.TakeWorld(w);
                    Assert.AreEqual(a.Count, b.Count, $"step {step}: TakeWorld count");
                    for (int i = 0; i < a.Count; i++) Assert.AreSame(a[i].Body, b[i].Body, $"step {step}: TakeWorld order");
                    what = "take world";
                }
                else
                {
                    // Lookups, in a random order between the three kinds (each may drop dead entries on the way).
                    int id = rng.Next(ids);
                    var key = new PlayerKey((ulong)rng.Next(keys + 1));   // 0 is None
                    Assert.AreEqual(old.TryGetByGhost(w, g, out var r1), GhostBodyRegistry.TryGetByGhost(w, g, out var e1), $"step {step}: by ghost");
                    Assert.AreSame(BodyOf(r1), BodyOf(e1), $"step {step}: by ghost entry");
                    Assert.AreEqual(old.TryGetByNetworkId(w, id, out var r2), GhostBodyRegistry.TryGetByNetworkId(w, id, out var e2), $"step {step}: by id");
                    Assert.AreSame(BodyOf(r2), BodyOf(e2), $"step {step}: by id entry");
                    Assert.AreEqual(old.TryGetByKey(key, out var r3), GhostBodyRegistry.TryGetByKey(key, out var e3), $"step {step}: by key");
                    Assert.AreSame(BodyOf(r3), BodyOf(e3), $"step {step}: by key entry");
                    what = "lookups";
                }

                // After every step: the same live entries, in the same order, and the view list in step with them.
                var oldList = old.Entries;
                var newList = GhostBodyRegistry.Entries;
                Assert.AreEqual(oldList.Count, GhostBodyRegistry.Count, $"step {step} ({what}): count");
                Assert.AreEqual(oldList.Count, newList.Count, $"step {step} ({what}): entries");
                Assert.AreEqual(newList.Count, PlayerBodies.All.Count, $"step {step} ({what}): views");
                for (int i = 0; i < oldList.Count; i++)
                {
                    Assert.AreSame(oldList[i].Body, newList[i].Body, $"step {step} ({what}): order at {i}");
                    Assert.AreSame(newList[i].View, PlayerBodies.All[i], $"step {step} ({what}): view at {i}");
                }
            }
        }

        [Test]
        public void AfterTheFirstWithAnIdGoes_TheNextInListOrderAnswers()
        {
            var w = NewWorld("lookup-dupes");
            var first = Body("first"); var second = Body("second"); var third = Body("third");
            GhostBodyRegistry.Register(w, GhostId(1), 9, new PlayerKey(4), first, false);
            GhostBodyRegistry.Register(w, GhostId(2), 9, new PlayerKey(4), second, false);
            GhostBodyRegistry.Register(w, GhostId(3), 9, new PlayerKey(4), third, false);
            Assert.IsTrue(GhostBodyRegistry.TryGetByNetworkId(w, 9, out var e)); Assert.AreSame(first, e.Body);
            Object.DestroyImmediate(first);
            Assert.IsTrue(GhostBodyRegistry.TryGetByNetworkId(w, 9, out e)); Assert.AreSame(second, e.Body);
            Assert.IsTrue(GhostBodyRegistry.TryGetByKey(new PlayerKey(4), out e)); Assert.AreSame(second, e.Body);
            GhostBodyRegistry.Unregister(w, GhostId(2), out _);
            Assert.IsTrue(GhostBodyRegistry.TryGetByNetworkId(w, 9, out e)); Assert.AreSame(third, e.Body);
            Assert.IsTrue(GhostBodyRegistry.TryGetByKey(new PlayerKey(4), out e)); Assert.AreSame(third, e.Body);
            // Re-registering the first ghost puts it LAST in the list: the third stays the answer, as with the old scan.
            var again = Body("again");
            GhostBodyRegistry.Register(w, GhostId(1), 9, new PlayerKey(4), again, false);
            Assert.IsTrue(GhostBodyRegistry.TryGetByNetworkId(w, 9, out e)); Assert.AreSame(third, e.Body);
            Assert.IsTrue(GhostBodyRegistry.TryGetByGhost(w, GhostId(1), out e)); Assert.AreSame(again, e.Body);
            Assert.IsFalse(GhostBodyRegistry.TryGetByNetworkId(NewWorld("lookup-other"), 9, out _), "ids are per world");
        }

        [TestCase(50), TestCase(200), TestCase(1000)]
        public void Timing_OneFrameOfPerPlayerLookups_OldScanVsMaps(int n)
        {
            var w = NewWorld("lookup-timing");
            var old = new ScanRegistry();
            for (int i = 0; i < n; i++)
            {
                var body = Body("t" + i);
                old.Register(w, GhostId(i), i, new PlayerKey((ulong)(i + 1)), body);
                GhostBodyRegistry.Register(w, GhostId(i), i, new PlayerKey((ulong)(i + 1)), body, false);
            }

            // One "frame" = what the per-player systems do: each player looked up by ghost, by network id and by key.
            int frames = n >= 1000 ? 3 : 20;
            long oldTicks = Frames(frames, () =>
            {
                for (int i = 0; i < n; i++)
                {
                    old.TryGetByGhost(w, GhostId(i), out _);
                    old.TryGetByNetworkId(w, i, out _);
                    old.TryGetByKey(new PlayerKey((ulong)(i + 1)), out _);
                }
            });
            long newTicks = Frames(frames, () =>
            {
                for (int i = 0; i < n; i++)
                {
                    GhostBodyRegistry.TryGetByGhost(w, GhostId(i), out _);
                    GhostBodyRegistry.TryGetByNetworkId(w, i, out _);
                    GhostBodyRegistry.TryGetByKey(new PlayerKey((ulong)(i + 1)), out _);
                }
            });

            double oldMs = oldTicks * 1000.0 / Stopwatch.Frequency / frames;
            double newMs = newTicks * 1000.0 / Stopwatch.Frequency / frames;
            string line = $"[perf chunk 1] GhostBodyRegistry N={n}: old scan {oldMs:F3} ms/frame, maps {newMs:F4} ms/frame, x{(newMs > 0 ? oldMs / newMs : 0):F0}";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);

            // Same answers on the timed data, and at N = 1000 the maps must be clearly faster (very loose: the expected gap is ~100x+).
            for (int i = 0; i < n; i++)
            {
                Assert.IsTrue(GhostBodyRegistry.TryGetByNetworkId(w, i, out var e));
                Assert.IsTrue(old.TryGetByNetworkId(w, i, out var r));
                Assert.AreSame(r.Body, e.Body);
            }
            if (n >= 1000) Assert.Less(newMs * 5, oldMs, line);
        }

        static long Frames(int frames, System.Action frame)
        {
            frame();   // warm up (JIT)
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++) frame();
            sw.Stop();
            return sw.ElapsedTicks;
        }
    }
}
