using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KryticalNova.Publisher
{
    /// <summary>
    /// <c>publisher-settings.json</c> beside the exe (written with these defaults on the first start). No secrets: the R2 key
    /// stays in its own file in <c>Documents\KryticalNova-Secrets</c> and only its path is named here.
    /// </summary>
    [DataContract]
    public sealed class Settings
    {
        public const string FileName = "publisher-settings.json";

        /// <summary>"local" = the stand-in bucket on this PC (free, the default); "r2" = Cloudflare R2, only once the owner has set it up.</summary>
        [DataMember(Order = 1)] public string target = "local";
        [DataMember(Order = 2)] public string python = "python";
        /// <summary>Where Browse starts: the folder Publish to Test stages into.</summary>
        [DataMember(Order = 3)] public string storeFolder = "";

        // The local stand-in (fake_r2.py): a test port far from the game's 7777/7778, the save service's 7801/7802 and the page's 8090.
        [DataMember(Order = 10)] public int localPort = 7920;
        [DataMember(Order = 11)] public string localFolder = "";

        // Cloudflare R2: empty until the owner's hand-steps are done (Own-CDN owner guide). Nothing is sent there before.
        [DataMember(Order = 20)] public string r2Endpoint = "";
        [DataMember(Order = 21)] public string r2Bucket = "kn-game";
        [DataMember(Order = 22)] public string r2KeyFile = "";
        [DataMember(Order = 23)] public string r2PublicBase = "";

        public bool R2Ready => !string.IsNullOrWhiteSpace(r2Endpoint) && !string.IsNullOrWhiteSpace(r2Bucket) && File.Exists(r2KeyFile ?? "") && !string.IsNullOrWhiteSpace(r2PublicBase);
        public bool IsLocal => !string.Equals(target, "r2", StringComparison.OrdinalIgnoreCase);

        public string LocalEndpoint => "http://127.0.0.1:" + localPort;
        public string PublicBase => IsLocal ? LocalEndpoint + "/pub" : r2PublicBase.TrimEnd('/');

        public static Settings Load(string folder)
        {
            string file = Path.Combine(folder, FileName);
            Settings settings = null;
            if (File.Exists(file))
            {
                try { using (var s = File.OpenRead(file)) settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(s); }
                catch (Exception) { settings = null; }
            }
            if (settings == null)
            {
                settings = new Settings();
                settings.Save(folder);
            }
            // The app lives in <project>\Builds\Publisher; Publish to Test stages into <project>\Builds\Release\Store.
            if (string.IsNullOrWhiteSpace(settings.storeFolder))
                settings.storeFolder = Path.GetFullPath(Path.Combine(folder, "..", "Release", "Store"));
            if (string.IsNullOrWhiteSpace(settings.localFolder))
                settings.localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "KryticalNova-LocalCDN");
            return settings;
        }

        public void Save(string folder)
        {
            try
            {
                using (var memory = new MemoryStream())
                {
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(memory, Encoding.UTF8, false, true, "  "))
                        new DataContractJsonSerializer(typeof(Settings)).WriteObject(writer, this);
                    File.WriteAllBytes(Path.Combine(folder, FileName), memory.ToArray());
                }
            }
            catch (Exception)
            {
                // a read-only folder: run with the defaults
            }
        }
    }
}
