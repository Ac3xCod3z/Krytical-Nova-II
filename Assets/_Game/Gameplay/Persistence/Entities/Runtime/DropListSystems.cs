using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>A drop on the ground to one player (M7.5; NGO's WorldDrop network variables): its server id, stack, place and hold.</summary>
    public struct DropShownRpc : IRpcCommand
    {
        public uint Id;
        public ushort ItemId, Count;
        public ulong Data;
        public int XMm, YMm, ZMm;
        public float HeldSecondsLeft;
        public byte HeldForMe;
    }

    /// <summary>A drop is gone (taken, cleared away or out of time).</summary>
    public struct DropGoneRpc : IRpcCommand
    {
        public uint Id;
    }

    /// <summary>What one player has been told of the drops, and what to tell them now (plain C#, tested on its own).</summary>
    public sealed class DropListDiff
    {
        readonly Dictionary<uint, (ushort item, ushort count, bool held)> m_Told = new Dictionary<uint, (ushort, ushort, bool)>();
        readonly HashSet<uint> m_Seen = new HashSet<uint>();
        readonly List<uint> m_Gone = new List<uint>();

        /// <summary>True when this drop is new to the player or changed since they were told (they must be told now).</summary>
        public bool Tell(uint id, ushort item, ushort count, bool held)
        {
            m_Seen.Add(id);
            if (m_Told.TryGetValue(id, out var had) && had.item == item && had.count == count && had.held == held) return false;
            m_Told[id] = (item, count, held);
            return true;
        }

        /// <summary>After a look over every drop: those told before and not seen now (gone).</summary>
        public List<uint> EndLook()
        {
            m_Gone.Clear();
            foreach (uint id in m_Told.Keys) if (!m_Seen.Contains(id)) m_Gone.Add(id);
            foreach (uint id in m_Gone) m_Told.Remove(id);
            m_Seen.Clear();
            return m_Gone;
        }
    }

    /// <summary>
    /// Server: tells every player in the game of the drops on the ground (NGO's WorldDrop network variables for everyone): each new or changed
    /// drop, and each that went. A hold says only whether it is held for that player (NGO's clients saw no more). Five times a second.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class DropListServerSystem : SystemBase
    {
        public float Interval = 0.2f;
        readonly Dictionary<Entity, DropListDiff> m_Lists = new Dictionary<Entity, DropListDiff>();
        readonly List<Entity> m_Left = new List<Entity>();
        EntityQuery m_InGame;
        double m_Next;

        /// <summary>Drops shown and gone messages sent (tests).</summary>
        public int Shown { get; private set; }
        public int Gone { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (now < m_Next) return;
            m_Next = now + Interval;
            var em = EntityManager;
            m_Left.Clear();
            foreach (Entity c in m_Lists.Keys) if (!em.Exists(c)) m_Left.Add(c);
            foreach (Entity c in m_Left) m_Lists.Remove(c);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            using var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int c = 0; c < connections.Length; c++)
            {
                if (!m_Lists.TryGetValue(connections[c], out DropListDiff list)) m_Lists[connections[c]] = list = new DropListDiff();
                Unity.Netcode.NetworkObject me = GhostBodyRegistry.TryGetByNetworkId(World, ids[c].Value, out GhostBodyEntry entry) && entry.Body != null
                    ? entry.Body.GetComponent<Unity.Netcode.NetworkObject>() : null;
                foreach (WorldDrop drop in WorldDrop.Active)
                {
                    if (drop == null || drop.ServerId == 0 || drop.Stack.IsEmpty) continue;
                    ItemStack stack = drop.Stack;
                    bool held = drop.IsHeld;
                    if (!list.Tell(drop.ServerId, stack.itemId, stack.count, held)) continue;
                    Vector3 p = drop.Position;
                    Send(connections[c], new DropShownRpc
                    {
                        Id = drop.ServerId, ItemId = stack.itemId, Count = stack.count, Data = stack.data,
                        XMm = Mathf.RoundToInt(p.x * 1000f), YMm = Mathf.RoundToInt(p.y * 1000f), ZMm = Mathf.RoundToInt(p.z * 1000f),
                        HeldSecondsLeft = drop.HeldSecondsLeft, HeldForMe = (byte)(held && me != null && drop.IsHeldFor(me) ? 1 : 0),
                    });
                    Shown++;
                }
                foreach (uint id in list.EndLook())
                {
                    Send(connections[c], new DropGoneRpc { Id = id });
                    Gone++;
                }
            }
        }

        void Send<T>(Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, rpc);
            EntityManager.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Client: shows the server's drops as pooled views (WorldDrop.ShowAsView) the loot HUD, pickup key and magnet read as before, and
    /// takes them away when the server says. A host process (a server world here) already has the server's own drops: no views.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class DropListClientSystem : SystemBase
    {
        EntityQuery m_Shown, m_Gone;
        readonly Dictionary<uint, WorldDrop> m_Views = new Dictionary<uint, WorldDrop>();

        /// <summary>Drops shown here now (tests).</summary>
        public int Views => m_Views.Count;

        protected override void OnCreate()
        {
            m_Shown = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<DropShownRpc>());
            m_Gone = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<DropGoneRpc>());
        }

        protected override void OnDestroy()
        {
            foreach (WorldDrop view in m_Views.Values) if (view != null) view.RemoveView();
            m_Views.Clear();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (m_Shown.IsEmpty && m_Gone.IsEmpty) return;
            using var shown = m_Shown.ToComponentDataArray<DropShownRpc>(Allocator.Temp);
            using var gone = m_Gone.ToComponentDataArray<DropGoneRpc>(Allocator.Temp);
            em.DestroyEntity(m_Shown);
            em.DestroyEntity(m_Gone);
            var server = ClientServerBootstrap.ServerWorld;
            if (server != null && server.IsCreated) return;
            WorldDrop prefab = PrefabFromLocalBody();
            foreach (DropShownRpc s in shown)
            {
                var stack = new ItemStack(s.ItemId, s.Count, s.Data);
                if (m_Views.TryGetValue(s.Id, out WorldDrop view) && view != null) { view.UpdateView(stack, s.HeldSecondsLeft, s.HeldForMe != 0); continue; }
                view = WorldDrop.ShowAsView(prefab, s.Id, stack, new Vector3(s.XMm, s.YMm, s.ZMm) * 0.001f, s.HeldSecondsLeft, s.HeldForMe != 0);
                if (view != null) m_Views[s.Id] = view;
            }
            foreach (DropGoneRpc g in gone)
            {
                if (!m_Views.TryGetValue(g.Id, out WorldDrop view)) continue;
                m_Views.Remove(g.Id);
                if (view != null) view.RemoveView();
            }
        }

        WorldDrop PrefabFromLocalBody()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out PlayerLoot loot))
                    return loot.DropPrefab;
            return null;
        }
    }
}
