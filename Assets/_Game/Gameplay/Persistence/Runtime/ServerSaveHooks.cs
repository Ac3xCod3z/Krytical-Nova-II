using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// The saves as the server's own code reaches them (<see cref="ServerHooks.Saves"/>): the bans' world document, saving
    /// everyone, the backups, the kept ledger and the save service's health. Every call goes straight to
    /// <see cref="ServerSave"/>, <see cref="ServerBackups"/> or the store; Networking only no longer names them.
    /// </summary>
    internal sealed class ServerSaveHooks : IServerSaves
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]   // EditMode tests of bans see the saves too
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => ServerHooks.Saves = new ServerSaveHooks();

        public bool HasAuthority => ServerSave.HasAuthority;
        public T World<T>(string name) where T : class, new() => ServerSave.World<T>(name);
        public void SaveWorld(string name) => ServerSave.SaveWorld(name);
        public List<string> AccountsWithCharacter(string characterName) => ServerSave.AccountsWithCharacter(characterName);
        public void FlushAll() => ServerSave.FlushAll();
        public bool CarryOverEnabled { set => ServerSave.CarryOverEnabled = value; }
        public ILedgerStore Ledger => ServerSave.Store as ILedgerStore;

        public bool ServiceReachable(out string problem)
        {
            problem = null;
            return !(ServerSave.Store is ServiceSaveStore service) || service.Reachable(out problem);
        }

        public string Status(out bool ok)
        {
            if (ServerSave.Store is ServiceSaveStore service)
            {
                ok = service.Healthy;
                return ok ? "saves: service ✓ database ✓ (" + service.Health.Replace("ok, ", "") + ")" : "saves: " + service.Health;
            }
            ok = true;
            return "saves: files";
        }

        public string Backup(string backupFolder, int kept, string reason) =>
            ServerBackups.MakeNow(ServerSave.DefaultRoot(), backupFolder, kept, reason);
    }
}
