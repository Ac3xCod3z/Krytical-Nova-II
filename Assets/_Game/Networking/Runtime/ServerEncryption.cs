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
        /// <summary>The name a server's certificate is made for, and the one every game checks (see <see cref="ServerCertificates"/>).</summary>
        public const string ServerName = ServerCertificates.ServerName;

        /// <summary>The certificate authority's public certificate (<see cref="ServerCertificates.AuthorityCertificate"/>).</summary>
        public const string AuthorityCertificate = ServerCertificates.AuthorityCertificate;

        /// <summary>The folder a server reads its certificate from when its settings name none: "certificates" beside its own folder.</summary>
        public static string DefaultFolder() => ServerCertificates.DefaultFolder(Path.GetDirectoryName(Application.dataPath));

#if UNITY_SERVER || UNITY_EDITOR // the server's settings exist only there: a game build has none
        /// <summary>Server: reads its certificate and key (settings, or the default folder). False with the reason when it cannot.</summary>
        public static bool TryLoadServerSecrets(ServerLaunchOptions options, out string certificate, out string privateKey, out string problem)
        {
            ServerCertificates.ResolvePaths(options.certificateFile, options.privateKeyFile, Path.GetDirectoryName(Application.dataPath), out string certificatePath, out string keyPath);
            return ServerCertificates.TryLoad(certificatePath, keyPath, out certificate, out privateKey, out problem);
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
