using KryticalNova.Core;
using KryticalNova.Persistence;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for the server's save store. Like the GM commands they only run where the game is
    /// decided (host, server or offline).
    /// </summary>
    public static class SaveCommands
    {
        private const string NoAuthority = "Only the host/offline player can use save commands.";
        private const string ConfirmWord = "confirm";

        [Command("gm.save", "Write every loaded character and every world document (Crews, Kiosks, Contacts) to the save store now.")]
        private static string Save()
        {
            if (!ServerSave.HasAuthority) return NoAuthority;
            ServerSave.FlushAll();
            return $"Saved {ServerSave.Online.Count} character(s). {Where()}";
        }

        [Command("gm.save.where", "Print where saves are kept and how many accounts are stored.")]
        private static string Where()
        {
            if (!ServerSave.HasAuthority) return NoAuthority;
            string place = ServerSave.Store is FileSaveStore files ? files.Root : ServerSave.Store.GetType().Name;
            return $"Saves: {place} ({ServerSave.AccountIds().Count} account(s), {ServerSave.Online.Count} loaded).";
        }

        [Command("gm.ledger", "Read the kept ledger: what an account gained and gave up (trades, Kiosk sales, shops, Vault, loot, " +
                              "forge, GM commands; every Cred by where it came from), newest first. gm.ledger <character name | " +
                              "account id | all> [days, 7] [lines, 20].")]
        private static string Ledger(string who = "all", int days = 7, int lines = 20)
        {
            if (!ServerSave.HasAuthority) return NoAuthority;
            string account = null;
            if (!string.Equals(who, "all", System.StringComparison.OrdinalIgnoreCase))
            {
                account = SaveKeys.IsId(who) ? who : AccountOf(who);
                if (account == null) return $"Nobody called {who} has a save here (a character name, an account id, or all).";
            }
            long since = System.DateTime.UtcNow.AddDays(-System.Math.Max(1, days)).Ticks;
            var found = ServerSave.ReadLedger(account, since, System.Math.Clamp(lines, 1, 500));
            if (found.Count == 0) return $"No ledger lines {(account == null ? "" : "for " + who + " ")}in the last {days} day(s).";
            var sb = new System.Text.StringBuilder($"Ledger {(account == null ? "of every account" : "of " + who + " (account " + account + ")")}, last {days} day(s), newest first:");
            foreach (LedgerEntry line in found) sb.Append('\n').Append(line.Describe());
            return sb.ToString();
        }

        // A character in the world, then a saved one, by name.
        private static string AccountOf(string name)
        {
            foreach (PlayerSaveAgent agent in ServerSave.Online)
                if (agent != null && agent.TryGetComponent(out KryticalNova.Player.PlayerIdentity identity)
                    && string.Equals(identity.CharacterName, name, System.StringComparison.OrdinalIgnoreCase)) return agent.AccountId;
            var accounts = ServerSave.AccountsWithCharacter(name);
            return accounts.Count > 0 ? accounts[0] : null;
        }

        [Command("gm.save.wipe", "Delete EVERY save: all accounts and characters, Crews, Kiosks and Contacts. Players in the world keep " +
                                 "playing but are no longer saved until they re-enter. Type gm.save.wipe confirm.")]
        private static string Wipe(string confirm = "")
        {
            if (!ServerSave.HasAuthority) return NoAuthority;
            if (confirm != ConfirmWord) return "This deletes every save and cannot be undone. Type: gm.save.wipe confirm";
            int deleted = ServerSave.WipeAll();
            return $"Deleted {deleted} save document(s).";
        }
    }
}
