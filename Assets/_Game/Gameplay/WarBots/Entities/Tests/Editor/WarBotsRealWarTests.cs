using System.Collections;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Crossfire.Entities;
using KryticalNova.Factions;
using KryticalNova.Networking.Entities;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Persistence;
using KryticalNova.Persistence.Entities;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace KryticalNova.WarBots.Entities.Tests
{
    /// <summary>
    /// War Bots with the REAL Player.prefab on a real server world (Master-24, 16 Nov 2026: plan B of the War Bots test phase, while no
    /// map has Rally Points yet). The real <see cref="WarBotServerSystem"/> is asked by Crossfire's <see cref="IWarBotFill"/> for 32 bots
    /// on each of two sides; each comes in through the game's own server setup (<see cref="PlayerWorldsSetup.InstallServer"/>: the real
    /// admission, ghost subscene and leave handler) at its side's Rally Point, the gates are open (no Crossfire clock in a test world),
    /// the bots find each other and fight until one side kills one of the other, and at the war's end every bot leaves.
    /// The real prefab needs Awake / OnEnable / Start, so the test enters Play mode itself (no domain reload in this project) and leaves it
    /// at the end. Explicit, like <c>RealPrefabSmokeTests</c>: never part of the batch EditMode run; run it by name, with nothing else
    /// using the Editor. Saves go to a <see cref="MemorySaveStore"/> (bots save nothing anyway).
    /// </summary>
    [Explicit("Real Player.prefab in Play mode: run by name only (War Bots plan B, Master-24 16 Nov 2026)")]
    public sealed class WarBotsRealWarTests
    {
        const int PerSide = 32;
        const string GhostSubScenePath = "Assets/_Game/Gameplay/Player/Entities/Scenes/PlayerGhostPrefabs.unity";   // the real one, as StrikeDummyTests
        const int MaxFightTicks = 5400;   // 3 minutes of game time at 30 Hz
        static readonly Vector3 Centre = new Vector3(900f, 0f, 900f);   // away from anything the scene has
        // 100 m apart, beyond a bot's sight (60 m), as the Crucible's yards are: the bots must hunt each other down (Master, 17 Nov 2026)
        static readonly Vector3 HegemonyRally = Centre + new Vector3(-50f, 0f, 0f);
        static readonly Vector3 NullRally = Centre + new Vector3(50f, 0f, 0f);

        [UnityTest]
        public IEnumerator ThirtyTwoBotsASide_ComeInAtTheirRallyPoint_FightAndKill_AndAllLeaveAtTheWarsEnd()
        {
            yield return new EnterPlayMode();
            LogAssert.ignoreFailingMessages = true;   // bodies without a NetworkManager print their NGO stubs' lines
            INetRoleAdapter oldAdapter = NetRole.Adapter;
            ISaveStore oldStore = ServerSave.Store;
            IWarBotFill hadFill = KryticalNova.Crossfire.Entities.WarBots.Fill;
            var made = new List<Object>();
            UnityEngine.AI.NavMeshDataInstance ground = default;
            KnTestWorld tw = null;
            WarBotServerSystem bots = null;
            var killsBy = new Dictionary<FactionId, int> { [FactionId.Hegemony] = 0, [FactionId.Null] = 0 };
            int anyKilled = 0, deaths = 0, botsKilled = 0;
            System.Action<CombatEntity, KillingBlow> onKill = null;
            try
            {
                BodyRoleNetRoleAdapter.Install();
                ServerSave.Store = new MemorySaveStore();
                GhostBodyRegistry.Clear();
                PlayerKeys.Reset();
                CrossfireClockServerSystem.ClockSource = () => (false, default(KryticalNova.World.WorldEnvironmentState));   // no clock: gates open
                ground = Ground(made);

                tw = new KnTestWorld();
                // one client in game, as a war always has (the vote needs a real player): netcode gives ghosts their ids only while a
                // connection is in game (GhostSendSystem), and a bot finds and targets the others by ghost id (run 2: 0 kills without one)
                tw.CreateWorlds(1);
                // the game's server setup; its real ghost subscene loaded the test world's way (ticked by hand, so it streams in here)
                Assert.IsNotNull(PlayerWorldsSetup.InstallServer(tw.Server, loadGhostScene: false), "the game's server setup");
                string guid = UnityEditor.AssetDatabase.AssetPathToGUID(GhostSubScenePath);
                Assert.IsFalse(string.IsNullOrEmpty(guid), "no ghost subscene: run Build Player Ghost Assets");
                tw.LoadSubScene(new Unity.Entities.Hash128(guid), 600);   // server and client: a client without it is disconnected
                tw.Connect(goInGame: true);
                Assert.GreaterOrEqual(tw.TickUntil(() => Ready(tw.Server), 600), 0,
                    "the real PlayerGhostPrefab is in the server world (the admission's own check)");
                bots = tw.Server.GetExistingSystemManaged<WarBotServerSystem>();
                Assert.IsNotNull(bots, "the server world runs War Bots");
                Assert.AreSame(bots, KryticalNova.Crossfire.Entities.WarBots.Fill, "War Bots is Crossfire's IWarBotFill");
                yield return null;
                // this test world's client has no body (no real player in the war): a long grace for the fight, the real one at the end
                var settings = Object.Instantiate(WarBotSettings.Instance);
                made.Add(settings);
                settings.noRealPlayerSeconds = 100000f;
                bots.Settings = settings;
                // the gates open: the server world's own Crossfire clock is put in the War with a GM's start + skip on a still clock (it ran
                // on its own fixed cycle here, so the bots waited shut in their yards: EditMode at ae71eb0b0, 0 kills in 5,400 ticks)
                var clock = tw.Server.GetExistingSystemManaged<CrossfireClockServerSystem>();
                Assert.IsNotNull(clock, "the server world runs Crossfire's clock");
                var war = Object.Instantiate(KryticalNova.Crossfire.CrossfireSettings.Instance);
                made.Add(war);
                war.anchorToNightfall = false;
                war.warMinutes = 60f;
                clock.Settings = war;
                clock.Clock = () => 5000.0;
                clock.Interval = 0f;
                for (int i = 0; i < 5; i++) { tw.Tick(1); yield return null; }
                Assert.IsTrue(clock.Run(CrossfireCommand.StartNow), "GM start: the Rally");
                for (int i = 0; i < 5; i++) { tw.Tick(1); yield return null; }
                Assert.IsTrue(clock.Run(CrossfireCommand.Skip), "GM skip: the War, gates open");
                for (int i = 0; i < 5; i++) { tw.Tick(1); yield return null; }
                Assert.AreEqual(KryticalNova.Crossfire.CrossfirePhase.War, clock.Current.Now.Phase, "the War: the gates are open");

                // the vote passed on both sides: Crossfire asks for 32 each, at its side's Rally Point, bracket X
                int hegemony = bots.Fill(new WarBotRequest { Faction = FactionId.Hegemony, Bracket = 10, Count = PerSide, RallyAt = HegemonyRally, RallyYaw = 90f },
                    new List<Entity>());
                int nul = bots.Fill(new WarBotRequest { Faction = FactionId.Null, Bracket = 10, Count = PerSide, RallyAt = NullRally, RallyYaw = 270f },
                    new List<Entity>());
                Assert.AreEqual(PerSide, hegemony, "Hegemony's 32 came in: " + bots.LastRefusal);
                Assert.AreEqual(PerSide, nul, "Null's 32 came in: " + bots.LastRefusal);
                Assert.AreEqual(2 * PerSide, bots.Live);
                Assert.AreEqual(PerSide, bots.Count(FactionId.Hegemony));
                Assert.AreEqual(PerSide, bots.Count(FactionId.Null));

                // each at its own Rally Point, a real body with a real ghost
                EntityManager em = tw.Server.EntityManager;
                var botGhosts = new List<Entity>();
                foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                {
                    if (!ReferenceEquals(e.World, tw.Server) || !bots.IsWarBot(e.Ghost)) continue;
                    botGhosts.Add(e.Ghost);
                    Assert.IsTrue(PlayerKeys.IsServerBot(e.Key), "a server-minted key");
                    Assert.Less(e.NetworkId, 0, "a ghost owner no connection has");
                }
                Assert.AreEqual(2 * PerSide, botGhosts.Count, "every bot has a ghost and a body");
                for (int i = 0; i < 10; i++) { tw.Tick(1); yield return null; }
                var ids = new HashSet<int>();
                foreach (Entity g in botGhosts)
                    if (em.HasComponent<Unity.NetCode.GhostInstance>(g)) ids.Add(em.GetComponentData<Unity.NetCode.GhostInstance>(g).ghostId);
                ids.Remove(0);
                Assert.AreEqual(2 * PerSide, ids.Count, "every bot ghost has an id of its own (what bots target by)");
                foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                {
                    if (!ReferenceEquals(e.World, tw.Server) || !bots.IsWarBot(e.Ghost) || e.Body == null) continue;
                    Vector3 rally = bots.SideOf(e.Ghost) == FactionId.Hegemony ? HegemonyRally : NullRally;
                    Vector3 d = e.Body.transform.position - rally; d.y = 0f;
                    Assert.Less(d.magnitude, 8f, e.Body.name + " came in at its own Rally Point");
                }

                // the fight: the kills each side makes, as Crossfire counts them (CombatEntity.AnyKilled, the killer's side)
                onKill = (victim, blow) =>
                {
                    anyKilled++;
                    FactionId killer = SideOfBody(bots, tw.Server, blow.Attacker != null ? blow.Attacker.gameObject : null);
                    FactionId dead = SideOfBody(bots, tw.Server, victim != null ? victim.gameObject : null);
                    if (killer != FactionId.None && dead != FactionId.None && killer != dead) killsBy[killer]++;
                    if (dead != FactionId.None) botsKilled++;   // a bot's death, as the game reports it (the loop stops before it sees the body down)
                };
                CombatEntity.AnyKilled += onKill;
                var wasDead = new HashSet<Entity>();
                int ticks = 0;
                while (ticks < MaxFightTicks && killsBy[FactionId.Hegemony] + killsBy[FactionId.Null] == 0)
                {
                    tw.Tick(1);
                    ticks++;
                    foreach (Entity g in botGhosts)
                    {
                        if (!em.Exists(g)) continue;
                        bool dead = Dead(em, g);
                        if (dead && wasDead.Add(g)) deaths++;
                        else if (!dead) wasDead.Remove(g);
                    }
                    yield return null;
                }
                int score = killsBy[FactionId.Hegemony] + killsBy[FactionId.Null];
                Debug.Log($"[WarBotsRealWar] admitted {bots.Admitted}, fight {ticks} ticks, kill events {anyKilled}, bot deaths {deaths}, "
                          + $"score Hegemony {killsBy[FactionId.Hegemony]} / Null {killsBy[FactionId.Null]}, live {bots.Live}");
                // the evidence per bot (Master, 17 Nov 2026): how far it moved from its yard, whether it has a target, the health it lost
                var lines = new System.Text.StringBuilder();
                int moved = 0, targeting = 0, hurt = 0;
                foreach (Entity g in botGhosts)
                {
                    if (!em.Exists(g)) continue;
                    FactionId side = bots.SideOf(g);
                    var p = em.GetComponentData<PlayerPose>(g);
                    Vector3 at = new Vector3(p.XMm, p.YMm, p.ZMm) * 0.001f, from = side == FactionId.Hegemony ? HegemonyRally : NullRally;
                    float away = Vector3.Distance(new Vector3(at.x, 0f, at.z), new Vector3(from.x, 0f, from.z));
                    int target = em.HasComponent<CombatState>(g) ? em.GetComponentData<CombatState>(g).TargetGhostId : 0;
                    float lost = 0f;
                    if (em.HasComponent<PlayerVitals>(g))
                    {
                        var v = em.GetComponentData<PlayerVitals>(g);
                        lost = v.MaxHealthMilli > 0 ? 1f - (float)v.HealthMilli / v.MaxHealthMilli : 0f;
                    }
                    if (away > 8f) moved++;
                    if (target != 0) targeting++;
                    if (lost > 0f) hurt++;
                    lines.Append($" {side}:{away:0}m/t{target}/-{lost:P0}");
                }
                Debug.Log($"[WarBotsRealWar] the bots chose: {Join(bots.Plans)}; casts refused: {Join(bots.Refusals)}");
                Debug.Log($"[WarBotsRealWar] per bot (side:metres from yard/target ghost/health lost): moved {moved}, targeting {targeting}, hurt {hurt};{lines}");

                // no real player in the war (the client has no body): past the grace every bot goes home, through the game's leave
                settings.noRealPlayerSeconds = 1f;
                int homeTicks = tw.TickUntil(() => bots.Live == 0, 120);
                for (int i = 0; i < 10; i++) { tw.Tick(1); yield return null; }
                Debug.Log($"[WarBotsRealWar] after the guard: live {bots.Live}, removed {bots.Removed}, went home alone {bots.WentHomeAlone} (in {homeTicks} ticks)");
                Assert.Greater(deaths + botsKilled, 0, $"a bot died in {ticks} ticks (kill events {anyKilled}, bots killed {botsKilled}, bodies seen down {deaths})");
                Assert.Greater(score, 0, $"a kill counted for the killer's side, as Crossfire scores it (kill events {anyKilled}, bot deaths {deaths})");
                Assert.AreEqual(1, bots.WentHomeAlone, "no real player left: the bots went home on their own");
                Assert.AreEqual(0, bots.Live, "no bot is left");
                Assert.AreEqual(2 * PerSide, bots.Removed, "every bot went");
                int ghostsLeft = 0;
                foreach (Entity g in botGhosts) if (em.Exists(g)) ghostsLeft++;
                Assert.AreEqual(0, ghostsLeft, "their ghosts went too");
                foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                    Assert.IsFalse(PlayerKeys.IsServerBot(e.Key), "no bot body is registered");
            }
            finally
            {
                if (onKill != null) CombatEntity.AnyKilled -= onKill;
                bots?.EndWar();
                tw?.Dispose();
                ground.Remove();
                foreach (Object o in made) if (o != null) Object.Destroy(o);
                CrossfireClockServerSystem.ClockSource = null;
                KryticalNova.Crossfire.Entities.WarBots.Fill = hadFill;
                GhostBodyRegistry.Clear();
                PlayerKeys.Reset();
                ServerSave.Store = oldStore;
                NetRole.Install(oldAdapter);
                LogAssert.ignoreFailingMessages = false;
            }
            yield return new ExitPlayMode();
        }

        static bool Ready(Unity.Entities.World server) => KnTestWorld.Count<PlayerGhostPrefab>(server) == 1;

        static bool Dead(EntityManager em, Entity ghost)
        {
            if (em.HasComponent<DeathState>(ghost) && em.GetComponentData<DeathState>(ghost).Pose != 0) return true;
            if (!em.HasComponent<PlayerVitals>(ghost)) return false;
            PlayerVitals v = em.GetComponentData<PlayerVitals>(ghost);
            return v.MaxHealthMilli != 0 && v.HealthMilli <= 0;
        }

        static string Join(IReadOnlyDictionary<string, int> d)
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kv in d) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value);
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        static FactionId SideOfBody(WarBotServerSystem bots, Unity.Entities.World server, GameObject body)
        {
            if (body == null) return FactionId.None;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, server) || e.Body == null) continue;
                if (e.Body == body || body.transform.IsChildOf(e.Body.transform)) return bots.SideOf(e.Ghost);
            }
            return FactionId.None;
        }

        /// <summary>Flat walkable ground with a NavMesh of its own (the server's planner paths on it) and a floor for the bodies.</summary>
        static UnityEngine.AI.NavMeshDataInstance Ground(List<Object> made)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            made.Add(floor);
            floor.transform.position = Centre;
            floor.transform.localScale = new Vector3(20f, 1f, 20f);   // 200 m
            var source = new UnityEngine.AI.NavMeshBuildSource
            {
                shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
                size = new Vector3(200f, 0.2f, 200f),
                transform = Matrix4x4.TRS(Centre + Vector3.down * 0.1f, Quaternion.identity, Vector3.one),
                area = 0,
            };
            UnityEngine.AI.NavMeshData data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(UnityEngine.AI.NavMesh.GetSettingsByIndex(0),
                new List<UnityEngine.AI.NavMeshBuildSource> { source },
                new Bounds(Centre, new Vector3(210f, 20f, 210f)), Vector3.zero, Quaternion.identity);
            return UnityEngine.AI.NavMesh.AddNavMeshData(data);
        }
    }
}
