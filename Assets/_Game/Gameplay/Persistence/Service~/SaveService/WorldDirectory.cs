// The world service (online phase 5, milestone 2), inside the save service: who is where, and travel tickets.
//
// - Presence: every map server reports every few seconds which map it runs, on which port, and which accounts it holds.
//   A server silent for longer than Silence is gone. The players' own saves and locks stay in the database; presence
//   is kept in memory only (it is rebuilt by the next reports when the service restarts).
// - Tickets: the server a player is on asks for a ticket to another map. The service gives a random, unguessable
//   ticket, good for TicketLife and for one use, and the address of that map's server. The destination server
//   redeems it with the service, which says whose it is and where they arrive, once. Nobody can forge one (it is only
//   a random name the service keeps), reuse one, or use one meant for another map.
//
// No game rule lives here (owner, 18 Oct 2026: the service never learns the game's rules): which portal leads where and
// who may use it are decided by the map servers.

using System.Security.Cryptography;

namespace KryticalNova.SaveService
{
    public sealed class WorldDirectory
    {
        public static readonly TimeSpan Silence = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan TicketLife = TimeSpan.FromSeconds(30);
        private const int MaxPlayersPerReport = 1000;
        private const int MaxTickets = 10000;

        private readonly Func<DateTime> now;
        private readonly object gate = new object();
        private readonly Dictionary<string, Seen> servers = new Dictionary<string, Seen>(StringComparer.Ordinal);
        private readonly Dictionary<string, Ticket> tickets = new Dictionary<string, Ticket>(StringComparer.Ordinal);

        public WorldDirectory(Func<DateTime>? clock = null) => now = clock ?? (() => DateTime.UtcNow);

        private sealed record Seen(PresenceReport Report, DateTime At);
        private sealed record Ticket(string Account, string Character, string From, string ToServer, string ToMap, string Arrival, DateTime Until);

        /// <summary>A map server says it is alive, which map it runs and who is on it. Returns why it is refused, or null.</summary>
        public string? Report(PresenceReport report)
        {
            if (string.IsNullOrWhiteSpace(report.Server) || string.IsNullOrWhiteSpace(report.Map)) return "server and map";
            if (report.Port < 1 || report.Port > 65535) return "port";
            if ((report.Players?.Count ?? 0) > MaxPlayersPerReport) return "too many players";
            lock (gate) servers[report.Server] = new Seen(report, now());
            return null;
        }

        /// <summary>A map server that stops says so at once (it would otherwise count as alive until Silence).</summary>
        public void Gone(string server)
        {
            lock (gate) servers.Remove(server);
        }

        /// <summary>The map servers alive now.</summary>
        public List<ServerView> Servers()
        {
            lock (gate)
            {
                Prune();
                var list = new List<ServerView>();
                foreach (Seen seen in servers.Values)
                    list.Add(new ServerView(seen.Report.Server!, seen.Report.Map!, seen.Report.Name ?? "", seen.Report.Port,
                                            seen.Report.Players?.Count ?? 0, (int)(now() - seen.At).TotalSeconds));
                list.Sort((a, b) => string.CompareOrdinal(a.Map, b.Map));
                return list;
            }
        }

        /// <summary>The server and map an account is on now, or null.</summary>
        public WhereAnswer? Where(string account)
        {
            lock (gate)
            {
                Prune();
                foreach (Seen seen in servers.Values)
                    foreach (PresentPlayer player in seen.Report.Players ?? new List<PresentPlayer>())
                        if (player.Account == account)
                            return new WhereAnswer(seen.Report.Server!, seen.Report.Map!, player.Character ?? "");
                return null;
            }
        }

        /// <summary>
        /// A ticket for a player to another map: only the server the player is on may ask, and only for a map whose server
        /// is alive. The answer carries the ticket and that server's port.
        /// </summary>
        public TicketAnswer Issue(TicketRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.ToMap))
                return TicketAnswer.No("account, from and toMap");
            lock (gate)
            {
                Prune();
                if (!servers.TryGetValue(request.From, out Seen? from)) return TicketAnswer.No("the asking server is not reporting");
                if (!(from.Report.Players ?? new List<PresentPlayer>()).Exists(p => p.Account == request.Account))
                    return TicketAnswer.No("that player is not on the asking server");
                Seen? to = null;
                foreach (Seen seen in servers.Values)
                    if (seen.Report.Map == request.ToMap && seen.Report.Server != request.From) { to = seen; break; }
                if (to == null) return TicketAnswer.No($"no server runs {request.ToMap} now");
                if (tickets.Count >= MaxTickets) return TicketAnswer.No("too many tickets waiting");
                // A player has one ticket at a time: a new one cancels the last.
                foreach (KeyValuePair<string, Ticket> old in new List<KeyValuePair<string, Ticket>>(tickets))
                    if (old.Value.Account == request.Account) tickets.Remove(old.Key);
                string name = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                tickets[name] = new Ticket(request.Account, request.Character ?? "", request.From, to.Report.Server!, request.ToMap,
                                           request.Arrival ?? "", now() + TicketLife);
                return new TicketAnswer(true, null, name, to.Report.Server!, to.Report.Map!, to.Report.Port, (int)TicketLife.TotalSeconds);
            }
        }

        /// <summary>The destination server redeems a ticket, once: whose it is and where they arrive.</summary>
        public RedeemAnswer Redeem(RedeemRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Ticket) || string.IsNullOrWhiteSpace(request.Server)) return RedeemAnswer.No("ticket and server");
            lock (gate)
            {
                Prune();
                if (!tickets.TryGetValue(request.Ticket, out Ticket? ticket)) return RedeemAnswer.No("no such ticket (forged, used or run out)");
                if (ticket.ToServer != request.Server) return RedeemAnswer.No("the ticket is for another server");
                tickets.Remove(request.Ticket); // once
                return new RedeemAnswer(true, null, ticket.Account, ticket.Character, ticket.From, ticket.ToMap, ticket.Arrival);
            }
        }

        // Silent servers and run-out tickets go. Called under the gate.
        private void Prune()
        {
            DateTime at = now();
            foreach (string server in new List<string>(servers.Keys))
                if (at - servers[server].At > Silence) servers.Remove(server);
            foreach (string name in new List<string>(tickets.Keys))
                if (tickets[name].Until < at) tickets.Remove(name);
        }
    }

    public sealed record PresentPlayer(string? Account, string? Character);
    public sealed record PresenceReport(string? Server, string? Map, string? Name, int Port, List<PresentPlayer>? Players);
    public sealed record GoneRequest(string? Server);
    public sealed record ServerView(string Server, string Map, string Name, int Port, int Players, int SecondsAgo);
    public sealed record WhereAnswer(string Server, string Map, string Character);
    public sealed record TicketRequest(string? Account, string? Character, string? From, string? ToMap, string? Arrival);
    public sealed record TicketAnswer(bool Granted, string? Refused, string? Ticket, string? Server, string? Map, int Port, int Seconds)
    {
        public static TicketAnswer No(string why) => new TicketAnswer(false, why, null, null, null, 0, 0);
    }
    public sealed record RedeemRequest(string? Ticket, string? Server);
    public sealed record RedeemAnswer(bool Granted, string? Refused, string? Account, string? Character, string? From, string? Map, string? Arrival)
    {
        public static RedeemAnswer No(string why) => new RedeemAnswer(false, why, null, null, null, null, null);
    }
}
