using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// The player this machine controls: the owned networked player, else the offline scene avatar. The player's
    /// inventory decides it, as it always has (<c>PlayerInventory.Local</c>), and sets it here. Every system's
    /// <c>X.Local</c> reads its own component of that player from here, found once and kept, instead of searching the
    /// player through the inventory on every call; systems below Items (World, Audio, Progression) can find the
    /// local player without knowing Items at all.
    /// </summary>
    public static class LocalPlayer
    {
        private static readonly Dictionary<Type, Component> Found = new Dictionary<Type, Component>();

        /// <summary>The local player's object, or null when there is none (the title, between maps, a server).</summary>
        public static GameObject Object { get; private set; }

        /// <summary>Raised after the local player changes (spawned, despawned, a new map's avatar).</summary>
        public static event Action Changed;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Object = null;
            Found.Clear();
        }

        /// <summary>Called by the player's inventory when the local player changes.</summary>
        public static void Set(GameObject player)
        {
            if (Object == player) return;
            Object = player;
            Found.Clear();
            Changed?.Invoke();
        }

        /// <summary>The local player's component of this type, or null (no local player, or none on it).</summary>
        public static T Get<T>() where T : Component
        {
            if (Object == null) return null;   // also true once the object is destroyed
            if (Found.TryGetValue(typeof(T), out Component kept) && kept != null) return (T)kept;
            T component = Object.GetComponent<T>();
            if (component != null) Found[typeof(T)] = component;
            return component;
        }

        /// <summary>The local player's position, when there is one.</summary>
        public static bool TryGetPosition(out Vector3 position)
        {
            position = Object != null ? Object.transform.position : default;
            return Object != null;
        }
    }
}
