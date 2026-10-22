using KryticalNova.Autopilot;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for Autopilot and Magnet. Like the GM commands they only run where the game is
    /// decided (host, server or offline) and act on the player picked by <see cref="GmTarget"/>. Autopilot Passes
    /// are given with item.give "Autopilot Pass (3 Days)" (14, 30).
    /// </summary>
    public static class AutopilotCommands
    {
        private const string NoAutopilot = "That player has no Autopilot (run Krytical Nova/Setup/Player Prefab).";
        private const string NoAutoward = "That player has no Autoward (run Krytical Nova/Setup/Player Prefab).";

        private static bool TryGet(out PlayerAutopilot autopilot, out string error)
        {
            autopilot = null;
            if (!GmTarget.TryGetPlayer(out GameObject player, out error)) return false;
            autopilot = player.GetComponent<PlayerAutopilot>();
            if (autopilot == null) { error = NoAutopilot; return false; }
            return true;
        }

        private static string Describe(PlayerAutopilot autopilot) =>
            $"Autopilot {(autopilot.IsOn ? "ON" : "off")} · {autopilot.TimeLeftLine}{(autopilot.Free ? " · free (GM)" : "")}" +
            $" · zone {(autopilot.CurrentZone != null && autopilot.CurrentZone.Definition != null ? autopilot.CurrentZone.Definition.id : "none")}";

        [Command("gm.autopilot", "Switch Autopilot for the targeted player without needing a Pass: gm.autopilot [on|off]. With nothing, prints its state.")]
        private static string Autopilot(string state = "")
        {
            if (!TryGet(out PlayerAutopilot autopilot, out string error)) return error;
            state = (state ?? string.Empty).Trim().ToLowerInvariant();
            if (state.Length == 0) return Describe(autopilot);
            if (state != "on" && state != "off") return "Use gm.autopilot on or off.";
            if (state == "on")
            {
                autopilot.ServerSetFree(true);
                string blocked = autopilot.StartBlockedReason();
                if (blocked != null) return blocked;
                autopilot.ServerSetOn(true);
            }
            else
            {
                autopilot.ServerSetOn(false, "Autopilot off.");
                autopilot.ServerSetFree(false);
            }
            return Describe(autopilot);
        }

        [Command("gm.autopilot.time", "Set the targeted player's Autopilot time to run this many days from now (0 ends it): gm.autopilot.time <days>.")]
        private static string Time(int days)
        {
            if (!TryGet(out PlayerAutopilot autopilot, out string error)) return error;
            if (days < 0) return "Give a number of days, 0 or more.";
            autopilot.ServerSetDays(days);
            return Describe(autopilot);
        }

        [Command("gm.autopilot.log", "Print what Autopilot does each turn and why it stopped, on this machine: gm.autopilot.log [on|off].")]
        private static string Log(string state = "")
        {
            if (!GmTarget.TryGetPlayer(out _, out string error)) return error;
            state = (state ?? string.Empty).Trim().ToLowerInvariant();
            AutopilotController.Logging = state == "on" || (state != "off" && !AutopilotController.Logging);
            return AutopilotController.Logging ? "Autopilot's turns are printed to the log." : "Autopilot's turns are no longer printed.";
        }

        [Command("gm.autobuff.days", "Set the targeted player's Autoward licence to run this many days from now (0 ends it): gm.autobuff.days <days>.")]
        private static string AutobuffDays(int days)
        {
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return error;
            var autobuff = player.GetComponent<PlayerAutobuff>();
            if (autobuff == null) return NoAutoward;
            autobuff.ServerSetDays(days);
            return autobuff.TimeLeftLine;
        }

        [Command("gm.autoward", "Autoward for the targeted player. gm.autoward: one of each Autoward Pass (3, 14 and 30 days) into the Pack, to use as a player would. gm.autoward <days>: the licence runs that many days from now (0 ends it).")]
        private static string Autoward(string days = "")
        {
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return error;
            if (!player.TryGetComponent(out PlayerAutobuff autobuff)) return NoAutoward;
            if (string.IsNullOrWhiteSpace(days)) return GiveAutowardPasses(player, autobuff);
            if (!int.TryParse(days.Trim(), out int count) || count < 0) return "Give a number of days, 0 or more.";
            autobuff.ServerSetDays(count);
            return autobuff.TimeLeftLine;
        }

        /// <summary>One of each Autoward Pass the settings list, into the player's Pack (gm.autoward and gm.kit autoward).</summary>
        internal static string GiveAutowardPasses(GameObject player, PlayerAutobuff autobuff)
        {
            if (autobuff.Settings == null || autobuff.Settings.autobuffTiers == null) return "Autoward is not set up.";
            if (!player.TryGetComponent(out KryticalNova.Items.PlayerInventory inventory)) return "That player has no Pack.";
            int given = 0, lost = 0;
            foreach (PassTier tier in autobuff.Settings.autobuffTiers)
                if (inventory.ServerGive(tier.passItemId, 1, earned: true) > 0) given++; else lost++;
            return $"{given} Autoward Passes into the Pack (use one from the Pack to add its days)." + (lost > 0 ? $" {lost} did not fit." : string.Empty) + $" {autobuff.TimeLeftLine}";
        }

        [Command("gm.magnet.test", "List every drop in the targeted player's pickup reach and whether Magnet would take it.")]
        private static string MagnetTest()
        {
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return error;
            var magnet = player.GetComponent<MagnetController>();
            return magnet != null ? magnet.Describe() : NoAutopilot;
        }
    }
}
