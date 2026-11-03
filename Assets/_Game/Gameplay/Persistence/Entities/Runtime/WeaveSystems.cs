using System;
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
    /// <summary>The owner's Weave, dye, Morph or toggle request (NGO's seven <c>PlayerWeaves</c> RPCs; M8 8.3.9).</summary>
    public struct WeaveOpRpc : IRpcCommand
    {
        public byte Op, Slot, Stage;
        public ushort Id;
    }

    /// <summary>One page of the owner's Weave library (NGO's owner-only netLines + netMorphs; M8 8.3.10), as <see cref="WeaveLibraryText"/>.</summary>
    public struct WeaveLibraryPageRpc : IRpcCommand
    {
        public const int PageChars = 480, MostPages = 64;
        public int Version;
        public byte Index, Count;
        public FixedString512Bytes Chunk;
    }

    /// <summary>The library as text: "L" then the gear lines, "|M" then the Morphs, each comma-separated (plain C#, tested on its own).</summary>
    public static class WeaveLibraryText
    {
        public static string Encode(IEnumerable<ushort> lines, IEnumerable<uint> morphs)
        {
            var text = new StringBuilder("L");
            var sortedLines = new List<ushort>(lines);
            sortedLines.Sort();
            for (int i = 0; i < sortedLines.Count; i++) text.Append(i == 0 ? "" : ",").Append(sortedLines[i]);
            text.Append("|M");
            var sortedMorphs = new List<uint>(morphs);
            sortedMorphs.Sort();
            for (int i = 0; i < sortedMorphs.Count; i++) text.Append(i == 0 ? "" : ",").Append(sortedMorphs[i]);
            return text.ToString();
        }

        public static bool TryDecode(string text, out ushort[] lines, out uint[] morphs)
        {
            lines = Array.Empty<ushort>();
            morphs = Array.Empty<uint>();
            if (string.IsNullOrEmpty(text) || !text.StartsWith("L")) return false;
            int bar = text.IndexOf("|M", StringComparison.Ordinal);
            if (bar < 0) return false;
            var l = new List<ushort>();
            foreach (string part in text.Substring(1, bar - 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            { if (!ushort.TryParse(part, out ushort v)) return false; l.Add(v); }
            var m = new List<uint>();
            foreach (string part in text.Substring(bar + 2).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            { if (!uint.TryParse(part, out uint v)) return false; m.Add(v); }
            lines = l.ToArray();
            morphs = m.ToArray();
            return true;
        }
    }

    /// <summary>Server: the Weave requests run on the server body by NGO's rules: the toggles at the Items limit, the rest at Money (as NGO).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WeaveRequestServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run (tests).</summary>
        public int Ran { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<WeaveOpRpc>());

        protected override void OnUpdate()
        {
            if (m_Asks.IsEmpty) return;
            using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var asks = m_Asks.ToComponentDataArray<WeaveOpRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Asks);
            for (int i = 0; i < asks.Length; i++)
            {
                var op = (WeaveOpKind)asks[i].Op;
                CallKind kind = op == WeaveOpKind.Helmet || op == WeaveOpKind.Cape ? CallKind.Items : CallKind.Money;
                if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, kind, out GameObject body) && body.TryGetComponent(out IBodyWeaves weaves))
                {
                    weaves.RunWeaveOp(op, asks[i].Slot, asks[i].Id, asks[i].Stage);
                    Ran++;
                }
            }
        }
    }

    /// <summary>Server: each player's Weave library to their own connection, whole, whenever its version changes (and on arrival).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WeaveLibrarySyncServerSystem : SystemBase
    {
        public float Interval = 0.5f;
        readonly Dictionary<Entity, int> m_Told = new Dictionary<Entity, int>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_InGame;
        double m_Next;

        /// <summary>Libraries sent (tests).</summary>
        public int Sent { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (now < m_Next) return;
            m_Next = now + Interval;
            var em = EntityManager;
            m_Gone.Clear();
            foreach (Entity ghost in m_Told.Keys) if (!em.Exists(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Told.Remove(ghost);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            using var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp);
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost)) continue;
                if (!entry.Body.TryGetComponent(out IBodyWeaveLibrary library)) continue;
                if (m_Told.TryGetValue(entry.Ghost, out int told) && told == library.Version) continue;
                Entity connection = Entity.Null;
                for (int i = 0; i < ids.Length; i++) if (ids[i].Value == entry.NetworkId) { connection = connections[i]; break; }
                if (connection == Entity.Null) continue;
                string text = WeaveLibraryText.Encode(library.Lines, library.Morphs);
                int count = Math.Max(1, (text.Length + WeaveLibraryPageRpc.PageChars - 1) / WeaveLibraryPageRpc.PageChars);
                if (count > WeaveLibraryPageRpc.MostPages) { m_Told[entry.Ghost] = library.Version; continue; }   // larger than any library NGO keeps
                for (int i = 0; i < count; i++)
                {
                    int start = i * WeaveLibraryPageRpc.PageChars;
                    string chunk = start < text.Length ? text.Substring(start, Math.Min(WeaveLibraryPageRpc.PageChars, text.Length - start)) : string.Empty;
                    Entity rpc = em.CreateEntity();
                    em.AddComponentData(rpc, new WeaveLibraryPageRpc { Version = library.Version, Index = (byte)i, Count = (byte)count, Chunk = new FixedString512Bytes(chunk) });
                    em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
                m_Told[entry.Ghost] = library.Version;
                Sent++;
            }
        }
    }

    /// <summary>Client: the pages of the own library put together (by version; a newer one starts over) and shown on the own view.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WeaveLibraryClientSystem : SystemBase
    {
        EntityQuery m_Pages;
        int m_Version = int.MinValue;
        string[] m_Parts;

        protected override void OnCreate() =>
            m_Pages = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<WeaveLibraryPageRpc>());

        protected override void OnUpdate()
        {
            if (m_Pages.IsEmpty) return;
            using var pages = m_Pages.ToComponentDataArray<WeaveLibraryPageRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Pages);
            foreach (WeaveLibraryPageRpc page in pages)
            {
                if (page.Count == 0 || page.Index >= page.Count) continue;
                if (page.Version != m_Version || m_Parts == null || m_Parts.Length != page.Count) { m_Version = page.Version; m_Parts = new string[page.Count]; }
                m_Parts[page.Index] = page.Chunk.ToString();
                bool whole = true;
                foreach (string part in m_Parts) whole &= part != null;
                if (!whole || !WeaveLibraryText.TryDecode(string.Concat(m_Parts), out ushort[] lines, out uint[] morphs)) continue;
                m_Parts = null;
                foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                    if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out IBodyWeaveLibrary own))
                    { own.MirrorLibrary(lines, morphs); break; }
            }
        }
    }
}
