using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using KryticalNova.Networking.Entities.Tests;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Server + N clients in one process (KnTestWorld), the player ghost spawned for every client, planners and scripted command
    /// sources installed. Client i owns ghost i. Everything is ticked by hand, so a test is deterministic.
    /// </summary>
    public sealed class PlayerRig : IDisposable
    {
        public KnTestWorld Tw;
        public World Server;
        public World[] Clients;
        public Entity[] ServerGhosts;                 // ghost of client i, in the server world
        public ScriptedCommandSource[] Sources;       // command source of client i
        public StraightLinePlanner ServerPlanner = new StraightLinePlanner();
        public StraightLinePlanner ClientPlanner = new StraightLinePlanner();

        public static PlayerRig Create(int clients = 2, int delayMs = 0, int3[] starts = null, int warmupTicks = 60, int dropInterval = 0)
        {
            var rig = new PlayerRig();
            rig.Tw = new KnTestWorld { SimulatedDelayMs = delayMs, SimulatedDropInterval = dropInterval };
            rig.Tw.CreateWorlds(clients);
            rig.Tw.LoadSubScene(PlayerTestAssets.SubSceneGuid);
            rig.Tw.Connect();
            rig.Server = rig.Tw.Server;
            rig.Clients = rig.Tw.Clients.ToArray();
            rig.Sources = new ScriptedCommandSource[clients];
            rig.ServerGhosts = new Entity[clients];
            PlayerEntitiesSetup.Install(rig.Server, rig.ServerPlanner);
            for (int i = 0; i < clients; i++)
            {
                rig.Sources[i] = new ScriptedCommandSource();
                PlayerEntitiesSetup.Install(rig.Clients[i], rig.ClientPlanner, rig.Sources[i]);
            }

            var prefab = KnTestWorld.Singleton<PlayerGhostPrefab>(rig.Server).Prefab;
            var em = rig.Server.EntityManager;
            for (int i = 0; i < clients; i++)
            {
                var at = starts != null && i < starts.Length ? starts[i] : new int3(0, 0, 5000 * i);
                rig.ServerGhosts[i] = PlayerEntitiesSetup.SpawnServer(em, prefab, KnTestWorld.NetworkIdOf(rig.Clients[i]), at);
            }

            int ok = rig.Tw.TickUntil(() =>
            {
                foreach (var c in rig.Clients)
                    if (KnTestWorld.Count<PlayerGhostTag>(c) != clients) return false;
                return true;
            }, 300);
            Assert.GreaterOrEqual(ok, 0, "player ghosts did not reach every client. Logs: " + string.Join(" | ", rig.Tw.Interesting));
            rig.Tw.Tick(warmupTicks);   // let the client clock settle: the first inputs must arrive on time
            return rig;
        }

        public void Tick(int n = 1) => Tw.Tick(n);

        public uint ServerTick => KnTestWorld.Singleton<NetworkTime>(Server).ServerTick.TickIndexForValidTick;

        // ---- finding ghosts ------------------------------------------------------------------------------------------
        /// <summary>The ghost client <paramref name="viewer"/> sees for the player of client <paramref name="owner"/>.</summary>
        public Entity GhostIn(int viewer, int owner)
        {
            var w = Clients[viewer];
            int ownerId = KnTestWorld.NetworkIdOf(Clients[owner]);
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<GhostOwner>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
                if (w.EntityManager.GetComponentData<GhostOwner>(e).NetworkId == ownerId) return e;
            Assert.Fail($"client {viewer} has no ghost of client {owner}");
            return Entity.Null;
        }

        public T Get<T>(World w, Entity e) where T : unmanaged, IComponentData => w.EntityManager.GetComponentData<T>(e);

        public MoveState ServerMove(int owner = 0) => Get<MoveState>(Server, ServerGhosts[owner]);
        public PlayerPose ServerPose(int owner = 0) => Get<PlayerPose>(Server, ServerGhosts[owner]);
        public MoveState OwnerMove(int owner = 0) => Get<MoveState>(Clients[owner], GhostIn(owner, owner));
        public PlayerPose OwnerPose(int owner = 0) => Get<PlayerPose>(Clients[owner], GhostIn(owner, owner));

        public static float3 M(PlayerPose p) => new float3(p.XMm, p.YMm, p.ZMm) * 0.001f;

        // ---- history comparison --------------------------------------------------------------------------------------
        public struct TraceCompare { public int Common, Mismatch, ServerOnly, Skipped; public int FirstMismatchTick; public string FirstDump; }

        /// <summary>
        /// Ticks the owner could not predict: a server order (or placement) takes effect on the first tick after it is issued, and the
        /// owner's trace entry for that one tick keeps its misprediction (the snapshot of that tick rolls it back and re-simulates only
        /// LATER ticks). The compare skips them; the state after them must still be equal.
        /// </summary>
        /// <summary>Skip the tick on which the server's order number changes (see CompareOwner); the following ticks are still compared.</summary>
        public bool SkipOrderStartTicks = true;

        public readonly System.Collections.Generic.HashSet<uint> Unpredictable = new System.Collections.Generic.HashSet<uint>();

        public void IssueOrder(int owner, int3 goal, bool runPaid = false)
        {
            uint next = ServerTick + 1;
            Unpredictable.Add(next); Unpredictable.Add(next + 1);
            PlayerEntitiesSetup.IssueOrder(Server.EntityManager, ServerGhosts[owner], goal, runPaid);
        }

        public void Teleport(int owner, int3 pos, int yaw = 0)
        {
            uint next = ServerTick + 1;
            Unpredictable.Add(next); Unpredictable.Add(next + 1);
            PlayerEntitiesSetup.Teleport(Server.EntityManager, ServerGhosts[owner], pos, yaw);
        }

        static string Dump(PlayerMoveTrace t) =>
            $"tick {t.Tick} order {t.OrderSeq} flags {t.Flags} progress {t.ProgressMm} speed {t.SpeedMmps} pos ({t.XMm},{t.YMm},{t.ZMm}) yaw {t.YawCdeg} runs {t.RunsForTick}";

        /// <summary>Compares the server's per-tick ring with the owner client's for ticks in [fromTick, toTick].</summary>
        public TraceCompare CompareOwner(int owner, uint fromTick, uint toTick)
        {
            var sh = Server.EntityManager.GetBuffer<PlayerMoveTrace>(ServerGhosts[owner], true);
            var ch = Clients[owner].EntityManager.GetBuffer<PlayerMoveTrace>(GhostIn(owner, owner), true);
            var r = new TraceCompare { FirstMismatchTick = -1 };
            for (int i = 0; i < sh.Length && i < ch.Length; i++)
            {
                var s = sh[i]; var c = ch[i];
                if (s.Tick == 0 || s.Tick < fromTick || s.Tick > toTick || Unpredictable.Contains(s.Tick)) continue;
                if (SkipOrderStartTicks && s.Tick > 0 && sh[(int)((s.Tick - 1) % (uint)sh.Length)] is var prev && prev.Tick == s.Tick - 1 && prev.OrderSeq != s.OrderSeq)
                { r.Skipped++; continue; }   // the server took up an order on this tick: the owner's entry for exactly this tick may be one step behind
                if (c.Tick != s.Tick) { r.ServerOnly++; continue; }
                r.Common++;
                if (s.ProgressMm != c.ProgressMm || s.SpeedMmps != c.SpeedMmps || s.XMm != c.XMm || s.YMm != c.YMm || s.ZMm != c.ZMm
                    || s.Flags != c.Flags || s.OrderSeq != c.OrderSeq || s.YawCdeg != c.YawCdeg)
                {
                    r.Mismatch++;
                    if (r.FirstMismatchTick < 0) { r.FirstMismatchTick = (int)s.Tick; r.FirstDump = "SERVER " + Dump(s) + " | OWNER " + Dump(c); }
                }
            }
            return r;
        }

        public void Dispose() => Tw?.Dispose();
    }
}
