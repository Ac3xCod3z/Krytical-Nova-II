using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner asks the server to use one of an item (by its id: the two Packs are not the same array; feel test item 5).</summary>
    public struct ItemUseRpc : IRpcCommand
    {
        public ushort ItemId;
    }

    /// <summary>The owner asks the server to cast a buff on the player's own body (Heavenly Serenity, a ward; feel test item 5).</summary>
    public struct SkillCastRpc : IRpcCommand
    {
        public FixedString64Bytes SkillId;
    }

    /// <summary>The server's answer to <see cref="ItemUseRpc"/> or <see cref="SkillCastRpc"/>, for the owner's own body to show.</summary>
    public struct RequestReplyRpc : IRpcCommand
    {
        public const byte ItemUsed = 1, ItemRefused = 2, SkillCast = 3, SkillRefused = 4;
        public byte Kind;
        /// <summary>Item used: it restored HP / Flux (the feel to play).</summary>
        public byte Health, Flux;
        /// <summary>Skill cast: which, at what rank.</summary>
        public FixedString64Bytes SkillId;
        public short Rank;
        /// <summary>Refused: the reason in the game's words.</summary>
        public FixedString128Bytes Text;
    }

    /// <summary>
    /// One Pack cell of the server's body for its owner's own body (the HUD and the hotbar show the server's counts and Flask drinks).
    /// <see cref="ClearAll"/> empties the owner's Pack first (on arrival: its starter bag is not the player's).
    /// </summary>
    public struct PackCellRpc : IRpcCommand
    {
        public const byte ClearAll = 255;
        public byte Index;
        public ushort ItemId;
        public ushort Count;
        public ulong Data;
    }

    /// <summary>
    /// Client: the local view body's requests (<see cref="IBodyRequests"/> on its tag) become RPCs to the server. Nothing is used or cast
    /// here: the server's answer comes back as <see cref="RequestReplyRpc"/>.
    /// </summary>
    public sealed class EntityBodyRequests : IBodyRequests
    {
        readonly World m_World;

        public EntityBodyRequests(World clientWorld) => m_World = clientWorld;

        public void UseItem(ushort itemId)
        {
            if (m_World == null || !m_World.IsCreated || itemId == 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new ItemUseRpc { ItemId = itemId });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void CastSkill(string skillId)
        {
            if (m_World == null || !m_World.IsCreated || string.IsNullOrEmpty(skillId) || skillId.Length > FixedString64Bytes.UTF8MaxLengthInBytes) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new SkillCastRpc { SkillId = new FixedString64Bytes(skillId) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }
    }

    /// <summary>
    /// Server: runs the owners' item uses and buff casts on their server bodies with the game's own rules (NGO's <c>ServerUse</c> and
    /// <c>ServerTryCast</c>: alive, the shared use cooldown, the Flask's drinks, a full pool, the cast checks), counted by the request
    /// limits (items, combat). A use changes one player's documents and writes no ledger line (NGO writes none for a use); the Pack is
    /// saved as any change is (the save agent). Answers each request so the owner's own body can show it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerRequestServerSystem : SystemBase
    {
        EntityQuery m_Uses, m_Casts;

        /// <summary>Requests run, refused, and dropped by the request limits (tests).</summary>
        public int Used { get; private set; }
        public int Refused { get; private set; }
        public int OverBudget { get; private set; }

        protected override void OnCreate()
        {
            m_Uses = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ItemUseRpc>());
            m_Casts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SkillCastRpc>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_Uses.IsEmpty)
            {
                var received = m_Uses.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var uses = m_Uses.ToComponentDataArray<ItemUseRpc>(Allocator.Temp);
                em.DestroyEntity(m_Uses);
                for (int i = 0; i < uses.Length; i++) UseItem(received[i].SourceConnection, uses[i].ItemId);
                received.Dispose();
                uses.Dispose();
            }
            if (!m_Casts.IsEmpty)
            {
                var received = m_Casts.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var casts = m_Casts.ToComponentDataArray<SkillCastRpc>(Allocator.Temp);
                em.DestroyEntity(m_Casts);
                for (int i = 0; i < casts.Length; i++) Cast(received[i].SourceConnection, casts[i].SkillId.ToString());
                received.Dispose();
                casts.Dispose();
            }
        }

        /// <summary>The server body of the connection's player; false (dropped) when there is none or the request limit says no.</summary>
        bool BodyOf(Entity connection, CallKind kind, out GameObject body)
        {
            body = null;
            var em = EntityManager;
            if (connection == Entity.Null || !em.Exists(connection) || !em.HasComponent<NetworkId>(connection)) return false;
            if (!GhostBodyRegistry.TryGetByNetworkId(World, em.GetComponentData<NetworkId>(connection).Value, out GhostBodyEntry entry) || entry.Body == null)
                return false;
            if (entry.Key != PlayerKey.None && RpcBudget.Spend(entry.Key, kind, UnityEngine.Time.realtimeSinceStartup) != RpcBudget.SpendResult.Allowed)
            {
                OverBudget++;
                return false;
            }
            body = entry.Body;
            return true;
        }

        void UseItem(Entity connection, ushort itemId)
        {
            if (!BodyOf(connection, CallKind.Items, out GameObject body)) return;
            if (UseOn(body, itemId, out RequestReplyRpc reply)) Used++; else Refused++;
            if (reply.Kind != 0) Reply(connection, reply);
        }

        /// <summary>
        /// One use of <paramref name="itemId"/> on a server body, by the inventory's own rules (NGO's <c>UseItem</c> -> <c>ServerUse</c>):
        /// true when it was used. <paramref name="reply"/> is the answer for its player (Kind 0 when there is nothing to say).
        /// </summary>
        public static bool UseOn(GameObject body, ushort itemId, out RequestReplyRpc reply)
        {
            reply = default;
            var inventory = body != null ? body.GetComponent<PlayerInventory>() : null;
            if (inventory == null) return false;
            string refused = null;
            bool used = false, health = false, flux = false;
            Action<string> onRefused = reason => refused ??= reason;
            Action<ushort, bool, bool> onUsed = (id, h, f) => { used = true; health = h; flux = f; };
            inventory.RequestFailed += onRefused;
            inventory.ItemUsed += onUsed;
            try { inventory.UseItem(itemId); }
            finally
            {
                inventory.RequestFailed -= onRefused;
                inventory.ItemUsed -= onUsed;
            }
            if (used) reply = new RequestReplyRpc { Kind = RequestReplyRpc.ItemUsed, Health = (byte)(health ? 1 : 0), Flux = (byte)(flux ? 1 : 0) };
            else if (refused != null) reply = new RequestReplyRpc { Kind = RequestReplyRpc.ItemRefused, Text = Fit(refused) };
            return used;
        }

        void Cast(Entity connection, string skillId)
        {
            if (!BodyOf(connection, CallKind.Combat, out GameObject body)) return;
            var skills = body.GetComponent<PlayerCombatSkills>();
            SkillDefinitionSO skill = skills != null ? skills.GetSkill(skillId) : null;
            if (skill == null) return;
            string reason;
            if (skill.IsAttack) reason = "Attacks are not networked yet on this server.";   // attacks come with the combat port
            else if (skills.ServerTryCast(skill, null, out reason))
            {
                Used++;
                Reply(connection, new RequestReplyRpc { Kind = RequestReplyRpc.SkillCast, SkillId = new FixedString64Bytes(skillId), Rank = (short)skills.GetRank(skill) });
                return;
            }
            Refused++;
            Reply(connection, new RequestReplyRpc { Kind = RequestReplyRpc.SkillRefused, SkillId = new FixedString64Bytes(skillId), Text = Fit(reason) });
        }

        void Reply(Entity connection, RequestReplyRpc reply)
        {
            Entity rpc = EntityManager.CreateEntity();
            EntityManager.AddComponentData(rpc, reply);
            EntityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        static FixedString128Bytes Fit(string text)
        {
            text ??= string.Empty;
            if (text.Length > 120) text = text.Substring(0, 120);
            return new FixedString128Bytes(text);
        }
    }

    /// <summary>
    /// Server: sends each player's Pack cells to their own client when they change (the server's body is where items are used; the
    /// owner's HUD and hotbar show its counts). On arrival the owner's Pack is cleared and every held cell sent.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerPackSyncServerSystem : SystemBase
    {
        /// <summary>Seconds between looks at the Packs.</summary>
        public float Interval = 0.1f;

        readonly Dictionary<Entity, ItemStack[]> m_Sent = new Dictionary<Entity, ItemStack[]>();
        readonly Dictionary<int, Entity> m_Connections = new Dictionary<int, Entity>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_InGame;
        float m_Next;

        /// <summary>Cells sent (tests).</summary>
        public int CellsSent { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());

        protected override void OnUpdate()
        {
            if (UnityEngine.Time.realtimeSinceStartup < m_Next) return;
            m_Next = UnityEngine.Time.realtimeSinceStartup + Interval;
            var em = EntityManager;
            m_Connections.Clear();
            var connections = m_InGame.ToEntityArray(Allocator.Temp);
            var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++) m_Connections[ids[i].Value] = connections[i];
            connections.Dispose();
            ids.Dispose();

            m_Gone.Clear();
            foreach (Entity ghost in m_Sent.Keys) if (!em.Exists(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Sent.Remove(ghost);

            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost)) continue;
                if (!m_Connections.TryGetValue(entry.NetworkId, out Entity connection)) continue;
                var inventory = entry.Body.GetComponent<PlayerInventory>();
                if (inventory == null) continue;
                if (!m_Sent.TryGetValue(entry.Ghost, out ItemStack[] sent))
                {
                    sent = new ItemStack[PlayerInventory.Capacity];
                    m_Sent[entry.Ghost] = sent;
                    Send(connection, new PackCellRpc { Index = PackCellRpc.ClearAll });
                }
                for (int i = 0; i < PlayerInventory.Capacity; i++)
                {
                    ItemStack now = inventory.Get(i);
                    if (now.Equals(sent[i])) continue;
                    sent[i] = now;
                    Send(connection, new PackCellRpc { Index = (byte)i, ItemId = now.itemId, Count = now.count, Data = now.data });
                    CellsSent++;
                }
            }
        }

        void Send(Entity connection, PackCellRpc cell)
        {
            Entity rpc = EntityManager.CreateEntity();
            EntityManager.AddComponentData(rpc, cell);
            EntityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Client: the server's answers and Pack cells reach the local view body: a use's feel, a refusal's words, a cast shown by the
    /// body's copy of its skills, and the Pack cells mirrored.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerRequestClientSystem : SystemBase
    {
        EntityQuery m_Replies, m_Cells;

        /// <summary>Answers and cells applied (tests).</summary>
        public int Replies { get; private set; }
        public int Cells { get; private set; }

        protected override void OnCreate()
        {
            m_Replies = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RequestReplyRpc>());
            m_Cells = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PackCellRpc>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            GameObject body = LocalBody();
            if (!m_Cells.IsEmpty)
            {
                var cells = m_Cells.ToComponentDataArray<PackCellRpc>(Allocator.Temp);
                em.DestroyEntity(m_Cells);
                var inventory = body != null ? body.GetComponent<PlayerInventory>() : null;
                if (inventory != null)
                    foreach (PackCellRpc cell in cells)
                    {
                        if (cell.Index == PackCellRpc.ClearAll)
                            for (int i = 0; i < PlayerInventory.Capacity; i++) inventory.MirrorCell(i, ItemStack.Empty);
                        else inventory.MirrorCell(cell.Index, new ItemStack(cell.ItemId, cell.Count, cell.Data));
                        Cells++;
                    }
                cells.Dispose();
            }
            if (!m_Replies.IsEmpty)
            {
                var replies = m_Replies.ToComponentDataArray<RequestReplyRpc>(Allocator.Temp);
                em.DestroyEntity(m_Replies);
                if (body != null) foreach (RequestReplyRpc reply in replies) Show(body, reply);
                replies.Dispose();
            }
        }

        void Show(GameObject body, in RequestReplyRpc reply)
        {
            Replies++;
            switch (reply.Kind)
            {
                case RequestReplyRpc.ItemUsed:
                    ClientPresentationRegistry.Feel?.Consumed(body.transform, reply.Health != 0, reply.Flux != 0);
                    break;
                case RequestReplyRpc.ItemRefused:
                    body.GetComponent<PlayerInventory>()?.ShowRefused(reply.Text.ToString());
                    break;
                case RequestReplyRpc.SkillCast:
                    body.GetComponent<PlayerCombatSkills>()?.ShowServerCast(reply.SkillId.ToString(), reply.Rank);
                    break;
                case RequestReplyRpc.SkillRefused:
                    body.GetComponent<PlayerCombatSkills>()?.ShowCastRefused(reply.Text.ToString());
                    break;
            }
        }

        GameObject LocalBody()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null) return entry.Body;
            return null;
        }
    }
}
