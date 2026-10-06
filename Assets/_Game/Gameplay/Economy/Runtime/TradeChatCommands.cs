using KryticalNova.Player;
using KryticalNova.Social;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// The chat box's trade command: /trade (the selected player), /trade name, /trade accept, /trade decline,
    /// /trade cancel. It acts for the local player; the server decides.
    /// </summary>
    public static class TradeChatCommands
    {
        private const string Offline = "Trading needs an online session with another player.";

        // ChatCommands forgets its commands at SubsystemRegistration (no domain reload); this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => ChatCommands.Register("trade", Trade);

        private static string Trade(string argument)
        {
            PlayerTrade mine = PlayerTrade.Local;
            if (mine == null || !mine.IsSpawned) return Offline;

            switch (argument.ToLowerInvariant())
            {
                case "accept":
                case "yes":
                    mine.Respond(true);
                    return null;
                case "decline":
                case "refuse":
                case "no":
                    mine.Respond(false);
                    return null;
                case "cancel":
                    mine.Cancel();
                    return null;
            }
            if (argument.Length > 0)
            {
                mine.Request(argument);
                return null; // the server answers
            }

            Transform selected = mine.TryGetComponent(out PointAndClickPlayer controls) ? controls.SelectedTarget : null;
            PlayerTrade target = selected != null ? selected.GetComponentInParent<PlayerTrade>() : null;
            if (target == null || target == mine) return "Select a player first, or use /trade <name>.";
            mine.Request(target);
            return null;
        }
    }
}
