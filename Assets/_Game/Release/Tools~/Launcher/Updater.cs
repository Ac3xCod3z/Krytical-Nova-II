using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace KryticalNova.Release
{
    /// <summary>What a check found: the signed list, and what this PC needs from it.</summary>
    public sealed class UpdatePlan
    {
        public UpdateManifest Manifest;
        public List<UpdateFile> Needed = new List<UpdateFile>();
        public long DownloadBytes;
        public bool LauncherOutdated;
        public int InstalledBuild;
        public bool GameInstalled;
    }

    /// <summary>
    /// Keeps the game beside the launcher (<c>Game\</c>) up to date from the signed update list. Only files whose hash
    /// differs are downloaded; each is checked against its hash before anything in the game folder changes, then all are
    /// moved in at once, and files the previous build had and this one does not are removed. Files the launcher never
    /// installed (performance logs, anything a player put there) are left alone.
    /// </summary>
    public sealed class Updater
    {
        private const int Parallel = 4;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        public readonly string LauncherFolder;
        public readonly string GameFolder;
        private readonly string stateFolder;
        private readonly string downloadFolder;
        private readonly LauncherConfig config;
        private readonly string publicKey;

        public Updater(string launcherFolder, LauncherConfig config, string publicKey)
        {
            LauncherFolder = launcherFolder;
            GameFolder = Path.Combine(launcherFolder, "Game");
            stateFolder = Path.Combine(GameFolder, ".launcher");
            downloadFolder = Path.Combine(stateFolder, "download");
            this.config = config;
            this.publicKey = publicKey;
        }

        // Cloud Content Delivery answers a badge with "no-store", so the update list is never a cached one; it refuses
        // query parameters of its own, so none are added.
        private string Url(string path) => config.source.Replace("{channel}", config.channel).Replace("{path}", path);

        // ------------------------------------------------------------------ check

        public async Task<UpdatePlan> Check(bool repair, CancellationToken cancel)
        {
            byte[] signed = await Http.GetByteArrayAsync(Url(UpdateManifest.SignedName)).ConfigureAwait(false);
            if (!UpdateManifest.Split(signed, out string signature, out byte[] bytes) || !UpdateCrypto.Verify(publicKey, bytes, signature))
                throw new InvalidDataException("The update list is not signed by Krytical Nova. Nothing was changed.");
            UpdateManifest manifest = UpdateManifest.FromBytes(bytes);
            if (manifest.format > UpdateManifest.CurrentFormat)
                throw new InvalidDataException("This update needs a newer launcher. Download the launcher again from the website.");

            InstalledState installed = InstalledState.Load(stateFolder);
            var plan = new UpdatePlan { Manifest = manifest, InstalledBuild = installed.build, GameInstalled = File.Exists(Path.Combine(GameFolder, manifest.gameExe)) };
            plan.LauncherOutdated = manifest.launcher != null &&
                                    !string.Equals(UpdateCrypto.Sha256Hex(LauncherExe), manifest.launcher.sha256, StringComparison.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                foreach (UpdateFile file in manifest.files)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (!IsCurrent(file, installed, repair)) plan.Needed.Add(file);
                }
            }, cancel).ConfigureAwait(false);
            plan.DownloadBytes = plan.Needed.Sum(f => f.packedSize);
            // A build that changed nothing in the game folder (a launcher-only release) is installed already.
            if (plan.Needed.Count == 0 && plan.GameInstalled && installed.build != manifest.build)
            {
                installed.build = manifest.build;
                installed.version = manifest.version;
                installed.Save(stateFolder);
                plan.InstalledBuild = manifest.build;
            }
            return plan;
        }

        // A file is current when it is the size the list says and its hash matches: from the record of what was
        // installed when the file has not been touched since, otherwise by hashing it again.
        private bool IsCurrent(UpdateFile file, InstalledState installed, bool repair)
        {
            string full = UpdateCrypto.SafeJoin(GameFolder, file.path);
            if (full == null) throw new InvalidDataException("The update list names a file outside the game folder: " + file.path);
            var info = new FileInfo(full);
            if (!info.Exists || info.Length != file.size) return false;
            InstalledFile record = installed.Find(file.path);
            if (!repair && record != null && record.sha256 == file.sha256 && record.ticks == info.LastWriteTimeUtc.Ticks) return true;
            return UpdateCrypto.Sha256Hex(full) == file.sha256;
        }

        // ------------------------------------------------------------------ the launcher itself

        public static string LauncherExe => Process.GetCurrentProcess().MainModule.FileName;

        /// <summary>
        /// Downloads the new launcher, checks it, puts it in place of this one (a running exe may be renamed, not
        /// overwritten) and returns its path for the caller to start. The old one is removed on the next start.
        /// </summary>
        public async Task<string> UpdateLauncher(UpdateFile launcher, IProgress<long> progress, CancellationToken cancel)
        {
            string self = LauncherExe;
            string fresh = self + ".new";
            await Download(launcher, fresh, progress, cancel).ConfigureAwait(false);
            string old = self + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(self, old);
            File.Move(fresh, self);
            return self;
        }

        public static void RemoveOldLauncher()
        {
            string old = LauncherExe + ".old";
            for (int i = 0; i < 10 && File.Exists(old); i++)
            {
                try { File.Delete(old); }
                catch (IOException) { Thread.Sleep(300); }
                catch (UnauthorizedAccessException) { Thread.Sleep(300); }
            }
        }

        // ------------------------------------------------------------------ the game

        public bool GameRunning(UpdateManifest manifest)
        {
            string exe = Path.Combine(GameFolder, manifest.gameExe);
            string name = Path.GetFileNameWithoutExtension(manifest.gameExe);
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (string.Equals(process.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception)
                {
                    return true; // cannot look inside it: assume it is ours rather than overwrite a running game
                }
            }
            return false;
        }

        public async Task Apply(UpdatePlan plan, IProgress<long> progress, CancellationToken cancel)
        {
            Directory.CreateDirectory(downloadFolder);
            long done = 0;
            var gate = new SemaphoreSlim(Parallel);
            var counter = new Progress<long>(bytes => { long now = Interlocked.Add(ref done, bytes); progress?.Report(now); });
            var tasks = plan.Needed.GroupBy(f => f.sha256).Select(async group =>
            {
                await gate.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    string target = Path.Combine(downloadFolder, group.Key);
                    if (!(File.Exists(target) && UpdateCrypto.Sha256Hex(target) == group.Key)) // kept from a broken-off update
                        await Download(group.First(), target, counter, cancel).ConfigureAwait(false);
                    else ((IProgress<long>)counter).Report(group.First().packedSize);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Everything is here and checked: only now does the game folder change.
            InstalledState previous = InstalledState.Load(stateFolder);
            foreach (UpdateFile file in plan.Needed)
            {
                string full = UpdateCrypto.SafeJoin(GameFolder, file.path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                if (File.Exists(full)) File.Delete(full);
                File.Copy(Path.Combine(downloadFolder, file.sha256), full);
            }
            var keep = new HashSet<string>(plan.Manifest.files.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
            foreach (InstalledFile old in previous.files)
            {
                if (keep.Contains(old.path)) continue;
                string full = UpdateCrypto.SafeJoin(GameFolder, old.path);
                if (full != null && File.Exists(full)) File.Delete(full);
            }

            var state = new InstalledState { build = plan.Manifest.build, version = plan.Manifest.version };
            foreach (UpdateFile file in plan.Manifest.files)
            {
                var info = new FileInfo(UpdateCrypto.SafeJoin(GameFolder, file.path));
                state.files.Add(new InstalledFile { path = file.path, sha256 = file.sha256, ticks = info.LastWriteTimeUtc.Ticks });
            }
            state.Save(stateFolder);
            Directory.Delete(downloadFolder, true);
            plan.Needed.Clear();
            plan.InstalledBuild = state.build;
            plan.GameInstalled = true;
        }

        // A dropped connection is tried again a few times, after a short wait, before the update stops.
        private async Task Download(UpdateFile file, string target, IProgress<long> progress, CancellationToken cancel)
        {
            for (int attempt = 1; ; attempt++)
            {
                long counted = 0;
                var tracking = new Progress<long>(bytes => { counted += bytes; progress?.Report(bytes); });
                try
                {
                    await DownloadOnce(file, target, tracking, cancel).ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (attempt < 4 && !(e is OperationCanceledException) && (e is HttpRequestException || e is IOException))
                {
                    Log.Write($"download of {file.path} broke off ({e.GetBaseException().Message}); try {attempt + 1} of 4");
                    progress?.Report(-counted); // the bar goes back by what this try had counted
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancel).ConfigureAwait(false);
                }
            }
        }

        // Downloads a file's compressed copy, unpacks it as it arrives and refuses it unless its hash and size match.
        private async Task DownloadOnce(UpdateFile file, string target, IProgress<long> progress, CancellationToken cancel)
        {
            string part = target + ".part";
            using (HttpResponseMessage response = await Http.GetAsync(Url(UpdateManifest.PackedPath(file.sha256)), HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var counting = new CountingStream(body, progress))
                using (var gzip = new GZipStream(counting, CompressionMode.Decompress))
                using (var output = File.Create(part))
                using (var sha = SHA256.Create())
                using (var hashing = new CryptoStream(output, sha, CryptoStreamMode.Write))
                {
                    try
                    {
                        await gzip.CopyToAsync(hashing, 81920, cancel).ConfigureAwait(false);
                    }
                    catch (InvalidDataException)
                    {
                        throw new InvalidDataException("A downloaded file was damaged (" + file.path + "). Press Update to try again.");
                    }
                    hashing.FlushFinalBlock();
                    if (output.Length != file.size || UpdateCrypto.Hex(sha.Hash) != file.sha256)
                        throw new InvalidDataException("A downloaded file was damaged (" + file.path + "). Press Update to try again.");
                }
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
        }

        public void Launch(UpdateManifest manifest)
        {
            string exe = Path.Combine(GameFolder, manifest.gameExe);
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = GameFolder, UseShellExecute = false });
        }
    }

    /// <summary>Counts the compressed bytes read, for the progress bar.</summary>
    internal sealed class CountingStream : Stream
    {
        private readonly Stream inner;
        private readonly IProgress<long> progress;

        public CountingStream(Stream inner, IProgress<long> progress)
        {
            this.inner = inner;
            this.progress = progress;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            if (read > 0) progress?.Report(read);
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>What the launcher installed last: the build and each file's hash and write time.</summary>
    [DataContract]
    public sealed class InstalledState
    {
        private const string FileName = "installed.json";

        [DataMember(Order = 1)] public int build;
        [DataMember(Order = 2)] public string version = "";
        [DataMember(Order = 3)] public List<InstalledFile> files = new List<InstalledFile>();

        private Dictionary<string, InstalledFile> byPath;

        public InstalledFile Find(string path)
        {
            if (byPath == null)
            {
                byPath = new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
                foreach (InstalledFile file in files) byPath[file.path] = file;
            }
            return byPath.TryGetValue(path, out InstalledFile found) ? found : null;
        }

        public static InstalledState Load(string folder)
        {
            string file = Path.Combine(folder, FileName);
            if (!File.Exists(file)) return new InstalledState();
            try
            {
                using (var stream = File.OpenRead(file))
                {
                    var state = (InstalledState)new DataContractJsonSerializer(typeof(InstalledState)).ReadObject(stream);
                    if (state.files == null) state.files = new List<InstalledFile>();
                    return state;
                }
            }
            catch (Exception)
            {
                return new InstalledState(); // unreadable: every file is checked by its hash instead
            }
        }

        public void Save(string folder)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, FileName);
            using (var stream = File.Create(file + ".tmp"))
                new DataContractJsonSerializer(typeof(InstalledState)).WriteObject(stream, this);
            if (File.Exists(file)) File.Delete(file);
            File.Move(file + ".tmp", file);
        }
    }

    [DataContract]
    public sealed class InstalledFile
    {
        [DataMember(Order = 1)] public string path = "";
        [DataMember(Order = 2)] public string sha256 = "";
        [DataMember(Order = 3)] public long ticks;
    }
}
