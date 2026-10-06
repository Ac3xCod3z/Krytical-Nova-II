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
            return $"Saves: {place} ({ServerSave.Store.Keys(ServerSave.AccountsPrefix).Count} account(s), {ServerSave.Online.Count} loaded).";
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
