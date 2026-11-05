using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>One link between a <see cref="CombatTarget"/> ghost and the scene object it stands for, in one world.</summary>
    public sealed class CombatTargetEntry
    {
        public World World { get; internal set; }
        public Entity Ghost { get; internal set; }
        public GameObject Body { get; internal set; }

        internal bool IsDead => World == null || !World.IsCreated || Body == null;
    }

    /// <summary>
    /// The attackable ghosts that are not players (M4.2b: the Strike Dummy; M5: Hostiles), each linked to the scene object it stands for
    /// in its world (the server's copy on the server, the client's own copy on a client; in "Play DOTS here" both are the one scene
    /// object). The player lookups (<see cref="GhostBodyRegistry"/>) ask here when a ghost id or body is not a player's. Main thread only.
    /// </summary>
    public static class CombatTargetRegistry
    {
        static readonly List<CombatTargetEntry> s_Entries = new List<CombatTargetEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Entries.Clear();

        public static IReadOnlyList<CombatTargetEntry> Entries { get { Prune(); return s_Entries; } }

        public static void Register(World world, Entity ghost, GameObject body)
        {
            Prune();
            Unregister(world, ghost);
            s_Entries.Add(new CombatTargetEntry { World = world, Ghost = ghost, Body = body });
        }

        public static bool Unregister(World world, Entity ghost)
        {
            for (int i = 0; i < s_Entries.Count; i++)
                if (ReferenceEquals(s_Entries[i].World, world) && s_Entries[i].Ghost == ghost) { s_Entries.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>The world's link for <paramref name="body"/>, or null.</summary>
        public static CombatTargetEntry Of(World world, GameObject body)
        {
            Prune();
            foreach (CombatTargetEntry e in s_Entries)
                if (ReferenceEquals(e.World, world) && e.Body == body) return e;
            return null;
        }

        /// <summary>The scene object of the target ghost with <paramref name="ghostId"/> in <paramref name="world"/>, or null.</summary>
        public static Transform BodyOf(World world, int ghostId)
        {
            if (ghostId == 0) return null;
            Prune();
            EntityManager em = world.EntityManager;
            foreach (CombatTargetEntry e in s_Entries)
                if (ReferenceEquals(e.World, world) && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                    return e.Body.transform;
            return null;
        }

        /// <summary>The ghost id of the target <paramref name="body"/> belongs to (in any world: the id is the server's), or 0.</summary>
        public static int GhostIdOf(Transform body)
        {
            if (body == null) return 0;
            Prune();
            foreach (CombatTargetEntry e in s_Entries)
            {
                if (body != e.Body.transform && !body.IsChildOf(e.Body.transform)) continue;
                EntityManager em = e.World.EntityManager;
                if (em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost))
                {
                    int id = em.GetComponentData<GhostInstance>(e.Ghost).ghostId;
                    if (id != 0) return id;
                }
            }
            return 0;
        }

        /// <summary>The object is a server world's copy: the server decides its HP, so no client may write it ("Play DOTS here").</summary>
        public static bool IsServerCopy(GameObject body)
        {
            Prune();
            foreach (CombatTargetEntry e in s_Entries)
                if (e.Body == body && e.World.IsServer()) return true;
            return false;
        }

        static void Prune()
        {
            for (int i = s_Entries.Count - 1; i >= 0; i--)
                if (s_Entries[i].IsDead) s_Entries.RemoveAt(i);
        }
    }
}
