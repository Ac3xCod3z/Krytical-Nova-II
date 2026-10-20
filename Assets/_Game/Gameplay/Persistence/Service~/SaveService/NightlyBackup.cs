// Online phase 4, milestone 6: the save database backed up every night (owner, 20 Oct 2026: by the save service, into
// Google Drive). The save service is the only program with the database password, so it makes the copy itself:
//
// - At 4 AM (and as soon as it starts when the last copy is more than a day old) it copies the whole database with
//   Postgres's own pg_dump into the backup folder: one compressed file a night, with a small manifest beside it (how
//   many saves and ledger lines it holds and a fingerprint of all of them, taken in the same instant as the copy).
// - A copy is made under a temporary name, checked readable (pg_restore --list) and only then named, so a stop in the
//   middle never leaves a broken one; then it is copied into the off-PC folder (Google Drive for desktop's My Drive,
//   which uploads it by itself).
// - Copies older than 14 days are deleted in both places, but never the newest.
// - The restore drill (SaveService --restore-drill) puts the newest copy into an empty practice database
//   (krytical_nova_drill, made once by "Make Drill Database.bat") and checks it against its manifest: the proof that a
//   copy really brings the saves back.
// - The tests' service (--test) makes no nightly copies.
//
// The password reaches pg_dump only through its environment (PGPASSWORD), never on a command line or in a log.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace KryticalNova.SaveService
{
    /// <summary>Where and when the nightly copies go. Read from backup-settings.json beside the service when there is one.</summary>
    public sealed record BackupSettings
    {
        [JsonPropertyName("hour")] public int Hour { get; init; } = 4;
        [JsonPropertyName("keepDays")] public int KeepDays { get; init; } = 14;
        /// <summary>The copies on this PC. Empty: Documents\KryticalNova-Backups\database.</summary>
        [JsonPropertyName("folder")] public string Folder { get; init; } = "";
        /// <summary>
        /// The off-PC copies. Empty: KryticalNova-Backups in Google Drive for desktop's My Drive (the drive it adds, usually
        /// G:\My Drive), once it is set up. "none": no off-PC copy.
        /// </summary>
        [JsonPropertyName("offsite")] public string Offsite { get; init; } = "";
        /// <summary>Postgres's programs (pg_dump, pg_restore). Empty: Program Files\PostgreSQL\18\bin.</summary>
        [JsonPropertyName("postgresBin")] public string PostgresBin { get; init; } = "";

        public string LocalFolder => !string.IsNullOrWhiteSpace(Folder) ? Folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Backups", "database");

        public string? OffsiteFolder
        {
            get
            {
                if (string.Equals(Offsite, "none", StringComparison.OrdinalIgnoreCase)) return null;
                if (!string.IsNullOrWhiteSpace(Offsite)) return Offsite;
                string? myDrive = GoogleDrive.MyDrive();
                return myDrive == null ? null : Path.Combine(myDrive, "KryticalNova-Backups");
            }
        }

        public string Bin => !string.IsNullOrWhiteSpace(PostgresBin) ? PostgresBin
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL", "18", "bin");

        public static BackupSettings Load(string file)
        {
            if (!File.Exists(file)) return new BackupSettings();
            return JsonSerializer.Deserialize<BackupSettings>(File.ReadAllText(file), Json.Options) ?? new BackupSettings();
        }
    }

    /// <summary>What one copy holds, written beside it and checked by the restore drill.</summary>
    public sealed record BackupManifest(
        [property: JsonPropertyName("database")] string Database,
        [property: JsonPropertyName("made")] DateTime Made,
        [property: JsonPropertyName("documents")] long Documents,
        [property: JsonPropertyName("ledgerLines")] long LedgerLines,
        [property: JsonPropertyName("fingerprint")] string Fingerprint,
        [property: JsonPropertyName("bytes")] long Bytes);

    /// <summary>How the nightly copies stand, for the health check (the Server app and the control page show it).</summary>
    public sealed record BackupStatus(
        [property: JsonPropertyName("last")] DateTime? Last,
        [property: JsonPropertyName("bytes")] long Bytes,
        [property: JsonPropertyName("offsite")] bool Offsite,
        [property: JsonPropertyName("kept")] int Kept,
        [property: JsonPropertyName("problem")] string? Problem);

    /// <summary>Google Drive for desktop on this PC: the My Drive folder it adds, and whether it runs (it uploads that folder).</summary>
    public static class GoogleDrive
    {
        /// <summary>The "My Drive" folder at the root of a drive Google Drive for desktop added (usually G:), or null.</summary>
        public static string? MyDrive()
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    string folder = Path.Combine(drive.RootDirectory.FullName, "My Drive");
                    if (Directory.Exists(folder)) return folder;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>True while Google Drive for desktop runs.</summary>
        public static bool Running() => Process.GetProcessesByName("GoogleDriveFS").Length > 0;
    }

    public sealed class NightlyBackup
    {
        public const string Prefix = "krytical_nova_";
        public const string Extension = ".dump";
        public const string ManifestExtension = ".json";

        private readonly BackupSettings settings;
        private readonly DatabaseSettings database;
        private readonly SemaphoreSlim running = new SemaphoreSlim(1, 1);
        private string? problem;

        public NightlyBackup(BackupSettings settings, DatabaseSettings database)
        {
            this.settings = settings;
            this.database = database;
        }

        /// <summary>Runs until the service stops: the 4 AM copy each night, and a catch-up when the last one is over a day old.</summary>
        public async Task RunAsync(CancellationToken stop)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stop); // let the service settle first
                if (Newest() is not FileInfo last || DateTime.Now - last.LastWriteTime > TimeSpan.FromHours(24))
                    await MakeAsync("catching up: no copy in the last day");
                while (!stop.IsCancellationRequested)
                {
                    await Task.Delay(Until(DateTime.Now, settings.Hour), stop);
                    await MakeAsync("nightly");
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>How long from <paramref name="now"/> until the next <paramref name="hour"/> o'clock.</summary>
        public static TimeSpan Until(DateTime now, int hour)
        {
            DateTime next = now.Date.AddHours(Math.Clamp(hour, 0, 23));
            if (next <= now) next = next.AddDays(1);
            return next - now;
        }

        public BackupStatus Status()
        {
            FileInfo? last = Newest();
            string? offsite = settings.OffsiteFolder;
            bool uploading = OffsiteUploads(offsite, out string? notUploading);
            bool copied = uploading && last != null && offsite != null && File.Exists(Path.Combine(offsite, last.Name));
            int kept = Directory.Exists(settings.LocalFolder) ? Directory.GetFiles(settings.LocalFolder, Prefix + "*" + Extension).Length : 0;
            return new BackupStatus(last?.LastWriteTime, last?.Length ?? 0, copied, kept, problem ?? notUploading);
        }

        /// <summary>
        /// False, with what to do, when nothing uploads the off-PC folder: Google Drive for desktop not set up, or its folder
        /// there but the app not running (the copies would land in it and never leave this PC). Another folder named in
        /// backup-settings.json is taken at its word.
        /// </summary>
        public static bool OffsiteUploads(string? offsite, out string? problem)
        {
            problem = null;
            if (offsite == null)
            {
                problem = "Google Drive for desktop is not set up, so the copies stay on this PC only: install it and sign in (the Server Guide says how).";
                return false;
            }
            string? myDrive = GoogleDrive.MyDrive();
            bool inGoogleDrive = myDrive != null && Path.GetFullPath(offsite).StartsWith(Path.GetFullPath(myDrive), StringComparison.OrdinalIgnoreCase);
            if (!inGoogleDrive || GoogleDrive.Running()) return true;
            problem = "Google Drive for desktop is not running, so the copies are in its folder but not uploaded: start it (and let it start with Windows).";
            return false;
        }

        private FileInfo? Newest()
        {
            if (!Directory.Exists(settings.LocalFolder)) return null;
            return new DirectoryInfo(settings.LocalFolder).GetFiles(Prefix + "*" + Extension)
                .OrderByDescending(f => f.Name, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>Makes one copy now (nightly, catching up, or asked for). Returns the line it logs.</summary>
        public async Task<string> MakeAsync(string why)
        {
            if (!await running.WaitAsync(0)) return "A backup is already being made.";
            try
            {
                string line = await MakeLockedAsync(why);
                Console.WriteLine(line);
                return line;
            }
            finally
            {
                running.Release();
            }
        }

        private async Task<string> MakeLockedAsync(string why)
        {
            string pgDump = Path.Combine(settings.Bin, "pg_dump.exe"), pgRestore = Path.Combine(settings.Bin, "pg_restore.exe");
            if (!File.Exists(pgDump)) return Fail($"pg_dump was not found in {settings.Bin} (set postgresBin in backup-settings.json).");
            try
            {
                Directory.CreateDirectory(settings.LocalFolder);
                DateTime now = DateTime.Now;
                string name = FileNameFor(now);
                string path = Path.Combine(settings.LocalFolder, name), partial = path + ".partial";
                if (File.Exists(partial)) File.Delete(partial);

                // One instant for the copy and its manifest: a transaction exports its snapshot, pg_dump copies exactly
                // that snapshot, and the counts and fingerprint are read in it too.
                await using var connection = new NpgsqlConnection(ConnectionString(database.Database));
                await connection.OpenAsync();
                await using NpgsqlTransaction snapshotHolder = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
                string snapshot = (string)(await new NpgsqlCommand("SELECT pg_export_snapshot()", connection, snapshotHolder).ExecuteScalarAsync())!;
                (long documents, long ledger, string fingerprint) = await FingerprintAsync(connection, snapshotHolder);
                (int code, string output) = await RunAsync(pgDump,
                    $"-h {database.Host} -p {database.Port} -U {database.User} -d {database.Database} -Fc --snapshot={snapshot} -f \"{partial}\"");
                await snapshotHolder.CommitAsync();
                if (code != 0) { TryDelete(partial); return Fail("pg_dump stopped: " + Short(output)); }

                // Readable from end to end before it counts as a backup.
                (code, output) = await RunAsync(pgRestore, $"--list \"{partial}\"");
                if (code != 0) { TryDelete(partial); return Fail("the copy could not be read back: " + Short(output)); }
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
                long bytes = new FileInfo(path).Length;
                var manifest = new BackupManifest(database.Database, now, documents, ledger, fingerprint, bytes);
                string manifestPath = Path.ChangeExtension(path, ManifestExtension);
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                // Off this PC: Google Drive for desktop uploads what lands in its folder.
                string where = "";
                string? offsite = settings.OffsiteFolder;
                if (offsite != null)
                {
                    try
                    {
                        Directory.CreateDirectory(offsite);
                        File.Copy(path, Path.Combine(offsite, name), true);
                        File.Copy(manifestPath, Path.Combine(offsite, Path.GetFileName(manifestPath)), true);
                        where = ", copied to " + offsite + (OffsiteUploads(offsite, out string? notUploading) ? "" : ". WARNING: " + notUploading);
                        Trim(offsite, now);
                    }
                    catch (Exception e)
                    {
                        Trim(settings.LocalFolder, now);
                        return Fail($"made {name}, but it could not be copied off this PC ({e.Message}).");
                    }
                }
                int removed = Trim(settings.LocalFolder, now);
                problem = null;
                if (offsite == null)
                {
                    OffsiteUploads(null, out problem);
                    where = ". WARNING: " + problem;
                }
                return $"[Backup] {name} ({bytes / 1024} KB, {documents} saves, {ledger} ledger lines; {why}){where}" +
                       (removed > 0 ? $" {removed} older than {settings.KeepDays} days removed." : "") + (where.EndsWith(".") ? "" : ".");
            }
            catch (Exception e)
            {
                return Fail(e.Message);
            }
        }

        private string Fail(string why)
        {
            problem = why;
            return "[Backup] FAILED: " + why;
        }

        /// <summary>The name of the copy made at <paramref name="when"/>; sorts by time.</summary>
        public static string FileNameFor(DateTime when) =>
            Prefix + when.ToString("yyyy-MM-dd_HH-mm", System.Globalization.CultureInfo.InvariantCulture) + Extension;

        /// <summary>The copies (and their manifests) in <paramref name="folder"/> older than the days kept; never the newest.</summary>
        public static List<string> ToDelete(IEnumerable<string> copies, DateTime now, int keepDays)
        {
            var sorted = copies.Where(f => Path.GetFileName(f).StartsWith(Prefix, StringComparison.Ordinal) && f.EndsWith(Extension, StringComparison.Ordinal))
                               .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
            var old = new List<string>();
            for (int i = 0; i < sorted.Count - 1; i++) // the newest is always kept
            {
                string stamp = Path.GetFileNameWithoutExtension(sorted[i]).Substring(Prefix.Length);
                if (DateTime.TryParseExact(stamp, "yyyy-MM-dd_HH-mm", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime made) && now - made > TimeSpan.FromDays(Math.Max(1, keepDays)))
                    old.Add(sorted[i]);
            }
            return old;
        }

        private int Trim(string folder, DateTime now)
        {
            int removed = 0;
            foreach (string old in ToDelete(Directory.GetFiles(folder, Prefix + "*" + Extension), now, settings.KeepDays))
            {
                TryDelete(old);
                TryDelete(Path.ChangeExtension(old, ManifestExtension));
                removed++;
            }
            return removed;
        }

        // ------------------------------------------------------------------ the restore drill

        /// <summary>
        /// Puts a copy (the newest when none is named) into the practice database and checks it against its manifest.
        /// Returns 0 when every save and ledger line came back as copied.
        /// </summary>
        public Task<int> DrillAsync(string? file) => RestoreAsync(file, database.Database + "_drill");

        /// <summary>
        /// The real restore (the Server Guide's runbook; "Restore Saves.bat"): puts a copy back into the save database itself.
        /// Only while no save service runs on this database, after a fresh safety copy of what is there now, and after the
        /// owner types RESTORE. Returns 0 when every save and ledger line came back as copied.
        /// </summary>
        public async Task<int> RestoreRealAsync(string? file, bool serviceRunning)
        {
            if (serviceRunning)
            {
                Console.WriteLine("The save service is running. Close the Krytical Nova Server app first (it stops the server and the save service), then run this again.");
                return 1;
            }
            file ??= Newest()?.FullName;
            if (file == null || !File.Exists(file)) { Console.WriteLine($"No copy to restore in {settings.LocalFolder}."); return 1; }
            Console.WriteLine($"This puts {Path.GetFileName(file)} back into {database.Database}, replacing every save in it now.");
            Console.WriteLine("A safety copy of the saves as they are now is made first.");
            Console.Write("Type RESTORE to go on, anything else to stop: ");
            if (Console.ReadLine()?.Trim() != "RESTORE") { Console.WriteLine("Stopped. Nothing was changed."); return 1; }
            string safety = await MakeAsync("safety copy before a restore");
            if (safety.Contains("FAILED")) { Console.WriteLine("The safety copy failed, so nothing was restored."); return 1; }
            return await RestoreAsync(file, database.Database);
        }

        private async Task<int> RestoreAsync(string? file, string target)
        {
            bool practice = target != database.Database;
            file ??= Newest()?.FullName;
            if (file == null || !File.Exists(file)) { Console.WriteLine($"No copy to practise with in {settings.LocalFolder}."); return 1; }
            string manifestPath = Path.ChangeExtension(file, ManifestExtension);
            if (!File.Exists(manifestPath)) { Console.WriteLine($"{Path.GetFileName(file)} has no manifest beside it."); return 1; }
            BackupManifest manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), Json.Options)!;
            Console.WriteLine($"{(practice ? "Restore drill" : "Restore")}: {Path.GetFileName(file)} (made {manifest.Made:d MMM yyyy HH:mm}, " +
                              $"{manifest.Documents} saves, {manifest.LedgerLines} ledger lines) into {(practice ? "the practice database " : "")}{target}.");

            try
            {
                await using var probe = new NpgsqlConnection(ConnectionString(target));
                await probe.OpenAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine(practice
                    ? $"The practice database {target} cannot be opened ({e.Message}). Run \"Make Drill Database.bat\" once first."
                    : $"The save database {target} cannot be opened ({e.Message}).");
                return 1;
            }

            var clock = Stopwatch.StartNew();
            (int code, string output) = await RunAsync(Path.Combine(settings.Bin, "pg_restore.exe"),
                $"-h {database.Host} -p {database.Port} -U {database.User} -d {target} --clean --if-exists --no-owner --no-privileges \"{file}\"");
            if (code != 0) { Console.WriteLine("pg_restore stopped: " + Short(output)); return 1; }

            await using var connection = new NpgsqlConnection(ConnectionString(target));
            await connection.OpenAsync();
            await using NpgsqlTransaction reading = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            (long documents, long ledger, string fingerprint) = await FingerprintAsync(connection, reading);
            bool same = documents == manifest.Documents && ledger == manifest.LedgerLines && fingerprint == manifest.Fingerprint;
            Console.WriteLine($"Restored in {clock.Elapsed.TotalSeconds:0.0} s: {documents} saves, {ledger} ledger lines; " +
                              (same ? "every save and ledger line matches the copy. " + (practice ? "The drill PASSED." : "The saves are back.")
                                    : "they do NOT match the copy's manifest. " + (practice ? "The drill FAILED." : "Check the copy, or restore the safety copy.")));
            return same ? 0 : 1;
        }

        // ------------------------------------------------------------------ shared

        // Counts and one fingerprint of every save (key, version and text) and every ledger line, in key order.
        private static async Task<(long documents, long ledger, string fingerprint)> FingerprintAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            const string sql = @"
SELECT (SELECT count(*) FROM documents),
       (SELECT count(*) FROM ledger),
       md5(coalesce((SELECT string_agg(key || ':' || version || ':' || md5(body), ',' ORDER BY key) FROM documents), '') || '|' ||
           coalesce((SELECT string_agg(id || ':' || md5(body), ',' ORDER BY id) FROM ledger), ''))";
            await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 300 };
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2));
        }

        private string ConnectionString(string name) => new NpgsqlConnectionStringBuilder
        {
            Host = database.Host, Port = database.Port, Database = name, Username = database.User, Password = database.Password,
            Timeout = 10, CommandTimeout = 300,
        }.ConnectionString;

        // Runs one of Postgres's programs with the password in its environment only.
        private async Task<(int code, string output)> RunAsync(string exe, string arguments)
        {
            var info = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            info.Environment["PGPASSWORD"] = database.Password;
            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, (await errors) + (await output));
        }

        private static string Short(string text)
        {
            text = text.Trim().Replace(Environment.NewLine, " ");
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
        }
    }
}
