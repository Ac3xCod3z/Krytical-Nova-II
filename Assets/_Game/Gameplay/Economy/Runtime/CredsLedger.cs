using System;
using System.Text;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// Counts the Creds that entered and left wallets since the server started, by where they came from and went to.
    /// It is how the economy is tuned: compare what players really earn per hour with the income target in
    /// <c>EconomySettings</c>. Server (or offline) only; nothing is saved.
    /// </summary>
    public static class CredsLedger
    {
        private static readonly long[] Earned = new long[Enum.GetValues(typeof(CredsSource)).Length];
        private static readonly long[] Spent = new long[Enum.GetValues(typeof(CredsSink)).Length];
        private static float startedAt;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Array.Clear(Earned, 0, Earned.Length);
            Array.Clear(Spent, 0, Spent.Length);
            startedAt = 0f;
        }

        public static void RecordEarned(CredsSource source, long amount)
        {
            if (amount <= 0) return;
            if (startedAt <= 0f) startedAt = Time.realtimeSinceStartup;
            Earned[(int)source] += amount;
        }

        public static void RecordSpent(CredsSink sink, long amount)
        {
            if (amount <= 0) return;
            if (startedAt <= 0f) startedAt = Time.realtimeSinceStartup;
            Spent[(int)sink] += amount;
        }

        public static long TotalEarned(CredsSource source) => Earned[(int)source];
        public static long TotalSpent(CredsSink sink) => Spent[(int)sink];

        /// <summary>One line per source and sink that moved anything, with totals.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            float hours = startedAt > 0f ? Mathf.Max(1f / 60f, (Time.realtimeSinceStartup - startedAt) / 3600f) : 0f;
            sb.AppendLine(hours > 0f ? $"Creds since the first movement ({hours:0.00} h ago):" : "No Creds have moved yet.");

            long earned = 0, spent = 0;
            foreach (CredsSource source in Enum.GetValues(typeof(CredsSource)))
            {
                long amount = Earned[(int)source];
                if (amount == 0) continue;
                earned += amount;
                sb.AppendLine($"  in   {source,-14} {WalletRules.Format(amount),18}");
            }
            foreach (CredsSink sink in Enum.GetValues(typeof(CredsSink)))
            {
                long amount = Spent[(int)sink];
                if (amount == 0) continue;
                spent += amount;
                sb.AppendLine($"  out  {sink,-14} {WalletRules.Format(amount),18}");
            }
            if (earned > 0 || spent > 0)
                sb.Append($"  total in {WalletRules.Format(earned)}, out {WalletRules.Format(spent)}. " +
                          "Trade and Vault movements appear on both sides: they move Creds, they do not make them.");
            return sb.ToString().TrimEnd();
        }
    }
}
