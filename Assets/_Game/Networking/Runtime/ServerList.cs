using System;
using System.Text;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>One server players can join: the name they read, its address and its game port.</summary>
    [Serializable]
    public sealed class ServerListing
    {
        /// <summary>The address the owner gives a list entry for the server that publishes the list (online step 5): it
        /// fills in its own public address, and its Relay code in Relay mode, each time it publishes.</summary>
        public const string AutoAddress = "auto";

        public string name = "";
        public string address = "";
        public int port = 7777;
        [Tooltip("Where the server answers \"how are you?\" (players, build). 0: the game port + 1.")]
        public int statusPort;
        [Tooltip("Set by a server in Relay mode: the join code games use to reach it through Unity Relay. Empty: joined directly.")]
        public string relay = "";
        [Tooltip("Set by the server for its own entry: which PC runs it (ThisMachine.Id), so that PC joins and starts it locally.")]
        public string machine = "";

        public int StatusPort => statusPort > 0 ? statusPort : port + 1;
        public bool IsAuto => string.Equals((address ?? "").Trim(), AutoAddress, StringComparison.OrdinalIgnoreCase);
        public bool ViaRelay => !string.IsNullOrEmpty(relay);
        /// <summary>True when this PC runs the server: it is asked and joined on this PC itself.</summary>
        public bool OnThisMachine => !string.IsNullOrEmpty(machine) && machine == ThisMachine.Id;
        /// <summary>Stays the same when the server's public address changes, so LAST PLAYED keeps pointing at it.</summary>
        public string Key => !string.IsNullOrEmpty(machine) ? "pc:" + machine + ":" + port : (address ?? "").Trim().ToLowerInvariant() + ":" + port;

        public ServerListing Copy() => new ServerListing { name = name, address = address, port = port, statusPort = statusPort, relay = relay, machine = machine };

        /// <summary>A Relay join code as Unity hands them out: a few letters and digits.</summary>
        public static bool IsRelayCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 12) return false;
            foreach (char c in code)
                if (!(c < 128 && char.IsLetterOrDigit(c))) return false;
            return true;
        }
    }

    /// <summary>
    /// This PC, as a short fingerprint of its name (the name itself is never published). A server puts it on its own
    /// list entry; a game on the same PC then asks and joins it on this PC itself, where the router's public address
    /// may not loop back.
    /// </summary>
    public static class ThisMachine
    {
        private static string id;

        public static string Id
        {
            get
            {
                if (id != null) return id;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("KryticalNova|" + Environment.MachineName.ToUpperInvariant()));
                    var hex = new StringBuilder(12);
                    for (int i = 0; i < 6; i++) hex.Append(hash[i].ToString("x2"));
                    id = hex.ToString();
                }
                return id;
            }
        }
    }

    /// <summary>
    /// The servers players see in the Servers window (online step 4). The owner keeps the list on the server's control
    /// page; the server uploads it as <c>servers.json</c> to its own Cloud Content Delivery bucket
    /// (<see cref="ListUrl"/>), separate from game builds, and every game reads it at character select. Players cannot
    /// add or remove servers (owner, 16 Oct 2026).
    /// </summary>
    [Serializable]
    public sealed class ServerListFile
    {
        public const string FileName = "servers.json";
        public const int MaxServers = 12;

        /// <summary>The newest release of the server-list bucket (public; its "latest" badge moves with every upload).</summary>
        public const string ListUrl = "https://f2ed0dbf-063b-4143-9e21-0f4bb3b3aa81.client-api.unity3dusercontent.com/client_api/v1/environments/411d9df6-0a7d-4179-9693-96bf92556b45/buckets/44b2f65a-ccbd-41be-beff-008f50178b18/release_by_badge/latest/entry_by_path/content/?path=servers.json";

        public int format = 1;
        public string updated = "";
        public ServerListing[] servers = new ServerListing[0];

        public static ServerListFile Parse(string json)
        {
            try
            {
                var list = JsonUtility.FromJson<ServerListFile>(json);
                if (list == null || list.servers == null) return null;
                return list;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Why a server cannot go on the list, or null.</summary>
        public static string Problem(string name, string address, int port)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Give the server a name.";
            if (name.Trim().Length > 32) return "A server name is at most 32 characters.";
            // Plain characters only: the game's labels would read < > as formatting.
            foreach (char c in name.Trim())
                if (!(char.IsLetterOrDigit(c) || " -_'().!&#".IndexOf(c) >= 0)) return "A server name holds letters, digits, spaces and - _ ' ( ) . ! & #.";
            if (string.IsNullOrWhiteSpace(address)) return "Give the server's address (numbers like 203.0.113.5, or a name like play.example.com).";
            string trimmed = address.Trim();
            if (trimmed.Length > 253) return "That address is too long.";
            foreach (char c in trimmed)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == ':')) return "An address holds only letters, digits, dots, dashes and colons.";
            if (port < 1 || port > 65534) return "A port is a number from 1 to 65534.";
            return null;
        }
    }

    /// <summary>
    /// A server's answer to "how are you?" on its status port, so the Servers window can show ONLINE, players and
    /// build before anyone joins. A request is as long as the answer can be, so the server never sends more than it
    /// receives (no use for flooding a third party).
    /// </summary>
    public static class ServerStatusProtocol
    {
        public const int RequestLength = 200;
        private const string Ask = "KN?status";
        private const string Answer = "KN!";

        [Serializable]
        public sealed class Status
        {
            public string name = "";
            public string build = "";
            public int players;
            public int max;
            public string relay = ""; // the Relay join code in Relay mode: newer than the published list's
        }

        public static byte[] Request()
        {
            var request = new byte[RequestLength];
            Encoding.ASCII.GetBytes(Ask, 0, Ask.Length, request, 0);
            return request;
        }

        public static bool IsRequest(byte[] data, int length) =>
            length >= RequestLength && length <= 512 && Encoding.ASCII.GetString(data, 0, Ask.Length) == Ask;

        /// <summary>The answer, never longer than a request.</summary>
        public static byte[] Reply(Status status)
        {
            byte[] reply = Encoding.UTF8.GetBytes(Answer + JsonUtility.ToJson(status));
            return reply.Length <= RequestLength ? reply : Encoding.UTF8.GetBytes(Answer + JsonUtility.ToJson(new Status { build = status.build, players = status.players, max = status.max, relay = status.relay }));
        }

        public static Status ReadReply(byte[] data, int length)
        {
            if (data == null || length <= Answer.Length || length > RequestLength) return null;
            string text = Encoding.UTF8.GetString(data, 0, length);
            if (!text.StartsWith(Answer, StringComparison.Ordinal)) return null;
            try
            {
                return JsonUtility.FromJson<Status>(text.Substring(Answer.Length));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
