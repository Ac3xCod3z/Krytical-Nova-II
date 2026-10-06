using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// A marked-out part of a zone where dealings between players have their own rules. Two things can be switched
    /// on, each on its own: Kiosks may be set up here (<see cref="allowKiosks"/>), and this is neutral ground
    /// (<see cref="crossFaction"/>): players of different factions may trade, buy from each other's Kiosks and add
    /// each other as contacts while they stand on it. Zones may overlap (a neutral town with a Kiosk plaza inside):
    /// a point has a property when any zone around it has it. A plain scene object, the same on every machine; the
    /// server reads it when it decides. Outside every zone, players deal only with their own faction and no Kiosk
    /// can open. A Crew is one faction everywhere: no zone changes that.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TradeZone : MonoBehaviour
    {
        private static readonly List<TradeZone> ActiveZones = new List<TradeZone>(4);

        [Tooltip("Shown to players: \"You can't set up a Kiosk outside <name>\".")]
        [SerializeField] private string zoneName = "Trade Plaza";
        [Tooltip("Width (x) and depth (z) of the zone in metres, centred on this object and turned with it.")]
        [SerializeField] private Vector2 size = new Vector2(60f, 60f);
        [Tooltip("Kiosks may be set up here.")]
        [SerializeField] private bool allowKiosks = true;
        [Tooltip("Neutral ground: players of different factions may trade, buy from each other's Kiosks and add each other as " +
                 "contacts here. The dedicated non-combat zone will be this; until it exists the starter town is marked so testing can go on.")]
        [SerializeField] private bool crossFaction;

        /// <summary>Every enabled trade zone on this machine. Do not modify.</summary>
        public static IReadOnlyList<TradeZone> Active => ActiveZones;

        public string ZoneName => zoneName;
        public Vector2 Size => size;
        public bool AllowKiosks => allowKiosks;
        public bool CrossFaction => crossFaction;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ActiveZones.Clear();

        private void OnEnable() => ActiveZones.Add(this);
        private void OnDisable() => ActiveZones.Remove(this);

        public bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f;
        }

        /// <summary>The zone around a point where Kiosks may be set up, or null.</summary>
        public static TradeZone KioskZoneAt(Vector3 point)
        {
            for (int i = 0; i < ActiveZones.Count; i++)
                if (ActiveZones[i] != null && ActiveZones[i].allowKiosks && ActiveZones[i].Contains(point)) return ActiveZones[i];
            return null;
        }

        /// <summary>True on neutral ground: where different factions may deal with each other.</summary>
        public static bool CrossFactionAt(Vector3 point)
        {
            for (int i = 0; i < ActiveZones.Count; i++)
                if (ActiveZones[i] != null && ActiveZones[i].crossFaction && ActiveZones[i].Contains(point)) return true;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = crossFaction ? new Color(1f, 0.8f, 0.2f, 0.9f) : new Color(0.3f, 0.9f, 0.8f, 0.9f);
            Gizmos.DrawWireCube(Vector3.up, new Vector3(size.x, 2f, size.y));
        }
    }
}
