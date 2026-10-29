using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Where a player who travelled arrives (the answer of the world service to a good ticket). Read by the player-body code at
    /// M3 when it spawns the body. Keyed by the proven account, never a NetworkId; used once.
    /// </summary>
    public readonly struct KnArrival
    {
        public readonly string Character;
        public readonly string FromServer;
        public readonly string Map;
        public readonly string Place;

        public KnArrival(string character, string fromServer, string map, string place)
        {
            Character = character ?? string.Empty;
            FromServer = fromServer ?? string.Empty;
            Map = map ?? string.Empty;
            Place = place ?? string.Empty;
        }
    }

    public static class KnArrivals
    {
        private static readonly Dictionary<string, KnArrival> s_Arrivals = new Dictionary<string, KnArrival>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => s_Arrivals.Clear();

        /// <summary>Main thread.</summary>
        public static void Expect(string account, KnArrival arrival)
        {
            if (!string.IsNullOrEmpty(account)) s_Arrivals[account] = arrival;
        }

        /// <summary>Main thread: takes the arrival once.</summary>
        public static bool TryTake(string account, out KnArrival arrival)
        {
            if (account != null && s_Arrivals.TryGetValue(account, out arrival))
            {
                s_Arrivals.Remove(account);
                return true;
            }
            arrival = default;
            return false;
        }

        public static int Count => s_Arrivals.Count;
    }

    /// <summary>How the ticket check talks to the world service: one POST of JSON, one JSON answer. A seam so a test can script the service.</summary>
    public interface IKnTicketTransport
    {
        /// <summary>Posts <paramref name="json"/> to <paramref name="path"/> (e.g. <c>world/redeem</c>) and returns the answer's text. A fault (no answer, an HTTP error) faults the task.</summary>
        Task<string> PostAsync(string path, string json);
    }

    /// <summary>The real transport: the save service's HTTP API with the shared service key in the <c>X-KN-Key</c> header (the same one <c>WorldLink</c> uses).</summary>
    public sealed class KnHttpTicketTransport : IKnTicketTransport, IDisposable
    {
        private readonly HttpClient m_Http;

        public KnHttpTicketTransport(string baseUrl, string key, double timeoutSeconds = 5)
        {
            m_Http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            m_Http.DefaultRequestHeaders.Add("X-KN-Key", key ?? string.Empty);
        }

        public async Task<string> PostAsync(string path, string json)
        {
            using (HttpResponseMessage answer = await m_Http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false))
            {
                string text = await answer.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!answer.IsSuccessStatusCode) throw new HttpRequestException("the world service answered " + (int)answer.StatusCode);
                return text;
            }
        }

        public void Dispose() => m_Http.Dispose();
    }

    /// <summary>
    /// <see cref="IKnTravelTicketCheck"/> against the world service (online phase 5, milestone 2, inside the save service):
    /// <c>POST /world/redeem {ticket, server}</c> answers {granted, refused, account, character, from, map, arrival}. The ticket is
    /// good once and for 30 seconds; the service knows which server it was made for. (The task brief called the call
    /// <c>/ticket/use</c>; the service built in phase 5 names it <c>/world/redeem</c>, and this follows the service.)
    /// <list type="bullet">
    /// <item>No ticket: a server that takes players directly (role main) lets them in; a map server (<paramref name="requireTicket"/>)
    /// turns them away, because a map is entered by travelling.</item>
    /// <item>A ticket: redeemed over HTTP, polled by the join check every tick like the Unity token (the task is looked at, never awaited).</item>
    /// <item>It must belong to the proven account: a ticket of someone else is refused, however valid.</item>
    /// <item>The world service not answering refuses the join (fail closed): a ticket cannot be trusted without it.</item>
    /// </list>
    /// </summary>
    public sealed class WorldServiceTicketCheck : IKnTravelTicketCheck
    {
        public const string NoTicketText = "This map is entered by travelling from another one. Go back to the main server and travel again.";
        public const string BadTicketText = "Your travel ticket is not valid any more (it is good for 30 seconds and once). Travel again.";
        public const string NoServiceText = "The world service did not answer, so your travel could not be checked. Try again in a moment.";

        [Serializable]
        private sealed class RedeemAnswer
        {
            public bool granted;
            public string refused, account, character, from, map, arrival;
        }

        private sealed class Pending
        {
            public Task<string> Call;
        }

        private readonly IKnTicketTransport m_Transport;
        private readonly string m_ServerId;
        private readonly bool m_RequireTicket;
        private readonly Action<string> m_Log;
        private readonly Dictionary<Entity, Pending> m_Pending = new Dictionary<Entity, Pending>();

        /// <param name="serverId">The id this server reports to the world service as (<c>machine:port</c>, as <c>WorldLink.ServerId</c>).</param>
        public WorldServiceTicketCheck(IKnTicketTransport transport, string serverId, bool requireTicket, Action<string> log = null)
        {
            m_Transport = transport;
            m_ServerId = serverId ?? string.Empty;
            m_RequireTicket = requireTicket;
            m_Log = log ?? (_ => { });
        }

        /// <summary>The id the NGO server reports as, for this machine and port.</summary>
        public static string ServerId(int port) => Environment.MachineName + ":" + port;

        public KnJoinVerdict Check(Entity connection, string account, string ticket)
        {
            if (string.IsNullOrEmpty(ticket))
                return m_RequireTicket ? KnJoinVerdict.Refuse(KnRefuseReason.BadTicket, NoTicketText) : KnJoinVerdict.Accept;

            if (!m_Pending.TryGetValue(connection, out Pending pending))
            {
                pending = new Pending();
                m_Pending[connection] = pending;
                try
                {
                    pending.Call = m_Transport.PostAsync("world/redeem", "{\"ticket\":" + KnJson.Quote(ticket) + ",\"server\":" + KnJson.Quote(m_ServerId) + "}");
                }
                catch (Exception e)
                {
                    pending.Call = Task.FromException<string>(e);
                }
            }
            if (!pending.Call.IsCompleted) return KnJoinVerdict.Pending;

            if (pending.Call.IsFaulted || pending.Call.IsCanceled)
            {
                m_Log("The world service could not be asked about a ticket (" + (pending.Call.Exception?.GetBaseException().GetType().Name ?? "cancelled") + ").");
                return KnJoinVerdict.Refuse(KnRefuseReason.BadTicket, NoServiceText);
            }

            RedeemAnswer answer;
            try { answer = JsonUtility.FromJson<RedeemAnswer>(pending.Call.Result); }
            catch (Exception) { answer = null; }
            if (answer == null || !answer.granted)
            {
                m_Log("A travel ticket was not accepted by the world service: " + (answer?.refused ?? "unreadable answer") + ".");
                return KnJoinVerdict.Refuse(KnRefuseReason.BadTicket, BadTicketText);
            }
            if (!string.Equals(answer.account, account, StringComparison.Ordinal))
            {
                m_Log("A travel ticket belongs to another account than the one that came with it.");
                return KnJoinVerdict.Refuse(KnRefuseReason.BadTicket, BadTicketText);
            }
            KnArrivals.Expect(account, new KnArrival(answer.character, answer.from, answer.map, answer.arrival));
            return KnJoinVerdict.Accept;
        }

        public void Forget(Entity connection) => m_Pending.Remove(connection);
    }
}
