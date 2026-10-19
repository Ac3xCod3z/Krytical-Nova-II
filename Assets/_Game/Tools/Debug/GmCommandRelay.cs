using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
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
            GameObject target = GmTarget.TryGetPlayer(out GameObject aimed, out _) ? aimed : null;
            Dictionary<int, int> before = PackCounts(target);
            try
            {
                object result = QuantumConsoleProcessor.InvokeCommand(line);
                return result != null ? result.ToString() : "Done.";
            }
            catch (Exception e)
            {
                return (e.InnerException ?? e).Message;
            }
            finally
            {
                KeepLedger(target, line, before);
            }
        }

        // The kept ledger (online phase 4, milestone 3): a GM command that changed what a player carries is one line on
        // that player's account, with every item it added or took and the command itself. Found by comparing the Pack
        // before and after, so every GM command is covered, also ones added later. Creds a GM gives are in the wallet's
        // tally (creds.gm).
        private static Dictionary<int, int> PackCounts(GameObject player)
        {
            if (player == null || !player.TryGetComponent(out PlayerInventory inventory)) return null;
            var pack = new ItemStack[PlayerInventory.Capacity];
            inventory.CopyPack(pack);
            var counts = new Dictionary<int, int>();
            foreach (ItemStack stack in pack)
                if (!stack.IsEmpty) counts[stack.itemId] = (counts.TryGetValue(stack.itemId, out int n) ? n : 0) + stack.count;
            return counts;
        }

        private static void KeepLedger(GameObject target, string line, Dictionary<int, int> before)
        {
            if (before == null || target == null || !GmTarget.TryGetPlayer(out GameObject now, out _) || now != target) return;
            Dictionary<int, int> after = PackCounts(target);
            if (after == null) return;
            LedgerEntry entry = KryticalNova.Economy.EconomyLedger.Line(target.transform, LedgerKinds.Gm, null, RunningFor + ": " + line.Trim());
            if (entry == null) return;
            ItemCatalog catalog = target.TryGetComponent(out PlayerInventory inventory) ? inventory.Catalog : null;
            var ids = new HashSet<int>(before.Keys);
            ids.UnionWith(after.Keys);
            foreach (int id in ids)
            {
                int change = (after.TryGetValue(id, out int a) ? a : 0) - (before.TryGetValue(id, out int b) ? b : 0);
                if (change == 0) continue;
                ItemDefinition item = catalog != null ? catalog.Get((ushort)id) : null;
                entry.With(id, change, item != null ? item.displayName : null);
            }
            if (entry.items.Count > 0) ServerSaveLedger(entry);
        }

        private static void ServerSaveLedger(LedgerEntry entry) => KryticalNova.Persistence.ServerSave.Ledger(entry);
    }
}
