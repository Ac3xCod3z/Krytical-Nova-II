using System.Collections.Generic;
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
    /// <summary>The owner's Follow of a Squad member by name (NGO's FollowRpc / FollowByNameRpc; M8 8.4.3b).</summary>
    public struct FollowRpc : IRpcCommand
    {
        public FixedString64Bytes Name;
    }

    /// <summary>Whom the owner follows now, or empty (NGO's owner FollowingRpc).</summary>
    public struct FollowingRpc : IRpcCommand
    {
        public FixedString64Bytes Name;
    }

    /// <summary>
    /// The player's own move orders seen on the server (plain C#): a click (or a jump with it) arrives as a new <c>PlayerInput.ClickSeq</c>;
    /// the first one seen for a player only sets the mark. What NGO's <c>PointAndClickPlayer.MoveOrdered</c> told PlayerFollow.
    /// </summary>
    public sealed class OwnClickWatch
    {
        readonly Dictionary<PlayerKey, ushort> m_Seen = new Dictionary<PlayerKey, ushort>();

        /// <summary>True when <paramref name="clickSeq"/> is a click of the player's own that was not seen before.</summary>
        public bool IsNewClick(PlayerKey key, ushort clickSeq)
        {
            bool known = m_Seen.TryGetValue(key, out ushort seen);
            m_Seen[key] = clickSeq;
            return known && clickSeq != 0 && clickSeq != seen;
        }

        public void Forget(PlayerKey key) => m_Seen.Remove(key);
    }

    /// <summary>Server: <see cref="IFollowOwnerLink"/> for the bodies of the entity netcode, to the player's connection by its PlayerKey.</summary>
    public sealed class EntityFollowOwnerLink : IFollowOwnerLink
    {
        public void Following(Component part, string memberName)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, new FollowingRpc { Name = ChatText.Name(memberName) });
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>Server: the owners' Follow requests at the Move limit (as NGO) on their server bodies, and their own clicks, which end a Follow.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FollowServerSystem : SystemBase
    {
        EntityQuery m_Asks;
        readonly OwnClickWatch m_Clicks = new OwnClickWatch();

        /// <summary>Requests run and own clicks passed on (tests).</summary>
        public int Ran { get; private set; }
        public int OwnClicks { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<FollowRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<FollowRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                {
                    if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Move, out GameObject body) || !body.TryGetComponent(out IBodyFollow follow)) continue;
                    follow.RunFollow(asks[i].Name.ToString());
                    Ran++;
                }
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerInput>(entry.Ghost)) continue;
                if (!m_Clicks.IsNewClick(entry.Key, em.GetComponentData<PlayerInput>(entry.Ghost).ClickSeq)) continue;
                if (entry.Body.TryGetComponent(out IBodyFollow follow)) { follow.OwnMoveOrdered(); OwnClicks++; }
            }
        }
    }

    /// <summary>Client: the own view shows whom it follows.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FollowClientSystem : SystemBase
    {
        EntityQuery m_Following;

        protected override void OnCreate() =>
            m_Following = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<FollowingRpc>());

        protected override void OnUpdate()
        {
            if (m_Following.IsEmpty) return;
            using var shown = m_Following.ToComponentDataArray<FollowingRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Following);
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out IBodyFollow own))
                {
                    foreach (FollowingRpc rpc in shown) own.MirrorFollowing(rpc.Name.ToString());
                    break;
                }
        }
    }
}
