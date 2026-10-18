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
    /// server's own pick (host, server window, control page) is left as it was. Every line, with the GM's name and
    /// account, goes in the GM record (<see cref="ServerRecords"/>: gm.log beside a dedicated server's saves).
    /// </summary>
    public static class GmCommandRelay
    {
        private static readonly Dictionary<ulong, GmTarget.Saved> Targets = new Dictionary<ulong, GmTarget.Saved>();

        /// <summary>
        /// Who the command running now is for, as the records name them ("GM Ace", "Server window", "Control page
        /// (192.168.1.20)"); "Host" otherwise (the host's own console, the GM Tools window). A ban says who did it.
        /// </summary>
        public static string RunningFor { get; private set; } = Host;

        private const string Host = "Host";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Boot()
        {
            Targets.Clear();
            RunningFor = Host;
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
                answer = RunLine(line, "GM " + (GmRoster.NameOf(clientId) ?? "client " + clientId));
            }
            finally
            {
                Targets[clientId] = GmTarget.Save(); // "gm.target self" leaves no pick: their own player next time
                GmTarget.Restore(server);
            }
            ServerRecords.Write(RecordKind.Gm, $"GM {GmRoster.NameOf(clientId) ?? "?"} as {ServerRecords.Who(clientId)}: {line} -> {ServerRecords.Short(answer)}");
            return answer;
        }

        /// <summary>Runs one command line on the game's thread for <paramref name="runningFor"/> and returns the answer.</summary>
        public static string RunLine(string line, string runningFor)
        {
            RunningFor = string.IsNullOrEmpty(runningFor) ? Host : runningFor;
            try
            {
                return RunLine(line);
            }
            finally
            {
                RunningFor = Host;
            }
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
