using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    /// <summary>The game's rules as a test double: records the calls, answers what the test says.</summary>
    public sealed class FakeRules : IServerBodyRules
    {
        public string Answer;
        public List<string> Log;
        public Func<string> AdmittedProbe;
        public readonly List<GameObject> Bodies = new List<GameObject>();
        public readonly List<ServerBodyContext> Contexts = new List<ServerBodyContext>();

        public string Begin(GameObject body, in ServerBodyContext context)
        {
            Bodies.Add(body);
            Contexts.Add(context);
            Log?.Add("begin");
            return Answer;
        }

        public void Admitted(GameObject body, Entity ghost, in ServerBodyContext context) =>
            Log?.Add("admitted" + (AdmittedProbe != null ? "(" + AdmittedProbe() + ")" : string.Empty));
    }

    /// <summary>
    /// Stand-in for the rules of a real body that must hear the player leave (PlayerSaveAgent, PlayerTrade): records each call and
    /// what the test's probe says about the world at that moment (e.g. the ghost's owner id). Declared here, not in a file of its own
    /// name: Unity refuses to add a MonoBehaviour whose own script file sits in an Editor folder ("it is an editor script").
    /// </summary>
    public sealed class LeaveRecorder : MonoBehaviour, IServerLeaving
    {
        /// <summary>Shared log of the test (set before the call).</summary>
        public List<string> Log;
        /// <summary>What to write at the moment of the call (the test reads the world here).</summary>
        public Func<string> Probe;
        public int Calls;

        public void OnServerLeaving()
        {
            Calls++;
            Log?.Add("leaving" + (Probe != null ? "(" + Probe() + ")" : string.Empty));
        }
    }

    /// <summary>Hands the same character to its client world every time it is asked (the client system sends it once).</summary>
    public sealed class FixedGoInGame : IKnGoInGameSource
    {
        public GoInGamePayload Payload = new GoInGamePayload
        {
            CharacterId = "0123456789abcdef0123456789abcdef",
            Name = "Vesper",
            RecipeText = "R1|test",
        };

        public bool TryGet(World clientWorld, out GoInGamePayload payload)
        {
            payload = Payload;
            return true;
        }
    }

    /// <summary>
    /// Server + N clients (KnTestWorld, IPC), the player ghost subscene loaded, straight-line planners, and the real
    /// <see cref="ServerBodyAdmission"/> building a stand-in body (a plain GameObject with a <see cref="LeaveRecorder"/>) through the
    /// real assembler. Clients enter one by one with <see cref="Enter"/>. Ticked by hand.
    /// </summary>
    public sealed class BodyRig : IDisposable
    {
        public KnTestWorld Tw;
        public World Server;
        public GameObject Template;
        public ServerBodyAdmission Admission;
        public KnAdmissionSystem AdmissionSystem;
        public PlayerLeaveHandler Leave;
        public readonly FakeRules Rules = new FakeRules();
        public readonly List<string> Log = new List<string>();
        /// <summary>Spawn points the rig's picker hands out (first free one, 1.5 m clearance); empty = the origin.</summary>
        public readonly List<Vector3> SpawnPoints = new List<Vector3>();
        /// <summary>The occupied list each pick saw (copies).</summary>
        public readonly List<List<Vector3>> OccupiedSeen = new List<List<Vector3>>();

        public static BodyRig Create(int clients)
        {
            GhostBodyRegistry.Clear();
            KnPlayerRefusals.Clear();
            LocalPlayer.Set(null);
            var rig = new BodyRig();
            rig.Template = new GameObject("StandInBody");
            rig.Template.AddComponent<LeaveRecorder>();
            rig.Rules.Log = rig.Log;

            rig.Tw = new KnTestWorld();
            rig.Tw.CreateWorlds(clients);
            rig.Tw.LoadSubScene(PlayerTestAssets.SubSceneGuid);
            rig.Tw.Connect(goInGame: false);
            rig.Tw.Tick(3);
            rig.Server = rig.Tw.Server;
            PlayerEntitiesSetup.Install(rig.Server, new StraightLinePlanner());
            foreach (World c in rig.Tw.Clients) PlayerEntitiesSetup.Install(c, new StraightLinePlanner());

            rig.Admission = new ServerBodyAdmission(rig.Server, rig.Template, rig.Rules)
            {
                Trace = step => rig.Log.Add(step),
                Picker = rig.Pick,
            };
            rig.AdmissionSystem = rig.Server.GetExistingSystemManaged<KnAdmissionSystem>();
            rig.AdmissionSystem.Admission = rig.Admission;
            rig.Leave = new PlayerLeaveHandler(rig.Server) { Trace = step => rig.Log.Add(step), Forget = key => rig.Log.Add("forget") };
            rig.Server.GetExistingSystemManaged<ConnectionClosedSystem>().Handlers.Add(rig.Leave);
            return rig;
        }

        bool Pick(IReadOnlyList<Vector3> occupied, out Vector3 position, out Quaternion rotation)
        {
            OccupiedSeen.Add(new List<Vector3>(occupied));
            rotation = Quaternion.identity;
            position = default;
            foreach (Vector3 p in SpawnPoints)
            {
                bool taken = false;
                foreach (Vector3 o in occupied)
                    if (new Vector2(o.x - p.x, o.z - p.z).sqrMagnitude < 1.5f * 1.5f) taken = true;
                if (!taken) { position = p; return true; }
            }
            return false;
        }

        /// <summary>Client <paramref name="client"/> asks to go in game; ticks until the server decided. Returns ticks used.</summary>
        public int Enter(int client, int maxTicks = 120) => Enter(Tw.Clients[client], maxTicks);

        public int Enter(World client, int maxTicks = 120)
        {
            int before = AdmissionSystem.Admitted + AdmissionSystem.Refused;
            client.GetExistingSystemManaged<KnClientGoInGameSystem>().Source = new FixedGoInGame();
            int used = Tw.TickUntil(() => AdmissionSystem.Admitted + AdmissionSystem.Refused > before, maxTicks);
            Assert.GreaterOrEqual(used, 0, "the server never decided. Logs: " + string.Join(" | ", Tw.Interesting));
            return used;
        }

        /// <summary>The client hangs up; ticks until the server has seen the close (the key is dead). Returns ticks used.</summary>
        public int Disconnect(World client, int maxTicks = 120)
        {
            PlayerKey key = KeyOf(client);
            using (var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>()))
            {
                var connections = q.ToEntityArray(Allocator.Temp);
                foreach (Entity c in connections) client.EntityManager.AddComponent<NetworkStreamRequestDisconnect>(c);
                connections.Dispose();
            }
            int used = Tw.TickUntil(() => !PlayerKeys.IsAlive(key), maxTicks);
            Assert.GreaterOrEqual(used, 0, "the server never saw the close. Logs: " + string.Join(" | ", Tw.Interesting));
            return used;
        }

        /// <summary>The server's key of a client's connection.</summary>
        public PlayerKey KeyOf(World client)
        {
            int id = KnTestWorld.NetworkIdOf(client);
            using var q = Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<ConnectionPlayerKey>());
            var ids = q.ToComponentDataArray<NetworkId>(Allocator.Temp);
            var keys = q.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            PlayerKey found = PlayerKey.None;
            for (int i = 0; i < ids.Length; i++)
                if (ids[i].Value == id) found = keys[i].PlayerKey;
            ids.Dispose();
            keys.Dispose();
            return found;
        }

        public PlayerKey KeyOf(int client) => KeyOf(Tw.Clients[client]);

        /// <summary>The server ghost of a key, or Entity.Null.</summary>
        public Entity GhostOf(PlayerKey key)
        {
            using var q = Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<PlayerGhostKey>());
            var ents = q.ToEntityArray(Allocator.Temp);
            var keys = q.ToComponentDataArray<PlayerGhostKey>(Allocator.Temp);
            Entity found = Entity.Null;
            for (int i = 0; i < ents.Length; i++)
                if (keys[i].Key == key.Value) found = ents[i];
            ents.Dispose();
            keys.Dispose();
            return found;
        }

        public GameObject BodyOf(PlayerKey key) => GhostBodyRegistry.TryGetServerBody(key, out GameObject body) ? body : null;

        public int ServerGhosts => KnTestWorld.Count<PlayerGhostTag>(Server);

        /// <summary>Connections on the server that are in game.</summary>
        public int InGameOnServer
        {
            get
            {
                using var q = Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
                return q.CalculateEntityCount();
            }
        }

        public void Tick(int n = 1) => Tw.Tick(n);

        public void Dispose()
        {
            Tw?.Dispose();
            foreach (GameObject body in Rules.Bodies)
                if (body != null) UnityEngine.Object.DestroyImmediate(body);
            if (Template != null) UnityEngine.Object.DestroyImmediate(Template);
            GhostBodyRegistry.Clear();
            KnPlayerRefusals.Clear();
            LocalPlayer.Set(null);
        }
    }
}
