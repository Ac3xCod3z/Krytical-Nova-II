using System;
using KryticalNova.Combat;
using KryticalNova.Player;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>The chat box's duel commands: /duel [name], /accept, /decline, /forfeit. They act for the local player.</summary>
    public static class DuelChatCommands
    {
        private const string Offline = "Duels need an online session with another player.";

        // ChatCommands forgets its commands at SubsystemRegistration (no domain reload); this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            ChatCommands.Register("duel", Duel);
            ChatCommands.Register("accept", _ => Answer(true));
            ChatCommands.Register("decline", _ => Answer(false));
            ChatCommands.Register("forfeit", _ => Forfeit());
        }

        private static string Duel(string name)
        {
            PlayerDuel mine = PlayerDuel.Local;
            if (mine == null || !mine.IsSpawned) return Offline;

            PlayerDuel target = null;
            if (name.Length > 0)
            {
                foreach (var other in UnityEngine.Object.FindObjectsByType<PlayerDuel>(FindObjectsInactive.Exclude))
                {
                    if (other == mine || !other.IsSpawned) continue;
                    if (string.Equals(other.DuelistName, name, StringComparison.OrdinalIgnoreCase)) { target = other; break; }
                }
                if (target == null) return $"No player named '{name}' is nearby.";
            }
            else
            {
                Transform selected = mine.TryGetComponent(out PointAndClickPlayer controls) ? controls.SelectedTarget : null;
                target = selected != null ? selected.GetComponentInParent<PlayerDuel>() : null;
                if (target == null || target == mine) return "Select a player first, or use /duel <name>.";
            }

            mine.Request(target);
            return null; // the server answers
        }

        private static string Answer(bool accept)
        {
            PlayerDuel mine = PlayerDuel.Local;
            if (mine == null || !mine.IsSpawned) return Offline;
            mine.Respond(accept);
            return null;
        }

        private static string Forfeit()
        {
            PlayerDuel mine = PlayerDuel.Local;
            if (mine == null || !mine.IsSpawned) return Offline;
            mine.Forfeit();
            return null;
        }
    }
}
