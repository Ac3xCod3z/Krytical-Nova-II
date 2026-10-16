using System;
using System.Collections.Generic;
using KryticalNova.Networking;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Runs the GM console lines of joined Game Masters on the server (<see cref="PlayerGm"/>). Each line runs as if
    /// typed on the server, aimed at the GM's own player; gm.target picks someone else for that GM only, and the
    /// server's own pick (host, server window, control page) is left as it was. Every line is written to the log
    /// with the GM's name.
    /// </summary>
    public static class GmCommandRelay
    {
        private static readonly Dictionary<ulong, GmTarget.Saved> Targets = new Dictionary<ulong, GmTarget.Saved>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Boot()
        {
            Targets.Clear();
            PlayerGm.CommandRunner = RunFor;
        }

        private static string RunFor(ulong clientId, string line)
        {
            GmTarget.Saved server = GmTarget.Save();
            GmTarget.Restore(Targets.TryGetValue(clientId, out GmTarget.Saved mine) && mine.HasTarget
                ? mine
                : new GmTarget.Saved { HasTarget = true, ClientId = clientId });
            string answer;
            try
            {
                answer = RunLine(line);
            }
            finally
            {
                Targets[clientId] = GmTarget.Save(); // "gm.target self" leaves no pick: their own player next time
                GmTarget.Restore(server);
            }
            Debug.Log($"[GM] {GmRoster.NameOf(clientId) ?? "client " + clientId}: {line}\n{answer}");
            return answer;
        }

        /// <summary>Runs one command line on the game's thread and returns the answer.</summary>
        public static string RunLine(string line)
        {
            if (!QuantumConsoleProcessor.TableGenerated)
            {
                if (!QuantumConsoleProcessor.TableIsGenerating) QuantumConsoleProcessor.GenerateCommandTable();
                if (!QuantumConsoleProcessor.TableGenerated) return "The commands are still being loaded: try again in a moment.";
            }
            try
            {
                object result = QuantumConsoleProcessor.InvokeCommand(line);
                return result != null ? result.ToString() : "Done.";
            }
            catch (Exception e)
            {
                return (e.InnerException ?? e).Message;
            }
        }
    }
}
