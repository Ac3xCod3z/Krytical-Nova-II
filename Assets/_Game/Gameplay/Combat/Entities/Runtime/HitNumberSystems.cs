using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// One hit's number for the attacker's own game (M4 step 7; NGO PlayerMeleeCombat.DamageFeedbackRpc, SendTo.Owner). An RPC is
    /// reliable: every number arrives, however many hits an area skill lands at once (owner, 5 Nov 2026).
    /// </summary>
    public struct HitNumberRpc : IRpcCommand
    {
        public int XMm, YMm, ZMm;
        public int DamageMilli, ReflectedMilli;
        /// <summary><see cref="HitEventRing"/> bits: critical, evaded, reflected, shattered, skill.</summary>
        public byte Flags;
    }

    public static class HitNumbers
    {
        /// <summary>Server: sends one number to the player with <paramref name="networkId"/>. False when nobody has it.</summary>
        public static bool Send(World server, int networkId, Vector3 position, DamageResult result, bool fromSkill)
        {
            if (server == null || !server.IsCreated || networkId == 0) return false;
            EntityManager em = server.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                if (ids[i].Value != networkId) continue;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, Of(position, result, fromSkill));
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                return true;
            }
            return false;
        }

        public static HitNumberRpc Of(Vector3 position, DamageResult result, bool fromSkill) => new HitNumberRpc
        {
            XMm = (int)math.round(position.x * 1000f), YMm = (int)math.round(position.y * 1000f), ZMm = (int)math.round(position.z * 1000f),
            DamageMilli = Mathf.RoundToInt(result.DamageDealt * 1000f),
            ReflectedMilli = Mathf.RoundToInt(result.ReflectedDamage * 1000f),
            Flags = (byte)((result.IsCritical ? HitEventRing.Critical : 0) | (result.IsEvaded ? HitEventRing.Evaded : 0)
                | (result.IsReflected ? HitEventRing.Reflected : 0) | (result.ShieldShattered ? HitEventRing.Shattered : 0)
                | (fromSkill ? HitEventRing.Skill : 0)),
        };

        public static DamageResult ResultOf(in HitNumberRpc hit) => new DamageResult
        {
            DamageDealt = hit.DamageMilli / 1000f,
            ReflectedDamage = hit.ReflectedMilli / 1000f,
            IsCritical = (hit.Flags & HitEventRing.Critical) != 0,
            IsEvaded = (hit.Flags & HitEventRing.Evaded) != 0,
            IsReflected = (hit.Flags & HitEventRing.Reflected) != 0,
            ShieldShattered = (hit.Flags & HitEventRing.Shattered) != 0,
        };
    }

    /// <summary>Server: each hit a player's server body deals is sent to that player's own game as a number (one per hit).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HitNumberServerSystem : SystemBase
    {
        readonly HashSet<PlayerMeleeCombat> m_Heard = new HashSet<PlayerMeleeCombat>();

        /// <summary>Numbers sent (tests).</summary>
        public int Sent { get; private set; }

        protected override void OnUpdate()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out PlayerMeleeCombat melee)
                    || !m_Heard.Add(melee)) continue;
                int networkId = entry.NetworkId;
                melee.DamageShown += (position, result, fromSkill) => { if (HitNumbers.Send(World, networkId, position, result, fromSkill)) Sent++; };
            }
            m_Heard.RemoveWhere(m => m == null);
        }
    }

    /// <summary>Client: every number this player dealt is shown on its own view, each once (NGO DamageFeedbackRpc's body).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HitNumberClientSystem : SystemBase
    {
        EntityQuery m_Numbers;

        /// <summary>Numbers received (tests).</summary>
        public int Received { get; private set; }

        protected override void OnCreate() =>
            m_Numbers = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<HitNumberRpc>());

        protected override void OnUpdate()
        {
            if (m_Numbers.IsEmpty) return;
            using var numbers = m_Numbers.ToComponentDataArray<HitNumberRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Numbers);
            PlayerMeleeCombat own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
            foreach (HitNumberRpc hit in numbers)
            {
                Received++;
                if (own != null)
                    own.ShowDamageFeedback(new Vector3(hit.XMm, hit.YMm, hit.ZMm) * 0.001f, HitNumbers.ResultOf(hit), (hit.Flags & HitEventRing.Skill) != 0);
            }
        }
    }
}
