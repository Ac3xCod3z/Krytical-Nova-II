using System.Collections.Generic;
using System.Text;
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
    /// <summary>The owner's Contacts request (NGO's twelve <c>PlayerContacts</c> RPCs; M8 8.4.2): a name or a character id, a note, a yes/no.</summary>
    public struct ContactOpRpc : IRpcCommand
    {
        public byte Op, Flag;
        public FixedString64Bytes A;
        public FixedString512Bytes B;
    }

    /// <summary>One page of the owner's Contacts window (NGO's owner ViewRpc) as Social's view text, under one <see cref="Seq"/>.</summary>
    public struct ContactPageRpc : IRpcCommand
    {
        public uint Seq;
        public byte Index, Count;
        public FixedString512Bytes Chunk;
    }

    /// <summary>A contact request to answer on screen, or (<see cref="Close"/>) its closing (NGO's owner PromptRpc / ClosePromptRpc).</summary>
    public struct ContactPromptRpc : IRpcCommand
    {
        public byte Close;
        public FixedString64Bytes Id, Name;
    }

    /// <summary>Text into wire pages that each fit a <see cref="FixedString512Bytes"/>, cut on a character boundary (plain C#).</summary>
    public static class TextPages
    {
        public const int PageBytes = 500;

        public static List<string> Split(string text, int pageBytes = PageBytes)
        {
            var pages = new List<string>();
            text ??= string.Empty;
            int start = 0, bytes = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int width = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                int size = Encoding.UTF8.GetByteCount(text.ToCharArray(i, width));
                if (bytes + size > pageBytes) { pages.Add(text.Substring(start, i - start)); start = i; bytes = 0; }
                bytes += size;
                i += width - 1;
            }
            pages.Add(text.Substring(start));
            return pages;
        }
    }

    /// <summary>
    /// Server: <see cref="IContactsOwnerLink"/> for the bodies of the entity netcode: the list, the prompt and its closing go to the connection of
    /// the player whose server body it is, by its <see cref="PlayerKey"/> (as the trade's link).
    /// </summary>
    public sealed class EntityContactsOwnerLink : IContactsOwnerLink
    {
        public const int MostPages = 255;
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
                Send(em, connection, new ContactPageRpc { Seq = seq, Index = (byte)i, Count = (byte)pages.Count, Chunk = new FixedString512Bytes(pages[i]) });
            Views++;
        }

        public void Prompt(Component part, string fromId, string fromName)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection))
                Send(em, connection, new ContactPromptRpc { Id = ChatText.Name(fromId), Name = ChatText.Name(fromName) });
        }

        public void ClosePrompt(Component part, string fromId)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection))
                Send(em, connection, new ContactPromptRpc { Close = 1, Id = ChatText.Name(fromId) });
        }

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>Server: the owners' Contacts requests at the Social limit (as NGO) on their server bodies.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ContactServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run (tests).</summary>
        public int Ran { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ContactOpRpc>());

        protected override void OnUpdate()
        {
            if (m_Asks.IsEmpty) return;
            using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var asks = m_Asks.ToComponentDataArray<ContactOpRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Asks);
            for (int i = 0; i < asks.Length; i++)
            {
                if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Social, out GameObject body) || !body.TryGetComponent(out IBodyContacts contacts)) continue;
                contacts.RunContactOp((ContactOpKind)asks[i].Op, asks[i].A.ToString(), asks[i].B.ToString(), asks[i].Flag != 0);
                Ran++;
            }
        }
    }

    /// <summary>Client: the pages of the own Contacts window put together (by Seq) and shown on the own view, and the prompts.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ContactClientSystem : SystemBase
    {
        EntityQuery m_Pages, m_Prompts;
        uint m_Seq;
        string[] m_Parts;

        protected override void OnCreate()
        {
            m_Pages = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ContactPageRpc>());
            m_Prompts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ContactPromptRpc>());
        }

        protected override void OnUpdate()
        {
            if (m_Pages.IsEmpty && m_Prompts.IsEmpty) return;
            IBodyContacts own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
            if (!m_Pages.IsEmpty)
            {
                using var pages = m_Pages.ToComponentDataArray<ContactPageRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(m_Pages);
                foreach (ContactPageRpc page in pages)
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
                using var prompts = m_Prompts.ToComponentDataArray<ContactPromptRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(m_Prompts);
                if (own == null) return;
                foreach (ContactPromptRpc prompt in prompts)
                    if (prompt.Close != 0) own.MirrorClosePrompt(prompt.Id.ToString()); else own.MirrorPrompt(prompt.Id.ToString(), prompt.Name.ToString());
            }
        }
    }
}
