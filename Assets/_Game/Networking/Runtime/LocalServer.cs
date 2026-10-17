using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The owner's own server, started from the game (owner, 16 Oct 2026). When Enter World finds the server off and it
    /// is this PC's, the Servers window offers START MY SERVER: its Start Server.bat opens in its own window, minimized, the game
    /// waits until it answers and joins. Only where the server's folder is known: the Editor (the project's
    /// Builds/Server), or the owner's installed game through a <c>my-server.txt</c> beside it naming the folder; a
    /// brother's game has neither and never offers it. For now (until the server moves to the other PC) the server
    /// started this way stops with the game: quitting closes its window, which is its safe stop.
    /// </summary>
    public static class LocalServer
    {
        public const string PointerFile = "my-server.txt";
        private const string StartScript = "Start Server.bat";

        /// <summary>For now the server stops with the game that started it (owner); false once it lives on its own PC.</summary>
        public static bool StopWithGame = true;

        private static Process started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            started = null;
            Application.quitting -= StopIfStartedHere;
            Application.quitting += StopIfStartedHere;
        }

        /// <summary>The server's folder on this PC, or null where none is known.</summary>
        public static string Folder
        {
            get
            {
                string folder = null;
#if UNITY_EDITOR
                // An Editor test may point at a copy (Library/KryticalNova.MyServer, one line: the folder).
                string root = Path.GetDirectoryName(Application.dataPath);
                string pointer = Path.Combine(root, "Library", "KryticalNova.MyServer");
                folder = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : Path.Combine(root, "Builds", "Server", "Windows", "KryticalNovaServer");
#else
                string pointer = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", PointerFile);
                if (File.Exists(pointer)) folder = File.ReadAllText(pointer).Trim();
#endif
                return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, StartScript)) ? folder : null;
            }
        }

        /// <summary>The game port the server's own settings use (7777 when they say nothing).</summary>
        public static int Port
        {
            get
            {
                string folder = Folder;
                string settings = folder != null ? Path.Combine(folder, "server-settings.json") : null;
                if (settings == null || !File.Exists(settings)) return 7777;
                var read = JsonUtility.FromJson<PortOnly>(File.ReadAllText(settings));
                return read != null && read.port > 0 ? read.port : 7777;
            }
        }

        [Serializable]
        private sealed class PortOnly
        {
            public int port;
        }

        /// <summary>True when this is the owner's server on this PC and the game knows how to start it.</summary>
        public static bool CanStart(ServerListing listing) =>
            listing != null && Folder != null && listing.port == Port && (listing.OnThisMachine || IsThisPc(listing.address));

        /// <summary>True while a server this game started is still running.</summary>
        public static bool Running => started != null && !started.HasExited;

        public static bool IsThisPc(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            string trimmed = address.Trim();
            if (trimmed == "localhost") return true;
            if (!IPAddress.TryParse(trimmed, out IPAddress ip)) return false;
            if (IPAddress.IsLoopback(ip)) return true;
            try
            {
                foreach (NetworkInterface card in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (UnicastIPAddressInformation own in card.GetIPProperties().UnicastAddresses)
                        if (own.Address.Equals(ip)) return true;
            }
            catch (NetworkInformationException)
            {
                // the cards cannot be read: only the loopback counts
            }
            return false;
        }

        /// <summary>Opens Start Server.bat in its own window. False and a reason when it could not.</summary>
        public static bool Start(out string error)
        {
            error = null;
            if (Running) return true;
            string folder = Folder;
            if (folder == null) { error = "This PC does not know where your server is."; return false; }
            try
            {
                // --close: the window goes with the server instead of waiting for a key (the bat checks it).
                started = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + StartScript + "\" --close\"")
                {
                    WorkingDirectory = folder,
                    UseShellExecute = true,
                    // Minimized: it waits in the taskbar and never covers the game or the Editor (owner).
                    WindowStyle = ProcessWindowStyle.Minimized,
                });
                Debug.Log($"[Network] Started your server from {folder}.");
                return true;
            }
            catch (Exception e)
            {
                error = "Your server could not start: " + e.Message;
                return false;
            }
        }

        // The server's own Stop (its control page on this PC): everyone saved and a backup made before it goes. The page's
        // password is in the server's settings file, which only the owner's PC has.
        private static void StopIfStartedHere()
        {
            if (!StopWithGame || !Running) return;
            started = null;
            try
            {
                var settings = JsonUtility.FromJson<PageOnly>(File.ReadAllText(Path.Combine(Folder, "server-settings.json")));
                if (settings == null || settings.pagePort <= 0 || string.IsNullOrEmpty(settings.pagePassword)) return;
                string page = "http://localhost:" + settings.pagePort;
                var cookies = new CookieContainer();
                string login = Post(page, "/api/login", "{\"password\":" + Quote(settings.pagePassword) + "}", cookies, null);
                string check = JsonUtility.FromJson<CheckOnly>(login)?.check;
                Post(page, "/api/stop", "{\"seconds\":0,\"reason\":\"The game that started the server was closed.\",\"restart\":false}", cookies, check);
                Debug.Log("[Network] Your server was started by this game and stops with it (its safe stop).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Network] Your server could not be stopped with the game (" + e.Message + "); close its window to stop it.");
            }
        }

        private static string Post(string page, string path, string body, CookieContainer cookies, string check)
        {
            var request = (HttpWebRequest)WebRequest.Create(page + path);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.CookieContainer = cookies;
            request.Timeout = 3000;
            request.Headers["Origin"] = page;
            if (check != null) request.Headers["X-KN-Check"] = check;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(body);
            using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
        }

        private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        [Serializable]
        private sealed class PageOnly
        {
            public int pagePort;
            public string pagePassword;
        }

        [Serializable]
        private sealed class CheckOnly
        {
            public string check;
        }
    }
}
