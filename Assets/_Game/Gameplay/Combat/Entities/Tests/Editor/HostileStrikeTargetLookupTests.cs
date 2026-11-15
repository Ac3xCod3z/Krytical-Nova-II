using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;
using World = Unity.Entities.World;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>
    /// Perf night chunk 4: <c>HostileStrikeServerSystem</c> and <c>HostileStrikeBridgeServerSystem</c> found the struck player by
    /// scanning every registry entry per strike; they now ask <see cref="GhostBodyRegistry.TryGetByGhost"/>. This runs the old scan
    /// and the map lookup on the same strikes (targets present, absent, of another world, with a destroyed body) and checks the same
    /// player every time, then times 100 strikes at N = 50 / 200 / 1000 players.
    /// </summary>
    public sealed class HostileStrikeTargetLookupTests
    {
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<World> m_Worlds = new List<World>();

        [SetUp] public void SetUp() { GhostBodyRegistry.Clear(); LocalPlayer.Set(null); }

        [TearDown]
        public void TearDown()
        {
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
            foreach (var w in m_Worlds) if (w.IsCreated) w.Dispose();
            m_Worlds.Clear();
            foreach (var g in m_Made) if (g != null) Object.DestroyImmediate(g);
            m_Made.Clear();
        }

        static Entity Ghost(int i) => new Entity { Index = i + 1, Version = 1 };

        // The old loop of both systems, verbatim.
        static GhostBodyEntry OldScan(World world, Entity target)
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (ReferenceEquals(entry.World, world) && entry.Ghost == target && entry.Body != null) return entry;
            return null;
        }

        static GhostBodyEntry NewLookup(World world, Entity target)
        {
            GhostBodyRegistry.TryGetByGhost(world, target, out GhostBodyEntry hit);
            return hit;
        }

        void Fill(int n, out World server, out World other)
        {
            server = new World("strike-server"); other = new World("strike-other");
            m_Worlds.Add(server); m_Worlds.Add(other);
            for (int i = 0; i < n; i++)
            {
                var body = new GameObject("player" + i) { hideFlags = HideFlags.HideAndDontSave }; m_Made.Add(body);
                // the same ghost ids live in the other world too (a host's client world): a strike must find the server world's
                GhostBodyRegistry.Register(i % 2 == 0 ? server : other, Ghost(i), i, new PlayerKey((ulong)(i + 1)), body, false);
                if (i % 2 == 0)
                {
                    var twin = new GameObject("twin" + i) { hideFlags = HideFlags.HideAndDontSave }; m_Made.Add(twin);
                    GhostBodyRegistry.Register(other, Ghost(i), i, PlayerKey.None, twin, false);
                }
            }
        }

        [Test]
        public void TheMapLookup_FindsTheSamePlayer_AsTheOldScan()
        {
            Fill(60, out World server, out World other);
            var rng = new System.Random(4);
            for (int s = 0; s < 400; s++)
            {
                if (s % 50 == 25)   // a body destroyed from outside between strikes
                {
                    var alive = GhostBodyRegistry.Entries;
                    Object.DestroyImmediate(alive[rng.Next(alive.Count)].Body);
                }
                World w = rng.Next(4) == 0 ? other : server;
                Entity target = Ghost(rng.Next(80));   // some ids have no player
                GhostBodyEntry a = OldScan(w, target), b = NewLookup(w, target);
                Assert.AreSame(a, b, $"strike {s}: ghost {target.Index}");
            }
        }

        [TestCase(50), TestCase(200), TestCase(1000)]
        public void Timing_100Strikes_OldScanVsMap(int n)
        {
            Fill(n, out World server, out _);
            var targets = new Entity[100];
            var rng = new System.Random(n);
            for (int i = 0; i < targets.Length; i++) targets[i] = Ghost(rng.Next(n / 2) * 2);   // the server world's players
            int rounds = n >= 1000 ? 5 : 30;
            foreach (Entity t in targets) Assert.AreSame(OldScan(server, t), NewLookup(server, t));   // same answers, and warm up

            var sw = Stopwatch.StartNew();
            for (int r = 0; r < rounds; r++) foreach (Entity t in targets) OldScan(server, t);
            double oldMs = sw.Elapsed.TotalMilliseconds / rounds;
            sw.Restart();
            for (int r = 0; r < rounds; r++) foreach (Entity t in targets) NewLookup(server, t);
            double newMs = sw.Elapsed.TotalMilliseconds / rounds;
            string line = $"[perf chunk 4] Hostile strike target, 100 strikes, N={n} players: old scan {oldMs:F3} ms, map {newMs:F4} ms";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            if (n >= 1000) Assert.Less(newMs * 5, oldMs, line);
        }
    }
}
