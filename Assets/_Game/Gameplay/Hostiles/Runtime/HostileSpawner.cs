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

        /// <summary>True on the server, the host, or with no session running.</summary>
        public static bool HasAuthority
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager == null || !manager.IsListening || manager.IsServer;
            }
        }

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

            HostileNet hostile = Object.Instantiate(prefab, ground.position, Quaternion.Euler(0f, facingDegrees, 0f));
            hostile.name = prefab.name;
            hostile.GmSpawned = gmSpawned;
            hostile.SetLevel(level);
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) hostile.NetworkObject.Spawn(true);
            return hostile;
        }

        /// <summary>Takes a Hostile out of the world on every peer.</summary>
        public static void Remove(HostileNet hostile)
        {
            if (hostile == null || !HasAuthority) return;
            if (hostile.IsSpawned) hostile.NetworkObject.Despawn(true);
            else Object.Destroy(hostile.gameObject);
        }
    }
}
