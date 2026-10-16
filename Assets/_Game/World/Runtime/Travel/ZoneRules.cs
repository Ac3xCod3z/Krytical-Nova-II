using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// One per map scene: says which map this is (its <see cref="ZoneCatalog"/> entry, found by the scene's name) and
    /// so its kind, level band and PvP rule. On load it tells the session which zone is open and lets
    /// <see cref="ZoneTravel"/> finish a trip here (players to the spawn points, the camera to the player). A scene
    /// without one is not a map (the front end).
    /// </summary>
    // After the session objects' Awake (ZoneSessionManager), so the zone it reports is this one.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class ZoneRules : MonoBehaviour
    {
        [SerializeField] private ZoneCatalog catalog;

        /// <summary>The open map's rules, or null outside the world.</summary>
        public static ZoneRules Current { get; private set; }

        /// <summary>True on a map where the factions are always at war (The Scar), whatever cross-faction PvP says.</summary>
        public static bool AlwaysOnPvp => Current != null && Current.Entry != null && Current.Entry.pvp == ZonePvpRule.AlwaysOn;

        public ZoneCatalog Catalog => catalog;
        public ZoneEntry Entry { get; private set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;

        private void Awake()
        {
            Current = this;
            Entry = catalog != null ? catalog.Find(gameObject.scene.name) : null;
            if (Entry == null) Debug.LogWarning($"[ZoneRules] '{gameObject.scene.name}' is not in the zone catalog.", this);
            var session = ZoneSessionManager.Instance;
            if (session != null) session.SetCurrentZone(gameObject.scene.name, Entry != null ? Entry.displayName : gameObject.scene.name);
        }

        private void Start() => ZoneTravel.SceneReady(this);

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

#if UNITY_EDITOR
        public void EditorSetCatalog(ZoneCatalog value) => catalog = value;
#endif
    }
}
