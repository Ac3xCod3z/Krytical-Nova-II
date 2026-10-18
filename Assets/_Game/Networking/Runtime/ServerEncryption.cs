using System;
using System.IO;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Direct connections to a dedicated server are encrypted (online phase 2, milestone 2); Relay connections already
    /// are. The server proves itself with a certificate our own certificate authority signed and keeps the matching key
    /// secret beside it; every game carries the authority's public certificate (below) and accepts only a server whose
    /// certificate it signed, under the name <see cref="ServerName"/>. The authority's key lives in the owner's
    /// Documents/KryticalNova-Secrets, never in the project; Networking/Tools~/certificates.sh makes a server's
    /// certificate from it. An Editor hosting its own test session stays unencrypted.
    /// </summary>
    public static class ServerEncryption
    {
        /// <summary>The name a server's certificate is made for, and the one every game checks.</summary>
        public const string ServerName = "krytical-nova-server";

        /// <summary>The certificate authority's public certificate (made 18 Oct 2026, valid 20 years).</summary>
        public const string AuthorityCertificate =
            "-----BEGIN CERTIFICATE-----\n" +
            "MIIDTTCCAjWgAwIBAgIUSf8HV8JTjVPmZ8Ah3Kie31Z3h0swDQYJKoZIhvcNAQEL\n" +
            "BQAwLjEsMCoGA1UEAwwjS3J5dGljYWwgTm92YSBjZXJ0aWZpY2F0ZSBhdXRob3Jp\n" +
            "dHkwHhcNMjYxMDE4MDEwNDU1WhcNNDYxMDEzMDEwNDU1WjAuMSwwKgYDVQQDDCNL\n" +
            "cnl0aWNhbCBOb3ZhIGNlcnRpZmljYXRlIGF1dGhvcml0eTCCASIwDQYJKoZIhvcN\n" +
            "AQEBBQADggEPADCCAQoCggEBAMoackhZEwCO+3yzSimsf9n8OdPto1YrJOTOHgkY\n" +
            "Q1ymLBYc4Zkv5dYsHiKCpG8QlMOzn2z5yQ5rWx5nBPrxn8a9r2/6v1RUaZ2AtJVR\n" +
            "qsSr+YbUsdHzmfSzrbu/XoUT6g0rWVJ5gMp3SHlecWGjfT0d4aWq0SIoAHAHHRjY\n" +
            "Id1HEkYqs/CMMQn8KCorRvYBwfz1v6ySwTyn8MsQJB9g+4sZviyQRe7GlXWfYOJI\n" +
            "gxPsXhDygQAkUWw6w8KpLMk6AB2qo0gfws18JRLkYRjHZAG5r8Xkr4GrnpvEReM0\n" +
            "Qn1zmD8KOEWYK9alHpwzIBu/90gR+NvxXMknSrOtDulMJ98CAwEAAaNjMGEwHQYD\n" +
            "VR0OBBYEFChv4CM3mgpfwZEey19+fLsw4tDYMB8GA1UdIwQYMBaAFChv4CM3mgpf\n" +
            "wZEey19+fLsw4tDYMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMA0G\n" +
            "CSqGSIb3DQEBCwUAA4IBAQCNZccaQ4jWapS/CA8YRTU3EDrPj+T/E1ihbMDLjbJ2\n" +
            "0hp8AFu4GC84XMkvKxUXibobZtwjh5Q5RvFlFYQR5lTawO8mIxGFoDpA5u830oQ7\n" +
            "NDMwpxJkglzvfjG0PCSXSvoPCYLk4O8uzTqtKMMvf0WETf4EywKre0xZy9gCPQ0j\n" +
            "xMFBADpNJYAtwW7bxURCVJ0KsyzmwOOK8/Q+97WGZZxX6T5p38OJSBGwatKfmPfs\n" +
            "Eyhem3X0Sx67bZsRPcpdgdoKmYoCujBAvfrjx6YwWGzsoAV3FbY62IrdnmJCC7YJ\n" +
            "Z60oZLdgMDoo0LbMZ5gLOcl3jBQCShyPgDyn2tiLPSZm\n" +
            "-----END CERTIFICATE-----\n";

        /// <summary>The folder a server reads its certificate from when its settings name none: "certificates" beside its own folder.</summary>
        public static string DefaultFolder()
        {
            string serverFolder = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string parent = Path.GetDirectoryName(Path.GetFullPath(serverFolder)) ?? serverFolder;
            return Path.Combine(parent, "certificates");
        }

#if UNITY_SERVER || UNITY_EDITOR // the server's settings exist only there: a game build has none
        /// <summary>Server: reads its certificate and key (settings, or the default folder). False with the reason when it cannot.</summary>
        public static bool TryLoadServerSecrets(ServerLaunchOptions options, out string certificate, out string privateKey, out string problem)
        {
            certificate = privateKey = null;
            string folder = DefaultFolder();
            string certificatePath = string.IsNullOrWhiteSpace(options.certificateFile) ? Path.Combine(folder, "server.crt") : options.certificateFile;
            string keyPath = string.IsNullOrWhiteSpace(options.privateKeyFile) ? Path.Combine(folder, "server.key") : options.privateKeyFile;
            try
            {
                if (!File.Exists(certificatePath) || !File.Exists(keyPath))
                {
                    problem = $"no certificate and key at {certificatePath} and {keyPath}";
                    return false;
                }
                certificate = File.ReadAllText(certificatePath);
                privateKey = File.ReadAllText(keyPath);
                problem = null;
                return true;
            }
            catch (Exception e)
            {
                problem = $"the certificate could not be read: {e.Message}";
                return false;
            }
        }
#endif

        /// <summary>A dedicated server: its direct connections are encrypted with its certificate and key.</summary>
        public static void UseOnServer(UnityTransport transport, string certificate, string privateKey)
        {
            transport.UseEncryption = true;
            transport.SetServerSecrets(certificate, privateKey);
        }

        /// <summary>A game joining a dedicated server directly: it accepts only a server our authority signed.</summary>
        public static void UseOnClient(UnityTransport transport)
        {
            transport.UseEncryption = true;
            transport.SetClientSecrets(ServerName, AuthorityCertificate);
        }

        /// <summary>No encryption (an Editor hosting its own test, or Relay, which encrypts by itself).</summary>
        public static void Off(UnityTransport transport) => transport.UseEncryption = false;
    }
}
