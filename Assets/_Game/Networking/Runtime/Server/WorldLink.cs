#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// This server's line to the world service (online phase 5, milestone 2; the save service grown, WorldDirectory): every
    /// few seconds it reports which map it runs, its port and which players (account and character) are on it, and keeps
    /// the answer, the map servers alive now, for the control page. When it stops it says so at once. Only when the server
    /// saves through the save service; a server that saves to files has no world. Travel tickets (/world/ticket, /redeem)
    /// are asked for by the travelling of milestone 3.
    /// </summary>
    public sealed class WorldLink : MonoBehaviour
    {
        public const float ReportEverySeconds = 10f;

        /// <summary>A map server alive now, as the world service last said.</summary>
        [Serializable]
        public struct MapServer
        {
            public string server, map, name;
            public int port, players, secondsAgo;
        }

        [Serializable] private sealed class Answer { public MapServer[] servers = new MapServer[0]; }
        [Serializable] private sealed class Player { public string account, character; }
        [Serializable] private sealed class Report { public string server, map, name; public int port; public Player[] players; }

        private static WorldLink instance;
        private static readonly object Gate = new object();
        private static MapServer[] known = new MapServer[0];
        private static string problem = "not reported yet";

        private ServerLaunchOptions options;
        private HttpClient http;
        private string serverId;
        private float nextReport;
        private int busy;

        /// <summary>The map servers alive now (this one included), as last heard; empty before the first report.</summary>
        public static MapServer[] Known { get { lock (Gate) return known; } }

        /// <summary>Why the last report did not go through, or null when it did.</summary>
        public static string Problem { get { lock (Gate) return problem; } }

        /// <summary>This server's name in the world service: the same as its name on the save service's locks.</summary>
        public static string ServerId(ServerLaunchOptions options) => Environment.MachineName + ":" + options.port;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            known = new MapServer[0];
            problem = "not reported yet";
        }

        /// <summary>Starts reporting, when the server saves through the save service.</summary>
        public static void Begin(ServerLaunchOptions options)
        {
            if (instance != null || !options.UsesSaveService) return;
            string key = File.Exists(options.SaveServiceKeyFile) ? File.ReadAllText(options.SaveServiceKeyFile).Trim() : string.Empty;
            var host = new GameObject("World Link");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<WorldLink>();
            instance.options = options;
            instance.serverId = ServerId(options);
            instance.http = new HttpClient { BaseAddress = new Uri(options.saveService.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(5) };
            instance.http.DefaultRequestHeaders.Add("X-KN-Key", key);
            Debug.Log($"[World] This server reports to the world service as {instance.serverId} ({options.map}).");
        }

        /// <summary>The server stops: it leaves the world at once (best effort, a second at most).</summary>
        public static void Leave()
        {
            if (instance == null || instance.http == null) return;
            try
            {
                string body = "{\"server\":" + Quote(instance.serverId) + "}";
                instance.http.PostAsync("world/gone", new StringContent(body, Encoding.UTF8, "application/json")).Wait(1000);
            }
            catch (Exception) { /* stopping anyway: the service drops a silent server after 30 seconds */ }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextReport) return;
            nextReport = Time.unscaledTime + ReportEverySeconds;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            string body = JsonUtility.ToJson(BuildReport()); // on the game's thread: the players are read here
            Task.Run(async () =>
            {
                try
                {
                    using HttpResponseMessage answer = await http.PostAsync("world/presence", new StringContent(body, Encoding.UTF8, "application/json"));
                    string text = await answer.Content.ReadAsStringAsync();
                    if (!answer.IsSuccessStatusCode) { SetProblem($"the world service answered {(int)answer.StatusCode}"); return; }
                    Answer parsed = JsonUtility.FromJson<Answer>(text);
                    lock (Gate)
                    {
                        known = parsed?.servers ?? new MapServer[0];
                        problem = null;
                    }
                }
                catch (Exception e)
                {
                    SetProblem("the world service did not answer (" + e.GetBaseException().Message + ")");
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                }
            });
        }

        private Report BuildReport()
        {
            var players = new List<Player>();
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsServer)
                foreach (NetworkClient client in network.ConnectedClientsList)
                {
                    if (client.PlayerObject == null) continue;
                    ServerHooks.PlayerFacts facts = ServerHooks.Describe(client.PlayerObject.gameObject);
                    string account = !string.IsNullOrEmpty(facts.Account) ? facts.Account
                        : ServerAccounts.TryGet(NetPlayerKeys.Find(client.ClientId), out ServerAccounts.Entry entry) ? entry.Account : string.Empty;
                    if (string.IsNullOrEmpty(account)) continue;
                    players.Add(new Player { account = account, character = facts.Character ?? string.Empty });
                }
            return new Report { server = serverId, map = options.map, name = ShownName(options), port = options.port, players = players.ToArray() };
        }

        // The listed server goes by its own name (Nova); a map server by the name the Server app gives its tab.
        private static string ShownName(ServerLaunchOptions options)
        {
            if (!options.IsMapServer) return options.serverName;
            foreach (MapServerEntry entry in options.mapServers)
                if (entry != null && entry.map == options.map && !string.IsNullOrWhiteSpace(entry.name)) return entry.name;
            return options.map;
        }

        private static void SetProblem(string why)
        {
            lock (Gate) problem = why;
        }

        private static string Quote(string text) => "\"" + (text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private void OnDestroy()
        {
            http?.Dispose();
            if (instance == this) instance = null;
        }
    }
}
#endif
