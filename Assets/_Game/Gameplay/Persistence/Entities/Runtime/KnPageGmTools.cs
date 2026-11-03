#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The entity netcode's page GM tools (M7.4, owner Q3 a: the command box and the GM list now): the command box runs the line as the server
    /// console does (<see cref="ServerHooks.RunCommand"/>, kept in the GM record); the GM buttons are <see cref="GmRosterEdits"/>, NGO's rules and
    /// words, with players picked by their key from the page's player list and the list written into the server's settings file.
    /// </summary>
    public sealed class KnPageGmTools : KnPageRules.ICommands
    {
        /// <summary>Writes the GM list into the settings file: null, or why it could not (tests put their own).</summary>
        public Func<GmEntry[], string> Save = SaveToSettings;

        static string SaveToSettings(GmEntry[] list)
        {
            ServerLaunchOptions options = ServerLaunchOptions.Current;
            if (options == null) return "The server's settings are not loaded: the GM list cannot be saved.";
            return options.SaveGameMasters(list, out string error) ? null : error;
        }

        public string Command(string line, string from) =>
            ServerHooks.RunCommand != null ? ServerHooks.RunCommand(line, from) : "Commands are not ready yet.";

        public string GmListJson()
        {
            var sb = new StringBuilder("[");
            foreach (GmEntry entry in GmRoster.All)
            {
                if (sb.Length > 1) sb.Append(',');
                sb.Append("{\"name\":").Append(ControlPageRules.Json(entry.name))
                  .Append(",\"bound\":").Append(entry.IsBound ? "true" : "false")
                  .Append(",\"signedIn\":").Append(GmRoster.IsSignedIn(entry.name) ? "true" : "false").Append('}');
            }
            return sb.Append(']').ToString();
        }

        public string GmAdd(string name) => GmRosterEdits.Add(name, Save);

        public string GmBind(string name, ulong playerKey)
        {
            if (GmRoster.Find((name ?? string.Empty).Trim()) == null) return $"There is no GM called {name}.";
            var key = new PlayerKey(playerKey);
            if (!PlayerKeys.TryGetConnection(key, out ulong connection)) return "That player is no longer here.";
            string player = GhostBodyRegistry.TryGetServerBody(key, out GameObject body) && ServerHooks.Describe(body) is { Found: true } facts
                ? facts.Character
                : "connection " + connection;
            return GmRosterEdits.Bind(name, connection, player, Save);
        }

        public string GmUnbind(string name) => GmRosterEdits.Unbind(name, Save);

        public string GmRemove(string name) => GmRosterEdits.Remove(name, Save);
    }
}
#endif
