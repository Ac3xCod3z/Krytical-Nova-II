using KryticalNova.Autopilot;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for Autopilot and Magnet. Like the GM commands they only run where the game is
    /// decided (host, server or offline) and act on the player picked by <see cref="GmTarget"/>. Autopilot Chips
    /// are given with item.give "Autopilot Chip".
    /// </summary>
    public static class AutopilotCommands
    {
        private const string NoAutopilot = "That player has no Autopilot (run Krytical Nova/Setup/Player Prefab).";

        private static bool TryGet(out PlayerAutopilot autopilot, out string error)
        {
            autopilot = null;
            if (!GmTarget.TryGetPlayer(out GameObject player, out error)) return false;
            autopilot = player.GetComponent<PlayerAutopilot>();
            if (autopilot == null) { error = NoAutopilot; return false; }
            return true;
        }

        private static string Describe(PlayerAutopilot autopilot) =>
            $"Autopilot {(autopilot.IsOn ? "ON" : "off")} · time left {PlayerAutopilot.FormatTime(autopilot.SecondsLeft)}{(autopilot.Free ? " · free (GM)" : "")}" +
            $" · zone {(autopilot.CurrentZone != null && autopilot.CurrentZone.Definition != null ? autopilot.CurrentZone.Definition.id : "none")}";

        [Command("gm.autopilot", "Switch Autopilot for the targeted player without needing a Chip: gm.autopilot [on|off]. With nothing, prints its state.")]
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

        [Command("gm.autopilot.time", "Set the targeted player's Autopilot time left, in minutes: gm.autopilot.time <minutes>.")]
        private static string Time(float minutes)
        {
            if (!TryGet(out PlayerAutopilot autopilot, out string error)) return error;
            if (minutes < 0f) return "Give a number of minutes, 0 or more.";
            autopilot.ServerSetSeconds(minutes * 60f);
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

        [Command("gm.autobuff.days", "Set the targeted player's Autobuff licence to run this many days from now (0 ends it): gm.autobuff.days <days>.")]
        private static string AutobuffDays(int days)
        {
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return error;
            var autobuff = player.GetComponent<PlayerAutobuff>();
            if (autobuff == null) return "That player has no Autobuff (run Krytical Nova/Setup/Player Prefab).";
            autobuff.ServerSetDays(days);
            return autobuff.TimeLeftLine;
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
