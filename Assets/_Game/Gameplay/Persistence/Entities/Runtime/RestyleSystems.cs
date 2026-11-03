using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>One page of a restyle (NGO's <c>PlayerRestyle.RestyleRpc</c>; M8 8.3.8): the proposed look as <see cref="RecipeText"/>.</summary>
    public struct RestylePageRpc : IRpcCommand
    {
        public const int PageChars = 480, MostPages = 4;
        public uint Seq;
        public byte Service, Index, Count;
        public FixedString512Bytes Chunk;
    }

    /// <summary>The restyle is done, to its owner (NGO's RestyledRpc).</summary>
    public struct RestyledRpc : IRpcCommand
    {
        public byte Done;
    }

    /// <summary>Server: <see cref="IRestyleOwnerLink"/> for the bodies of the entity netcode, by the body's <c>PlayerKey</c>.</summary>
    public sealed class EntityRestyleOwnerLink : IRestyleOwnerLink
    {
        public void Restyled(Component part)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, new RestyledRpc { Done = 1 });
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Server: a restyle's pages are put together per connection (at most <see cref="RestylePageRpc.MostPages"/>, a new Seq starts over, a closed
    /// connection's pages go); a whole one is read back into a look and run on the server body by NGO's rules at the Money limit (one call).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RestyleServerSystem : SystemBase
    {
        EntityQuery m_Pages;
        readonly Dictionary<Entity, (uint seq, byte service, string[] pages)> m_Pending = new Dictionary<Entity, (uint, byte, string[])>();
        readonly List<Entity> m_Left = new List<Entity>();

        /// <summary>Restyles run, and restyles whose text could not be read (tests).</summary>
        public int Ran { get; private set; }
        public int Unreadable { get; private set; }

        protected override void OnCreate() =>
            m_Pages = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RestylePageRpc>());

        protected override void OnUpdate()
        {
            var em = EntityManager;
            m_Left.Clear();
            foreach (Entity c in m_Pending.Keys) if (!em.Exists(c)) m_Left.Add(c);
            foreach (Entity c in m_Left) m_Pending.Remove(c);
            if (m_Pages.IsEmpty) return;
            using var received = m_Pages.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var pages = m_Pages.ToComponentDataArray<RestylePageRpc>(Allocator.Temp);
            em.DestroyEntity(m_Pages);
            for (int i = 0; i < pages.Length; i++)
            {
                RestylePageRpc page = pages[i];
                Entity c = received[i].SourceConnection;
                if (page.Count == 0 || page.Count > RestylePageRpc.MostPages || page.Index >= page.Count) continue;
                if (!m_Pending.TryGetValue(c, out var pending) || pending.seq != page.Seq || pending.pages.Length != page.Count)
                    m_Pending[c] = pending = (page.Seq, page.Service, new string[page.Count]);
                pending.pages[page.Index] = page.Chunk.ToString();
                bool whole = true;
                foreach (string part in pending.pages) whole &= part != null;
                if (!whole) continue;
                m_Pending.Remove(c);
                if (!RecipeText.TryDecode(string.Concat(pending.pages), out CharacterRecipe proposed)) { Unreadable++; continue; }
                if (PlayerRequestServerSystem.TryBody(World, c, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyRestyle restyle))
                {
                    restyle.RunRestyle(pending.service, proposed);
                    Ran++;
                }
            }
        }
    }

    /// <summary>Client: the server's "done" on the own view (the new look itself arrives with the look text, as for everyone).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RestyleClientSystem : SystemBase
    {
        EntityQuery m_Done;

        protected override void OnCreate() =>
            m_Done = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RestyledRpc>());

        protected override void OnUpdate()
        {
            if (m_Done.IsEmpty) return;
            EntityManager.DestroyEntity(m_Done);
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out IBodyRestyle restyle))
                { restyle.MirrorRestyled(); break; }
        }
    }
}
