using System;
using System.IO;
using KryticalNova.Core;
using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The release lane of the entity server (cutover, 12 Nov 2026): the DOTS server as the REAL server of this PC. Its build differs from
    /// the test lane's (<see cref="ServerBuildBuilder.EntityDefines"/>) in four ways, all stated here and tested without a build:
    /// no <c>KN_TEST_LANE</c> (so it may use 7777 / 8090 / the real save service, publishes the server list and does not hand every
    /// character the maximum level); the Sky Lift off (<see cref="SkyLiftLink.OffDefine"/>); real-lane first settings (port 7777, page 8090,
    /// the real save service 7801, DTLS, a list entry marked <c>entities</c>, no map servers, the real GMs carried over); and its own
    /// folder <c>Builds/Server/Windows</c> in the DOTS clone. The NGO server's folder is read for its certificate and GMs and never written.
    /// Secrets (the certificate's key, GM passwords, the page password) are copied or carried and never printed.
    /// </summary>
    public static class EntityReleaseServer
    {
        /// <summary>The release server's defines: the entity server and the Sky Lift off. Never <c>KN_TEST_LANE</c>.</summary>
        public static readonly string[] Defines = { "KN_ENTITIES_SERVER", SkyLiftLink.OffDefine };

        public const int GamePort = 7777, PagePort = 8090;
        public const string SaveService = "http://127.0.0.1:7801";
        public const string ListName = "Nova";

        /// <summary>The release server's folder: <c>Builds/Server/Windows/KryticalNovaServer</c> of this (DOTS) project, beside the app, save service and certificates.</summary>
        public static string OutputFolder => ServerBuildBuilder.OutputFolder(ServerBuildBuilder.Os.Windows);

        /// <summary>The release server's report, beside its folder.</summary>
        public static string ReportPath => ServerBuildBuilder.Root + "/BuildReport-EntityRelease.txt";

        /// <summary>
        /// The release server's first <c>server-settings.json</c>. <paramref name="ngoSettings"/> is the NGO server's file text, or null when
        /// it is not there: its GMs, name, player limit, public address, list bucket and call limits are carried; every port, the save service,
        /// the map servers and the server list are the release lane's own. No <c>plainTransport</c> key (DTLS on), no password.
        /// </summary>
        public static string SettingsJson(string ngoSettings)
        {
            var options = new ServerLaunchOptions();
            if (!string.IsNullOrWhiteSpace(ngoSettings))
            {
                try { JsonUtility.FromJsonOverwrite(ngoSettings, options); }
                catch (Exception) { options = new ServerLaunchOptions(); }
            }
            string listed = options.serverList != null && options.serverList.Length > 0 && options.serverList[0] != null
                && !string.IsNullOrWhiteSpace(options.serverList[0].name) ? options.serverList[0].name : ListName;
            options.port = GamePort;
            options.pagePort = PagePort;
            options.statusPort = PagePort;                       // the entity server answers the game's UDP status on its page port (KnServerEntry)
            options.saveService = SaveService;
            options.saveServiceKeyFile = string.Empty;           // the owner's secrets folder, as the NGO server
            options.pagePassword = string.Empty;                 // not carried: the entity page has no password of this file
            options.certificateFile = string.Empty;              // certificates/server.crt|key beside the server folder (copied by CopyCertificates)
            options.privateKeyFile = string.Empty;
            options.connectionMode = ServerLaunchOptions.Direct;
            options.map = string.IsNullOrWhiteSpace(options.map) ? "Zone01_StarterTown" : options.map;
            options.mapServers = new MapServerEntry[0];          // Transit Row left out (owner, D4)
            options.role = ServerLaunchOptions.MainRole;
            options.serverList = new[]
            {
                new ServerListing { name = listed, address = ServerListing.AutoAddress, port = GamePort, statusPort = PagePort, entities = true },
            };
            string json = JsonUtility.ToJson(options, true);
            // The keys the entity server and the app read that are not fields of the options: "netcode" marks it for the Server app.
            return SettingsKeepKeys.Merge("{\"" + ServerBuildBuilder.EntityNetcodeKey + "\": \"entities\"}", json);
        }

        /// <summary>
        /// Copies the NGO server's <c>certificates</c> folder (server.crt, server.key) to <paramref name="releaseCertificates"/>, as it is: build time
        /// only, never into the repository, nothing printed. False and a reason when there is none to copy.
        /// </summary>
        public static bool CopyCertificates(string ngoCertificates, string releaseCertificates, out string problem)
        {
            problem = null;
            if (!Directory.Exists(ngoCertificates)) { problem = "the NGO server has no certificates folder at " + ngoCertificates; return false; }
            foreach (string name in new[] { "server.crt", "server.key" })
                if (!File.Exists(Path.Combine(ngoCertificates, name))) { problem = "the NGO certificates folder lacks " + name; return false; }
            Directory.CreateDirectory(releaseCertificates);
            foreach (string name in new[] { "server.crt", "server.key" })
                File.Copy(Path.Combine(ngoCertificates, name), Path.Combine(releaseCertificates, name), true);
            return true;
        }

        /// <summary>The certificates beside the release server's folder (where <c>ServerCertificates</c> looks when the settings name none).</summary>
        public static string CertificatesFolder => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(OutputFolder)) ?? ".", "certificates");

        /// <summary>The NGO server's folder tree that is read at build time: <c>&lt;main&gt;/Builds/Server/Windows</c>.</summary>
        public static string NgoWindowsFolder =>
            Path.Combine(Environment.GetEnvironmentVariable("KN_NGO_PROJECT") ?? @"C:\Users\dlaco\Krytical Nova II", "Builds", "Server", "Windows");

        /// <summary>The NGO server's settings text, or null when it cannot be read. Never logged.</summary>
        public static string ReadNgoSettings()
        {
            try
            {
                string file = Path.Combine(NgoWindowsFolder, "KryticalNovaServer", "server-settings.json");
                return File.Exists(file) ? File.ReadAllText(file) : null;
            }
            catch (Exception) { return null; }
        }
    }
}
