using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KryticalNova.Networking
{
    /// <summary>
    /// How a roster request and its answer travel (online phase 4, milestone 5). The request rides in the join message
    /// (<see cref="JoinRules.Payload"/>) and the answer comes back as the reason the server gives for not letting that
    /// connection in, so a question about characters needs no port, page or service of its own and goes wherever a join
    /// goes (directly, encrypted, or through Unity Relay). Both are packed small: a look is long, and a reason must fit
    /// one network message.
    /// </summary>
    public static class RosterWire
    {
        /// <summary>The start of a reason that is a roster answer, not a refusal.</summary>
        public const string ReplyTag = "KNR:";
        /// <summary>Longest request a server reads, packed (the join message holds the sign-in token too).</summary>
        public const int MaxRequestChars = 3000; // room for a sign-in carried along (RosterRequest.carry) beside the join's own
        /// <summary>Longest answer a server sends, packed: a reason is sent as two bytes a letter and must fit one message.</summary>
        public const int MaxReplyChars = 2600;

        /// <summary>Text, gzipped and written in base64 (no '|', which separates the join message's parts).</summary>
        public static string Pack(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text ?? string.Empty);
            using (var output = new MemoryStream())
            {
                using (var zip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true)) zip.Write(raw, 0, raw.Length);
                return Convert.ToBase64String(output.ToArray());
            }
        }

        /// <summary>The text back, or null when <paramref name="packed"/> is not packed text.</summary>
        public static string Unpack(string packed)
        {
            if (string.IsNullOrEmpty(packed)) return null;
            try
            {
                using (var input = new MemoryStream(Convert.FromBase64String(packed)))
                using (var zip = new GZipStream(input, CompressionMode.Decompress))
                using (var reader = new StreamReader(zip, Encoding.UTF8))
                {
                    // A packed request is small; a reader that keeps reading past this is not answered.
                    var buffer = new char[64 * 1024];
                    int read, total = 0;
                    var text = new StringBuilder();
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > 256 * 1024) return null;
                        text.Append(buffer, 0, read);
                    }
                    return text.ToString();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The reason a server gives with a roster answer.</summary>
        public static string Reply(string answer) => ReplyTag + Pack(answer);

        /// <summary>True when the reason is a roster answer; <paramref name="answer"/> is its text.</summary>
        public static bool TryReadReply(string reason, out string answer)
        {
            answer = null;
            if (string.IsNullOrEmpty(reason) || !reason.StartsWith(ReplyTag, StringComparison.Ordinal)) return false;
            answer = Unpack(reason.Substring(ReplyTag.Length));
            return answer != null;
        }
    }

    /// <summary>
    /// The roster on the server (online phase 4, milestone 5): a game's question about its characters (list, create,
    /// delete, rename), answered for the account its sign-in proved. Request and answer are JSON text the game's
    /// character code reads; Networking only carries them. (Moved to Core so the Netcode for Entities server can ask it too.)
    /// </summary>
    public interface IServerRoster
    {
        /// <summary>
        /// The answer to <paramref name="request"/> from <paramref name="account"/>. <paramref name="oldAccount"/> is the
        /// account the game made up before sign-in (its characters are carried over once, as on joining).
        /// </summary>
        string Answer(string account, string oldAccount, string request);
    }

    /// <summary>The one slot the roster hangs in (Persistence fills it; both servers ask it). Empty = this server keeps no characters.</summary>
    public static class RosterLookup
    {
        public static IServerRoster Current;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
