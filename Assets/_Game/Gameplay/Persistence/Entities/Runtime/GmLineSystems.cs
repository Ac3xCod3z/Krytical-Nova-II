using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>A GM's console line, from the owner to the server (NGO's <c>PlayerGm.CommandServerRpc</c>; M7.4).</summary>
    public struct GmLineRpc : IRpcCommand
    {
        public FixedString512Bytes Line;
    }

    /// <summary>One part of the server's answer to a GM line (NGO's <c>PlayerGm.ReplyRpc</c>); the owner joins the parts until <see cref="More"/> is 0.</summary>
    public struct GmReplyRpc : IRpcCommand
    {
        public FixedString512Bytes Text;
        public byte More;
    }

    /// <summary>The server made the owner GM, or took it back (NGO's <c>PlayerGm.gmName</c> network variable, for the owner).</summary>
    public struct GmStateRpc : IRpcCommand
    {
        public FixedString32Bytes Name;
        public byte IsGm;
    }

    /// <summary>
    /// On a CLIENT world that runs in the server's own process and is the host's (Play DOTS here, set by the Editor-only KnPlayHere): NGO's
    /// host rule, its player is GM (<see cref="GmRoster.Host"/>). A built server never has one, so only bound accounts are GM there; a client
    /// in another process is never seen by the server as this (findings item 9, 6 Nov 2026).
    /// </summary>
    public struct PlayHereHostTag : IComponentData { }

    /// <summary>
    /// Server: GM rights and GM lines of the entity netcode's players, by NGO's rules (<c>PlayerGm</c>): a player whose proven account the owner
    /// bound to a GM is GM from the moment they come in (<see cref="GmRoster.SignInBound"/>) and stops being one when they leave; each line is
    /// limited as an account call, cut to 500 characters, refused with "You are not a GM on this server." for anyone else, and run by
    /// <see cref="PlayerGm.CommandRunner"/> for that player's connection id (the one <see cref="PlayerKeys"/>, the roster and the records use);
    /// only they get the answer.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class GmLineServerSystem : SystemBase
    {
        /// <summary>The most a reply part carries (UTF-8 bytes; a FixedString512Bytes holds 509).</summary>
        public const int PartBytes = 500;

        EntityQuery m_Lines, m_InGame, m_Ghosts;
        readonly Dictionary<ulong, Entity> m_Connections = new Dictionary<ulong, Entity>();
        readonly Dictionary<int, ulong> m_ByNetworkId = new Dictionary<int, ulong>();
        readonly HashSet<ulong> m_Known = new HashSet<ulong>();
        readonly List<ulong> m_Gone = new List<ulong>();

        /// <summary>Lines run, refused (not a GM), and dropped by the request limits (tests).</summary>
        public int Run { get; private set; }
        public int NotGm { get; private set; }
        public int OverBudget { get; private set; }

        protected override void OnCreate()
        {
            m_Lines = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GmLineRpc>());
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>(),
                ComponentType.ReadOnly<ConnectionPlayerKey>());
            m_Ghosts = GetEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadWrite<PlayerGmTag>());
            GmRoster.Granted += OnGranted;
            GmRoster.Revoked += OnRevoked;
        }

        protected override void OnDestroy()
        {
            GmRoster.Granted -= OnGranted;
            GmRoster.Revoked -= OnRevoked;
            foreach (ulong id in m_Known) GmRoster.Forget(id);
            m_Known.Clear();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            m_Connections.Clear();
            m_ByNetworkId.Clear();
            using (var connections = m_InGame.ToEntityArray(Allocator.Temp))
            using (var keys = m_InGame.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp))
            using (var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp))
                for (int i = 0; i < connections.Length; i++)
                {
                    m_Connections[keys[i].Serial] = connections[i];
                    m_ByNetworkId[ids[i].Value] = keys[i].Serial;
                }

            // NGO's OnNetworkSpawn / OnNetworkDespawn of PlayerGm: a bound GM is GM again on every join; who left is forgotten
            m_Gone.Clear();
            foreach (ulong id in m_Known) if (!m_Connections.ContainsKey(id)) m_Gone.Add(id);
            foreach (ulong id in m_Gone) { m_Known.Remove(id); GmRoster.Forget(id); }
            foreach (ulong id in m_Connections.Keys)
                if (m_Known.Add(id)) GmRoster.SignInBound(id, out _);
            FindHost();
            ShowTags();

            if (m_Lines.IsEmpty) return;
            var received = m_Lines.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            var lines = m_Lines.ToComponentDataArray<GmLineRpc>(Allocator.Temp);
            em.DestroyEntity(m_Lines);
            for (int i = 0; i < lines.Length; i++) RunLine(received[i].SourceConnection, lines[i].Line.ToString());
            received.Dispose();
            lines.Dispose();
        }

        /// <summary>Times a host connection was made GM (tests).</summary>
        public int HostsMade { get; private set; }

        // NGO's host rule (GmRoster.IsGm: the host's own player is GM): the in-game connection of a client world in THIS process that carries
        // PlayHereHostTag. Looked at only while there is no host in the game (a handful of worlds, not per player: no job needed).
        void FindHost()
        {
            if (GmRoster.Host is ulong had && m_Connections.ContainsKey(had)) return;
            if (GmRoster.Host.HasValue) GmRoster.SetHost(null);   // the host left
            foreach (World world in World.All)
            {
                if (!world.IsCreated || !world.IsClient() || world.IsServer()) continue;
                var em = world.EntityManager;
                using var tag = em.CreateEntityQuery(ComponentType.ReadOnly<PlayHereHostTag>());
                if (tag.IsEmpty) continue;
                using var ids = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
                if (ids.CalculateEntityCount() != 1) continue;
                if (!m_ByNetworkId.TryGetValue(ids.GetSingleton<NetworkId>().Value, out ulong serial)) continue;
                GmRoster.SetHost(serial);
                HostsMade++;
                Entity connection = m_Connections[serial];
                State(connection, true, GmRoster.HostName);
                Send(connection, "You are GM (" + GmRoster.HostName + ": you host this game). F2 opens the GM console; gm.help lists the commands.");
                return;
            }
        }

        // The [GM] tag everyone sees (NGO: PlayerGm.ShowsTag from its network variable): each player's ghost says whether the roster has them.
        void ShowTags()
        {
            if (m_Ghosts.IsEmpty) return;
            var em = EntityManager;
            using var ghosts = m_Ghosts.ToEntityArray(Allocator.Temp);
            using var owners = m_Ghosts.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            using var tags = m_Ghosts.ToComponentDataArray<PlayerGmTag>(Allocator.Temp);
            for (int i = 0; i < ghosts.Length; i++)
            {
                byte on = (byte)(m_ByNetworkId.TryGetValue(owners[i].NetworkId, out ulong id) && GmRoster.IsGm(id) ? 1 : 0);
                if (tags[i].On != on) em.SetComponentData(ghosts[i], new PlayerGmTag { On = on });
            }
        }

        void RunLine(Entity connection, string line)
        {
            var em = EntityManager;
            if (connection == Entity.Null || !em.Exists(connection) || !em.HasComponent<ConnectionPlayerKey>(connection)) return;
            ConnectionPlayerKey key = em.GetComponentData<ConnectionPlayerKey>(connection);
            if (!m_Connections.ContainsKey(key.Serial)) return;   // not in the game
            if (RpcBudget.Spend(key.PlayerKey, CallKind.Account, UnityEngine.Time.realtimeSinceStartup) != RpcBudget.SpendResult.Allowed)
            {
                OverBudget++;
                return;
            }
            line = line.Trim();
            if (line.Length == 0) return;
            if (line.Length > PlayerGm.LongestLine) line = line.Substring(0, PlayerGm.LongestLine);
            if (!GmRoster.IsGm(key.Serial))
            {
                NotGm++;
                Send(connection, "You are not a GM on this server.");
                return;
            }
            string answer = PlayerGm.CommandRunner != null ? PlayerGm.CommandRunner(key.Serial, line) : "GM commands are not available on this server.";
            Run++;
            Send(connection, string.IsNullOrEmpty(answer) ? "Done." : answer);
        }

        // The owner bound this player's account to a GM on the control page (or they came in bound): NGO's PlayerGm.Become.
        void OnGranted(ulong id, string name)
        {
            if (!m_Connections.TryGetValue(id, out Entity connection) || !EntityManager.Exists(connection)) return;
            State(connection, true, name);
            UnityEngine.Debug.Log($"[GM] {name} is in the game as GM (connection {id}).");
            Send(connection, $"You are GM ({name}) on this server. F2 opens the GM console; gm.help lists the commands.");
        }

        void OnRevoked(ulong id)
        {
            if (!m_Connections.TryGetValue(id, out Entity connection) || !EntityManager.Exists(connection)) return;
            State(connection, false, null);
            Send(connection, "Your GM rights were removed on this server.");
        }

        void State(Entity connection, bool isGm, string name)
        {
            var em = EntityManager;
            Entity rpc = em.CreateEntity();
            var state = new GmStateRpc { IsGm = (byte)(isGm ? 1 : 0) };
            if (!string.IsNullOrEmpty(name)) state.Name = new FixedString32Bytes(name.Length > 20 ? name.Substring(0, 20) : name);
            em.AddComponentData(rpc, state);
            em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        void Send(Entity connection, string text)
        {
            var em = EntityManager;
            List<string> parts = Parts(text, PartBytes);
            for (int i = 0; i < parts.Count; i++)
            {
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new GmReplyRpc { Text = new FixedString512Bytes(parts[i]), More = (byte)(i < parts.Count - 1 ? 1 : 0) });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        /// <summary><paramref name="text"/> in parts of at most <paramref name="maxBytes"/> UTF-8 bytes, never splitting a character.</summary>
        public static List<string> Parts(string text, int maxBytes)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(text)) { parts.Add(string.Empty); return parts; }
            int start = 0, bytes = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int width = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                int size = Encoding.UTF8.GetByteCount(text.ToCharArray(i, width));
                if (bytes + size > maxBytes)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i;
                    bytes = 0;
                }
                bytes += size;
                i += width - 1;
            }
            parts.Add(text.Substring(start));
            return parts;
        }
    }

    /// <summary>
    /// Client: the GM link of this machine's player (<see cref="PlayerGm.Remote"/>): the console's lines go to the server, the server's GM rights
    /// and answers come back (the answer, joined from its parts, through <see cref="PlayerGm.Reply"/>, as NGO's <c>ReplyRpc</c>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class GmLineClientSystem : SystemBase
    {
        public sealed class Link : PlayerGm.IRemoteLink
        {
            readonly World m_World;
            internal Link(World world) => m_World = world;

            public bool IsGm { get; internal set; }
            public string GmName { get; internal set; } = string.Empty;

            public void Send(string line)
            {
                if (m_World == null || !m_World.IsCreated || string.IsNullOrWhiteSpace(line)) return;
                line = line.Trim();
                if (line.Length > PlayerGm.LongestLine) line = line.Substring(0, PlayerGm.LongestLine);
                var em = m_World.EntityManager;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new GmLineRpc { Line = new FixedString512Bytes(line) });
                em.AddComponent<SendRpcCommandRequest>(rpc);   // no target: to the server
            }
        }

        EntityQuery m_Replies, m_States, m_Tags;
        readonly StringBuilder m_Answer = new StringBuilder();
        readonly Dictionary<Entity, (byte on, UnityEngine.GameObject body)> m_Shown = new Dictionary<Entity, (byte, UnityEngine.GameObject)>();
        readonly List<Entity> m_Gone = new List<Entity>();

        /// <summary>This world's link (tests send through it), answers handed on, and the last one (tests).</summary>
        public Link GmLink { get; private set; }
        public int Replies { get; private set; }
        public string LastReply { get; private set; }

        protected override void OnCreate()
        {
            m_Replies = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GmReplyRpc>());
            m_States = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GmStateRpc>());
            m_Tags = GetEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<PlayerGmTag>());
            GmLink = new Link(World);
            PlayerGm.Remote = GmLink;
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(PlayerGm.Remote, GmLink)) PlayerGm.Remote = null;
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            ShowTags();
            if (!m_States.IsEmpty)
            {
                using var states = m_States.ToComponentDataArray<GmStateRpc>(Allocator.Temp);
                em.DestroyEntity(m_States);
                foreach (GmStateRpc state in states)
                {
                    GmLink.IsGm = state.IsGm != 0;
                    GmLink.GmName = state.Name.ToString();
                }
            }
            if (m_Replies.IsEmpty) return;
            using var replies = m_Replies.ToComponentDataArray<GmReplyRpc>(Allocator.Temp);
            em.DestroyEntity(m_Replies);
            foreach (GmReplyRpc part in replies)
            {
                m_Answer.Append(part.Text.ToString());
                if (part.More != 0) continue;
                LastReply = m_Answer.ToString();
                m_Answer.Clear();
                Replies++;
                PlayerGm.RaiseReply(LastReply);
            }
        }

        /// <summary>Players shown with the [GM] tag here (tests).</summary>
        public int TagsShown { get; private set; }

        // Each player's body takes its ghost's GM tag (the namecard shows "[GM] " from PlayerGm.ShowsTag); again when the body is new.
        void ShowTags()
        {
            m_Gone.Clear();
            foreach (Entity ghost in m_Shown.Keys) if (!EntityManager.Exists(ghost) || !EntityManager.HasComponent<PlayerGmTag>(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Shown.Remove(ghost);
            if (m_Tags.IsEmpty) { TagsShown = 0; return; }
            using var ghosts = m_Tags.ToEntityArray(Allocator.Temp);
            using var tags = m_Tags.ToComponentDataArray<PlayerGmTag>(Allocator.Temp);
            int shown = 0;
            for (int i = 0; i < ghosts.Length; i++)
            {
                if (tags[i].On != 0) shown++;
                UnityEngine.GameObject body = GhostBodyRegistry.TryGetByGhost(World, ghosts[i], out GhostBodyEntry entry) ? entry.Body : null;
                if (m_Shown.TryGetValue(ghosts[i], out var had) && had.on == tags[i].On && had.body == body) continue;
                m_Shown[ghosts[i]] = (tags[i].On, body);
                if (body != null && body.TryGetComponent(out PlayerGm gm)) gm.ShowTagFromServer(tags[i].On != 0);
            }
            TagsShown = shown;
        }
    }
}
