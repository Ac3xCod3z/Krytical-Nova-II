using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// Server: ghosts that are not players but that a Hostile may still strike, with the hidden body (a component; the caller takes its IDamageable) that takes the blow (day list item 19,
    /// phase 4: the Mount Companion in its TANK stance). The Hostiles' strike bridge and the blow queue look here when a strike's target ghost
    /// has no player body in <see cref="GhostBodyRegistry"/>. The owning system registers and unregisters; entries whose world is gone or whose
    /// body was destroyed are dropped as they are found. Main thread only; statics reset on entering Play.
    /// </summary>
    public static class StandInTargets
    {
        struct Entry
        {
            public World World;
            public Entity Ghost;
            public Component Body;
        }

        static readonly List<Entry> s_Entries = new List<Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Entries.Clear();

        /// <summary>Drops every entry (tests).</summary>
        public static void Clear() => s_Entries.Clear();

        /// <summary>Entries alive now.</summary>
        public static int Count { get { Prune(); return s_Entries.Count; } }

        /// <summary><paramref name="body"/> takes the blows aimed at <paramref name="ghost"/> in <paramref name="world"/> (a second call replaces the first).</summary>
        public static void Register(World world, Entity ghost, Component body)
        {
            Unregister(world, ghost);
            if (world == null || body == null) return;
            s_Entries.Add(new Entry { World = world, Ghost = ghost, Body = body });
        }

        public static void Unregister(World world, Entity ghost)
        {
            for (int i = s_Entries.Count - 1; i >= 0; i--)
                if (ReferenceEquals(s_Entries[i].World, world) && s_Entries[i].Ghost == ghost) s_Entries.RemoveAt(i);
        }

        /// <summary>The body that takes blows aimed at <paramref name="ghost"/>, or false.</summary>
        public static bool TryGet(World world, Entity ghost, out Component body)
        {
            Prune();
            for (int i = 0; i < s_Entries.Count; i++)
                if (ReferenceEquals(s_Entries[i].World, world) && s_Entries[i].Ghost == ghost) { body = s_Entries[i].Body; return true; }
            body = null;
            return false;
        }

        static void Prune()
        {
            for (int i = s_Entries.Count - 1; i >= 0; i--)
            {
                Entry e = s_Entries[i];
                if (e.World == null || !e.World.IsCreated || e.Body == null) s_Entries.RemoveAt(i);
            }
        }
    }
}
