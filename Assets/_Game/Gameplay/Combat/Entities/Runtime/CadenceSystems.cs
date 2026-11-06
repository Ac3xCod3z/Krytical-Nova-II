using System.Collections.Generic;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>The own view's Cadence press for its server body (Cadence step 2; NGO PlayerCadence.PressRpc, Owner -> Server).</summary>
    public struct CadencePressRpc : IRpcCommand
    {
        public FixedString64Bytes SkillId;
        /// <summary>The press on the shared clock, as the player heard the beat (NGO pressedAt).</summary>
        public double PressedAt;
    }

    /// <summary>The server body's Cadence word for its owner's game (NGO EarnedRpc / ComboRpc, SendTo.Owner).</summary>
    public struct CadenceOwnerRpc : IRpcCommand
    {
        public const byte Earned = 1, Combo = 2;
        public byte Kind;
        public FixedString64Bytes SkillId;
        public byte Step;
        public byte Redline;
    }

    /// <summary>
    /// Server: a press from an owner's view is judged by that player's server body as NGO's PressRpc body (budget CallKind.Combat), and
    /// the server body's "on Cadence" and combo steps go to the owner's game, each once.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CadenceServerSystem : SystemBase
    {
        readonly HashSet<PlayerCadence> m_Heard = new HashSet<PlayerCadence>();
        EntityQuery m_Presses;

        /// <summary>Presses judged, words sent (tests).</summary>
        public int Judged { get; private set; }
        public int Sent { get; private set; }

        protected override void OnCreate() =>
            m_Presses = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CadencePressRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Presses.IsEmpty)
            {
                using var received = m_Presses.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var presses = m_Presses.ToComponentDataArray<CadencePressRpc>(Allocator.Temp);
                em.DestroyEntity(m_Presses);
                for (int i = 0; i < presses.Length; i++)
                {
                    Entity connection = received[i].SourceConnection;
                    if (!em.Exists(connection) || !em.HasComponent<NetworkId>(connection)) continue;
                    if (!GhostBodyRegistry.TryGetByNetworkId(World, em.GetComponentData<NetworkId>(connection).Value, out GhostBodyEntry entry)
                        || entry.Body == null || !entry.Body.TryGetComponent(out PlayerCadence cadence)) continue;
                    if (RpcBudget.Spend(entry.Key, CallKind.Combat, UnityEngine.Time.realtimeSinceStartup) != RpcBudget.SpendResult.Allowed) continue;
                    cadence.ServerNotePressFromView(presses[i].SkillId.ToString(), presses[i].PressedAt);
                    Judged++;
                }
            }

            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out PlayerCadence cadence)
                    || !m_Heard.Add(cadence)) continue;
                int networkId = entry.NetworkId;
                cadence.EarnedForOwner += skillId =>
                {
                    if (HitNumbers.SendTo(World, networkId, new CadenceOwnerRpc { Kind = CadenceOwnerRpc.Earned, SkillId = new FixedString64Bytes(skillId) })) Sent++;
                };
                cadence.ComboForOwner += (step, redline) =>
                {
                    if (HitNumbers.SendTo(World, networkId, new CadenceOwnerRpc { Kind = CadenceOwnerRpc.Combo, Step = (byte)step, Redline = (byte)(redline ? 1 : 0) })) Sent++;
                };
            }
            m_Heard.RemoveWhere(c => c == null);
        }
    }

    /// <summary>
    /// Client: the own view's Cadence presses go to the server (its PlayerCadence judges nothing here), and the server's words are shown
    /// on the own view: "ON CADENCE" and the COMBO / REDLINE steps (NGO EarnedRpc / ComboRpc bodies).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CadenceClientSystem : SystemBase
    {
        PlayerCadence m_Own;
        EntityQuery m_Words;

        /// <summary>Presses sent, words shown (tests).</summary>
        public int Pressed { get; private set; }
        public int Shown { get; private set; }

        protected override void OnCreate() =>
            m_Words = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CadenceOwnerRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            PlayerCadence own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
            if (!ReferenceEquals(own, m_Own))
            {
                if (m_Own != null) m_Own.PressFromView -= Send;
                m_Own = own;
                if (m_Own != null) m_Own.PressFromView += Send;
            }

            if (m_Words.IsEmpty) return;
            using var words = m_Words.ToComponentDataArray<CadenceOwnerRpc>(Allocator.Temp);
            em.DestroyEntity(m_Words);
            foreach (CadenceOwnerRpc word in words)
            {
                if (m_Own == null) continue;
                if (word.Kind == CadenceOwnerRpc.Earned) m_Own.ShowEarnedFromServer(word.SkillId.ToString());
                else if (word.Kind == CadenceOwnerRpc.Combo) m_Own.ShowComboFromServer(word.Step, word.Redline != 0);
                Shown++;
            }
        }

        protected override void OnDestroy()
        {
            if (m_Own != null) m_Own.PressFromView -= Send;
        }

        void Send(string skillId, double pressedAt)
        {
            EntityManager em = EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new CadencePressRpc { SkillId = new FixedString64Bytes(skillId), PressedAt = pressedAt });
            em.AddComponent<SendRpcCommandRequest>(rpc);   // to the server
            Pressed++;
        }
    }
}
