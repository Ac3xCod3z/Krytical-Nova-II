using System.Collections.Generic;
using System.Text;
using KryticalNova.Items;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// Magnet: picks up the drops the player's filter wants as soon as they are within ordinary pickup reach,
    /// without walking. It runs on the player's own game and asks for each pickup the way the pickup key does,
    /// so reach, the hold on someone else's drop and the faction rule are all still the server's to check.
    /// It works with Autopilot on or off, but only while the character has Autopilot time left (owner, 15 Oct
    /// 2026: a free Magnet took anything put down in reach, a GM's gm.drop included).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAutopilot))]
    public sealed class MagnetController : MonoBehaviour
    {
        private const float RetrySeconds = 1.5f; // a drop the server would not hand over is left alone for a moment

        private PlayerAutopilot autopilot;
        private PlayerInventory inventory;
        private PlayerLoot loot;
        private NetworkObject networkObject;
        private readonly Dictionary<WorldDrop, float> asked = new Dictionary<WorldDrop, float>();
        private float nextScan;

        private void Awake()
        {
            autopilot = GetComponent<PlayerAutopilot>();
            inventory = GetComponent<PlayerInventory>();
            loot = GetComponent<PlayerLoot>();
            networkObject = GetComponent<NetworkObject>();
        }

        private bool IsLocal => inventory != null && PlayerInventory.Local == inventory;

        /// <summary>True when the Pack has no empty cell. A stack may still take more of what it holds.</summary>
        public bool PackFull => inventory != null && inventory.UsedCells >= PlayerInventory.Capacity;

        private void Update()
        {
            if (!IsLocal || loot == null || autopilot.Settings == null || Time.time < nextScan) return;
            nextScan = Time.time + autopilot.Settings.magnetScanSeconds;
            if (!MagnetRules.MayRun(autopilot.Preferences, autopilot.SecondsLeft, autopilot.Free)) return;

            WorldDrop drop = NearestWanted(loot.PickupKeyRange, false);
            if (drop == null) return;
            asked[drop] = Time.time + RetrySeconds;
            loot.RequestPickup(drop);
        }

        /// <summary>
        /// The nearest drop within <paramref name="reach"/> metres that the filter wants, the player may take and
        /// the Pack has room for. <paramref name="ignoreAsked"/> also counts drops just asked for.
        /// </summary>
        public WorldDrop NearestWanted(float reach, bool ignoreAsked)
        {
            WorldDrop best = null;
            float bestSqr = reach * reach;
            IReadOnlyList<WorldDrop> drops = WorldDrop.Active;
            for (int i = 0; i < drops.Count; i++)
            {
                WorldDrop drop = drops[i];
                if (drop == null || !Takes(drop)) continue;
                if (!ignoreAsked && asked.TryGetValue(drop, out float until) && Time.time < until) continue;
                Vector3 offset = drop.transform.position - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > bestSqr) continue;
                bestSqr = offset.sqrMagnitude;
                best = drop;
            }
            if (asked.Count > 64) asked.Clear(); // drops that are long gone
            return best;
        }

        private bool Takes(WorldDrop drop)
        {
            ItemDefinition item = drop.Definition;
            if (item == null || !MagnetRules.Wants(autopilot.Preferences, item)) return false;
            if (networkObject != null && drop.IsReservedForAnother(networkObject)) return false;
            if (LootRules.IsCreds(item.itemId)) return true; // Creds go to the wallet, not the Pack
            // A full Pack still takes what stacks onto something it holds.
            return !PackFull || (item.maxStack > 1 && inventory.CountItem(item.itemId) > 0);
        }

        /// <summary>Every drop in pickup reach and what the filter makes of it, for GM tools.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            float reach = loot != null ? loot.PickupKeyRange : 3.5f;
            string time = autopilot.Free ? "GM free mode" : autopilot.SecondsLeft > 0f ? PlayerAutopilot.FormatTime(autopilot.SecondsLeft) + " of Autopilot time" : "no Autopilot time (Magnet waits)";
            sb.AppendLine($"Magnet {(autopilot.Preferences.magnetOn ? "on" : "off")}, {time}, reach {reach:0.#} m, Pack {(PackFull ? "full" : "has room")}:");
            int listed = 0;
            foreach (WorldDrop drop in WorldDrop.Active)
            {
                if (drop == null) continue;
                Vector3 offset = drop.transform.position - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > reach * reach) continue;
                ItemDefinition item = drop.Definition;
                string verdict = item == null ? "unknown item"
                    : !MagnetRules.Wants(autopilot.Preferences, item) ? "filter says no"
                    : networkObject != null && drop.IsReservedForAnother(networkObject) ? "held for someone else"
                    : !Takes(drop) ? "no room" : "take";
                sb.AppendLine($"  {drop.DisplayName} ({(item != null ? item.rarity.ToString() : "?")}) at {offset.magnitude:0.#} m: {verdict}");
                listed++;
            }
            if (listed == 0) sb.Append("  nothing in reach.");
            return sb.ToString().TrimEnd();
        }
    }
}
