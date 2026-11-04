using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner's Sponsor request (NGO's <c>PlayerSponsor</c> RequestRpc / RequestByNameRpc / RespondRpc / EndRpc; M8 8.4.4).</summary>
    public struct SponsorOpRpc : IRpcCommand
    {
        public byte Op, Flag;
        public FixedString64Bytes A;
    }

    /// <summary>One page of the owner's Sponsor window (NGO's owner ViewRpc + netBoostOn) as Social's view text, under one <see cref="Seq"/>.</summary>
    public struct SponsorPageRpc : IRpcCommand
    {
        public uint Seq;
        public byte Index, Count, BoostOn;
        public int Points;
        public FixedString512Bytes Chunk;
    }

    /// <summary>A Sponsor request to answer on screen, or (<see cref="Close"/>) its closing (NGO's owner PromptRpc / ClosePromptRpc).</summary>
    public struct SponsorPromptRpc : IRpcCommand
    {
        public byte Close, TheyWouldSponsor;
        public float Seconds;
        public FixedString64Bytes From;
    }

    /// <summary>Server: <see cref="ISponsorOwnerLink"/> for the bodies of the entity netcode, to the player's connection by its PlayerKey.</summary>
    public sealed class EntitySponsorOwnerLink : ISponsorOwnerLink
    {
        public const int MostPages = 16;
        static uint s_Seq;

        /// <summary>Views sent (tests).</summary>
        public static int Views { get; private set; }

        public void View(Component part, string viewText, int points, bool boostOn)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            List<string> pages = TextPages.Split(viewText);
            if (pages.Count > MostPages) return;
            uint seq = ++s_Seq;
            for (int i = 0; i < pages.Count; i++)
                Send(em, connection, new SponsorPageRpc
                {
                    Seq = seq, Index = (byte)i, Count = (byte)pages.Count, BoostOn = (byte)(boostOn ? 1 : 0), Points = points, Chunk = new FixedString512Bytes(pages[i]),
                });
            Views++;
        }

        public void Prompt(Component part, string fromName, bool theyWouldSponsor, float seconds)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection))
                Send(em, connection, new SponsorPromptRpc { TheyWouldSponsor = (byte)(theyWouldSponsor ? 1 : 0), Seconds = seconds, From = ChatText.Name(fromName) });
        }

        public void ClosePrompt(Component part)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) Send(em, connection, new SponsorPromptRpc { Close = 1 });
        }

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>Server: the owners' Sponsor requests at the Social limit (as NGO) on their server bodies.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SponsorServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run (tests).</summary>
        public int Ran { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SponsorOpRpc>());

        protected override void OnUpdate()
        {
            if (m_Asks.IsEmpty) return;
            using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var asks = m_Asks.ToComponentDataArray<SponsorOpRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Asks);
            for (int i = 0; i < asks.Length; i++)
            {
                if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Social, out GameObject body) || !body.TryGetComponent(out IBodySponsor sponsor)) continue;
                sponsor.RunSponsorOp((SponsorOpKind)asks[i].Op, asks[i].A.ToString(), asks[i].Flag != 0);
                Ran++;
            }
        }
    }

    /// <summary>Client: the pages of the own Sponsor window put together (by Seq) and shown on the own view, and the prompts.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SponsorClientSystem : SystemBase
    {
        EntityQuery m_Pages, m_Prompts;
        uint m_Seq;
        string[] m_Parts;

        protected override void OnCreate()
        {
            m_Pages = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SponsorPageRpc>());
            m_Prompts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SponsorPromptRpc>());
        }

        protected override void OnUpdate()
        {
            if (m_Pages.IsEmpty && m_Prompts.IsEmpty) return;
            IBodySponsor own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
            if (!m_Pages.IsEmpty)
            {
                using var pages = m_Pages.ToComponentDataArray<SponsorPageRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(m_Pages);
                foreach (SponsorPageRpc page in pages)
                {
                    if (page.Count == 0 || page.Index >= page.Count) continue;
                    if (page.Seq != m_Seq || m_Parts == null || m_Parts.Length != page.Count) { m_Seq = page.Seq; m_Parts = new string[page.Count]; }
                    m_Parts[page.Index] = page.Chunk.ToString();
                    bool whole = true;
                    foreach (string part in m_Parts) whole &= part != null;
                    if (!whole) continue;
                    string text = string.Concat(m_Parts);
                    m_Parts = null;
                    own?.MirrorView(text, page.Points, page.BoostOn != 0);
                }
            }
            if (!m_Prompts.IsEmpty)
            {
                using var prompts = m_Prompts.ToComponentDataArray<SponsorPromptRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(m_Prompts);
                if (own == null) return;
                foreach (SponsorPromptRpc prompt in prompts)
                    if (prompt.Close != 0) own.MirrorClosePrompt(); else own.MirrorPrompt(prompt.From.ToString(), prompt.TheyWouldSponsor != 0, prompt.Seconds);
            }
        }
    }
}
