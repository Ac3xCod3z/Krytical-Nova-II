using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace KryticalNova.SaveService
{
    /// <summary>The database side: save documents with versions, and the account locks.</summary>
    public sealed class DocumentStore
    {
        // The tables, made on the first start. "schema" says which version of this layout the database has, so a later
        // milestone can step it up (milestone 2 splits accounts into characters and parts).
        private const int SchemaVersion = 1;

        private readonly NpgsqlDataSource data;

        public DocumentStore(NpgsqlDataSource data) => this.data = data;

        public async Task PrepareAsync()
        {
            await using NpgsqlCommand make = data.CreateCommand(@"
CREATE TABLE IF NOT EXISTS schema (version int NOT NULL);
CREATE TABLE IF NOT EXISTS documents (
  key      text PRIMARY KEY,
  body     text NOT NULL,
  version  bigint NOT NULL,
  updated  timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS locks (
  owner    text PRIMARY KEY,
  holder   text NOT NULL,
  until    timestamptz NOT NULL
);");
            await make.ExecuteNonQueryAsync();
            await using NpgsqlCommand read = data.CreateCommand("SELECT version FROM schema LIMIT 1");
            object? have = await read.ExecuteScalarAsync();
            if (have == null)
            {
                await using NpgsqlCommand set = data.CreateCommand("INSERT INTO schema (version) VALUES ($1)");
                set.Parameters.AddWithValue(SchemaVersion);
                await set.ExecuteNonQueryAsync();
            }
            else if ((int)have > SchemaVersion)
                throw new InvalidOperationException($"The database is at layout {have}, newer than this service ({SchemaVersion}): use the newer service.");
        }

        public async Task<long> CountAsync()
        {
            await using NpgsqlCommand count = data.CreateCommand("SELECT count(*) FROM documents");
            return (long)(await count.ExecuteScalarAsync() ?? 0L);
        }

        public async Task<Document?> ReadAsync(string key)
        {
            await using NpgsqlCommand read = data.CreateCommand("SELECT body, version FROM documents WHERE key = $1");
            read.Parameters.AddWithValue(key);
            await using NpgsqlDataReader row = await read.ExecuteReaderAsync();
            return await row.ReadAsync() ? new Document(key, row.GetString(0), row.GetInt64(1)) : null;
        }

        /// <summary>
        /// Writes a document. With <paramref name="expected"/> the write happens only if the document is still at that
        /// version (0: it must not exist yet); without, it always happens. The version goes up by one each write.
        /// </summary>
        public async Task<WriteResult> WriteAsync(string key, string body, long? expected)
        {
            await using NpgsqlCommand write = expected == null
                ? data.CreateCommand(@"
INSERT INTO documents (key, body, version) VALUES ($1, $2, 1)
ON CONFLICT (key) DO UPDATE SET body = EXCLUDED.body, version = documents.version + 1, updated = now()
RETURNING version")
                : expected == 0
                    ? data.CreateCommand(@"
INSERT INTO documents (key, body, version) VALUES ($1, $2, 1)
ON CONFLICT (key) DO NOTHING
RETURNING version")
                    : data.CreateCommand(@"
UPDATE documents SET body = $2, version = version + 1, updated = now()
WHERE key = $1 AND version = $3
RETURNING version");
            write.Parameters.AddWithValue(key);
            write.Parameters.AddWithValue(body);
            if (expected is > 0) write.Parameters.AddWithValue(expected.Value);
            object? version = await write.ExecuteScalarAsync();
            if (version != null) return new WriteResult(true, (long)version);
            Document? now = await ReadAsync(key);
            return new WriteResult(false, now?.Version ?? 0);
        }

        /// <summary>
        /// Several writes and deletes together (online phase 4, milestone 2): all of them happen, or none. A write whose
        /// expected version no longer holds turns the whole batch away, naming that document and its version now.
        /// </summary>
        public async Task<BatchResult> BatchAsync(IReadOnlyList<WriteRequest> writes, IReadOnlyList<string> deletes)
        {
            await using NpgsqlConnection connection = await data.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            var versions = new List<KeyVersion>(writes.Count);
            foreach (WriteRequest write in writes)
            {
                long? version = await WriteOneAsync(connection, transaction, write.Key, write.Body ?? "", write.Expected);
                if (version == null)
                {
                    await transaction.RollbackAsync();
                    Document? now = await ReadAsync(write.Key);
                    return new BatchResult(false, write.Key, now?.Version ?? 0, new List<KeyVersion>());
                }
                versions.Add(new KeyVersion(write.Key, version.Value));
            }
            foreach (string key in deletes)
            {
                await using var delete = new NpgsqlCommand("DELETE FROM documents WHERE key = $1", connection, transaction);
                delete.Parameters.AddWithValue(key);
                await delete.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            return new BatchResult(true, null, 0, versions);
        }

        private static async Task<long?> WriteOneAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, string body, long? expected)
        {
            string sql = expected == null
                ? @"INSERT INTO documents (key, body, version) VALUES ($1, $2, 1)
ON CONFLICT (key) DO UPDATE SET body = EXCLUDED.body, version = documents.version + 1, updated = now() RETURNING version"
                : expected == 0
                    ? "INSERT INTO documents (key, body, version) VALUES ($1, $2, 1) ON CONFLICT (key) DO NOTHING RETURNING version"
                    : "UPDATE documents SET body = $2, version = version + 1, updated = now() WHERE key = $1 AND version = $3 RETURNING version";
            await using var write = new NpgsqlCommand(sql, connection, transaction);
            write.Parameters.AddWithValue(key);
            write.Parameters.AddWithValue(body);
            if (expected is > 0) write.Parameters.AddWithValue(expected.Value);
            object? version = await write.ExecuteScalarAsync();
            return version == null ? null : (long)version;
        }

        /// <summary>Many documents in one trip (a character's parts when it loads). Missing ones are left out.</summary>
        public async Task<List<Document>> ReadManyAsync(IReadOnlyList<string> keys)
        {
            await using NpgsqlCommand read = data.CreateCommand("SELECT key, body, version FROM documents WHERE key = ANY($1)");
            read.Parameters.AddWithValue(keys.ToArray());
            var found = new List<Document>();
            await using NpgsqlDataReader row = await read.ExecuteReaderAsync();
            while (await row.ReadAsync()) found.Add(new Document(row.GetString(0), row.GetString(1), row.GetInt64(2)));
            return found;
        }

        /// <summary>The database's size on disk, in bytes (the control page's Saves chip).</summary>
        public async Task<long> SizeAsync()
        {
            await using NpgsqlCommand size = data.CreateCommand("SELECT pg_database_size(current_database())");
            return (long)(await size.ExecuteScalarAsync() ?? 0L);
        }

        public async Task DeleteAsync(string key)
        {
            await using NpgsqlCommand delete = data.CreateCommand("DELETE FROM documents WHERE key = $1");
            delete.Parameters.AddWithValue(key);
            await delete.ExecuteNonQueryAsync();
        }

        public async Task<List<string>> KeysAsync(string prefix)
        {
            await using NpgsqlCommand keys = data.CreateCommand("SELECT key FROM documents WHERE starts_with(key, $1) ORDER BY key COLLATE \"C\"");
            keys.Parameters.AddWithValue(prefix);
            var list = new List<string>();
            await using NpgsqlDataReader row = await keys.ExecuteReaderAsync();
            while (await row.ReadAsync()) list.Add(row.GetString(0));
            return list;
        }

        /// <summary>
        /// Takes or renews the lock on <paramref name="owner"/> (an account) for <paramref name="holder"/> (a game server)
        /// for <paramref name="seconds"/>. Granted when nobody holds it, the same holder does, or the last hold ran out.
        /// </summary>
        public async Task<LockResult> LockAsync(string owner, string holder, int seconds)
        {
            await using NpgsqlCommand take = data.CreateCommand(@"
INSERT INTO locks (owner, holder, until) VALUES ($1, $2, now() + make_interval(secs => $3))
ON CONFLICT (owner) DO UPDATE SET holder = EXCLUDED.holder, until = EXCLUDED.until
  WHERE locks.holder = EXCLUDED.holder OR locks.until < now()
RETURNING holder, until");
            take.Parameters.AddWithValue(owner);
            take.Parameters.AddWithValue(holder);
            take.Parameters.AddWithValue((double)seconds);
            await using (NpgsqlDataReader row = await take.ExecuteReaderAsync())
                if (await row.ReadAsync()) return new LockResult(true, row.GetString(0), row.GetDateTime(1));
            await using NpgsqlCommand who = data.CreateCommand("SELECT holder, until FROM locks WHERE owner = $1");
            who.Parameters.AddWithValue(owner);
            await using NpgsqlDataReader current = await who.ExecuteReaderAsync();
            return await current.ReadAsync() ? new LockResult(false, current.GetString(0), current.GetDateTime(1)) : new LockResult(false, "", DateTime.UtcNow);
        }

        public async Task UnlockAsync(string owner, string holder)
        {
            await using NpgsqlCommand free = data.CreateCommand("DELETE FROM locks WHERE owner = $1 AND holder = $2");
            free.Parameters.AddWithValue(owner);
            free.Parameters.AddWithValue(holder);
            await free.ExecuteNonQueryAsync();
        }
    }

    public sealed record Document(string Key, string Body, long Version);
    public sealed record WriteResult(bool Written, long Version);
    public sealed record LockResult(bool Granted, string Holder, DateTime Until);
    public sealed record WriteRequest(string Key, string? Body, long? Expected);
    public sealed record LockRequest(string? Owner, string? Holder, int Seconds);
    public sealed record KeyVersion(string Key, long Version);
    public sealed record BatchRequest(List<WriteRequest>? Writes, List<string>? Deletes);
    public sealed record BatchResult(bool Written, string? Conflict, long ConflictVersion, List<KeyVersion> Versions);
    public sealed record ReadRequest(List<string>? Keys);

    public sealed record DatabaseSettings(
        [property: JsonPropertyName("host")] string Host,
        [property: JsonPropertyName("port")] int Port,
        [property: JsonPropertyName("database")] string Database,
        [property: JsonPropertyName("user")] string User,
        [property: JsonPropertyName("password")] string Password);

    public static class Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>The same key rules as the game's SaveKeys: lower-case paths of letters, digits, - and _.</summary>
    public static class SaveKeyRules
    {
        public static bool IsValid(string? key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 96 || key[0] == '/' || key[^1] == '/' || key.Contains("//")) return false;
            foreach (char c in key)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '/')) return false;
            return true;
        }
    }

    /// <summary>The secret the game servers send with every request: made once, kept in the secrets folder.</summary>
    public static class ServiceKey
    {
        public static string LoadOrMake(string file)
        {
            if (File.Exists(file))
            {
                string key = File.ReadAllText(file).Trim();
                if (key.Length >= 32) return key;
            }
            string made = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, made);
            Console.WriteLine("A new service key was made in " + file + ".");
            return made;
        }
    }
}
