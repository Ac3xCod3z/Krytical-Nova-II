using System;
using System.Text;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>One server players can join: the name they read, its address and its game port.</summary>
    [Serializable]
    public sealed class ServerListing
    {
        public string name = "";
        public string address = "";
        public int port = 7777;
        [Tooltip("Where the server answers \"how are you?\" (players, build). 0: the game port + 1.")]
        public int statusPort;

        public int StatusPort => statusPort > 0 ? statusPort : port + 1;
        public string Key => (address ?? "").Trim().ToLowerInvariant() + ":" + port;

        public ServerListing Copy() => new ServerListing { name = name, address = address, port = port, statusPort = statusPort };
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
            return reply.Length <= RequestLength ? reply : Encoding.UTF8.GetBytes(Answer + JsonUtility.ToJson(new Status { build = status.build, players = status.players, max = status.max }));
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
