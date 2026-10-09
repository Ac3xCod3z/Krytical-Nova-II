using KryticalNova.Items;
using KryticalNova.Progression;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for Serums. Like the GM commands they only run where the game is decided (host,
    /// server or offline) and act on the player picked by <see cref="GmTarget"/>. Until drops and shops exist they
    /// are the only source of Serums.
    /// </summary>
    public static class SerumCommands
    {
        private const string NoSerums = "That player has no Serum component (run Krytical Nova/Setup/Player Prefab).";

        private static bool TryGetSerums(out PlayerSerums serums, out string error)
        {
            serums = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            serums = player.GetComponent<PlayerSerums>();
            if (serums != null && serums.Settings != null) return true;
            error = NoSerums;
            return false;
        }

        [Command("gm.serums", "Give the targeted player Serums: gm.serums <count> (default 10) of each kind.")]
        private static string Give(int count = 10)
        {
            if (!TryGetSerums(out var serums, out var error)) return error;
            var inventory = serums.GetComponent<PlayerInventory>();
            if (inventory == null) return NoSerums;
            int kinds = 0;
            for (int i = 0; i < SerumRules.KindCount(serums.Settings); i++)
                if (inventory.ServerGive(serums.Settings.serums[i].itemId, count) > 0) kinds++;
            return $"Gave {count} of each Serum ({kinds} kinds).";
        }

        [Command("gm.serum", "Set how many of a kind of Serum the targeted player has taken: gm.serum <kind> <taken>. Kind is its number " +
                             "(1 = the first on the Character Sheet) or a word of its name (strength, vitality, agility, spirit, strike, ward). " +
                             "Stops at the cap. No values lists what has been taken.")]
        private static string Set(string kind = "", int taken = 0)
        {
            if (!TryGetSerums(out var serums, out var error)) return error;
            SerumSettings settings = serums.Settings;
            int kinds = SerumRules.KindCount(settings);
            if (kind.Length == 0)
            {
                var list = new System.Text.StringBuilder($"Serums taken (cap {settings.Cap}):");
                for (int i = 0; i < kinds; i++) list.Append('\n').Append(i + 1).Append(". ").Append(SerumRules.SheetLine(settings, i, serums.State));
                return list.ToString();
            }

            int index = -1;
            if (int.TryParse(kind, out int number)) index = number - 1;
            else
                for (int i = 0; i < kinds && index < 0; i++)
                    if (settings.serums[i].displayName.IndexOf(kind, System.StringComparison.OrdinalIgnoreCase) >= 0) index = i;
            if (index < 0 || index >= kinds) return $"No Serum '{kind}'. gm.serum lists them.";
            return serums.ServerSetTaken(index, taken) ? SerumRules.SheetLine(settings, index, serums.State) : "Could not change that.";
        }
    }
}
