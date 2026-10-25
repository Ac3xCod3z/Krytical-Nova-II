using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace KryticalNova.Release
{
    /// <summary>
    /// The update list: every file of one game build with its size and SHA-256, and the launcher of that build. It is
    /// published as <c>manifest.signed</c>: an ECDSA P-256 signature of the list's exact bytes (made with the owner's
    /// key) on the first line, the list after it. One file, because the content network caches each address for a few
    /// minutes on its own: a list and a signature kept apart could arrive from different builds. A launcher reads
    /// nothing from a list whose signature does not check out against the public key built into it. Each file is stored once, compressed, under its own hash (<see cref="PackedPath"/>), so a file
    /// that did not change between builds is neither uploaded nor downloaded again.
    /// </summary>
    [DataContract]
    public sealed class UpdateManifest
    {
        public const string FileName = "manifest.json";
        public const string SignedName = "manifest.signed";
        public const int CurrentFormat = 1;

        [DataMember(Order = 1)] public int format = CurrentFormat;
        [DataMember(Order = 2)] public int build;
        [DataMember(Order = 3)] public string version = "";
        [DataMember(Order = 4)] public string created = "";
        [DataMember(Order = 5)] public string gameExe = "";
        [DataMember(Order = 6)] public List<UpdateFile> files = new List<UpdateFile>();
        [DataMember(Order = 7)] public UpdateFile launcher;
        /// <summary>
        /// The launcher's news (owner, 18 Oct 2026): the most exciting changes since the last LIVE release, at most
        /// <see cref="LauncherNews.MostItems"/>. Inside the signed list, so nobody can put news on a player's launcher; a
        /// launcher from before it skips the field.
        /// </summary>
        [DataMember(Order = 8, EmitDefaultValue = false)] public List<NewsItem> news;
        /// <summary>The news of earlier releases, newest first, for the launcher's PAST NEWS (owner, 18 Oct 2026).</summary>
        [DataMember(Order = 9, EmitDefaultValue = false)] public List<NewsRelease> newsHistory;
        /// <summary>
        /// The whole breakdown of the release (owner, 22 Oct 2026): every change, in plain words, under a few headings,
        /// where the news keeps to the highlights. Signed with the rest; a launcher from before it skips the field.
        /// </summary>
        [DataMember(Order = 10, EmitDefaultValue = false)] public List<PatchNotesSection> patchNotes;

        /// <summary>Where a file's compressed copy is kept, by its hash.</summary>
        public static string PackedPath(string sha256) => "files/" + sha256 + ".gz";

        public byte[] ToBytes()
        {
            var serializer = new DataContractJsonSerializer(typeof(UpdateManifest));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return stream.ToArray();
            }
        }

        /// <summary>The signature line, then the list.</summary>
        public static byte[] Sign(byte[] manifest, string signature)
        {
            byte[] head = Encoding.ASCII.GetBytes(signature.Trim() + "\n");
            var all = new byte[head.Length + manifest.Length];
            Buffer.BlockCopy(head, 0, all, 0, head.Length);
            Buffer.BlockCopy(manifest, 0, all, head.Length, manifest.Length);
            return all;
        }

        /// <summary>Splits a signed list into its signature and the exact bytes that were signed.</summary>
        public static bool Split(byte[] signed, out string signature, out byte[] manifest)
        {
            signature = null;
            manifest = null;
            int newline = Array.IndexOf(signed, (byte)'\n');
            if (newline <= 0 || newline > 200) return false;
            signature = Encoding.ASCII.GetString(signed, 0, newline).Trim();
            manifest = new byte[signed.Length - newline - 1];
            Buffer.BlockCopy(signed, newline + 1, manifest, 0, manifest.Length);
            return true;
        }

        public static UpdateManifest FromBytes(byte[] bytes)
        {
            var serializer = new DataContractJsonSerializer(typeof(UpdateManifest));
            using (var stream = new MemoryStream(bytes))
            {
                var manifest = (UpdateManifest)serializer.ReadObject(stream);
                if (manifest.files == null) manifest.files = new List<UpdateFile>();
                return manifest;
            }
        }
    }

    [DataContract]
    public sealed class UpdateFile
    {
        /// <summary>Relative to the game's folder, with forward slashes. For the launcher: its file name.</summary>
        [DataMember(Order = 1)] public string path = "";
        [DataMember(Order = 2)] public long size;
        [DataMember(Order = 3)] public string sha256 = "";
        /// <summary>The size of the compressed copy that is downloaded.</summary>
        [DataMember(Order = 4)] public long packedSize;
    }

    /// <summary>One headline on the launcher: a short title, and what changed, shown when the title is clicked.</summary>
    [DataContract]
    public sealed class NewsItem
    {
        [DataMember(Order = 1)] public string title = "";
        [DataMember(Order = 2)] public string body = "";
    }

    /// <summary>The news one earlier release went out with: its label ("v0.1.0 build 8 · 18 Oct 2026") and headlines.</summary>
    [DataContract]
    public sealed class NewsRelease
    {
        [DataMember(Order = 1)] public string label = "";
        [DataMember(Order = 2)] public List<NewsItem> items = new List<NewsItem>();
        /// <summary>Its patch notes, when it had them.</summary>
        [DataMember(Order = 3, EmitDefaultValue = false)] public List<PatchNotesSection> patchNotes;
    }

    /// <summary>One heading of the patch notes ("New", "Changed", "Fixed"...) and its lines, one change each.</summary>
    [DataContract]
    public sealed class PatchNotesSection
    {
        [DataMember(Order = 1)] public string heading = "";
        [DataMember(Order = 2)] public List<string> lines = new List<string>();
    }

    /// <summary>
    /// The news file the owner's release carries (<c>Release/Tools~/launcher-news.json</c>): written before a release
    /// meant for LIVE, from everything that changed since the last one, and checked here so the launcher can always lay
    /// it out. When new news is written, the news before it moves to the top of <see cref="history"/>, labelled with the
    /// release it went out in, so players can look back.
    /// </summary>
    [DataContract]
    public sealed class LauncherNews
    {
        public const int MostItems = 7;
        public const int LongestTitle = 32;
        public const int LongestBody = 300;
        public const int MostReleasesKept = 24;
        public const int MostSections = 16;
        public const int MostLines = 200;
        public const int LongestHeading = 40;
        public const int LongestLine = 400;

        [DataMember(Order = 1)] public List<NewsItem> items = new List<NewsItem>();
        [DataMember(Order = 2)] public List<NewsRelease> history = new List<NewsRelease>();
        /// <summary>This release's patch notes: the whole breakdown, where <see cref="items"/> are the highlights.</summary>
        [DataMember(Order = 3, EmitDefaultValue = false)] public List<PatchNotesSection> patchNotes;

        public static LauncherNews FromFile(string file)
        {
            var serializer = new DataContractJsonSerializer(typeof(LauncherNews));
            using (var stream = File.OpenRead(file))
            {
                var news = (LauncherNews)serializer.ReadObject(stream);
                if (news.items == null) news.items = new List<NewsItem>();
                if (news.history == null) news.history = new List<NewsRelease>();
                if (news.patchNotes == null) news.patchNotes = new List<PatchNotesSection>();
                return news;
            }
        }

        /// <summary>Why the news cannot go out as written, or null.</summary>
        public string Problem()
        {
            string problem = Problem(items, "News");
            if (problem != null) return problem;
            problem = Problem(patchNotes, "The patch notes");
            if (problem != null) return problem;
            if (history.Count > MostReleasesKept) return $"The news history keeps {MostReleasesKept} releases at most; drop the oldest.";
            for (int i = 0; i < history.Count; i++)
            {
                NewsRelease release = history[i];
                if (release == null || string.IsNullOrWhiteSpace(release.label)) return $"Past news {i + 1} has no label.";
                problem = Problem(release.items ?? new List<NewsItem>(), "Past news \"" + release.label + "\"");
                if (problem != null) return problem;
                problem = Problem(release.patchNotes, "The patch notes of \"" + release.label + "\"");
                if (problem != null) return problem;
            }
            return null;
        }

        private static string Problem(List<NewsItem> list, string what)
        {
            if (list.Count > MostItems) return $"{what} has {list.Count} items; the launcher shows {MostItems} at most.";
            for (int i = 0; i < list.Count; i++)
            {
                NewsItem item = list[i];
                if (item == null || string.IsNullOrWhiteSpace(item.title)) return $"{what}, item {i + 1} has no title.";
                if (item.title.Trim().Length > LongestTitle) return $"{what}, item {i + 1}'s title is longer than {LongestTitle} characters.";
                if (string.IsNullOrWhiteSpace(item.body)) return $"{what}, item {i + 1} has nothing to say when it is opened.";
                if (item.body.Trim().Length > LongestBody) return $"{what}, item {i + 1}'s text is longer than {LongestBody} characters.";
            }
            return null;
        }

        private static string Problem(List<PatchNotesSection> sections, string what)
        {
            if (sections == null || sections.Count == 0) return null;
            if (sections.Count > MostSections) return $"{what} have {sections.Count} headings; {MostSections} at most.";
            int lines = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                PatchNotesSection section = sections[i];
                if (section == null || string.IsNullOrWhiteSpace(section.heading)) return $"{what}, heading {i + 1} is empty.";
                if (section.heading.Trim().Length > LongestHeading) return $"{what}, heading {i + 1} is longer than {LongestHeading} characters.";
                if (section.lines == null || section.lines.Count == 0) return $"{what}, \"{section.heading.Trim()}\" has no lines.";
                foreach (string line in section.lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) return $"{what}, \"{section.heading.Trim()}\" has an empty line.";
                    if (line.Trim().Length > LongestLine) return $"{what}, a line under \"{section.heading.Trim()}\" is longer than {LongestLine} characters.";
                }
                lines += section.lines.Count;
            }
            return lines > MostLines ? $"{what} have {lines} lines; {MostLines} at most." : null;
        }

        /// <summary>The patch notes, trimmed, for the signed list; null when there are none.</summary>
        public static List<PatchNotesSection> Clean(List<PatchNotesSection> sections)
        {
            if (sections == null || sections.Count == 0) return null;
            var clean = new List<PatchNotesSection>();
            foreach (PatchNotesSection section in sections)
                clean.Add(new PatchNotesSection { heading = section.heading.Trim(), lines = section.lines.ConvertAll(l => l.Trim()) });
            return clean;
        }

        /// <summary>The same items, trimmed, for the signed list.</summary>
        public static List<NewsItem> Clean(List<NewsItem> list)
        {
            var clean = new List<NewsItem>();
            foreach (NewsItem item in list ?? new List<NewsItem>()) clean.Add(new NewsItem { title = item.title.Trim(), body = item.body.Trim() });
            return clean;
        }
    }

    /// <summary>Hashes, signatures and paths shared by the launcher and the release tool.</summary>
    public static class UpdateCrypto
    {
        public static string Sha256Hex(Stream stream)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream));
        }

        public static string Sha256Hex(string file)
        {
            using (var stream = File.OpenRead(file)) return Sha256Hex(stream);
        }

        public static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        /// <summary>A public key as base64 of its X and Y (32 bytes each), the form built into the launcher.</summary>
        public static ECDsa PublicKey(string base64)
        {
            byte[] q = Convert.FromBase64String(base64);
            if (q.Length != 64) throw new ArgumentException("A public key is 64 bytes.");
            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(q, 0, x, 0, 32);
            Buffer.BlockCopy(q, 32, y, 0, 32);
            return ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
        }

        /// <summary>True when <paramref name="signature"/> (base64) is the key's signature of these exact bytes.</summary>
        public static bool Verify(string publicKeyBase64, byte[] data, string signature)
        {
            if (string.IsNullOrWhiteSpace(publicKeyBase64) || string.IsNullOrWhiteSpace(signature)) return false;
            try
            {
                using (ECDsa key = PublicKey(publicKeyBase64))
                    return key.VerifyData(data, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>A relative path that stays inside the folder it is joined to, or null.</summary>
        public static string SafeJoin(string folder, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains("..") || relative.Contains(":")) return null;
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }
}
