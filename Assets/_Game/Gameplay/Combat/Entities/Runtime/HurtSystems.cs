using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// The hurt effect on the hit player's own screen (M4 step 9; NGO PlayerResources.DamagedRpc, SendTo.Owner): the share of Max HP a
    /// real hit took, from any attacker (a Hostile, another player). Reliable.
    /// </summary>
    public struct HurtRpc : IRpcCommand
    {
        /// <summary>The share of Max HP, in thousandths (0-1000).</summary>
        public short FractionMilli;
    }

    /// <summary>Server: every real hit on a player's server body is sent to that player's own game as the hurt effect.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HurtServerSystem : SystemBase
    {
        readonly HashSet<PlayerResources> m_Heard = new HashSet<PlayerResources>();

        /// <summary>Hurt effects sent (tests).</summary>
        public int Sent { get; private set; }

        protected override void OnUpdate()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out PlayerResources resources)
                    || !m_Heard.Add(resources)) continue;
                int networkId = entry.NetworkId;
                resources.Hurt += fraction => { if (HitNumbers.SendTo(World, networkId, Of(fraction))) Sent++; };
            }
            m_Heard.RemoveWhere(r => r == null);
        }

        public static HurtRpc Of(float fraction01) => new HurtRpc { FractionMilli = (short)Mathf.RoundToInt(Mathf.Clamp01(fraction01) * 1000f) };
    }

    /// <summary>Client: the hurt effect on this player's screen, once per message (NGO DamagedRpc's body).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HurtClientSystem : SystemBase
    {
        EntityQuery m_Hurts;

        /// <summary>Hurt effects received (tests).</summary>
        public int Received { get; private set; }

        protected override void OnCreate() =>
            m_Hurts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<HurtRpc>());

        protected override void OnUpdate()
        {
            if (m_Hurts.IsEmpty) return;
            using var hurts = m_Hurts.ToComponentDataArray<HurtRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Hurts);
            foreach (HurtRpc hurt in hurts)
            {
                Received++;
                ClientPresentationRegistry.Feel?.LocalPlayerDamaged(hurt.FractionMilli / 1000f);
            }
        }
    }
}
