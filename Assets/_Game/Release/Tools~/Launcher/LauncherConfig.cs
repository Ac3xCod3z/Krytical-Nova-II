using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace KryticalNova.Release
{
    /// <summary>
    /// Where the launcher looks for updates. Built in: the <c>live</c> line on Unity's Cloud Content Delivery. A
    /// <c>launcher.json</c> beside the exe overrides it, so the owner's own launcher can follow <c>test</c>
    /// (<c>{"channel":"test"}</c>) or a test store on this PC. <c>{channel}</c> and <c>{path}</c> in the source are
    /// filled in for each request.
    /// </summary>
    [DataContract]
    public sealed class LauncherConfig
    {
        public const string FileName = "launcher.json";

        [DataMember(Order = 1, EmitDefaultValue = false)] public string channel;
        [DataMember(Order = 2, EmitDefaultValue = false)] public string source;

        public static LauncherConfig Load(string folder)
        {
            var config = new LauncherConfig();
            string file = Path.Combine(folder, FileName);
            if (File.Exists(file))
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                        config = (LauncherConfig)new DataContractJsonSerializer(typeof(LauncherConfig)).ReadObject(stream) ?? new LauncherConfig();
                }
                catch (Exception)
                {
                    config = new LauncherConfig(); // a broken file: the built-in line
                }
            }
            if (string.IsNullOrWhiteSpace(config.channel)) config.channel = "live";
            if (string.IsNullOrWhiteSpace(config.source)) config.source = BuiltIn("UpdateSource.txt");
            config.channel = config.channel.Trim().ToLowerInvariant();
            config.source = config.source.Trim();
            return config;
        }

        /// <summary>A text file built into the launcher (the update source, the public key).</summary>
        public static string BuiltIn(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return "";
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd().Trim();
            }
        }
    }
}
