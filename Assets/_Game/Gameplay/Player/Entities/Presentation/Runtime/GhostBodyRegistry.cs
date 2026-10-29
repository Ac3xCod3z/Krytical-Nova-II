using System.Collections.Generic;
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
    /// </summary>
    public static class GhostBodyRegistry
    {
        static readonly List<GhostBodyEntry> s_Entries = new List<GhostBodyEntry>();
        static readonly List<IPlayerBody> s_Views = new List<IPlayerBody>();
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
            if (isLocal) LocalPlayer.Set(entry.Body);
            else if (ReferenceEquals(LocalPlayer.Object, entry.Body)) LocalPlayer.Set(null);
        }

        public static bool TryGetByGhost(World world, Entity ghost, out GhostBodyEntry entry)
        {
            Prune();
            int i = IndexOf(world, ghost);
            entry = i >= 0 ? s_Entries[i] : null;
            return entry != null;
        }

        public static bool TryGetByNetworkId(World world, int networkId, out GhostBodyEntry entry)
        {
            Prune();
            foreach (var e in s_Entries)
                if (ReferenceEquals(e.World, world) && e.NetworkId == networkId) { entry = e; return true; }
            entry = null;
            return false;
        }

        /// <summary>By the server's key (where keys exist: the host and the dedicated server's own view).</summary>
        public static bool TryGetByKey(PlayerKey key, out GhostBodyEntry entry)
        {
            Prune();
            if (key.IsValid)
                foreach (var e in s_Entries)
                    if (e.Key == key) { entry = e; return true; }
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

        static int IndexOf(World world, Entity ghost)
        {
            for (int i = 0; i < s_Entries.Count; i++)
                if (ReferenceEquals(s_Entries[i].World, world) && s_Entries[i].Ghost == ghost) return i;
            return -1;
        }

        static void RemoveAt(int i)
        {
            if (i < 0) return;
            var e = s_Entries[i];
            if (e.Body != null && ReferenceEquals(LocalPlayer.Object, e.Body)) LocalPlayer.Set(null);
            s_Views.Remove(e.View);
            s_Entries.RemoveAt(i);
        }

        static void Prune()
        {
            for (int i = s_Entries.Count - 1; i >= 0; i--)
                if (s_Entries[i].IsDead) RemoveAt(i);
        }
    }
}
