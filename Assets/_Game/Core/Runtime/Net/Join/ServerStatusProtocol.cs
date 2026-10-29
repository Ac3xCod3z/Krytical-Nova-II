using System;
using System.Text;
using UnityEngine;

namespace KryticalNova.Networking
{
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
