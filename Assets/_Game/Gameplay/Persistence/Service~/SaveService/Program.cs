// The save service (online phase 4, milestone 1): the only program that talks to the save database. Game servers ask it
// to read and write save documents over the private network; it alone holds the database password.
//
// - Every request carries the service key (header X-KN-Key), a secret shared with the game servers in
//   Documents\KryticalNova-Secrets\save-service-key.txt (made on the first start). Anything without it is refused.
// - Every document has a version; a write that names the version it read is refused when someone wrote since, so a
//   server that lost track can never overwrite newer data.
// - A lock per account: one game server at a time may hold a player's account; a lock runs out on its own when the
//   server holding it stops renewing (it crashed), so the player can join elsewhere.
// - A batch (a player's save, a trade: several documents and ledger lines) is one transaction, all or none; a batch sent
//   again after its answer was lost is recognised by its id and not applied twice (milestone 3).
// - It listens on this PC only (http://127.0.0.1:7801) until the save service moves beside the servers on a private
//   network. With --test it serves the tests' own database (krytical_nova_test) on port 7802 instead.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KryticalNova.SaveService;
using Npgsql;

string secrets = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets");
string databaseFile = Path.Combine(secrets, "save-service.json");
string keyFile = Path.Combine(secrets, "save-service-key.txt");

if (!File.Exists(databaseFile))
{
    Console.Error.WriteLine($"No database details at {databaseFile}. Run \"Setup Database.bat\" first.");
    return 1;
}
DatabaseSettings database = JsonSerializer.Deserialize<DatabaseSettings>(File.ReadAllText(databaseFile), Json.Options)
                            ?? throw new InvalidDataException(databaseFile + " could not be read.");
// --test: the tests' own database and port (the Editor's tests and the measure server), never the real saves.
bool test = args.Contains("--test");
if (test) database = database with { Database = database.Database + "_test" };
string urls = test ? "http://127.0.0.1:7802" : "http://127.0.0.1:7801";
string serviceKey = ServiceKey.LoadOrMake(keyFile);

var connection = new NpgsqlConnectionStringBuilder
{
    Host = database.Host, Port = database.Port, Database = database.Database, Username = database.User, Password = database.Password,
    MaxPoolSize = 20, Timeout = 5, CommandTimeout = 10,
};
var store = new DocumentStore(NpgsqlDataSource.Create(connection.ConnectionString));
await store.PrepareAsync();

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? urls);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

// The key on every request, compared in constant time.
byte[] expected = Encoding.UTF8.GetBytes(serviceKey);
app.Use(async (context, next) =>
{
    byte[] given = Encoding.UTF8.GetBytes(context.Request.Headers["X-KN-Key"].ToString());
    if (given.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(given, expected))
    {
        context.Response.StatusCode = 401;
        return;
    }
    await next();
});

app.MapGet("/health", async () => Results.Ok(new { ok = true, documents = await store.CountAsync(), bytes = await store.SizeAsync() }));

app.MapGet("/doc", async (string key) =>
{
    if (!SaveKeyRules.IsValid(key)) return Results.BadRequest(new { error = "not a save key" });
    Document? found = await store.ReadAsync(key);
    return found == null ? Results.NotFound() : Results.Ok(found);
});

app.MapPut("/doc", async (WriteRequest request) =>
{
    if (!SaveKeyRules.IsValid(request.Key)) return Results.BadRequest(new { error = "not a save key" });
    WriteResult result = await store.WriteAsync(request.Key, request.Body ?? "", request.Expected);
    return result.Written ? Results.Ok(result) : Results.Conflict(result);
});

// Several writes and deletes, all or none (a player's save: only the parts that changed, and the header).
app.MapPost("/batch", async (BatchRequest request) =>
{
    List<WriteRequest> writes = request.Writes ?? new List<WriteRequest>();
    List<string> deletes = request.Deletes ?? new List<string>();
    List<JsonElement> ledger = request.Ledger ?? new List<JsonElement>();
    if (writes.Count + deletes.Count + ledger.Count > 500) return Results.BadRequest(new { error = "too many in one batch" });
    foreach (WriteRequest write in writes)
        if (!SaveKeyRules.IsValid(write.Key)) return Results.BadRequest(new { error = "not a save key: " + write.Key });
    foreach (string key in deletes)
        if (!SaveKeyRules.IsValid(key)) return Results.BadRequest(new { error = "not a save key: " + key });
    BatchResult result = await store.BatchAsync(request.Id, writes, deletes, ledger);
    return result.Written ? Results.Ok(result) : Results.Conflict(result);
});

// The kept ledger (milestone 3): read for a GM, newest first; trimmed after the days the server keeps it.
app.MapGet("/ledger", async (string? account, long? since, int? limit) =>
{
    List<string> lines = await store.ReadLedgerAsync(account ?? "", since ?? 0, Math.Clamp(limit ?? 50, 1, 50000));
    return Results.Text("[" + string.Join(",", lines) + "]", "application/json");
});

app.MapPost("/ledger/prune", async (PruneRequest request) => Results.Ok(new { dropped = await store.PruneLedgerAsync(request.Before) }));

// Many documents in one trip.
app.MapPost("/read", async (ReadRequest request) =>
{
    List<string> keys = request.Keys ?? new List<string>();
    if (keys.Count > 500) return Results.BadRequest(new { error = "too many keys" });
    foreach (string key in keys)
        if (!SaveKeyRules.IsValid(key)) return Results.BadRequest(new { error = "not a save key: " + key });
    return Results.Ok(await store.ReadManyAsync(keys));
});

app.MapDelete("/doc", async (string key) =>
{
    if (!SaveKeyRules.IsValid(key)) return Results.BadRequest(new { error = "not a save key" });
    await store.DeleteAsync(key);
    return Results.Ok();
});

app.MapGet("/keys", async (string? prefix) => Results.Ok(await store.KeysAsync(prefix ?? "")));

app.MapPost("/lock", async (LockRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Owner) || string.IsNullOrWhiteSpace(request.Holder)) return Results.BadRequest(new { error = "owner and holder" });
    LockResult result = await store.LockAsync(request.Owner, request.Holder, Math.Clamp(request.Seconds, 10, 3600));
    return Results.Ok(result);
});

app.MapPost("/unlock", async (LockRequest request) =>
{
    await store.UnlockAsync(request.Owner ?? "", request.Holder ?? "");
    return Results.Ok();
});

Console.WriteLine($"Krytical Nova save service: database {database.Database} on {database.Host}:{database.Port}, " +
                  $"listening on {builder.Configuration["urls"] ?? urls}{(test ? " (TEST saves)" : "")}.");
await app.RunAsync();
return 0;
