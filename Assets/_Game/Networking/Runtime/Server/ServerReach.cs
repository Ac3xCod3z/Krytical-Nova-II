#if UNITY_SERVER
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// How players outside the house reach this server (online step 5, owner's picks of 16 Oct 2026). In **Direct**
    /// mode they come to this PC's public address and the forwarded ports; in **Relay** mode through Unity Relay with a
    /// join code (no router work; the owner's backup for when forwarding fails). Either way nobody types anything: a
    /// server-list entry whose address is <c>auto</c> (<see cref="ServerListing.AutoAddress"/>) is this server, and the
    /// server fills in its public address and Relay code each time it publishes. It looks its public address up at
    /// start and every ten minutes and publishes the list again by itself when the address or the code changed; a server
    /// whose settings name its <c>publicAddress</c> (a rented machine) uses that and asks no address service.
    /// Also the one place the list is uploaded from (the control page's PUBLISH LIST calls <see cref="Publish"/>).
    /// Only in a server build.
    /// </summary>
    public sealed class ServerReach : MonoBehaviour
    {
        private const float CheckEverySeconds = 600f;
        private const int LookupTimeoutMs = 8000;
        // Plain-text "your address is" services, asked in turn; nothing is sent but the request itself.
        private static readonly string[] AddressServices = { "https://api.ipify.org", "https://checkip.amazonaws.com" };
        private static readonly object PublishGate = new object();

        private static ServerReach instance;
        private ServerLaunchOptions options;
        private float nextCheck;
        private int busy;

        /// <summary>This PC's address on the internet, as the last look-up found it; empty when not known yet.</summary>
        public static string PublicAddress { get; private set; } = string.Empty;
        /// <summary>The Relay join code while the server is reachable through Relay; empty otherwise.</summary>
        public static string RelayCode { get; private set; } = string.Empty;
        /// <summary>How Relay is doing, for the control page (empty in Direct mode).</summary>
        public static string RelayNote { get; private set; } = string.Empty;
        /// <summary>What the server last did about the list by itself, for the control page.</summary>
        public static string AutoNote { get; private set; } = string.Empty;
        /// <summary>When the list was last uploaded (by the page or by itself).</summary>
        public static string LastPublished { get; private set; } = "never from this run";

        // What the newest upload carried for the "auto" entries.
        private static string publishedAddress, publishedRelay;

        public static void Begin(ServerLaunchOptions options)
        {
            if (instance != null) return;
            var host = new GameObject("Server Reach");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ServerReach>();
            instance.options = options;
            if (options.UsesRelay) RelayNote = "Asking Unity Relay for a place...";
            if (!HasAutoEntry(options))
                AutoNote = "No entry of the list has the address auto, so the server does not publish it by itself.";
        }

        /// <summary>Relay gave the server its place: players join with <paramref name="code"/> (published at once).</summary>
        public static void RelayReady(string code)
        {
            RelayCode = code ?? string.Empty;
            RelayNote = "Reachable through Unity Relay, join code " + RelayCode + ". Games read it from the server list.";
            if (instance != null) instance.nextCheck = 0f;
        }

        /// <summary>Relay could not be reached; the server tries again.</summary>
        public static void RelayFailed(string reason) =>
            RelayNote = "Unity Relay is not answering (" + reason + "). Trying again every 30 seconds.";

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            nextCheck = Time.unscaledTime + CheckEverySeconds;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { CheckAndPublish(); }
                finally { Interlocked.Exchange(ref busy, 0); }
            });
        }

        // On a worker thread: an upload takes several seconds and must not stop the game.
        private void CheckAndPublish()
        {
            // A rented machine names its address in the settings (publicAddress): nothing is asked of an address service.
            string found = !string.IsNullOrEmpty(options.publicAddress) ? options.publicAddress : LookUpPublicAddress();
            if (found != null)
            {
                if (found != PublicAddress) Debug.Log($"[Server] This PC's public address: {found}.");
                PublicAddress = found;
            }
            if (!HasAutoEntry(options)) return;
            if (options.UsesRelay && string.IsNullOrEmpty(RelayCode)) return; // the code comes first
            if (!options.UsesRelay && string.IsNullOrEmpty(PublicAddress))
            {
                AutoNote = "The public address could not be looked up, so the list was not published. Tries again in 10 minutes.";
                return;
            }
            if (PublicAddress == publishedAddress && RelayCode == publishedRelay) return;
            string answer = Publish(options, out bool ok);
            AutoNote = ok
                ? $"Published by itself at {DateTime.Now:HH:mm} ({(options.UsesRelay ? "Relay code " + RelayCode : "address " + PublicAddress)})."
                : answer + " Tries again in 10 minutes.";
            Debug.Log("[Server] Server list: " + AutoNote);
        }

        private static bool HasAutoEntry(ServerLaunchOptions options)
        {
            foreach (ServerListing entry in options.serverList ?? new ServerListing[0])
                if (entry != null && entry.IsAuto) return true;
            return false;
        }

        /// <summary>This PC's public IPv4 address from the first service that answers, or null.</summary>
        private static string LookUpPublicAddress()
        {
            foreach (string service in AddressServices)
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(service);
                    request.Timeout = LookupTimeoutMs;
                    request.ReadWriteTimeout = LookupTimeoutMs;
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        string text = reader.ReadToEnd().Trim();
                        if (text.Length <= 45 && IPAddress.TryParse(text, out IPAddress ip) && ip.AddressFamily == AddressFamily.InterNetwork
                            && !ControlPageRules.IsHomeNetwork(ip))
                            return ip.ToString();
                    }
                }
                catch (Exception)
                {
                    // the next service, or none this time
                }
            }
            return null;
        }

        /// <summary>
        /// Uploads servers.json to the list's own bucket as a new release (its "latest" badge is what every game reads),
        /// with the "auto" entries filled in. Runs Unity's command-line tool; call it off the game's thread.
        /// </summary>
        public static string Publish(ServerLaunchOptions options, out bool ok)
        {
            ok = false;
            lock (PublishGate)
            {
                string address = PublicAddress, relay = options.UsesRelay ? RelayCode : string.Empty;
                ServerListing[] source = options.serverList ?? new ServerListing[0];
                var list = new ServerListing[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    list[i] = source[i].Copy();
                    if (!source[i].IsAuto) continue;
                    // This server: players' games join its public address, or its Relay code in Relay mode.
                    list[i].address = !string.IsNullOrEmpty(address) ? address : ServerListing.AutoAddress;
                    list[i].port = options.port;
                    list[i].statusPort = options.statusPort;
                    list[i].relay = relay;
                    list[i].machine = ThisMachine.Id;
                }
                string folder = Path.Combine(options.ServerFolder, "ServerList");
                try
                {
                    Directory.CreateDirectory(folder);
                    var file = new ServerListFile { updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), servers = list };
                    File.WriteAllText(Path.Combine(folder, ServerListFile.FileName), JsonUtility.ToJson(file, true));
                }
                catch (Exception e)
                {
                    return "The list could not be written: " + e.Message;
                }
                string ugs = options.UgsPath;
                if (!File.Exists(ugs)) return $"Unity's command-line tool is not on this PC ({ugs}). The Server Guide says how to put it there and log it in once.";
                string arguments = $"ccd entries sync \"{folder}\" -b {options.listBucket} -x -r -n \"server list\" -e {options.cloudEnvironment} -p {options.cloudProjectId}";
                try
                {
                    var info = new System.Diagnostics.ProcessStartInfo(ugs, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var process = System.Diagnostics.Process.Start(info))
                    {
                        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                        if (!process.WaitForExit(120000)) { process.Kill(); return "The upload took too long and was stopped. Try again."; }
                        if (process.ExitCode != 0)
                        {
                            Debug.LogWarning("[Server] Publishing the server list failed:\n" + output);
                            return output.Contains("login") || output.Contains("credential")
                                ? "The upload was refused: log Unity's tool in on this PC once (Server Guide, Publish the server list)."
                                : "The upload failed; the server's log has the details.";
                        }
                    }
                }
                catch (Exception e)
                {
                    return "The upload could not start: " + e.Message;
                }
                publishedAddress = address;
                publishedRelay = relay;
                LastPublished = DateTime.Now.ToString("d MMM HH:mm");
                ok = true;
                Debug.Log($"[Server] Server list published ({list.Length} server(s)).");
                return $"Published {list.Length} server(s). Games see the list within a few minutes.";
            }
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
#endif
