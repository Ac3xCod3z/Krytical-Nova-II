using System;
using System.IO;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The plain parts of the server's encryption (online phase 2, milestone 2), shared by both transports: the name every
    /// server certificate is made for, the public certificate of our own certificate authority every game carries, and reading a
    /// server's certificate and key from disk. Netcode for GameObjects (<c>ServerEncryption</c>) and Netcode for Entities
    /// (<c>KnTransport</c>) both use it. The authority's PRIVATE key lives in the owner's secrets folder and never in the project.
    /// </summary>
    public static class ServerCertificates
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
        /// <param name="serverFolder">The folder of the server build (the parent of its Data folder).</param>
        public static string DefaultFolder(string serverFolder)
        {
            serverFolder = string.IsNullOrEmpty(serverFolder) ? "." : serverFolder;
            string parent = Path.GetDirectoryName(Path.GetFullPath(serverFolder)) ?? serverFolder;
            return Path.Combine(parent, "certificates");
        }

        /// <summary>The certificate and key paths a server uses: the ones its settings name, else server.crt and server.key in the default folder.</summary>
        public static void ResolvePaths(string certificateFile, string privateKeyFile, string serverFolder, out string certificatePath, out string keyPath)
        {
            string folder = DefaultFolder(serverFolder);
            certificatePath = string.IsNullOrWhiteSpace(certificateFile) ? Path.Combine(folder, "server.crt") : certificateFile;
            keyPath = string.IsNullOrWhiteSpace(privateKeyFile) ? Path.Combine(folder, "server.key") : privateKeyFile;
        }

        /// <summary>Reads the certificate and key. False with the reason (a sentence for the log, never the key text) when they cannot be read.</summary>
        public static bool TryLoad(string certificatePath, string keyPath, out string certificate, out string privateKey, out string problem)
        {
            certificate = privateKey = null;
            try
            {
                if (!File.Exists(certificatePath) || !File.Exists(keyPath))
                {
                    problem = $"no certificate and key at {certificatePath} and {keyPath}";
                    return false;
                }
                certificate = File.ReadAllText(certificatePath);
                privateKey = File.ReadAllText(keyPath);
                if (!certificate.Contains("-----BEGIN CERTIFICATE-----") || !privateKey.Contains("-----BEGIN"))
                {
                    certificate = privateKey = null;
                    problem = $"{certificatePath} or {keyPath} is not a PEM file";
                    return false;
                }
                problem = null;
                return true;
            }
            catch (Exception e)
            {
                certificate = privateKey = null;
                problem = $"the certificate could not be read: {e.Message}";
                return false;
            }
        }
    }
}
