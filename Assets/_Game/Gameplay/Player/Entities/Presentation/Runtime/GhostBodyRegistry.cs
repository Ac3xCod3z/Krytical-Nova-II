using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// One player ghost and the objects that show it: the presentation body (client), the read-only <see cref="GhostPlayerBody"/> view the
    /// rest of the game sees, and the follow state. Plain C#; created by <see cref="GhostBodyRegistry.Register"/> only.
    /// </summary>
    public sealed class GhostBodyEntry
    {
        public World World { get; internal set; }
        public Entity Ghost { get; internal set; }
        public int NetworkId { get; internal set; }
        /// <summary>The server's key for the owner; <see cref="PlayerKey.None"/> on a client (it keeps no keys).</summary>
        public PlayerKey Key { get; internal set; }
        public GameObject Body { get; internal set; }
        public PlayerPresentationState State { get; internal set; }
        public GhostPlayerBody View { get; internal set; }
        public bool IsLocal { get; internal set; }
        public FollowState Follow;

        /// <summary>The world is gone or the body was destroyed from outside: nothing may keep pointing at it.</summary>
        internal bool IsDead => World == null || !World.IsCreated || Body == null;
    }

    /// <summary>
    /// Which GameObject shows which player ghost. Replaces keeping a GameObject per networked object: the ghost lives in an entity
    /// world, the body in the scene, and this is the only link between them (design GhostToBodyRegistry). Lookups drop dead entries
    /// (disposed world, destroyed body) as they find them, so a stale entry can never be handed out. Statics are reset on entering
    /// Play without a domain reload. Main thread only.
    /// <para>
    /// Everything that lists bodies for the rest of the game goes through here: the registered <see cref="GhostPlayerBody"/> views are
    /// the list <see cref="PlayerBodies"/> serves, and the local player's body is set into <see cref="LocalPlayer"/>.
    /// </para>
    /// <para>
    /// The list keeps the order (it is what <see cref="Entries"/> and <see cref="PlayerBodies"/> serve); three maps beside it make the
    /// lookups O(1) instead of a prune plus a scan per call (perf night chunk 1: per-ghost systems made that O(n^2) a frame). The
    /// network-id and key maps hold the FIRST entry of the list for their value, so a duplicate gives the same answer the scan gave.
    /// A lookup that lands on a dead entry drops it and looks again; <see cref="Count"/>, <see cref="Entries"/> and
    /// <see cref="Register"/> still drop every dead entry first.
    /// </para>
    /// </summary>
    public static class GhostBodyRegistry
    {
        /// <summary>A value inside one world: worlds compare by reference, as the old scans did.</summary>
        readonly struct InWorld<T> : IEquatable<InWorld<T>> where T : struct, IEquatable<T>
        {
            readonly World m_World;
            readonly T m_Value;
            public InWorld(World world, T value) { m_World = world; m_Value = value; }
            public bool Equals(InWorld<T> other) => ReferenceEquals(m_World, other.m_World) && m_Value.Equals(other.m_Value);
            public override bool Equals(object obj) => obj is InWorld<T> other && Equals(other);
            public override int GetHashCode() => (RuntimeHelpers.GetHashCode(m_World) * 397) ^ m_Value.GetHashCode();
        }

        static readonly List<GhostBodyEntry> s_Entries = new List<GhostBodyEntry>();
        static readonly List<IPlayerBody> s_Views = new List<IPlayerBody>();   // index for index with s_Entries
        static readonly Dictionary<InWorld<Entity>, GhostBodyEntry> s_ByGhost = new Dictionary<InWorld<Entity>, GhostBodyEntry>();
        static readonly Dictionary<InWorld<int>, GhostBodyEntry> s_ByNetworkId = new Dictionary<InWorld<int>, GhostBodyEntry>();
        static readonly Dictionary<PlayerKey, GhostBodyEntry> s_ByKey = new Dictionary<PlayerKey, GhostBodyEntry>();
        static readonly Dictionary<PlayerKey, GameObject> s_ServerBodies = new Dictionary<PlayerKey, GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        /// <summary>Drops everything, including the hooks into <see cref="PlayerBodies"/> and <see cref="LocalPlayer"/> (tests, domain-reload-free Play).</summary>
        public static void Clear()
        {
            if (ReferenceEquals(PlayerBodies.All, s_Views)) PlayerBodies.Use(null);
            foreach (var e in s_Entries)
                if (e.Body != null && ReferenceEquals(LocalPlayer.Object, e.Body)) LocalPlayer.Set(null);
            s_Entries.Clear();
            s_Views.Clear();
            s_ByGhost.Clear();
            s_ByNetworkId.Clear();
            s_ByKey.Clear();
            s_ServerBodies.Clear();
        }

        // ------------------------------------------------------------------------------------------------ the ghost -> body links

        /// <summary>Entries alive now (dead ones are dropped first).</summary>
        public static int Count { get { Prune(); return s_Entries.Count; } }

        public static IReadOnlyList<GhostBodyEntry> Entries { get { Prune(); return s_Entries; } }

        /// <summary>
        /// Registers the body that shows <paramref name="ghost"/>. A second registration of the same ghost replaces the first (the old
        /// body is NOT destroyed: the caller owns bodies). A local body is set into <see cref="LocalPlayer"/>.
        /// </summary>
        public static GhostBodyEntry Register(World world, Entity ghost, int networkId, PlayerKey key, GameObject body, bool isLocal)
        {
            Prune();
            RemoveAt(IndexOf(world, ghost));
            var entry = new GhostBodyEntry { World = world, Ghost = ghost, NetworkId = networkId, Key = key, Body = body };
            entry.State = body != null ? body.GetComponent<PlayerPresentationState>() : null;
            entry.View = new GhostPlayerBody(entry);
            s_Entries.Add(entry);
            s_Views.Add(entry.View);
            s_ByGhost[new InWorld<Entity>(world, ghost)] = entry;
            s_ByNetworkId.TryAdd(new InWorld<int>(world, networkId), entry);   // an earlier entry with this id stays the answer
            if (key.IsValid) s_ByKey.TryAdd(key, entry);
            if (!ReferenceEquals(PlayerBodies.All, s_Views)) PlayerBodies.Use(s_Views);
            SetLocal(entry, isLocal);
            return entry;
        }

        /// <summary>Removes the link; returns the entry (the caller destroys the body) or false when there was none.</summary>
        public static bool Unregister(World world, Entity ghost, out GhostBodyEntry entry)
        {
            int i = IndexOf(world, ghost);
            entry = i >= 0 ? s_Entries[i] : null;
            RemoveAt(i);
            return entry != null;
        }

        /// <summary>Removes and returns every entry of one world (the world is going away); the caller destroys the bodies.</summary>
        public static List<GhostBodyEntry> TakeWorld(World world)
        {
            var taken = new List<GhostBodyEntry>();
            for (int i = s_Entries.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(s_Entries[i].World, world)) continue;
                taken.Add(s_Entries[i]);
                RemoveAt(i);
            }
            taken.Reverse();
            return taken;
        }

        /// <summary>Marks the entry as the local player's (or not) and keeps <see cref="LocalPlayer"/> in step.</summary>
        public static void SetLocal(GhostBodyEntry entry, bool isLocal)
        {
            entry.IsLocal = isLocal;
            if (entry.Body == null) return;
            // A body built as a ClientView carries its role: its local flag follows (PointAndClickPlayer and the stubs listen).
            BodyRoleTag tag = entry.Body.GetComponent<BodyRoleTag>();
            if (tag != null && tag.Kind == NetRoleKind.ClientView) tag.SetLocal(isLocal);
            if (isLocal) LocalPlayer.Set(entry.Body);
            else if (ReferenceEquals(LocalPlayer.Object, entry.Body)) LocalPlayer.Set(null);
        }

        public static bool TryGetByGhost(World world, Entity ghost, out GhostBodyEntry entry)
        {
            var id = new InWorld<Entity>(world, ghost);
            while (s_ByGhost.TryGetValue(id, out entry))
            {
                if (!entry.IsDead) return true;
                if (!DropDead(entry)) break;   // dead: never handed out
            }
            entry = null;
            return false;
        }

        public static bool TryGetByNetworkId(World world, int networkId, out GhostBodyEntry entry)
        {
            var id = new InWorld<int>(world, networkId);
            while (s_ByNetworkId.TryGetValue(id, out entry))
            {
                if (!entry.IsDead) return true;
                if (!DropDead(entry)) break;   // the next live entry with this id (in list order) takes its place
            }
            entry = null;
            return false;
        }

        /// <summary>By the server's key (where keys exist: the host and the dedicated server's own view).</summary>
        public static bool TryGetByKey(PlayerKey key, out GhostBodyEntry entry)
        {
            if (key.IsValid)
                while (s_ByKey.TryGetValue(key, out entry))
                {
                    if (!entry.IsDead) return true;
                    if (!DropDead(entry)) break;
                }
            entry = null;
            return false;
        }

        // ------------------------------------------------------------------------------------------------ the server body

        /// <summary>Server only: the GameObject (NetRole.ServerBody) that carries the rules for this player. Filled by the later server-body slice.</summary>
        public static void SetServerBody(PlayerKey key, GameObject serverBody)
        {
            if (!key.IsValid) return;
            if (serverBody == null) s_ServerBodies.Remove(key);
            else s_ServerBodies[key] = serverBody;
        }

        public static bool TryGetServerBody(PlayerKey key, out GameObject serverBody)
        {
            if (s_ServerBodies.TryGetValue(key, out serverBody))
            {
                if (serverBody != null) return true;
                s_ServerBodies.Remove(key);   // destroyed from outside: never hand it out
            }
            serverBody = null;
            return false;
        }

        public static int ServerBodyCount
        {
            get
            {
                List<PlayerKey> dead = null;
                foreach (var kv in s_ServerBodies)
                    if (kv.Value == null) (dead ??= new List<PlayerKey>()).Add(kv.Key);
                if (dead != null) foreach (var k in dead) s_ServerBodies.Remove(k);
                return s_ServerBodies.Count;
            }
        }

        // ------------------------------------------------------------------------------------------------ internals

        static int IndexOf(World world, Entity ghost) =>
            s_ByGhost.TryGetValue(new InWorld<Entity>(world, ghost), out GhostBodyEntry e) ? s_Entries.IndexOf(e) : -1;

        static void RemoveAt(int i)
        {
            if (i < 0) return;
            var e = s_Entries[i];
            if (e.Body != null && ReferenceEquals(LocalPlayer.Object, e.Body)) LocalPlayer.Set(null);
            s_Views.RemoveAt(i);
            s_Entries.RemoveAt(i);

            var ghostId = new InWorld<Entity>(e.World, e.Ghost);
            if (s_ByGhost.TryGetValue(ghostId, out GhostBodyEntry g) && ReferenceEquals(g, e)) s_ByGhost.Remove(ghostId);

            // The network-id and key maps hold the first entry of the list for their value; when that one goes, the next one with the
            // same value takes its place. Entries before i cannot have it (the removed one was the first), so the search starts at i.
            var netId = new InWorld<int>(e.World, e.NetworkId);
            if (s_ByNetworkId.TryGetValue(netId, out GhostBodyEntry n) && ReferenceEquals(n, e))
            {
                s_ByNetworkId.Remove(netId);
                for (int j = i; j < s_Entries.Count; j++)
                    if (ReferenceEquals(s_Entries[j].World, e.World) && s_Entries[j].NetworkId == e.NetworkId) { s_ByNetworkId[netId] = s_Entries[j]; break; }
            }
            if (e.Key.IsValid && s_ByKey.TryGetValue(e.Key, out GhostBodyEntry k) && ReferenceEquals(k, e))
            {
                s_ByKey.Remove(e.Key);
                for (int j = i; j < s_Entries.Count; j++)
                    if (s_Entries[j].Key == e.Key) { s_ByKey[e.Key] = s_Entries[j]; break; }
            }
        }

        /// <summary>Removes a dead entry a lookup landed on; false only if it was not in the list (never expected: stops the lookup loop).</summary>
        static bool DropDead(GhostBodyEntry e)
        {
            int i = s_Entries.IndexOf(e);
            if (i < 0) return false;
            RemoveAt(i);
            return true;
        }

        static void Prune()
        {
            for (int i = s_Entries.Count - 1; i >= 0; i--)
                if (s_Entries[i].IsDead) RemoveAt(i);
        }
    }
}
