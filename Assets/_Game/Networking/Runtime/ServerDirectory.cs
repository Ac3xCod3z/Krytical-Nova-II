using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace KryticalNova.Networking
{
    /// <summary>A line of the Servers window: a server, or "play here", with what its status port last said.</summary>
    public sealed class ServerEntry
    {
        public enum State { Checking, Online, Off, Update, Full, Here }

        public ServerListing Listing;   // null for "play here"
        public string Name;
        public string Note;              // where it came from, or why it cannot be joined
        public State Status = State.Checking;
        public int Players, Max, Milliseconds = -1;
        public string Build;

        public bool PlayHere => Listing == null;
        public string Key => PlayHere ? ServerDirectory.HereKey : Listing.Key;
        public bool CanJoin => PlayHere || Status == State.Online || Status == State.Checking;
    }

    /// <summary>
    /// The servers the Servers window lists (online step 4): the owner's published list
    /// (<see cref="ServerListFile.ListUrl"/>, kept on this PC for when the internet is down), and in the Editor and
    /// test builds also "This PC (testing)" and "Play here (no server)". Each server's status port is asked how it is.
    /// Remembers the last server chosen; Enter on character select joins it.
    /// </summary>
    public static class ServerDirectory
    {
        public const string HereKey = "here";
        private const string LastKey = "KryticalNova.Servers.Last";
        private const int ProbeTimeoutMs = 1200;

        private static readonly List<ServerEntry> entries = new List<ServerEntry>();
        private static Runner runner;

        /// <summary>Raised on the game's thread whenever the list or a status changed.</summary>
        public static event Action Changed;

        public static IReadOnlyList<ServerEntry> Entries => entries;

        /// <summary>The Editor and test builds: they may play without a server and see a server on this PC.</summary>
        public static bool Development => !GameBuild.IsRelease;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            entries.Clear();
            runner = null;
            Changed = null;
        }

        private static string CacheFile => Path.Combine(Application.persistentDataPath, ServerListFile.FileName);

        // ------------------------------------------------------------------ the last server

        /// <summary>The last choice: a server's key, "here", or empty when none was ever made.</summary>
        public static string LastKeyChosen => PlayerPrefs.GetString(LastKey, Development ? HereKey : string.Empty);

        public static void Remember(ServerEntry entry)
        {
            PlayerPrefs.SetString(LastKey, entry.Key);
            PlayerPrefs.Save();
        }

        /// <summary>The entry Enter joins, or null when none was chosen yet (or it left the list).</summary>
        public static ServerEntry Last
        {
            get
            {
                string key = LastKeyChosen;
                foreach (ServerEntry entry in entries)
                    if (entry.Key == key) return entry;
                return null;
            }
        }

        // ------------------------------------------------------------------ the list

        /// <summary>Fills the list from this PC's copy at once, then fetches the published one and asks every server.</summary>
        public static void Refresh()
        {
            Build(ReadCache());
            if (runner == null)
            {
                var host = new GameObject("Server Directory");
                UnityEngine.Object.DontDestroyOnLoad(host);
                runner = host.AddComponent<Runner>();
            }
            runner.StartCoroutine(Fetch());
        }

        private static ServerListFile ReadCache()
        {
            try
            {
                return File.Exists(CacheFile) ? ServerListFile.Parse(File.ReadAllText(CacheFile)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerator Fetch()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(ServerListFile.ListUrl))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    ServerListFile list = ServerListFile.Parse(request.downloadHandler.text);
                    if (list != null)
                    {
                        try { File.WriteAllText(CacheFile, request.downloadHandler.text); }
                        catch (Exception) { /* the copy on this PC is only for when the internet is down */ }
                        Build(list);
                    }
                }
            }
            ProbeAll();
        }

        private static void Build(ServerListFile published)
        {
            entries.Clear();
            if (published != null)
                foreach (ServerListing listing in published.servers)
                {
                    if (listing == null || ServerListFile.Problem(listing.name, listing.address, listing.port) != null) continue;
                    if (entries.Count >= ServerListFile.MaxServers) break;
                    entries.Add(new ServerEntry { Listing = listing, Name = listing.name, Note = listing.address + " : " + listing.port });
                }
            if (Development)
            {
                // An Editor test session moves every local session to its own port: this line follows it.
                ushort port = EditorTestSession.TryGetPort(out ushort testPort) ? testPort : (ushort)7777;
                var local = new ServerListing { name = "This PC (testing)", address = "127.0.0.1", port = port };
                entries.Add(new ServerEntry { Listing = local, Name = local.name, Note = "a server started on this PC" });
                entries.Add(new ServerEntry { Name = "Play here (no server)", Note = Application.isEditor ? "the Editor hosts, as before" : "offline, as before", Status = ServerEntry.State.Here });
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ how each server is

        private static void ProbeAll()
        {
            foreach (ServerEntry entry in entries)
            {
                if (entry.PlayHere) continue;
                ServerEntry probed = entry;
                Task.Run(() => Probe(probed.Listing)).ContinueWith(task =>
                {
                    ServerStatusProtocol.Status status = task.Status == TaskStatus.RanToCompletion ? task.Result.status : null;
                    runner.Post(() => Apply(probed, status, task.Status == TaskStatus.RanToCompletion ? task.Result.ms : -1));
                });
            }
        }

        private static (ServerStatusProtocol.Status status, int ms) Probe(ServerListing listing)
        {
            try
            {
                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = ProbeTimeoutMs;
                    IPAddress[] addresses = Dns.GetHostAddresses(listing.address);
                    if (addresses.Length == 0) return (null, -1);
                    var target = new IPEndPoint(addresses[0], listing.StatusPort);
                    byte[] request = ServerStatusProtocol.Request();
                    Stopwatch clock = Stopwatch.StartNew();
                    udp.Send(request, request.Length, target);
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] reply = udp.Receive(ref from);
                    return (ServerStatusProtocol.ReadReply(reply, reply.Length), (int)clock.ElapsedMilliseconds);
                }
            }
            catch (Exception)
            {
                return (null, -1);
            }
        }

        private static void Apply(ServerEntry entry, ServerStatusProtocol.Status status, int ms)
        {
            if (status == null)
            {
                entry.Status = ServerEntry.State.Off;
                entry.Note = $"{entry.Listing.address} : {entry.Listing.port}   ·   not answering (off, or not reachable from here)";
            }
            else
            {
                entry.Players = status.players;
                entry.Max = status.max;
                entry.Build = status.build;
                entry.Milliseconds = ms;
                bool sameBuild = status.build == GameBuild.Version || Application.isEditor;
                entry.Status = !sameBuild ? ServerEntry.State.Update : status.max > 0 && status.players >= status.max ? ServerEntry.State.Full : ServerEntry.State.Online;
                entry.Note = !sameBuild
                    ? $"Runs {GameBuild.Describe(status.build)}, you have {GameBuild.Describe(GameBuild.Version)}: open the launcher to update."
                    : entry.Status == ServerEntry.State.Full
                        ? $"Full ({status.players} of {status.max}). Try again in a little while."
                        : $"{entry.Listing.address} : {entry.Listing.port}";
            }
            Changed?.Invoke();
        }

        /// <summary>Hosts the download and brings probe results back to the game's thread.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private readonly Queue<Action> posted = new Queue<Action>();

            public void Post(Action action)
            {
                lock (posted) posted.Enqueue(action);
            }

            private void Update()
            {
                while (true)
                {
                    Action next;
                    lock (posted)
                    {
                        if (posted.Count == 0) return;
                        next = posted.Dequeue();
                    }
                    next();
                }
            }
        }
    }
}
