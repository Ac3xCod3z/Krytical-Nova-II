using KryticalNova.Combat;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for the fight itself: whether a player is in one, and why. Like the GM commands
    /// they only run where the game is decided (host, server or offline) and act on the player picked by
    /// <see cref="GmTarget"/>.
    /// </summary>
    public static class CombatCommands
    {
        private const string NoState = "That player has no combat state (run Krytical Nova/Setup/Player Prefab).";
        // What holds a player in a fight on the GM's word, as a war or The Scar will hold them one day.
        private static readonly object GmHold = new object();

        private static PlayerCombatState watched;

        // Statics survive Play Mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => watched = null;

        private static bool TryGet(out PlayerCombatState state, out string error)
        {
            state = null;
            if (!GmTarget.TryGetPlayer(out GameObject player, out error)) return false;
            state = player.GetComponent<PlayerCombatState>();
            if (state == null) { error = NoState; return false; }
            return true;
        }

        private static string Describe(PlayerCombatState state) =>
            state.InCombat ? $"In a fight: {state.Reasons}." : "Not in a fight.";

        [Command("gm.combat", "Whether the targeted player is in a fight and why (Blow, Attack, Callout, Hostile, Held): gm.combat. " +
                              "gm.combat hold on|off keeps them in one, as a war or The Scar will. " +
                              "gm.combat watch on|off prints to the log each time they enter or leave a fight.")]
        private static string Combat(string what = "", string state = "")
        {
            if (!TryGet(out PlayerCombatState combat, out string error)) return error;
            what = (what ?? string.Empty).Trim().ToLowerInvariant();
            state = (state ?? string.Empty).Trim().ToLowerInvariant();
            if (what.Length == 0) return Describe(combat);
            if ((what != "hold" && what != "watch") || (state != "on" && state != "off"))
                return "Use gm.combat, gm.combat hold on|off or gm.combat watch on|off.";

            if (what == "hold")
            {
                combat.ServerSetHeld(GmHold, state == "on");
                return Describe(combat);
            }

            if (watched != null) watched.Changed -= OnWatchedChanged;
            watched = state == "on" ? combat : null;
            if (watched != null) watched.Changed += OnWatchedChanged;
            return watched != null ? $"Watching {GmTarget.Describe()}: every start and end of a fight is printed to the log. {Describe(combat)}"
                                   : "No longer watching.";
        }

        [Command("gm.cadence", "Cadence for the targeted player: its numbers, and what became of their last skill key pressed in a fight " +
                               "(how far from the beat, on Cadence or not, whether it was spent on a cast). " +
                               "gm.cadence window on|off shows a running readout on the targeted player's own screen (yours, or another's with " +
                               "gm.target; on a server too): the beats coming, where each press fell, how many " +
                               "were on and off the beat, the average timing. gm.cadence reset starts its counts again. " +
                               "gm.cadence log on|off prints every press of every player to the log.")]
        private static string Cadence(string what = "", string state = "")
        {
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return error;
            var cadence = player.GetComponent<PlayerCadence>();
            if (cadence == null || cadence.Settings == null) return "That player has no Cadence (run Krytical Nova/Setup/Player Prefab).";
            what = (what ?? string.Empty).Trim().ToLowerInvariant();
            state = (state ?? string.Empty).Trim().ToLowerInvariant();
            if (what == "log" && (state == "on" || state == "off"))
            {
                PlayerCadence.Logging = state == "on";
                return PlayerCadence.Logging ? "Every press is printed to the log." : "Presses are no longer printed.";
            }
            if (what == "window" && (state == "on" || state == "off" || state.Length == 0))
            {
                // Shown on the screen of the player it reads: here for the host's own or an offline player; on a server,
                // on the targeted player's own game, which the server then sends every press it judges.
                bool remote = cadence.IsSpawned && !cadence.IsOwnedByServer;
                bool shown = remote ? cadence.ReadoutMirrored : PlayerCadence.ReadoutShown;
                bool on = state.Length == 0 ? !shown : state == "on";
                if (!cadence.ServerShowReadout(on)) return "The Cadence readout can only be switched where the game is decided.";
                if (!remote) return on ? "The Cadence readout is on the right of the screen (it follows your own player)." : "The Cadence readout is off.";
                return on ? $"The Cadence readout is on that player's own screen, on the right ({GmTarget.Describe()})." : $"The Cadence readout is off ({GmTarget.Describe()}).";
            }
            if (what == "reset" && state.Length == 0)
            {
                cadence.ResetTally();
                return "Counts and history started again.";
            }
            if (what.Length > 0) return "Use gm.cadence, gm.cadence window on|off, gm.cadence reset or gm.cadence log on|off.";

            CadenceSettings settings = cadence.Settings;
            return $"Cadence {(settings.enabled ? "on" : "OFF")}: within {settings.windowMs:0} ms of a beat, +{settings.bonusPercent:0.#} % damage, " +
                   $"kept {settings.keepSeconds:0.#} s, {(settings.againstPlayers ? "also against players" : "not against players")}." +
                   System.Environment.NewLine +
                   $"Last press: {cadence.LastPress}";
        }

        private static void OnWatchedChanged(bool fighting)
        {
            if (watched == null) return;
            Debug.Log(fighting ? $"[Combat] {watched.name} is in a fight: {watched.Reasons}." : $"[Combat] {watched.name} is out of the fight.");
        }
    }
}
