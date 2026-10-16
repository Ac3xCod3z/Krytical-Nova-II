#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The rules of the server's control page, apart from its socket so they can be tested: who may reach it (the home
    /// network only), how a request is read (small, plain HTTP/1.1), the lock after wrong passwords, and the checks
    /// that a button press came from the page itself and not from another site. Only in a server build (and the Editor).
    /// </summary>
    public static class ControlPageRules
    {
        public const int MaxHeaderBytes = 16 * 1024;
        public const int MaxBodyBytes = 16 * 1024;
        public const int WrongTriesBeforeLock = 5;
        public static readonly TimeSpan LockTime = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan SessionTime = TimeSpan.FromHours(12);

        /// <summary>
        /// True for this PC and the home network: loopback, the private IPv4 ranges (10/8, 172.16/12, 192.168/16),
        /// link-local, and IPv6 loopback, link-local and unique-local. Anything from the internet is refused.
        /// </summary>
        public static bool IsHomeNetwork(IPAddress address)
        {
            if (address == null) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return true;
            byte[] b = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return address.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC; // fc00::/7
            return false;
        }

        /// <summary>One request as read from the socket.</summary>
        public sealed class Request
        {
            public string Method, Path, Query;
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Body = string.Empty;

            public string Header(string name) => Headers.TryGetValue(name, out string value) ? value : null;

            public string Cookie(string name)
            {
                string all = Header("Cookie");
                if (string.IsNullOrEmpty(all)) return null;
                foreach (string part in all.Split(';'))
                {
                    int equals = part.IndexOf('=');
                    if (equals > 0 && part.Substring(0, equals).Trim() == name) return part.Substring(equals + 1).Trim();
                }
                return null;
            }
        }

        /// <summary>Reads one HTTP/1.1 request, or null when it is malformed or too large.</summary>
        public static Request Read(Stream stream)
        {
            var head = new MemoryStream();
            int matched = 0;
            while (head.Length < MaxHeaderBytes)
            {
                int b = stream.ReadByte();
                if (b < 0) return null;
                head.WriteByte((byte)b);
                matched = b == (matched % 2 == 0 ? '\r' : '\n') ? matched + 1 : (b == '\r' ? 1 : 0);
                if (matched == 4) break;
            }
            if (matched != 4) return null;
            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
            var request = new Request { Method = first[0].ToUpperInvariant() };
            string target = first[1];
            int question = target.IndexOf('?');
            request.Path = question >= 0 ? target.Substring(0, question) : target;
            request.Query = question >= 0 ? target.Substring(question + 1) : string.Empty;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) request.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            if (int.TryParse(request.Header("Content-Length"), out int length) && length > 0)
            {
                if (length > MaxBodyBytes) return null;
                var body = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = stream.Read(body, read, length - read);
                    if (n <= 0) return null;
                    read += n;
                }
                request.Body = Encoding.UTF8.GetString(body);
            }
            return request;
        }

        /// <summary>
        /// The Host header names this machine as seen from the home network: an IP address, localhost or a bare
        /// machine name. A site's name there means a web page tricked the browser into calling us (DNS rebinding).
        /// </summary>
        public static bool IsAcceptableHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string name = host;
            if (name.StartsWith("[", StringComparison.Ordinal))
            {
                int close = name.IndexOf(']');
                return close > 0 && IPAddress.TryParse(name.Substring(1, close - 1), out _);
            }
            int colon = name.LastIndexOf(':');
            if (colon > 0) name = name.Substring(0, colon);
            if (IPAddress.TryParse(name, out _)) return true;
            if (string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            // A bare machine name (no dots), as Windows shares them on a home network.
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '-')) return false;
            return name.Length > 0;
        }

        /// <summary>A button press may only come from the page itself: no Origin, or the same host the request went to.</summary>
        public static bool IsSameOrigin(Request request)
        {
            string origin = request.Header("Origin");
            if (string.IsNullOrEmpty(origin)) return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri uri)) return false;
            string host = request.Header("Host") ?? string.Empty;
            return string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Compares two secrets in a time that does not depend on where they differ.</summary>
        public static bool SecretEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            byte[] x = Encoding.UTF8.GetBytes(a), y = Encoding.UTF8.GetBytes(b);
            int diff = x.Length ^ y.Length;
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
                diff |= (i < x.Length ? x[i] : 0) ^ (i < y.Length ? y[i] : 0);
            return diff == 0;
        }

        /// <summary>A random token for a session or a request check.</summary>
        public static string NewToken(int bytes = 24)
        {
            var data = new byte[bytes];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(data);
            var sb = new StringBuilder(bytes * 2);
            foreach (byte b in data) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>A password that is easy to read out and type: three short words of letters and a number.</summary>
        public static string NewPassword()
        {
            const string letters = "abcdefghjkmnpqrstuvwxyz";
            var data = new byte[12];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(data);
            var sb = new StringBuilder();
            for (int i = 0; i < 9; i++)
            {
                if (i > 0 && i % 3 == 0) sb.Append('-');
                sb.Append(letters[data[i] % letters.Length]);
            }
            sb.Append('-').Append(10 + data[9] % 90);
            return sb.ToString();
        }

        /// <summary>Wrong passwords per address, and the lock after too many. Thread-safe.</summary>
        public sealed class Lockout
        {
            private readonly Dictionary<string, (int Wrong, DateTime LockedUntil)> byAddress = new Dictionary<string, (int, DateTime)>();
            private readonly object gate = new object();

            /// <summary>True while the address is locked out; <paramref name="left"/> is how long for.</summary>
            public bool IsLocked(string address, DateTime now, out TimeSpan left)
            {
                lock (gate)
                {
                    left = TimeSpan.Zero;
                    if (!byAddress.TryGetValue(address, out var entry) || entry.LockedUntil <= now) return false;
                    left = entry.LockedUntil - now;
                    return true;
                }
            }

            /// <summary>Counts a wrong password; returns the tries left before the lock (0 when it just locked).</summary>
            public int Wrong(string address, DateTime now)
            {
                lock (gate)
                {
                    byAddress.TryGetValue(address, out var entry);
                    int wrong = entry.LockedUntil > now ? entry.Wrong : (entry.Wrong >= WrongTriesBeforeLock ? 0 : entry.Wrong);
                    wrong++;
                    DateTime until = wrong >= WrongTriesBeforeLock ? now + LockTime : DateTime.MinValue;
                    byAddress[address] = (wrong >= WrongTriesBeforeLock ? WrongTriesBeforeLock : wrong, until);
                    return Math.Max(0, WrongTriesBeforeLock - wrong);
                }
            }

            public void Right(string address)
            {
                lock (gate) byAddress.Remove(address);
            }
        }

        /// <summary>Escapes text for a JSON string.</summary>
        public static string Json(string text)
        {
            if (text == null) return "null";
            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '<' || c == '>' || c == '&') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
#endif
