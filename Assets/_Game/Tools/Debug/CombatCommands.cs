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

        private static void OnWatchedChanged(bool fighting)
        {
            if (watched == null) return;
            Debug.Log(fighting ? $"[Combat] {watched.name} is in a fight: {watched.Reasons}." : $"[Combat] {watched.name} is out of the fight.");
        }
    }
}
