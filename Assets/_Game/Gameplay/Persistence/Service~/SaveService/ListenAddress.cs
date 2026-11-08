// Where the save service may listen (Linux plan item 10): on this machine or a private network only, never where the
// internet can reach it. The firewall is not the only guard: a stray --urls, ASPNETCORE_URLS or DOTNET_URLS that names a
// public address, every address (0.0.0.0, +, *, [::]) or a host name is refused before the database is opened, and the
// service stops with the reason.

using System.Net;
using System.Net.Sockets;

namespace KryticalNova.SaveService
{
    public static class ListenAddress
    {
        /// <summary>Why the service must not listen on <paramref name="urls"/> (one or more, split by ';'), or null when every one is private.</summary>
        public static string? Refusal(string urls)
        {
            foreach (string url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // "+" and "*" (every address) are not hosts a Uri reads: refused before parsing.
                string host = url.Contains("://+") || url.Contains("://*") ? "*"
                    : Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.Host.Trim('[', ']') : "";
                if (!IsPrivateHost(host))
                    return $"The save service listens on this machine or a private network only, not on {url}. "
                           + "Check --urls and the ASPNETCORE_URLS / DOTNET_URLS settings.";
            }
            return null;
        }

        /// <summary>
        /// localhost, a loopback address, a private IPv4 (10/8, 172.16/12, 192.168/16) or a private IPv6 (fc00::/7). Every
        /// address (0.0.0.0, ::), a public address and a host name (it may name anything) are not.
        /// </summary>
        public static bool IsPrivateHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IPAddress.TryParse(host, out IPAddress? address)) return false;
            if (IPAddress.IsLoopback(address)) return true;
            byte[] b = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
            return address.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xFE) == 0xFC;
        }
    }
}
