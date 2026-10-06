using KryticalNova.Economy;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console economy commands. Like the GM commands they only run where the game is decided (host, server
    /// or offline) and act on the player picked by <see cref="GmTarget"/>. Until Hostile drops, Contracts and shops
    /// exist, these are the only way Creds enter the game.
    /// </summary>
    public static class EconomyCommands
    {
        private static bool TryGetWallet(out PlayerWallet wallet, out string error)
        {
            wallet = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            wallet = player.GetComponent<PlayerWallet>();
            if (wallet == null) error = "That player has no wallet (run Krytical Nova/Setup/Player Prefab).";
            return wallet != null;
        }

        [Command("gm.creds", "Add Creds to the targeted player (negative takes them away). With no amount it prints the balance. " +
                             "A wallet never holds more than its cap.")]
        private static string Creds(long amount = 0)
        {
            if (!TryGetWallet(out var wallet, out var error)) return error;
            if (amount > 0)
            {
                long added = wallet.ServerEarn(amount, CredsSource.Gm);
                return $"+{WalletRules.FormatCreds(added)}{(added < amount ? " (wallet full)" : string.Empty)}. Balance {WalletRules.FormatCreds(wallet.Creds)}.";
            }
            if (amount < 0)
            {
                long taken = System.Math.Min(-amount, wallet.Creds);
                wallet.ServerSpend(taken, CredsSink.Gm);
                return $"-{WalletRules.FormatCreds(taken)}. Balance {WalletRules.FormatCreds(wallet.Creds)}.";
            }
            return $"Balance {WalletRules.FormatCreds(wallet.Creds)} of {WalletRules.Format(wallet.Cap)}.";
        }

        [Command("gm.creds.set", "Set the targeted player's Creds outright.")]
        private static string SetCreds(long amount)
        {
            if (!TryGetWallet(out var wallet, out var error)) return error;
            wallet.ServerSet(amount);
            return $"Balance {WalletRules.FormatCreds(wallet.Creds)}.";
        }

        [Command("gm.kiosk", "List every Kiosk the server knows: owner, shop, open or closed, time left, shelf and till.")]
        private static string Kiosks()
        {
            var all = KioskService.All;
            if (all.Count == 0) return "No Kiosks.";
            var lines = new System.Text.StringBuilder();
            long now = System.DateTime.UtcNow.Ticks;
            foreach (KioskRecord record in all)
                lines.AppendLine($"{record.ownerName}: '{record.shopName}' {(record.open ? "open, " + KioskRules.TimeLeft(now, record.expiresUtc) + " left" : "closed")}, " +
                                 $"{record.listings.Count} listing(s), till {WalletRules.Format(record.till)}, sold {record.soldCount} for {WalletRules.Format(record.soldCreds)}");
            return lines.ToString().TrimEnd();
        }

        [Command("gm.kiosk.expire", "Run the targeted player's Kiosk out of time now: the stand goes and what is left waits to be collected.")]
        private static string ExpireKiosk()
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerKiosk kiosk)) return "That player has no Kiosk component (run Krytical Nova/Setup/Player Prefab).";
            KioskRecord record = KioskService.Find(kiosk.CharacterId);
            if (record == null || !record.open) return "That player has no open Kiosk.";
            record.expiresUtc = System.DateTime.UtcNow.Ticks;
            KioskService.ServerExpire(record);
            return $"'{record.shopName}' expired.";
        }

        [Command("gm.trade.reset", "Forget what the targeted player's account has sent in trades today, so the daily Creds limit starts again.")]
        private static string ResetTradeAllowance()
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerTrade trade)) return "That player has no trade component (run Krytical Nova/Setup/Player Prefab).";
            trade.ServerResetDailyAllowance();
            return $"Daily trade allowance reset: {WalletRules.FormatCreds(trade.ServerRemainingToday())} can be sent today.";
        }

        [Command("gm.vault", "Open your Vault here (gm.vault deep for the Deep Vault). Storage then works anywhere, with no " +
                             "Vaultkeeper or Wayfinder near, until the session ends; gm.vault off puts the rule back.")]
        private static string Vault(string which = "")
        {
            var network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer) return "Only the host or server can do that.";
            if (which == "off")
            {
                PlayerStorage.GmAnywhere = false;
                return "Storage needs its NPC again.";
            }
            bool deep = which == "deep";
            PlayerStorage.GmAnywhere = true;
            var windows = KryticalNova.Core.ClientPresentationRegistry.Services;
            bool opened = windows != null && windows.OpenService(deep ? KryticalNova.Core.ServiceIds.DeepVault : KryticalNova.Core.ServiceIds.Vault);
            return opened ? $"{(deep ? "Deep Vault" : "Vault")} opened. Storage works anywhere until gm.vault off."
                : "No HUD here to open the window in; storage requests work anywhere now.";
        }

        [Command("gm.creds.report", "Print the Creds that entered and left wallets since the server started, by source and sink, " +
                                    "and what the targeted player's level is meant to earn per hour.")]
        private static string Report()
        {
            string report = CredsLedger.Report();
            if (!TryGetWallet(out var wallet, out _) || wallet.Settings == null) return report;
            int level = wallet.TryGetComponent<KryticalNova.Progression.PlayerCharacterProgression>(out var progression) ? progression.CurrentLevel : 1;
            return $"{report}\nIncome target at level {level}: {WalletRules.FormatCreds(wallet.Settings.CredsPerHour(level))} per hour.";
        }
    }
}
