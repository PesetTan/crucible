using Microsoft.Data.Sqlite;

namespace MtgSimulator;

public sealed class Store
{
    private readonly string connection;
    private readonly SemaphoreSlim writer = new(1, 1);
    public Store(AppPaths paths)
    {
        connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(paths.Data, "simulator.sqlite"), DefaultTimeout = 30 }.ToString();
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS decks(id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS runs(id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS slots(id TEXT PRIMARY KEY, run_id TEXT NOT NULL REFERENCES runs(id), payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS slots_run ON slots(run_id);
            CREATE TABLE IF NOT EXISTS idempotency(key TEXT PRIMARY KEY, request_hash TEXT NOT NULL, run_id TEXT NOT NULL REFERENCES runs(id));
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connection); db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON;"; cmd.ExecuteNonQuery(); return db; }
    private List<T> Read<T>(string table, string? runId = null)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT payload FROM {table}" + (runId is null ? "" : " WHERE run_id=$run");
        if (runId is not null) cmd.Parameters.AddWithValue("$run", runId);
        using var reader = cmd.ExecuteReader(); var result = new List<T>();
        while (reader.Read()) result.Add(Json.Decode<T>(reader.GetString(0))); return result;
    }
    public List<DeckManifest> Decks() => Read<DeckManifest>("decks");
    public List<SimulationRun> Runs() => Read<SimulationRun>("runs").OrderByDescending(r => r.CreatedAt).ToList();
    public List<GameSlot> Slots(string run) => Read<GameSlot>("slots", run);
    public DeckManifest Deck(string id) => Decks().SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Deck not found.");
    public SimulationRun Run(string id) => Runs().SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Run not found.");
    public GameSlot Slot(string run, string id) => Slots(run).SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Game not found.");
    public async Task SaveDeck(DeckManifest deck) => await Save("decks", deck.Id, Json.Encode(deck));
    public async Task SaveRun(SimulationRun run) => await Save("runs", run.Id, Json.Encode(run));
    public async Task SaveSlot(GameSlot slot) => await Save("slots", slot.Id, Json.Encode(slot), slot.RunId);
    private async Task Save(string table, string id, string payload, string? run = null)
    {
        await writer.WaitAsync();
        try { using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = run is null ? $"INSERT INTO {table}(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload"
                : "INSERT INTO slots(id,run_id,payload) VALUES($id,$run,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$payload", payload); if (run is not null) cmd.Parameters.AddWithValue("$run", run); cmd.ExecuteNonQuery();
        } finally { writer.Release(); }
    }
    public async Task<string> CreateRun(SimulationRun run, List<GameSlot> slots, string key, string requestHash)
    {
        await writer.WaitAsync();
        try {
            using var db = Open(); using var tx = db.BeginTransaction(); using var lookup = db.CreateCommand(); lookup.Transaction = tx;
            lookup.CommandText = "SELECT request_hash,run_id FROM idempotency WHERE key=$key"; lookup.Parameters.AddWithValue("$key", key);
            using (var reader = lookup.ExecuteReader()) if (reader.Read()) { if (reader.GetString(0) != requestHash) throw new InvalidOperationException("Idempotency key was used for another configuration."); return reader.GetString(1); }
            using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO runs VALUES($id,$payload)";
            cmd.Parameters.AddWithValue("$id", run.Id); cmd.Parameters.AddWithValue("$payload", Json.Encode(run)); cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO slots VALUES($id,$run,$payload)"; cmd.Parameters.AddWithValue("$run", run.Id);
            foreach(var slot in slots) { cmd.Parameters["$id"].Value = slot.Id; cmd.Parameters["$payload"].Value = Json.Encode(slot); cmd.ExecuteNonQuery(); }
            cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO idempotency VALUES($key,$hash,$run)";
            cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$hash", requestHash); cmd.Parameters.AddWithValue("$run", run.Id); cmd.ExecuteNonQuery(); tx.Commit(); return run.Id;
        } finally { writer.Release(); }
    }
}
