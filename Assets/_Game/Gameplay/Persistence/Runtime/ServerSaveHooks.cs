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
    internal sealed class ServerSaveHooks : IServerSaves, IServerShutdown
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]   // EditMode tests of bans see the saves too
#endif
        // After every SubsystemRegistration reset: Core's ServerShutdown clears its Handler there, and Unity runs the methods of one load
        // type in no set order across assemblies. In the entity server build (8 Nov 2026) the reset ran after this, so the stop path found
        // no handler and saved nothing ("Nothing is registered to save").
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var hooks = new ServerSaveHooks();
            ServerHooks.Saves = hooks;
            ServerHooks.Roster = new ServerRoster();
            ServerShutdown.Handler = hooks; // the Netcode for Entities server's stop path saves everyone through this
        }

        public bool HasAuthority => ServerSave.HasAuthority;
        public T World<T>(string name) where T : class, new() => ServerSave.World<T>(name);
        public void SaveWorld(string name) => ServerSave.SaveWorld(name);
        public List<string> AccountsWithCharacter(string characterName) => ServerSave.AccountsWithCharacter(characterName);
        public void FlushAll() => ServerSave.FlushAll();
        void IServerShutdown.SaveAll() => ServerSave.FlushAll();
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
