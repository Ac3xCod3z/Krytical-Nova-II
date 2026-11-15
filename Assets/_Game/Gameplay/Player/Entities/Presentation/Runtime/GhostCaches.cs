using System.Collections.Generic;
using Unity.Entities;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// Client caches keyed by a ghost entity drop the ghosts that went (relevancy condition 3a, perf round 3 R3-3). With relevancy on,
    /// other players and Hostiles leave and re-enter a client's 150 m all session, each time as a new entity (a new version), so a cache
    /// that only ever adds would grow without end. A new entity is never confused with the old one: every caller seeds a ghost it meets
    /// for the first time, so dropping an entry never replays anything. Main thread only.
    /// </summary>
    public static class GhostCaches
    {
        /// <summary>Frames between two prunes of one cache (the entries are small; a few seconds of growth is nothing).</summary>
        public const int PruneEveryFrames = 300;

        static readonly List<Entity> s_Gone = new List<Entity>();

        /// <summary>Every <see cref="PruneEveryFrames"/> frames: removes the entries whose ghost no longer exists. Cheap on the other frames.</summary>
        public static void Prune<T>(Dictionary<Entity, T> cache, EntityManager em)
        {
            if (cache.Count == 0 || UnityEngine.Time.frameCount % PruneEveryFrames != 0) return;
            DropGone(cache, em);
        }

        /// <summary>Removes the entries whose ghost no longer exists, now (tests, and <see cref="Prune{T}"/>).</summary>
        public static int DropGone<T>(Dictionary<Entity, T> cache, EntityManager em)
        {
            s_Gone.Clear();
            foreach (Entity ghost in cache.Keys) if (!em.Exists(ghost)) s_Gone.Add(ghost);
            foreach (Entity ghost in s_Gone) cache.Remove(ghost);
            int dropped = s_Gone.Count;
            s_Gone.Clear();
            return dropped;
        }
    }
}
