using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner's Squad request (NGO's six <c>PlayerParty</c> RPCs; M8 8.4.3): a player's name, a yes/no.</summary>
    public struct PartyOpRpc : IRpcCommand
    {
        public byte Op, Flag;
        public FixedString64Bytes Name;
    }

    /// <summary>The owner's Squad roster, leader first, at most five (NGO's owner RosterRpc): ids are PlayerKeys, the owner's own 0.</summary>
    public struct PartyRosterRpc : IRpcCommand
    {
        public byte Count;
        public ulong Id0, Id1, Id2, Id3, Id4;
        public int L0, L1, L2, L3, L4;
        public FixedString64Bytes N0, N1, N2, N3, N4;

        public const int Most = 5;

        public static PartyRosterRpc From(BodyPartyMember[] members)
        {
            var rpc = new PartyRosterRpc();
            int count = members != null ? System.Math.Min(members.Length, Most) : 0;
            rpc.Count = (byte)count;
            for (int i = 0; i < count; i++)
            {
                ulong id = members[i].id;
                int level = members[i].level;
                FixedString64Bytes name = ChatText.Name(members[i].name);
                switch (i)
                {
                    case 0: rpc.Id0 = id; rpc.L0 = level; rpc.N0 = name; break;
                    case 1: rpc.Id1 = id; rpc.L1 = level; rpc.N1 = name; break;
                    case 2: rpc.Id2 = id; rpc.L2 = level; rpc.N2 = name; break;
                    case 3: rpc.Id3 = id; rpc.L3 = level; rpc.N3 = name; break;
                    default: rpc.Id4 = id; rpc.L4 = level; rpc.N4 = name; break;
                }
            }
            return rpc;
        }

        public BodyPartyMember[] Members()
        {
            int count = System.Math.Min((int)Count, Most);
            var members = new BodyPartyMember[count];
            for (int i = 0; i < count; i++)
                members[i] = i switch
                {
                    0 => new BodyPartyMember { id = Id0, level = L0, name = N0.ToString() },
                    1 => new BodyPartyMember { id = Id1, level = L1, name = N1.ToString() },
                    2 => new BodyPartyMember { id = Id2, level = L2, name = N2.ToString() },
                    3 => new BodyPartyMember { id = Id3, level = L3, name = N3.ToString() },
                    _ => new BodyPartyMember { id = Id4, level = L4, name = N4.ToString() },
                };
            return members;
        }
    }

    /// <summary>A Squad invitation to answer on screen, or (<see cref="Close"/>) its closing (NGO's owner InvitePromptRpc / InviteClosedRpc).</summary>
    public struct PartyInviteRpc : IRpcCommand
    {
        public byte Close;
        public float Seconds;
        public FixedString64Bytes From;
    }

    /// <summary>Server: <see cref="IPartyOwnerLink"/> for the bodies of the entity netcode, to the player's connection by its PlayerKey.</summary>
    public sealed class EntityPartyOwnerLink : IPartyOwnerLink
    {
        /// <summary>Rosters sent (tests).</summary>
        public static int Rosters { get; private set; }

        public void Roster(Component part, BodyPartyMember[] members)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            Send(em, connection, PartyRosterRpc.From(members));
            Rosters++;
        }

        public void Invite(Component part, string inviterName, float seconds)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection))
                Send(em, connection, new PartyInviteRpc { Seconds = seconds, From = ChatText.Name(inviterName) });
        }

        public void InviteClosed(Component part)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) Send(em, connection, new PartyInviteRpc { Close = 1 });
        }

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Server: the owners' Squad requests at the Social limit (as NGO) on their server bodies; and every player ghost's Squad id equal to its
    /// body's (NGO's netPartyId, read by everyone: the minimap, namecards and the hostiles' shared tags).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PartyServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run and ghosts rewritten (tests).</summary>
        public int Ran { get; private set; }
        public int Writes { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PartyOpRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<PartyOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                {
                    if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Social, out GameObject body) || !body.TryGetComponent(out IBodyParty party)) continue;
                    party.RunPartyOp((PartyOpKind)asks[i].Op, asks[i].Name.ToString(), asks[i].Flag != 0);
                    Ran++;
                }
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerPartyTag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out IBodyParty party)) continue;
                if (em.GetComponentData<PlayerPartyTag>(entry.Ghost).Id == party.PartyId) continue;
                em.SetComponentData(entry.Ghost, new PlayerPartyTag { Id = party.PartyId });
                Writes++;
            }
        }
    }

    /// <summary>Client: every view shows its player's Squad id from the ghost; the own view its roster and invitations.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PartyClientSystem : SystemBase
    {
        EntityQuery m_Rosters, m_Invites;

        protected override void OnCreate()
        {
            m_Rosters = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PartyRosterRpc>());
            m_Invites = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PartyInviteRpc>());
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            IBodyParty own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !entry.Body.TryGetComponent(out IBodyParty party)) continue;
                if (em.HasComponent<PlayerPartyTag>(entry.Ghost)) party.MirrorPartyId(em.GetComponentData<PlayerPartyTag>(entry.Ghost).Id);
                if (entry.IsLocal) own = party;
            }
            if (!m_Rosters.IsEmpty)
            {
                using var rosters = m_Rosters.ToComponentDataArray<PartyRosterRpc>(Allocator.Temp);
                em.DestroyEntity(m_Rosters);
                if (own != null) foreach (PartyRosterRpc roster in rosters) own.MirrorRoster(roster.Members());
            }
            if (!m_Invites.IsEmpty)
            {
                using var invites = m_Invites.ToComponentDataArray<PartyInviteRpc>(Allocator.Temp);
                em.DestroyEntity(m_Invites);
                if (own != null)
                    foreach (PartyInviteRpc invite in invites)
                        if (invite.Close != 0) own.MirrorInviteClosed(); else own.MirrorInvite(invite.From.ToString(), invite.Seconds);
            }
        }
    }
}
