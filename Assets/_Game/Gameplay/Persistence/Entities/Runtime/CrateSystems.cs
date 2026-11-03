using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner's store or withdraw (NGO's <c>PlayerCrate</c> StoreRpc / WithdrawRpc; M8 group 4): the Pack cell or the item id, a count.</summary>
    public struct CrateOpRpc : IRpcCommand
    {
        public byte Withdraw;
        public ushort A, Count;
    }

    /// <summary>One page of the owner's Crate (NGO's owner ViewRpc) as <see cref="CrateText"/>, version-stamped.</summary>
    public struct CratePageRpc : IRpcCommand
    {
        public const int PageChars = 480, MostPages = 32;
        public int Version;
        public byte Index, Count;
        public FixedString512Bytes Chunk;
    }

    /// <summary>The Crate as text: "id:count" pairs joined by ',' (plain C#).</summary>
    public static class CrateText
    {
        public static string Encode(List<(ushort itemId, int count)> entries)
        {
            var text = new StringBuilder();
            for (int i = 0; i < entries.Count; i++) text.Append(i == 0 ? "" : ",").Append(entries[i].itemId).Append(':').Append(entries[i].count);
            return text.ToString();
        }

        public static bool TryDecode(string text, List<(ushort itemId, int count)> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(text)) return true;
            foreach (string pair in text.Split(','))
            {
                int colon = pair.IndexOf(':');
                if (colon <= 0 || !ushort.TryParse(pair.Substring(0, colon), out ushort id) || !int.TryParse(pair.Substring(colon + 1), out int count)) return false;
                into.Add((id, count));
            }
            return true;
        }
    }

    /// <summary>Server: the Crate requests at the Money limit (as NGO) on the server body; each player's Crate to them whenever it changes.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrateServerSystem : SystemBase
    {
        public float Interval = 0.25f;
        EntityQuery m_Asks, m_InGame;
        readonly Dictionary<Entity, int> m_Told = new Dictionary<Entity, int>();
        readonly List<Entity> m_Gone = new List<Entity>();
        readonly List<(ushort, int)> m_Entries = new List<(ushort, int)>();
        double m_Next;

        /// <summary>Requests run and Crates sent (tests).</summary>
        public int Ran { get; private set; }
        public int Sent { get; private set; }

        protected override void OnCreate()
        {
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CrateOpRpc>());
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<CrateOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyCrate crate))
                    {
                        if (asks[i].Withdraw != 0) crate.RunWithdraw(asks[i].A, asks[i].Count); else crate.RunStore(asks[i].A, asks[i].Count);
                        Ran++;
                    }
            }
            double now = SystemAPI.Time.ElapsedTime;
            if (now < m_Next) return;
            m_Next = now + Interval;
            m_Gone.Clear();
            foreach (Entity ghost in m_Told.Keys) if (!em.Exists(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Told.Remove(ghost);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            using var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp);
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !entry.Body.TryGetComponent(out IBodyCrate crate)) continue;
                if (m_Told.TryGetValue(entry.Ghost, out int told) && told == crate.Version) continue;
                Entity connection = Entity.Null;
                for (int i = 0; i < ids.Length; i++) if (ids[i].Value == entry.NetworkId) { connection = connections[i]; break; }
                if (connection == Entity.Null) continue;
                crate.ReadEntries(m_Entries);
                string text = CrateText.Encode(m_Entries);
                int count = Math.Max(1, (text.Length + CratePageRpc.PageChars - 1) / CratePageRpc.PageChars);
                m_Told[entry.Ghost] = crate.Version;
                if (count > CratePageRpc.MostPages) continue;
                for (int i = 0; i < count; i++)
                {
                    int start = i * CratePageRpc.PageChars;
                    string chunk = start < text.Length ? text.Substring(start, Math.Min(CratePageRpc.PageChars, text.Length - start)) : string.Empty;
                    Entity rpc = em.CreateEntity();
                    em.AddComponentData(rpc, new CratePageRpc { Version = crate.Version, Index = (byte)i, Count = (byte)count, Chunk = new FixedString512Bytes(chunk) });
                    em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
                Sent++;
            }
        }
    }

    /// <summary>Client: the pages of the own Crate put together (by version) and shown on the own view.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrateClientSystem : SystemBase
    {
        EntityQuery m_Pages;
        int m_Version = int.MinValue;
        string[] m_Parts;
        readonly List<(ushort, int)> m_Entries = new List<(ushort, int)>();

        protected override void OnCreate() =>
            m_Pages = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CratePageRpc>());

        protected override void OnUpdate()
        {
            if (m_Pages.IsEmpty) return;
            using var pages = m_Pages.ToComponentDataArray<CratePageRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Pages);
            foreach (CratePageRpc page in pages)
            {
                if (page.Count == 0 || page.Index >= page.Count) continue;
                if (page.Version != m_Version || m_Parts == null || m_Parts.Length != page.Count) { m_Version = page.Version; m_Parts = new string[page.Count]; }
                m_Parts[page.Index] = page.Chunk.ToString();
                bool whole = true;
                foreach (string part in m_Parts) whole &= part != null;
                if (!whole || !CrateText.TryDecode(string.Concat(m_Parts), m_Entries)) continue;
                m_Parts = null;
                foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                    if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out IBodyCrate own))
                    { own.MirrorEntries(m_Entries); break; }
            }
        }
    }
}
