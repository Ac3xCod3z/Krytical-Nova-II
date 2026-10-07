using KryticalNova.Progression;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for the two Renown ladders (Ascension and Nova Standings). Like the GM commands they
    /// only run where the game is decided (host, server or offline) and act on the player picked by
    /// <see cref="GmTarget"/>. Renown itself is given with gm.renown (ForgeCommands): nothing in the game earns it yet.
    /// </summary>
    public static class NovaCommands
    {
        private const string NoNova = "That player has no Nova component (run Krytical Nova/Setup/Player Prefab).";

        private static bool TryGetNova(out PlayerNova nova, out string error)
        {
            nova = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            nova = player.GetComponent<PlayerNova>();
            if (nova != null && nova.Settings != null) return true;
            error = NoNova;
            return false;
        }

        private static string Describe(PlayerNova nova)
        {
            NovaSettings rules = nova.Settings;
            var text = new System.Text.StringBuilder(NovaRules.AscensionLine(rules.ascension, nova.Ascension));
            NovaState state = nova.State;
            foreach (StandingTrackInfo info in rules.standings.tracks)
                text.Append("  |  ").Append(info.name).Append(' ').Append(state.Standing(info.track));
            text.Append("  |  worn: ").Append(nova.WornStanding ?? "none");
            return text.ToString();
        }

        [Command("gm.ascension", "Set the targeted player's Ascension level (0-96): gm.ascension <level>. With no level it prints both ladders.")]
        private static string Ascension(int level = -1)
        {
            if (!TryGetNova(out var nova, out var error)) return error;
            if (level >= 0 && !nova.ServerSetAscension(level)) return "Could not change that player's Ascension.";
            return Describe(nova) + ".";
        }

        [Command("gm.standing", "Set a Nova Standing of the targeted player: gm.standing <vanguard|bulwark|ironblood|mindwell|all> <0-12>.")]
        private static string Standing(string track, int level)
        {
            if (!TryGetNova(out var nova, out var error)) return error;
            if (track == "all")
            {
                for (int i = 0; i < NovaRules.TrackCount; i++) nova.ServerSetStanding((StandingTrack)i, level);
                return Describe(nova) + ".";
            }
            if (!System.Enum.TryParse(track, true, out StandingTrack which) || !System.Enum.IsDefined(typeof(StandingTrack), which))
                return $"Unknown track '{track}'. Use vanguard, bulwark, ironblood, mindwell or all.";
            return nova.ServerSetStanding(which, level) ? Describe(nova) + "." : "Could not change that Standing.";
        }

        [Command("gm.standing.wear", "Choose the Nova Standing the targeted player wears: gm.standing.wear <vanguard|bulwark|ironblood|mindwell|none>. " +
                                     "The player must hold a rank on that track.")]
        private static string StandingWear(string track)
        {
            if (!TryGetNova(out var nova, out var error)) return error;
            if (track == "none") return nova.ServerSetWorn(default, false) ? Describe(nova) + "." : "Could not change what that player wears.";
            if (!System.Enum.TryParse(track, true, out StandingTrack which) || !System.Enum.IsDefined(typeof(StandingTrack), which))
                return $"Unknown track '{track}'. Use vanguard, bulwark, ironblood, mindwell or none.";
            return nova.ServerSetWorn(which, true) ? Describe(nova) + "." : $"That player holds no rank on {which} yet: give one with gm.standing first.";
        }

        [Command("gm.herald", "Open the Herald's window here. It then works anywhere, with no Herald near, until the session ends; gm.herald off puts the rule back.")]
        private static string Herald(string which = "")
        {
            var network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer) return "Only the host or server can do that.";
            if (which == "off")
            {
                PlayerNova.GmAnywhere = false;
                return "Ascension and Nova Standings need a Herald again.";
            }
            PlayerNova.GmAnywhere = true;
            var windows = KryticalNova.Core.ClientPresentationRegistry.Services;
            bool opened = windows != null && windows.OpenService(KryticalNova.Core.ServiceIds.Herald);
            return opened ? "Herald opened. It works anywhere until gm.herald off."
                : "No HUD here to open the window in; Herald requests work anywhere now.";
        }
    }
}
