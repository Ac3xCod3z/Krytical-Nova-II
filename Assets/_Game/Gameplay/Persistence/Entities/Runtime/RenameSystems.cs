using KryticalNova.Networking;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner asks for a new name, spending the Name Change Pass in that Pack cell (NGO's <c>PlayerRename.RenameRpc</c>).</summary>
    public struct RenameRpc : IRpcCommand
    {
        public byte BagIndex;
        public FixedString64Bytes Name;
    }

    /// <summary>The server's answer to the owner (NGO's <c>RenamedRpc</c> / <c>RefusedRpc</c>): taken, with the name as kept; or why not.</summary>
    public struct RenameReplyRpc : IRpcCommand
    {
        public byte Taken;
        public FixedString128Bytes Text;
    }

    /// <summary>
    /// Server: the Name Change Pass on the entity netcode (wave 2, 12 Nov 2026). An ask is proven (its connection's body, the Account call
    /// limit, as NGO) and handed to that server body's <see cref="PlayerRename.ServerRenameAsked"/>, which alone decides with NGO's rules: the
    /// name against every other name in the game (the "names" world document), the Pack cell read by the server at the take, then the
    /// rename, the ledger line and one save of the account and the names list together; the answer waits for that save's word, and a
    /// refused save is undone (owner, 12 Nov 2026). Only the cell index and the wished name come from
    /// the ask. Other players' name plates follow through the look text (PlayerIdentity.ServerRename raises Changed).
    /// <para>Limit, kept as written (lead's condition 5): one map server only (the single main thread serialises two asks for one name).
    /// When map servers come back, the save service must own the name check.</para>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RenameServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Asks decided, names taken (tests).</summary>
        public int Asked { get; private set; }
        public int Taken { get; private set; }

        protected override void OnCreate()
        {
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RenameRpc>());
        }

        // Answers waiting to be sent: a rename is answered after its save's word (owner, 12 Nov 2026: a refused save is undone).
        readonly System.Collections.Generic.List<(Entity connection, string refusal, string clean)> m_Answers =
            new System.Collections.Generic.List<(Entity, string, string)>();

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<RenameRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                {
                    if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Account, out GameObject body)
                        || !body.TryGetComponent(out PlayerRename rename)) continue;   // over the limit or no body: dropped, as NGO's budget does
                    Asked++;
                    Entity connection = received[i].SourceConnection;
                    rename.ServerRenameAsked(asks[i].BagIndex, asks[i].Name.ToString(), (refusal, clean) => m_Answers.Add((connection, refusal, clean)));
                }
            }
            if (m_Answers.Count == 0) return;
            foreach ((Entity connection, string refusal, string clean) in m_Answers)
            {
                if (!em.Exists(connection)) continue;   // the player left before the save's word
                var reply = new RenameReplyRpc { Taken = (byte)(refusal == null ? 1 : 0) };
                reply.Text.CopyFromTruncated(refusal ?? clean ?? string.Empty);
                if (refusal == null) Taken++;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, reply);
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
            m_Answers.Clear();
        }
    }

    /// <summary>
    /// Client: the own view's way to ask (<see cref="PlayerRename.Link"/>) and the server's answer onto the dialog's events
    /// (<see cref="PlayerRename.ApplyServerAnswer"/>): a taken name counts down to character select, a refusal asks again. The view decides nothing.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RenameClientSystem : SystemBase
    {
        sealed class Link : PlayerRename.IOwnerLink
        {
            readonly World m_World;
            internal Link(World world) => m_World = world;

            public void Rename(byte bagIndex, string name)
            {
                if (m_World == null || !m_World.IsCreated) return;
                var ask = new RenameRpc { BagIndex = bagIndex };
                ask.Name.CopyFromTruncated(name ?? string.Empty);
                var em = m_World.EntityManager;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, ask);
                em.AddComponent<SendRpcCommandRequest>(rpc);   // no target: to the server
            }
        }

        EntityQuery m_Replies;
        Link m_Link;

        /// <summary>Answers handed to the dialog (tests).</summary>
        public int Answers { get; private set; }

        protected override void OnCreate()
        {
            m_Replies = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RenameReplyRpc>());
            m_Link = new Link(World);
            PlayerRename.Link = m_Link;
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(PlayerRename.Link, m_Link)) PlayerRename.Link = null;
        }

        protected override void OnUpdate()
        {
            if (PlayerRename.Link == null) PlayerRename.Link = m_Link;   // re-bound: a relog in the same process can leave the static null
            if (m_Replies.IsEmpty) return;
            var em = EntityManager;
            using var replies = m_Replies.ToComponentDataArray<RenameReplyRpc>(Allocator.Temp);
            em.DestroyEntity(m_Replies);
            foreach (RenameReplyRpc reply in replies)
            {
                PlayerRename.ApplyServerAnswer(reply.Taken != 0, reply.Text.ToString());
                Answers++;
            }
        }
    }
}
