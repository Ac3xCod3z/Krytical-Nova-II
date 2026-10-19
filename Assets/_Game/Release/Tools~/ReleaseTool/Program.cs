using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace KryticalNova.Release
{
    /// <summary>
    /// The owner's release tool, run by the Unity menus under Krytical Nova ▸ Release (or by hand):
    /// <list type="bullet">
    /// <item><c>keygen --pub &lt;file&gt;</c>: makes the update signing key once. The private half is kept in this Windows
    /// account only (DPAPI), never in the project; the public half is written to the file, which the launcher builds in.</item>
    /// <item><c>export-key &lt;file&gt;</c> / <c>import-key &lt;file&gt;</c>: a backup of the private key, for a USB stick kept
    /// apart. Without the key no update can ever be signed again, and every launcher would have to be replaced by hand.</item>
    /// <item><c>stage --game &lt;dir&gt; --launcher &lt;exe&gt; --out &lt;dir&gt; [--version x] [--news &lt;file&gt;]</c>: hashes the build,
    /// compresses the files the store does not have yet into <c>out/files</c>, writes and signs <c>out/manifest.json</c>
    /// with the launcher's news in it (<see cref="LauncherNews"/>).</item>
    /// <item><c>verify --out &lt;dir&gt; --pub &lt;file&gt;</c>: checks the signature and that every file the list names is there.</item>
    /// </list>
    /// </summary>
    public static class Program
    {
        // Files of a Unity build that are for debugging only and never shipped.
        private static readonly string[] NeverShipped = { "_BackUpThisFolder_ButDontShipItWithYourGame", "_BurstDebugInformation_DoNotShip", "PerformanceLogs" };

        private static string KeyFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                      "KryticalNova", "release", "update-signing-key.dat");

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) return Usage();
                switch (args[0])
                {
                    case "keygen": return KeyGen(Option(args, "--pub"), args.Contains("--force"));
                    case "export-key": return ExportKey(args.Length > 1 ? args[1] : null);
                    case "import-key": return ImportKey(args.Length > 1 ? args[1] : null);
                    case "public-key": Console.WriteLine(PublicKeyText(LoadKey())); return 0;
                    case "stage": return Stage(Option(args, "--game"), Option(args, "--launcher"), Option(args, "--out"), Option(args, "--version"), Option(args, "--build"), Option(args, "--news"));
                    case "verify": return Verify(Option(args, "--out"), Option(args, "--pub"));
                    default: return Usage();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERROR: " + e.Message);
                return 1;
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("ReleaseTool keygen --pub <file> [--force] | export-key <file> | import-key <file> | public-key |");
            Console.Error.WriteLine("            stage --game <dir> --launcher <exe> --out <dir> [--version <text>] [--build <n>] [--news <file>] | verify --out <dir> --pub <file>");
            return 2;
        }

        private static string Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // ------------------------------------------------------------------ the key

        private static int KeyGen(string pubFile, bool force)
        {
            if (pubFile == null) throw new ArgumentException("keygen needs --pub <file>.");
            if (File.Exists(KeyFile) && !force)
                throw new InvalidOperationException($"A signing key already exists ({KeyFile}). Making a new one would leave every launcher refusing updates; --force only if that is meant.");
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                SaveKey(key.ExportParameters(true));
                File.WriteAllText(pubFile, PublicKeyText(key.ExportParameters(false)) + Environment.NewLine);
                Console.WriteLine("Signing key made and kept in this Windows account: " + KeyFile);
                Console.WriteLine("Public key written to " + pubFile + ". Back the private key up with: ReleaseTool export-key <file on a USB stick>");
            }
            return 0;
        }

        private static string PublicKeyText(ECParameters key)
        {
            var q = new byte[64];
            Buffer.BlockCopy(Pad(key.Q.X), 0, q, 0, 32);
            Buffer.BlockCopy(Pad(key.Q.Y), 0, q, 32, 32);
            return Convert.ToBase64String(q);
        }

        private static byte[] Pad(byte[] value)
        {
            if (value.Length == 32) return value;
            var padded = new byte[32];
            Buffer.BlockCopy(value, 0, padded, 32 - value.Length, value.Length);
            return padded;
        }

        private static byte[] Serialize(ECParameters key) => Encoding.ASCII.GetBytes(string.Join("|",
            Convert.ToBase64String(key.D), Convert.ToBase64String(key.Q.X), Convert.ToBase64String(key.Q.Y)));

        private static ECParameters Deserialize(byte[] data)
        {
            string[] parts = Encoding.ASCII.GetString(data).Trim().Split('|');
            if (parts.Length != 3) throw new InvalidDataException("That is not a Krytical Nova signing key.");
            return new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = Convert.FromBase64String(parts[0]),
                Q = new ECPoint { X = Convert.FromBase64String(parts[1]), Y = Convert.FromBase64String(parts[2]) },
            };
        }

        private static void SaveKey(ECParameters key)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyFile));
            File.WriteAllBytes(KeyFile, ProtectedData.Protect(Serialize(key), null, DataProtectionScope.CurrentUser));
        }

        private static ECParameters LoadKey()
        {
            if (!File.Exists(KeyFile)) throw new FileNotFoundException("No signing key in this Windows account (looked for " + KeyFile + "). Make one (keygen) or bring the backup (import-key).");
            return Deserialize(ProtectedData.Unprotect(File.ReadAllBytes(KeyFile), null, DataProtectionScope.CurrentUser));
        }

        private static int ExportKey(string file)
        {
            if (file == null) throw new ArgumentException("export-key needs a file.");
            File.WriteAllBytes(file, Serialize(LoadKey()));
            Console.WriteLine("Private key written to " + file + ". Keep it offline (a USB stick in a drawer); anyone with it can sign updates.");
            return 0;
        }

        private static int ImportKey(string file)
        {
            if (file == null) throw new ArgumentException("import-key needs a file.");
            ECParameters key = Deserialize(File.ReadAllBytes(file));
            using (ECDsa.Create(key)) { } // a broken key fails here, before it replaces anything
            SaveKey(key);
            Console.WriteLine("Signing key imported into this Windows account. Public key: " + PublicKeyText(key));
            return 0;
        }

        // ------------------------------------------------------------------ a release

        private static int Stage(string gameDir, string launcherExe, string outDir, string version, string buildArg, string newsFile)
        {
            if (gameDir == null || launcherExe == null || outDir == null) throw new ArgumentException("stage needs --game, --launcher and --out.");
            if (!Directory.Exists(gameDir)) throw new DirectoryNotFoundException(gameDir);
            if (!File.Exists(launcherExe)) throw new FileNotFoundException(launcherExe);
            // The news is checked before anything is packed: news the launcher cannot lay out stops the release.
            LauncherNews news = null;
            if (!string.IsNullOrWhiteSpace(newsFile))
            {
                if (!File.Exists(newsFile)) throw new FileNotFoundException("No news file at " + newsFile);
                news = LauncherNews.FromFile(newsFile);
                string problem = news.Problem();
                if (problem != null) throw new InvalidDataException(problem + " (" + newsFile + ")");
            }
            ECParameters keyParams = LoadKey();

            Directory.CreateDirectory(Path.Combine(outDir, "files"));
            string manifestFile = Path.Combine(outDir, UpdateManifest.FileName);
            int previous = File.Exists(manifestFile) ? UpdateManifest.FromBytes(File.ReadAllBytes(manifestFile)).build : 0;
            // The build number the game was built with, when given (the game and its server compare it); else the next one.
            int build = int.TryParse(buildArg, out int asked) && asked > 0 ? asked : previous + 1;
            if (build <= previous) throw new InvalidOperationException($"Build {build} is not newer than the build already staged ({previous}).");
            var manifest = new UpdateManifest
            {
                build = build,
                created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            };
            manifest.version = (string.IsNullOrWhiteSpace(version) ? "" : version.Trim() + " ") + "build " + manifest.build;

            string root = Path.GetFullPath(gameDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            int added = 0;
            long addedBytes = 0;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string relative = file.Substring(root.Length).Replace('\\', '/');
                if (NeverShipped.Any(n => relative.Split('/')[0].EndsWith(n, StringComparison.OrdinalIgnoreCase))) continue;
                UpdateFile entry = Pack(file, relative, outDir, ref added, ref addedBytes);
                manifest.files.Add(entry);
                if (relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !relative.Contains("/") && !relative.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                    manifest.gameExe = relative;
            }
            if (manifest.gameExe.Length == 0) throw new InvalidDataException("The build has no game exe at its top.");
            manifest.launcher = Pack(launcherExe, Path.GetFileName(launcherExe), outDir, ref added, ref addedBytes);
            if (news != null && news.items.Count > 0) manifest.news = LauncherNews.Clean(news.items);
            if (news != null && news.history.Count > 0)
                manifest.newsHistory = news.history.Select(r => new NewsRelease { label = r.label.Trim(), items = LauncherNews.Clean(r.items) }).ToList();

            byte[] bytes = manifest.ToBytes();
            string signature;
            using (ECDsa key = ECDsa.Create(keyParams)) signature = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256));
            File.WriteAllBytes(manifestFile, bytes);
            File.WriteAllBytes(Path.Combine(outDir, UpdateManifest.SignedName), UpdateManifest.Sign(bytes, signature));
            string oldSignature = Path.Combine(outDir, "manifest.sig"); // the first format kept it apart
            if (File.Exists(oldSignature)) File.Delete(oldSignature);

            // The store keeps only what this build names: a CCD release holds its own copy of every file, so older
            // builds stay downloadable from their releases while the next upload stays small.
            var named = new HashSet<string>(manifest.files.Concat(new[] { manifest.launcher }).Select(f => f.sha256 + ".gz"), StringComparer.OrdinalIgnoreCase);
            foreach (string packed in Directory.GetFiles(Path.Combine(outDir, "files")))
                if (!named.Contains(Path.GetFileName(packed))) File.Delete(packed);

            long total = manifest.files.Sum(f => f.packedSize);
            Console.WriteLine($"STAGED build={manifest.build} files={manifest.files.Count} new={added} newBytes={addedBytes} downloadBytes={total} exe={manifest.gameExe} news={manifest.news?.Count ?? 0}");
            return 0;
        }

        // A file's compressed copy goes into the store under its hash, unless it is there already.
        private static UpdateFile Pack(string file, string relative, string outDir, ref int added, ref long addedBytes)
        {
            string hash = UpdateCrypto.Sha256Hex(file);
            string packed = Path.Combine(outDir, UpdateManifest.PackedPath(hash).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(packed))
            {
                string part = packed + ".part";
                using (var input = File.OpenRead(file))
                using (var output = File.Create(part))
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                    input.CopyTo(gzip);
                File.Move(part, packed);
                added++;
                addedBytes += new FileInfo(packed).Length;
            }
            return new UpdateFile { path = relative, size = new FileInfo(file).Length, sha256 = hash, packedSize = new FileInfo(packed).Length };
        }

        private static int Verify(string outDir, string pubFile)
        {
            if (outDir == null || pubFile == null) throw new ArgumentException("verify needs --out and --pub.");
            if (!UpdateManifest.Split(File.ReadAllBytes(Path.Combine(outDir, UpdateManifest.SignedName)), out string signature, out byte[] bytes))
                throw new InvalidDataException("The signed update list is not in the expected form.");
            if (!UpdateCrypto.Verify(File.ReadAllText(pubFile).Trim(), bytes, signature)) throw new InvalidDataException("The signature does not match this public key.");
            UpdateManifest manifest = UpdateManifest.FromBytes(bytes);
            var missing = manifest.files.Concat(new[] { manifest.launcher })
                                  .Where(f => !File.Exists(Path.Combine(outDir, UpdateManifest.PackedPath(f.sha256)))).ToList();
            if (missing.Count > 0) throw new InvalidDataException(missing.Count + " file(s) are missing from the store, first: " + missing[0].path);
            Console.WriteLine($"VERIFIED build={manifest.build} files={manifest.files.Count}");
            return 0;
        }
    }
}
