using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Who a joining player is (online phase 2, owner's pick of 18 Oct 2026: the game signs in to Unity by itself, no login
    /// screen, and the server checks Unity's proof). A game sends its Unity sign-in token with its join request; the
    /// server checks the token's signature against Unity's published keys (<see cref="UnityKeys"/>), that it has not
    /// expired, that Unity issued it and that it belongs to this game's project, and only then takes the player id in it.
    /// The account id every save is kept under is made from that id (<see cref="AccountIds"/>), so a game can no longer
    /// name the account it plays.
    /// </summary>
    public static class UnityToken
    {
        public const string Issuer = "https://player-auth.services.api.unity.com";
        /// <summary>Clocks drift: a token a minute past its time is still taken.</summary>
        public static readonly TimeSpan Skew = TimeSpan.FromMinutes(1);

        [Serializable] private sealed class Header { public string alg; public string kid; }
        [Serializable] private sealed class Claims { public string sub; public string iss; public long exp; public long nbf; public string project_id; }

        /// <summary>
        /// True and the Unity player id when <paramref name="token"/> is a valid Unity sign-in of this project; otherwise
        /// the reason, for the server's log (players read a plainer one).
        /// </summary>
        public static bool Verify(string token, IReadOnlyDictionary<string, RSAParameters> keys, string projectId, DateTime nowUtc,
            out string playerId, out string reason)
        {
            playerId = null;
            reason = null;
            if (string.IsNullOrEmpty(token)) { reason = "no sign-in token"; return false; }
            string[] parts = token.Split('.');
            if (parts.Length != 3) { reason = "not a token"; return false; }

            Header header;
            Claims claims;
            string claimsJson;
            try
            {
                header = JsonUtility.FromJson<Header>(Encoding.UTF8.GetString(FromBase64Url(parts[0])));
                claimsJson = Encoding.UTF8.GetString(FromBase64Url(parts[1]));
                claims = JsonUtility.FromJson<Claims>(claimsJson);
            }
            catch (Exception)
            {
                reason = "unreadable token";
                return false;
            }
            if (header == null || claims == null) { reason = "unreadable token"; return false; }
            if (header.alg != "RS256") { reason = "unexpected signature kind " + header.alg; return false; }
            if (keys == null || header.kid == null || !keys.TryGetValue(header.kid, out RSAParameters key)) { reason = "unknown signing key"; return false; }

            byte[] signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            byte[] signature;
            try { signature = FromBase64Url(parts[2]); }
            catch (Exception) { reason = "unreadable signature"; return false; }
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(key);
                if (!rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) { reason = "bad signature"; return false; }
            }

            DateTime expires = DateTimeOffset.FromUnixTimeSeconds(claims.exp).UtcDateTime;
            if (claims.exp <= 0 || nowUtc > expires + Skew) { reason = "expired"; return false; }
            if (claims.nbf > 0 && nowUtc + Skew < DateTimeOffset.FromUnixTimeSeconds(claims.nbf).UtcDateTime) { reason = "not valid yet"; return false; }
            if (claims.iss != Issuer) { reason = "issued by " + claims.iss; return false; }
            if (!string.IsNullOrEmpty(projectId) && !OfProject(claims, claimsJson, projectId)) { reason = "another project's token"; return false; }
            if (string.IsNullOrEmpty(claims.sub)) { reason = "no player in the token"; return false; }
            playerId = claims.sub;
            return true;
        }

        // The project is in project_id, or in the audience as "upid:<project>" (a string or a list).
        private static bool OfProject(Claims claims, string claimsJson, string projectId)
        {
            if (string.Equals(claims.project_id, projectId, StringComparison.OrdinalIgnoreCase)) return true;
            return claimsJson.IndexOf("\"upid:" + projectId + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static byte[] FromBase64Url(string text)
        {
            string s = text.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        public static string ToBase64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The account id a proven player plays under: the same 32 lowercase letters and digits every save key uses.</summary>
    public static class AccountIds
    {
        public static string FromUnityPlayer(string playerId)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("unity|" + (playerId ?? string.Empty)));
                var id = new StringBuilder(32);
                for (int i = 0; i < 16; i++) id.Append(hash[i].ToString("x2"));
                return id.ToString();
            }
        }
    }

    /// <summary>
    /// Unity's published keys for its sign-in tokens, fetched and kept by the server (they change now and then: a token
    /// signed with a key not yet known fetches them again, at most once a minute).
    /// </summary>
    public static class UnityKeys
    {
        public const string Address = "https://player-auth.services.api.unity.com/.well-known/jwks.json";
        private static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);
        private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
        private static readonly object Gate = new object();
        private static Dictionary<string, RSAParameters> keys = new Dictionary<string, RSAParameters>();
        private static DateTime fetchedUtc = DateTime.MinValue, triedUtc = DateTime.MinValue;
        private static Task<bool> fetching;

        [Serializable] private sealed class KeySet { public Key[] keys; }
        [Serializable] private sealed class Key { public string kid; public string kty; public string alg; public string n; public string e; }

        public static IReadOnlyDictionary<string, RSAParameters> Current { get { lock (Gate) return keys; } }

        public static bool Ready { get { lock (Gate) return keys.Count > 0; } }

        /// <summary>Fetches the keys when there are none, they are old, or <paramref name="kid"/> is not among them.</summary>
        public static Task<bool> EnsureAsync(string kid = null)
        {
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                bool missing = keys.Count == 0 || (kid != null && !keys.ContainsKey(kid));
                bool stale = now - fetchedUtc > RefreshEvery;
                if (!missing && !stale) return Task.FromResult(true);
                if (fetching != null && !fetching.IsCompleted) return fetching;
                if (now - triedUtc < RetryAfter && keys.Count > 0) return Task.FromResult(true);
                triedUtc = now;
                fetching = FetchAsync();
                return fetching;
            }
        }

        private static async Task<bool> FetchAsync()
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    string json = await http.GetStringAsync(Address).ConfigureAwait(false);
                    Dictionary<string, RSAParameters> read = Parse(json);
                    if (read.Count == 0) return false;
                    lock (Gate)
                    {
                        keys = read;
                        fetchedUtc = DateTime.UtcNow;
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Server] Unity's sign-in keys could not be fetched: " + e.Message);
                return false;
            }
        }

        public static Dictionary<string, RSAParameters> Parse(string json)
        {
            var read = new Dictionary<string, RSAParameters>();
            KeySet set = JsonUtility.FromJson<KeySet>(json);
            if (set?.keys == null) return read;
            foreach (Key key in set.keys)
            {
                if (key == null || key.kty != "RSA" || string.IsNullOrEmpty(key.kid) || string.IsNullOrEmpty(key.n) || string.IsNullOrEmpty(key.e)) continue;
                read[key.kid] = new RSAParameters { Modulus = UnityToken.FromBase64Url(key.n), Exponent = UnityToken.FromBase64Url(key.e) };
            }
            return read;
        }

        /// <summary>Tests: use these keys instead of Unity's.</summary>
        public static void UseForTests(Dictionary<string, RSAParameters> testKeys)
        {
            lock (Gate)
            {
                keys = testKeys ?? new Dictionary<string, RSAParameters>();
                fetchedUtc = DateTime.UtcNow;
            }
        }

        /// <summary>The key id a token was signed with, or null.</summary>
        public static string KeyIdOf(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            int dot = token.IndexOf('.');
            if (dot <= 0) return null;
            try
            {
                Match kid = Regex.Match(Encoding.UTF8.GetString(UnityToken.FromBase64Url(token.Substring(0, dot))), "\"kid\"\\s*:\\s*\"([^\"]+)\"");
                return kid.Success ? kid.Groups[1].Value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Server: the proven account of each connection, from its join request (and the made-up account id it played under
    /// before, for the one-time carry-over). Forgotten when the connection leaves.
    /// </summary>
    public static class ServerAccounts
    {
        public struct Entry
        {
            public string Account;
            public string OldAccount;
        }

        private static readonly Dictionary<ulong, Entry> Entries = new Dictionary<ulong, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            lock (Entries) Entries.Clear();
        }

        public static void Remember(ulong clientId, string account, string oldAccount)
        {
            lock (Entries) Entries[clientId] = new Entry { Account = account, OldAccount = oldAccount ?? string.Empty };
        }

        public static bool TryGet(ulong clientId, out Entry entry)
        {
            lock (Entries) return Entries.TryGetValue(clientId, out entry);
        }

        public static void Forget(ulong clientId)
        {
            lock (Entries) Entries.Remove(clientId);
        }
    }
}
