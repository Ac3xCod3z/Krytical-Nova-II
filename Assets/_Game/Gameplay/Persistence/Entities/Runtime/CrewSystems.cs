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
    /// <summary>The owner's Crew request (NGO's twelve <c>PlayerCrew</c> RPCs; M8 8.4.5): names, a title or the announcement, the symbol bytes, a yes/no.</summary>
    public struct CrewOpRpc : IRpcCommand
    {
        public byte Op, Flag, B0, B1, B2, B3;
        public FixedString64Bytes A;
        public FixedString512Bytes B;
    }

    /// <summary>One page of the owner's Crew window (NGO's owner ViewRpc) as Social's view text, under one <see cref="Seq"/>.</summary>
    public struct CrewPageRpc : IRpcCommand
    {
        public uint Seq;
        public byte Index, Count;
        public FixedString512Bytes Chunk;
    }

    /// <summary>A Crew invitation to answer on screen, or (<see cref="Close"/>) its closing (NGO's owner PromptRpc / ClosePromptRpc).</summary>
    public struct CrewPromptRpc : IRpcCommand
    {
        public byte Close;
        public float Seconds;
        public FixedString64Bytes From;
        public FixedString32Bytes Crew;
    }

    /// <summary>Server: <see cref="ICrewOwnerLink"/> for the bodies of the entity netcode, to the player's connection by its PlayerKey.</summary>
    public sealed class EntityCrewOwnerLink : ICrewOwnerLink
    {
        public const int MostPages = 64;
        static uint s_Seq;

        /// <summary>Views sent (tests).</summary>
        public static int Views { get; private set; }

        public void View(Component part, string viewText)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            List<string> pages = TextPages.Split(viewText);
            if (pages.Count > MostPages) return;
            uint seq = ++s_Seq;
            for (int i = 0; i < pages.Count; i++)
                Send(em, connection, new CrewPageRpc { Seq = seq, Index = (byte)i, Count = (byte)pages.Count, Chunk = new FixedString512Bytes(pages[i]) });
            Views++;
        }

        public void Prompt(Component part, string inviterName, string crewName, float seconds)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            var prompt = new CrewPromptRpc { Seconds = seconds, From = ChatText.Name(inviterName) };
            prompt.Crew.CopyFromTruncated(crewName ?? string.Empty);
            Send(em, connection, prompt);
        }

        public void ClosePrompt(Component part)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) Send(em, connection, new CrewPromptRpc { Close = 1 });
        }

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>The Crew tag on the wire (plain C#): <see cref="BodyCrewTag"/> to and from the <see cref="PlayerCrewTag"/> ghost field.</summary>
    public static class CrewTagWire
    {
        public static PlayerCrewTag From(in BodyCrewTag tag)
        {
            var wire = new PlayerCrewTag { Rank = tag.rank, Symbol = tag.symbol, Colour = tag.colour, TagColour = tag.tagColour, TagBackground = tag.tagBackground };
            wire.Crew.CopyFromTruncated(tag.crew ?? string.Empty);
            wire.Title.CopyFromTruncated(tag.title ?? string.Empty);
            return wire;
        }

        public static BodyCrewTag To(in PlayerCrewTag wire) => new BodyCrewTag
        {
            crew = wire.Crew.ToString(), title = wire.Title.ToString(), rank = wire.Rank, symbol = wire.Symbol, colour = wire.Colour,
            tagColour = wire.TagColour, tagBackground = wire.TagBackground,
        };

        public static bool Same(in PlayerCrewTag a, in PlayerCrewTag b) => a.Crew.Equals(b.Crew) && a.Title.Equals(b.Title) && a.Rank == b.Rank
            && a.Symbol == b.Symbol && a.Colour == b.Colour && a.TagColour == b.TagColour && a.TagBackground == b.TagBackground;
    }

    /// <summary>
    /// Server: the owners' Crew requests at the Social limit (as NGO) on their server bodies; and every player ghost's Crew tag equal to its
    /// body's (NGO's netTag, read by everyone: the namecards).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrewServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run and ghosts rewritten (tests).</summary>
        public int Ran { get; private set; }
        public int Writes { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CrewOpRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<CrewOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                {
                    if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Social, out GameObject body) || !body.TryGetComponent(out IBodyCrew crew)) continue;
                    CrewOpRpc ask = asks[i];
                    crew.RunCrewOp((CrewOpKind)ask.Op, ask.A.ToString(), ask.B.ToString(), ask.B0, ask.B1, ask.B2, ask.B3, ask.Flag != 0);
                    Ran++;
                }
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerCrewTag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out IBodyCrew crew)) continue;
                PlayerCrewTag now = CrewTagWire.From(crew.ShownTag);
                if (CrewTagWire.Same(now, em.GetComponentData<PlayerCrewTag>(entry.Ghost))) continue;
                em.SetComponentData(entry.Ghost, now);
                Writes++;
            }
        }
    }

    /// <summary>Client: every view shows its player's Crew tag from the ghost; the own view its Crew window and invitations.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrewClientSystem : SystemBase
    {
        EntityQuery m_Pages, m_Prompts;
        uint m_Seq;
        string[] m_Parts;

        protected override void OnCreate()
        {
            m_Pages = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CrewPageRpc>());
            m_Prompts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CrewPromptRpc>());
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            IBodyCrew own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !entry.Body.TryGetComponent(out IBodyCrew crew)) continue;
                if (em.HasComponent<PlayerCrewTag>(entry.Ghost)) crew.MirrorTag(CrewTagWire.To(em.GetComponentData<PlayerCrewTag>(entry.Ghost)));
                if (entry.IsLocal) own = crew;
            }
            if (!m_Pages.IsEmpty)
            {
                using var pages = m_Pages.ToComponentDataArray<CrewPageRpc>(Allocator.Temp);
                em.DestroyEntity(m_Pages);
                foreach (CrewPageRpc page in pages)
                {
                    if (page.Count == 0 || page.Index >= page.Count) continue;
                    if (page.Seq != m_Seq || m_Parts == null || m_Parts.Length != page.Count) { m_Seq = page.Seq; m_Parts = new string[page.Count]; }
                    m_Parts[page.Index] = page.Chunk.ToString();
                    bool whole = true;
                    foreach (string part in m_Parts) whole &= part != null;
                    if (!whole) continue;
                    string text = string.Concat(m_Parts);
                    m_Parts = null;
                    own?.MirrorView(text);
                }
            }
            if (!m_Prompts.IsEmpty)
            {
                using var prompts = m_Prompts.ToComponentDataArray<CrewPromptRpc>(Allocator.Temp);
                em.DestroyEntity(m_Prompts);
                if (own == null) return;
                foreach (CrewPromptRpc prompt in prompts)
                    if (prompt.Close != 0) own.MirrorClosePrompt(); else own.MirrorPrompt(prompt.From.ToString(), prompt.Crew.ToString(), prompt.Seconds);
            }
        }
    }
}
