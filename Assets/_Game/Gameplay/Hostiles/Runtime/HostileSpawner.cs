using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// Puts single Hostiles into the world and takes them out, where the game is decided (server, host or
    /// offline). The species it knows are the Hostile prefabs in the NetworkManager's prefab list, so a species
    /// that can be spawned here is one every client can show.
    /// </summary>
    public static class HostileSpawner
    {
        private const float NavMeshReach = 4f;

        /// <summary>
        /// True on the server, the host, or with no session running; never while the entity netcode's worlds are live (NGO is not
        /// listening there, so every client would spawn Hostiles of its own); <see cref="AuthorityOverride"/> decides when set.
        /// </summary>
        public static bool HasAuthority => AuthorityOverride != null ? AuthorityOverride() : !KryticalNova.Core.EntityNetcodeWorlds.Live && NgoAuthority;

        /// <summary>A Hostile body may decide here as far as the entity netcode is concerned (the override, else no live entity worlds).</summary>
        internal static bool EntityNetcodeAllows => AuthorityOverride != null ? AuthorityOverride() : !KryticalNova.Core.EntityNetcodeWorlds.Live;

        /// <summary>The Netcode for GameObjects rule: the server, the host, or no session running.</summary>
        public static bool NgoAuthority
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager == null || !manager.IsListening || manager.IsServer;
            }
        }

        /// <summary>
        /// Who decides the Hostiles when the entity netcode runs this process (null: no Hostiles while its worlds are live, the NGO rule
        /// otherwise). M5.5's server bodies set it (M5.0 seam). <see cref="HostileNet"/> follows it too.
        /// </summary>
        public static System.Func<bool> AuthorityOverride { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AuthorityOverride = null;

        /// <summary>Every Hostile species the session can spawn, by id.</summary>
        public static List<HostileNet> Species()
        {
            var found = new List<HostileNet>();
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return found;
            foreach (NetworkPrefab entry in manager.NetworkConfig.Prefabs.Prefabs)
                if (entry.Prefab != null && entry.Prefab.TryGetComponent(out HostileNet hostile) && hostile.Definition != null)
                    found.Add(hostile);
            found.Sort((a, b) => a.Definition.id.CompareTo(b.Definition.id));
            return found;
        }

        /// <summary>The species with this id or name (the display name, or the asset's, spaces and case ignored).</summary>
        public static HostileNet Find(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return null;
            string wanted = Normalise(idOrName);
            foreach (HostileNet species in Species())
            {
                HostileDefinition d = species.Definition;
                if (d.id.ToString() == wanted || Normalise(d.displayName) == wanted || Normalise(d.name) == wanted
                    || Normalise(d.name) == "hostile_" + wanted) return species;
            }
            return null;
        }

        private static string Normalise(string text) => text.Trim().Replace(" ", string.Empty).ToLowerInvariant();

        /// <summary>
        /// Spawns one Hostile on the NavMesh at or near a point, at its species' level or another. Returns null
        /// without authority or when there is no walkable ground near the point.
        /// </summary>
        public static HostileNet Spawn(HostileNet prefab, Vector3 position, float facingDegrees, int level = 0, bool gmSpawned = false)
        {
            if (prefab == null || !HasAuthority) return null;
            if (!NavMesh.SamplePosition(position, out NavMeshHit ground, NavMeshReach, NavMesh.AllAreas)) return null;

            HostileNet hostile = HostilePool.Take(prefab, ground.position, Quaternion.Euler(0f, facingDegrees, 0f));
            hostile.GmSpawned = gmSpawned;
            hostile.Zone = null;
            hostile.SetLevel(level);
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) hostile.NetworkObject.Spawn(true);
            return hostile;
        }

        /// <summary>Takes a Hostile out of the world on every peer.</summary>
        public static void Remove(HostileNet hostile)
        {
            if (hostile == null || !HasAuthority) return;
            // Despawning hands the body to the pool through the prefab handler, here and on every client.
            if (hostile.IsSpawned) hostile.NetworkObject.Despawn(true);
            else HostilePool.Return(hostile);
        }
    }
}
