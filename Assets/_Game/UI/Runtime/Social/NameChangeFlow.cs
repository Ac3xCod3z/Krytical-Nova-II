using KryticalNova.CharacterCreation;
using KryticalNova.Items;
using KryticalNova.Persistence;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Name Change Pass on this player's game (owner, 22 Oct 2026): using it asks for the new name (the HUD's amount
    /// dialog in its word mode), which the server checks (<see cref="PlayerRename"/>); a refusal asks again with the reason,
    /// and a new name counts down 5 seconds and goes to character select (<see cref="GameMenuController.LeaveAfterRename"/>).
    /// </summary>
    public static class NameChangeFlow
    {
        private const string Heading = "NAME CHANGE";
        private const string Explanation =
            "Your character's new name: 3 to 16 letters and digits, starting with a letter. Names are unique in the whole game. " +
            "The pass is used only when the server takes the name; you then go back to character select so it knows you by it.";

        private static int bagIndex = -1;
        private static string lastTyped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Boot()
        {
            bagIndex = -1;
            lastTyped = null;
            PlayerInventory.NameChangeAsked -= Ask;
            PlayerInventory.NameChangeAsked += Ask;
            PlayerRename.Refused -= OnRefused;
            PlayerRename.Refused += OnRefused;
            PlayerRename.Renamed -= OnRenamed;
            PlayerRename.Renamed += OnRenamed;
        }

        private static void Ask(int cell)
        {
            bagIndex = cell;
            Open(null);
        }

        private static void Open(string problem) =>
            AmountDialogController.AskText(Heading, Explanation, lastTyped, Check, Send, problem);

        // The rules this game can check itself; the server checks them again, with every other name.
        private static string Check(string typed) => CharacterNameRules.Validate(typed, out _, out string reason) ? null : reason;

        private static void Send(string typed)
        {
            lastTyped = typed;
            if (PlayerRename.Local == null || bagIndex < 0)
            {
                Open("Your character is not in the world.");
                return;
            }
            PlayerRename.Local.RequestRename(bagIndex, typed);
        }

        private static void OnRefused(string reason) => Open(reason);

        private static void OnRenamed(string name)
        {
            bagIndex = -1;
            lastTyped = null;
            GameMenuController.LeaveAfterRename(name, PlayerRename.BackToSelectSeconds);
        }
    }
}
