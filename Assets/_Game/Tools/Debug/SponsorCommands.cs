using KryticalNova.Social;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for Sponsor / Recruit. Like the GM commands they only run where the game is decided
    /// (host or server) and act on the player picked by <see cref="GmTarget"/>.
    /// </summary>
    public static class SponsorCommands
    {
        private static bool TryGetSponsor(out PlayerSponsor sponsor, out string error)
        {
            sponsor = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            sponsor = player.GetComponent<PlayerSponsor>();
            if (sponsor == null) error = "That player has no Sponsor component (run Krytical Nova/Setup/Player Prefab).";
            else if (!sponsor.IsSpawned) { sponsor = null; error = "Sponsoring needs an online session."; }
            return sponsor != null;
        }

        private static PlayerSponsor Of(ulong clientId)
        {
            foreach (var (id, player) in GmTarget.ConnectedPlayers())
                if (id == clientId) return player != null ? player.GetComponent<PlayerSponsor>() : null;
            return null;
        }

        [Command("gm.sponsor", "Link two connected players as Sponsor and Recruit at once, whatever their levels: gm.sponsor <clientIdA> <clientIdB> (see gm.players). " +
                               "The higher level sponsors. With no ids it prints the targeted player's links and points.")]
        private static string Sponsor(long clientA = -1, long clientB = -1)
        {
            if (clientA < 0 || clientB < 0)
            {
                if (!TryGetSponsor(out var mine, out var error)) return error;
                var lines = new System.Text.StringBuilder($"{mine.SponsorName} (level {mine.Level}): {mine.Points:N0} Sponsor points.");
                SponsorLink link = SponsorService.SponsorOf(mine.CharacterId);
                if (link != null) lines.Append($"\nSponsor: {link.sponsorName} (level {link.sponsorLevel}); bonus now {mine.ServerExperienceBonus(out bool inSquad) * 100f:0}%{(inSquad ? " (in the Sponsor's Squad)" : string.Empty)}.");
                var recruits = new System.Collections.Generic.List<SponsorLink>();
                SponsorService.RecruitsOf(mine.CharacterId, recruits);
                foreach (SponsorLink recruit in recruits) lines.Append($"\nRecruit: {recruit.recruitName} (level {recruit.recruitLevel}).");
                if (link == null && recruits.Count == 0) lines.Append("\nNo links.");
                return lines.ToString();
            }
            PlayerSponsor a = Of((ulong)clientA), b = Of((ulong)clientB);
            if (a == null || b == null) return "No connected player with one of those client ids. Try gm.players.";
            return SponsorService.GmLink(a, b);
        }

        [Command("gm.sponsor.end", "End every Sponsor link of the targeted player.")]
        private static string End()
        {
            if (!TryGetSponsor(out var mine, out var error)) return error;
            return $"{SponsorService.GmEndAll(mine)} link(s) ended.";
        }

        [Command("gm.sponsor.points", "Set the targeted player's Sponsor points. With no number it prints them.")]
        private static string Points(int value = -1)
        {
            if (!TryGetSponsor(out var mine, out var error)) return error;
            if (value >= 0) mine.ServerSetPoints(value);
            return $"{mine.Points:N0} Sponsor points.";
        }

        [Command("gm.sponsor.window", "Open the Sponsor / Recruit window (players open it with /sponsor, or from Contacts).")]
        private static string Window()
        {
            var windows = KryticalNova.Core.ClientPresentationRegistry.Services;
            return windows != null && windows.OpenService(KryticalNova.Core.ServiceIds.Sponsor) ? "Sponsor window opened." : "No HUD here to open the window in.";
        }
    }
}
