using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;

namespace KryticalNova.Player.Entities.Bodies
{
    // ---------------------------------------------------------------------------------------------------------------------------
    // The look of observers (M3a-2b step 8). The ghost carries only PlayerLook.Version; a client that meets a version it has not
    // got asks the server once (LookRequestRpc), the server answers with the look text in pages (LookReplyRpc), the client keeps it
    // in a cache by version and puts it on the body (ILookApplier, the game: PlayerIdentity.ApplyView). Versions are unique for the
    // whole server process, so a cached text never belongs to anyone else, even when a NetworkId is handed out again.
    // ---------------------------------------------------------------------------------------------------------------------------

    /// <summary>Client to server: "what does the player with this NetworkId wear?" (the version the client saw on the ghost).</summary>
    public struct LookRequestRpc : IRpcCommand
    {
        public int OwnerNetworkId;
        public uint Version;
    }

    /// <summary>Server to client: page <see cref="Page"/> of <see cref="PageCount"/> of the look text of <see cref="Version"/>.</summary>
    public struct LookReplyRpc : IRpcCommand
    {
        public uint Version;
        public byte Page;
        public byte PageCount;
        public FixedString512Bytes Chunk;
    }

    /// <summary>Puts a look (text made by the game) on a body. The game decodes it (name + recipe).</summary>
    public interface ILookApplier
    {
        void Apply(GameObject body, string lookText);
    }

    /// <summary>Look texts by version, plain C#. Bounded: when full it starts again (a miss only costs one more request).</summary>
    public sealed class LookCache
    {
        public const int Capacity = 512;
        readonly Dictionary<uint, string> m_Texts = new Dictionary<uint, string>();

        public int Count => m_Texts.Count;

        public bool TryGet(uint version, out string text) => m_Texts.TryGetValue(version, out text);

        public void Put(uint version, string text)
        {
            if (version == 0 || text == null) return;
            if (m_Texts.Count >= Capacity && !m_Texts.ContainsKey(version)) m_Texts.Clear();
            m_Texts[version] = text;
        }

        public void Clear() => m_Texts.Clear();
    }

    /// <summary>
    /// Server: keeps each player ghost's look text and answers the clients' requests. <see cref="SetLook"/> is how the game says a
    /// look changed (entry, a restyle): the ghost's <see cref="PlayerLook.Version"/> goes up once per different text.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(KnRulesGroup))]
    public partial class PlayerLookServerSystem : SystemBase
    {
        // Unique in the process (several server worlds in tests, a restarted world): a version never names two looks.
        static uint s_LastVersion;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_LastVersion = 0;

        /// <summary>Most requests answered per update; the rest wait (the client asks again).</summary>
        public int MaxRepliesPerUpdate = 64;

        readonly Dictionary<Entity, KeyValuePair<uint, string>> m_Looks = new Dictionary<Entity, KeyValuePair<uint, string>>();
        readonly List<string> m_Pages = new List<string>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_Requests, m_Ghosts;

        public int Replies { get; private set; }
        public int Unanswered { get; private set; }

        protected override void OnCreate()
        {
            m_Requests = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<LookRequestRpc>());
            m_Ghosts = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerLook, GhostOwner>()
                .WithNone<PlayerLeaving>());
            RequireForUpdate(m_Requests);
        }

        /// <summary>The ghost's look is now <paramref name="text"/>; returns its version (unchanged when the text is the same).</summary>
        public uint SetLook(Entity ghost, string text)
        {
            if (!EntityManager.Exists(ghost) || !EntityManager.HasComponent<PlayerLook>(ghost)) return 0;
            text ??= string.Empty;
            if (m_Looks.TryGetValue(ghost, out var known) && known.Value == text) return known.Key;
            uint version = ++s_LastVersion;
            m_Looks[ghost] = new KeyValuePair<uint, string>(version, text);
            EntityManager.SetComponentData(ghost, new PlayerLook { Version = version });
            return version;
        }

        public bool TryGetLook(Entity ghost, out uint version, out string text)
        {
            if (m_Looks.TryGetValue(ghost, out var known)) { version = known.Key; text = known.Value; return true; }
            version = 0;
            text = null;
            return false;
        }

        protected override void OnUpdate()
        {
            ForgetGoneGhosts();
            var rpcs = m_Requests.ToEntityArray(Allocator.Temp);
            var received = m_Requests.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            var requests = m_Requests.ToComponentDataArray<LookRequestRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Requests);
            int answered = 0;
            for (int i = 0; i < rpcs.Length; i++)
            {
                if (answered >= MaxRepliesPerUpdate) { Unanswered++; continue; }
                if (Answer(received[i].SourceConnection, requests[i].OwnerNetworkId)) answered++;
            }
            rpcs.Dispose();
            received.Dispose();
            requests.Dispose();
        }

        bool Answer(Entity connection, int ownerNetworkId)
        {
            if (connection == Entity.Null || !EntityManager.Exists(connection)) return false;
            Entity ghost = GhostOfOwner(ownerNetworkId);
            if (ghost == Entity.Null || !m_Looks.TryGetValue(ghost, out var look)) { Unanswered++; return false; }
            if (!GoInGamePages.TrySplit(look.Value, m_Pages))
            {
                Debug.LogError("[KnServer] a look text is not ASCII or longer than " + GoInGamePages.MaxChars + " characters; not sent.");
                Unanswered++;
                return false;
            }
            for (int p = 0; p < m_Pages.Count; p++)
            {
                Entity rpc = EntityManager.CreateEntity();
                EntityManager.AddComponentData(rpc, new LookReplyRpc
                {
                    Version = look.Key,
                    Page = (byte)p,
                    PageCount = (byte)m_Pages.Count,
                    Chunk = new FixedString512Bytes(m_Pages[p]),
                });
                EntityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
            Replies++;
            return true;
        }

        Entity GhostOfOwner(int networkId)
        {
            if (networkId == 0) return Entity.Null;
            var ghosts = m_Ghosts.ToEntityArray(Allocator.Temp);
            var owners = m_Ghosts.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            Entity found = Entity.Null;
            for (int i = 0; i < ghosts.Length; i++)
                if (owners[i].NetworkId == networkId) { found = ghosts[i]; break; }
            ghosts.Dispose();
            owners.Dispose();
            return found;
        }

        void ForgetGoneGhosts()
        {
            if (m_Looks.Count == 0) return;
            m_Gone.Clear();
            foreach (Entity ghost in m_Looks.Keys)
                if (!EntityManager.Exists(ghost) || !EntityManager.HasComponent<PlayerLook>(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Looks.Remove(ghost);
        }
    }

    /// <summary>
    /// Client: puts the right look on every player body. A version it has in its cache is applied at once; one it has not is asked
    /// for (again after <see cref="RetryFrames"/> when no answer came). Until then the body shows the prefab's own look.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]   // after the bodies are made (PlayerBodiesBeforeCameraGroup ends the simulation)
    public partial class PlayerLookClientSystem : SystemBase
    {
        /// <summary>Set by the game (<c>PlayerWorldsSetup</c>); without it looks are cached but not shown.</summary>
        public ILookApplier Applier;

        public readonly LookCache Cache = new LookCache();

        /// <summary>Frames before an unanswered request is sent again.</summary>
        public int RetryFrames = 30;

        public int Requests { get; private set; }
        public int Applied { get; private set; }

        readonly Dictionary<Entity, uint> m_Applied = new Dictionary<Entity, uint>();
        readonly Dictionary<uint, int> m_AskedAt = new Dictionary<uint, int>();
        readonly Dictionary<uint, GoInGamePages.Assembler> m_Pages = new Dictionary<uint, GoInGamePages.Assembler>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_Replies, m_Ghosts, m_Connection;
        int m_Frame;

        protected override void OnCreate()
        {
            m_Replies = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<LookReplyRpc>());
            m_Ghosts = GetEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<PlayerLook>(), ComponentType.ReadOnly<GhostOwner>());
            m_Connection = GetEntityQuery(ComponentType.ReadOnly<NetworkId>());
            RequireAnyForUpdate(m_Replies, m_Ghosts);
        }

        /// <summary>Forgets which looks were put on which bodies (they are put on again from the cache, without asking).</summary>
        public void ForgetApplied() => m_Applied.Clear();

        protected override void OnUpdate()
        {
            m_Frame++;
            TakeReplies();

            var ghosts = m_Ghosts.ToEntityArray(Allocator.Temp);
            var looks = m_Ghosts.ToComponentDataArray<PlayerLook>(Allocator.Temp);
            var owners = m_Ghosts.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            for (int i = 0; i < ghosts.Length; i++)
            {
                uint version = looks[i].Version;
                if (version == 0) continue;
                if (m_Applied.TryGetValue(ghosts[i], out uint done) && done == version) continue;
                if (!GhostBodyRegistry.TryGetByGhost(World, ghosts[i], out GhostBodyEntry entry) || entry.Body == null) continue;
                if (Cache.TryGet(version, out string text))
                {
                    Applier?.Apply(entry.Body, text);
                    m_Applied[ghosts[i]] = version;
                    Applied++;
                    continue;
                }
                if (m_AskedAt.TryGetValue(version, out int at) && m_Frame - at < RetryFrames) continue;
                Ask(owners[i].NetworkId, version);
            }
            ghosts.Dispose();
            looks.Dispose();
            owners.Dispose();
            ForgetGoneGhosts();
        }

        void Ask(int ownerNetworkId, uint version)
        {
            if (m_Connection.IsEmpty) return;
            m_AskedAt[version] = m_Frame;
            Entity rpc = EntityManager.CreateEntity();
            EntityManager.AddComponentData(rpc, new LookRequestRpc { OwnerNetworkId = ownerNetworkId, Version = version });
            EntityManager.AddComponentData(rpc, new SendRpcCommandRequest()); // to the server
            Requests++;
        }

        void TakeReplies()
        {
            if (m_Replies.IsEmpty) return;
            var replies = m_Replies.ToComponentDataArray<LookReplyRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Replies);
            for (int i = 0; i < replies.Length; i++)
            {
                LookReplyRpc reply = replies[i];
                if (reply.Version == 0 || Cache.TryGet(reply.Version, out _)) continue;
                if (!m_Pages.TryGetValue(reply.Version, out var pages)) m_Pages[reply.Version] = pages = new GoInGamePages.Assembler();
                KnRosterPages.Add result = pages.Take(reply.Page, reply.PageCount, reply.Chunk.ToString());
                if (result == KnRosterPages.Add.Bad) { m_Pages.Remove(reply.Version); continue; }
                if (result != KnRosterPages.Add.Complete) continue;
                Cache.Put(reply.Version, pages.Text);
                m_Pages.Remove(reply.Version);
                m_AskedAt.Remove(reply.Version);
            }
            replies.Dispose();
        }

        void ForgetGoneGhosts()
        {
            if (m_Applied.Count == 0) return;
            m_Gone.Clear();
            foreach (Entity ghost in m_Applied.Keys)
                if (!EntityManager.Exists(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Applied.Remove(ghost);
            if (m_Pages.Count > 64) m_Pages.Clear(); // replies that never completed
        }
    }
}
