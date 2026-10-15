using Microsoft.Data.Sqlite;

namespace DeckTester;

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
            CREATE TABLE IF NOT EXISTS session_timings(id TEXT PRIMARY KEY, run_id TEXT NOT NULL REFERENCES runs(id), payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS session_timings_run ON session_timings(run_id);
            CREATE TABLE IF NOT EXISTS run_matchup_notes(run_id TEXT NOT NULL REFERENCES runs(id), deck_a TEXT NOT NULL, deck_b TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(run_id,deck_a,deck_b));
            """;
        cmd.ExecuteNonQuery();
        // Tier annotations are separate from immutable list/run fingerprints.
        cmd.CommandText = "PRAGMA table_info(decks)";
        var columns = new HashSet<string>();
        using (var reader = cmd.ExecuteReader()) while (reader.Read()) columns.Add(reader.GetString(1));
        foreach (var column in new[] { "archetype_tier", "tier_source_url", "tier_snapshot_date" })
            if (!columns.Contains(column)) { cmd.CommandText = $"ALTER TABLE decks ADD COLUMN {column} TEXT"; cmd.ExecuteNonQuery(); }
        if (!columns.Contains("friendly_name")) { cmd.CommandText = "ALTER TABLE decks ADD COLUMN friendly_name TEXT"; cmd.ExecuteNonQuery(); }
        if (!columns.Contains("strategy")) { cmd.CommandText = "ALTER TABLE decks ADD COLUMN strategy TEXT"; cmd.ExecuteNonQuery(); }
        if (!columns.Contains("subtype")) { cmd.CommandText = "ALTER TABLE decks ADD COLUMN subtype TEXT"; cmd.ExecuteNonQuery(); }
        cmd.CommandText = "SELECT id,json_extract(payload,'$.name') FROM decks WHERE strategy IS NULL";
        var unclassified = new List<(string Id,string Name)>();
        using (var reader = cmd.ExecuteReader()) while (reader.Read()) unclassified.Add((reader.GetString(0),reader.GetString(1)));
        foreach (var (id,name) in unclassified) if (DeckStrategy.ForName(name) is {} strategy) {
            cmd.Parameters.Clear(); cmd.CommandText = "UPDATE decks SET strategy=$strategy WHERE id=$id";
            cmd.Parameters.AddWithValue("$strategy",strategy); cmd.Parameters.AddWithValue("$id",id); cmd.ExecuteNonQuery();
        }
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT id,json_extract(payload,'$.name') FROM decks WHERE subtype IS NULL";
        var withoutSubtype = new List<(string Id,string Name)>();
        using (var reader = cmd.ExecuteReader()) while (reader.Read()) withoutSubtype.Add((reader.GetString(0),reader.GetString(1)));
        foreach (var (id,name) in withoutSubtype) if (DeckStrategy.SubtypeForName(name) is {} subtype) {
            cmd.Parameters.Clear(); cmd.CommandText = "UPDATE decks SET subtype=$subtype WHERE id=$id";
            cmd.Parameters.AddWithValue("$subtype",subtype); cmd.Parameters.AddWithValue("$id",id); cmd.ExecuteNonQuery();
        }
        // Repair classification lost by older Workshop saves, including nested copies.
        // Only fill missing annotations; immutable payloads and explicit labels stay intact.
        cmd.Parameters.Clear();
        cmd.CommandText = """
            UPDATE decks AS child SET
              strategy=COALESCE(child.strategy,(SELECT parent.strategy FROM decks AS parent WHERE parent.id=json_extract(child.payload,'$.parentDeckId'))),
              subtype=COALESCE(child.subtype,(SELECT parent.subtype FROM decks AS parent WHERE parent.id=json_extract(child.payload,'$.parentDeckId')))
            WHERE json_extract(child.payload,'$.sourceType')='custom'
              AND EXISTS(SELECT 1 FROM decks AS parent WHERE parent.id=json_extract(child.payload,'$.parentDeckId')
                AND ((child.strategy IS NULL AND parent.strategy IS NOT NULL) OR (child.subtype IS NULL AND parent.subtype IS NOT NULL)))
            """;
        while (cmd.ExecuteNonQuery() > 0) { }
        cmd.CommandText = "SELECT id FROM decks WHERE friendly_name IS NULL ORDER BY id";
        var unnamed = new List<string>();
        using (var reader = cmd.ExecuteReader()) while (reader.Read()) unnamed.Add(reader.GetString(0));
        foreach (var id in unnamed) {
            cmd.Parameters.Clear();
            cmd.CommandText = "UPDATE decks SET friendly_name=$name WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$name", NewFriendlyName(db));
            cmd.ExecuteNonQuery();
        }
        cmd.Parameters.Clear();
        cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS decks_friendly_name ON decks(friendly_name)";
        cmd.ExecuteNonQuery();
        var tierPath = Path.Combine(paths.Root, "docs", "archetype-tiers-2026-10-11.json");
        if (File.Exists(tierPath)) {
            using var snapshot = System.Text.Json.JsonDocument.Parse(File.ReadAllText(tierPath));
            var root = snapshot.RootElement;
            foreach (var rating in root.GetProperty("ratings").EnumerateObject()) {
                cmd.Parameters.Clear();
                cmd.CommandText = "UPDATE decks SET archetype_tier=$tier,tier_source_url=$url,tier_snapshot_date=$date WHERE json_extract(payload,'$.sourceType')='public' AND json_extract(payload,'$.name')=$name AND (tier_snapshot_date IS NULL OR tier_snapshot_date <= $date)";
                cmd.Parameters.AddWithValue("$tier", rating.Value.GetString()!);
                cmd.Parameters.AddWithValue("$url", root.GetProperty("sourceUrl").GetString()!);
                cmd.Parameters.AddWithValue("$date", root.GetProperty("snapshotDate").GetString()!);
                cmd.Parameters.AddWithValue("$name", rating.Name);
                cmd.ExecuteNonQuery();
            }
        }
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
    public List<DeckManifest> Decks()=>ReadDecks();
    private List<DeckManifest> ReadDecks(string? id=null)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload,archetype_tier,tier_source_url,tier_snapshot_date,friendly_name,strategy,subtype FROM decks"+(id is null?"":" WHERE id=$id");
        if(id is not null)cmd.Parameters.AddWithValue("$id",id);
        using var reader = cmd.ExecuteReader(); var decks = new List<DeckManifest>();
        while (reader.Read()) decks.Add(Json.Decode<DeckManifest>(reader.GetString(0)) with {
            ArchetypeTier = reader.IsDBNull(1) ? null : reader.GetString(1),
            TierSourceUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
            TierSnapshotDate = reader.IsDBNull(3) ? null : reader.GetString(3),
            FriendlyName = reader.IsDBNull(4) ? null : reader.GetString(4),
            Strategy = reader.IsDBNull(5) ? null : reader.GetString(5),
            Subtype = reader.IsDBNull(6) ? null : reader.GetString(6)
        });
        return decks;
    }
    public List<SimulationRun> Runs() => Read<SimulationRun>("runs").OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt).ToList();
    public List<HistoryRun> RunHistory()
    {
        using var db=Open();using var transaction=db.BeginTransaction();using var cmd=db.CreateCommand();
        cmd.Transaction=transaction;
        cmd.CommandText="SELECT payload FROM runs";
        var runs=new List<SimulationRun>();
        using(var reader=cmd.ExecuteReader())while(reader.Read())runs.Add(Json.Decode<SimulationRun>(reader.GetString(0)));
        var counts=new Dictionary<string,int[]>();
        var matchups=new Dictionary<string,List<HistoryMatchup>>();
        // Project only the outcome fields; never deserialize per-turn samples or attempts.
        cmd.CommandText="""
            WITH outcomes AS MATERIALIZED (
                SELECT run_id,json_extract(payload,'$.deckA') AS a,json_extract(payload,'$.deckB') AS b,
                    json_extract(payload,'$.status') AS status,
                    json_type(payload,'$.result')='object' AS has_result,
                    json_extract(payload,'$.result.winner') AS winner,
                    coalesce(json_extract(payload,'$.result.draw'),0) AS draw,
                    json_extract(payload,'$.cachedFromRunId') IS NOT NULL AS reused
                FROM slots
            )
            SELECT run_id,a,b,
                sum(status='completed'),sum(status='failed'),sum(status='cancelled'),sum(status='running'),sum(status='pending'),sum(reused),
                sum(status='completed' AND coalesce(has_result,0)),
                sum(status='completed' AND coalesce(has_result,0) AND coalesce(winner=a,0)),
                sum(status='completed' AND coalesce(has_result,0) AND coalesce(winner=b,0)),
                sum(status='completed' AND coalesce(has_result,0) AND draw)
            FROM outcomes GROUP BY run_id,a,b
            """;
        using(var reader=cmd.ExecuteReader())while(reader.Read()){
            var id=reader.GetString(0);
            if(!counts.TryGetValue(id,out var totals))counts[id]=totals=new int[6];
            for(var i=0;i<6;i++)totals[i]+=reader.GetInt32(i+3);
            if(!matchups.TryGetValue(id,out var pairs))matchups[id]=pairs=[];
            pairs.Add(new(reader.GetString(1),reader.GetString(2),reader.GetInt32(9),reader.GetInt32(10),reader.GetInt32(11),reader.GetInt32(12)));
        }
        cmd.CommandText="SELECT run_id,payload FROM session_timings";
        var timings=new Dictionary<string,List<SessionTiming>>();
        using(var reader=cmd.ExecuteReader())while(reader.Read()){
            var id=reader.GetString(0);
            if(!timings.TryGetValue(id,out var records))timings[id]=records=[];
            records.Add(Json.Decode<SessionTiming>(reader.GetString(1)));
        }
        return runs.OrderByDescending(r=>r.CreatedAt).Select(run=>{
            var totals=counts.GetValueOrDefault(run.Id)??new int[6];
            return new HistoryRun(new(run,totals[0],totals[1],totals[2],totals[3],totals[4],totals[5]),
                matchups.GetValueOrDefault(run.Id)??[],timings.GetValueOrDefault(run.Id)??[]);
        }).ToList();
    }
    public List<ActiveSessionProgress> ActiveSessionProgress()=>Runs().Where(r=>r.Status is "queued" or "running" or "cancelling").Select(SessionProgress).ToList();
    public ActiveSessionProgress SessionProgress(string id)=>SessionProgress(Run(id));
    public ActiveSessionProgress SessionProgress(SimulationRun run)
    {
            using var db=Open();using var cmd=db.CreateCommand();
            cmd.CommandText="""
                SELECT
                    coalesce(sum(json_extract(payload,'$.status')='completed'),0),
                    coalesce(sum(json_extract(payload,'$.status')='failed'),0),
                    coalesce(sum(json_extract(payload,'$.status')='cancelled'),0),
                    coalesce(sum(json_extract(payload,'$.status')='running'),0),
                    coalesce(sum(json_extract(payload,'$.status')='pending'),0),
                    coalesce(sum(json_extract(payload,'$.cachedFromRunId') IS NOT NULL),0)
                FROM slots WHERE run_id=$run
                """;
            cmd.Parameters.AddWithValue("$run",run.Id);using var reader=cmd.ExecuteReader();reader.Read();
            return new(run,reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5));
    }
    public List<SessionTiming> SessionTimings(string run)=>Read<SessionTiming>("session_timings",run).OrderBy(t=>t.StartedAt).ToList();
    public List<RunMatchupNote> RunMatchupNotes(string run)
    {
        _=Run(run);
        return Read<RunMatchupNote>("run_matchup_notes",run);
    }
    public async Task SaveRunMatchupNote(RunMatchupNote note)
    {
        var run=Run(note.RunId);
        if(note.DeckA==note.DeckB||!run.Config.DeckIds.Contains(note.DeckA)||!run.Config.DeckIds.Contains(note.DeckB))
            throw new ArgumentException("Note matchup must belong to the run.");
        await writer.WaitAsync();try{
            using var db=Open();using var cmd=db.CreateCommand();
            cmd.CommandText="INSERT INTO run_matchup_notes(run_id,deck_a,deck_b,payload) VALUES($run,$a,$b,$payload) ON CONFLICT(run_id,deck_a,deck_b) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$run",note.RunId);cmd.Parameters.AddWithValue("$a",note.DeckA);cmd.Parameters.AddWithValue("$b",note.DeckB);
            cmd.Parameters.AddWithValue("$payload",Json.Encode(note));cmd.ExecuteNonQuery();
        }finally{writer.Release();}
    }
    public List<SessionTimingSample> SessionTimingSamples(string engineHash,string adapterHash)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT r.payload,t.payload FROM session_timings t JOIN runs r ON r.id=t.run_id
            WHERE json_extract(t.payload,'$.status')='completed'
                AND json_extract(t.payload,'$.attemptCount')>=5
                AND json_extract(r.payload,'$.config.optimizerPolicy.id') IS NULL
                AND json_extract(r.payload,'$.config.engineHash')=$engine
                AND json_extract(r.payload,'$.config.adapterHash')=$adapter
            ORDER BY json_extract(t.payload,'$.finishedAt') DESC LIMIT 200
            """;
        cmd.Parameters.AddWithValue("$engine",engineHash);cmd.Parameters.AddWithValue("$adapter",adapterHash);
        using var reader=cmd.ExecuteReader();var samples=new List<SessionTimingSample>();
        while(reader.Read()){
            var run=Json.Decode<SimulationRun>(reader.GetString(0));var timing=Json.Decode<SessionTiming>(reader.GetString(1));
            samples.Add(new(run.Config,timing.ActiveSeconds,timing.ScheduledSeconds,timing.AttemptCount));
        }
        return samples;
    }
    public async Task SaveSessionTiming(SessionTiming timing)
    {
        await writer.WaitAsync();try{
            using var db=Open();using var cmd=db.CreateCommand();
            cmd.CommandText="INSERT INTO session_timings(id,run_id,payload) VALUES($id,$run,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$id",timing.Id);cmd.Parameters.AddWithValue("$run",timing.RunId);cmd.Parameters.AddWithValue("$payload",Json.Encode(timing));cmd.ExecuteNonQuery();
        }finally{writer.Release();}
    }
    public static bool SameCombination(IEnumerable<string> left,IEnumerable<string> right)=>left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal),StringComparer.Ordinal);
    private static bool SameProfiles(Dictionary<string,string>? left,Dictionary<string,string>? right) =>
        left is null || right is null ? left is null && right is null :
        left.Count==right.Count && left.All(pair=>right.TryGetValue(pair.Key,out var profile)&&profile==pair.Value);
    public static bool CompatibleSession(RunConfig left,RunConfig right)=>left.RootSeed==right.RootSeed&&left.EngineHash==right.EngineHash&&left.AdapterHash==right.AdapterHash&&left.PerformanceMode==right.PerformanceMode&&left.Pilot==right.Pilot&&left.OpeningPolicy==right.OpeningPolicy&&left.SeedScheme==right.SeedScheme&&left.AttributesHash==right.AttributesHash&&left.OptimizerPolicy?.Id==right.OptimizerPolicy?.Id&&left.ProtocolSet?.Hash==right.ProtocolSet?.Hash&&SameProfiles(left.PilotProfiles,right.PilotProfiles)&&SameCombination(left.DeckIds,right.DeckIds);
    public OptimizerPolicy PublishedOptimizerPolicy(string id)
    {
        if(id.Length!=64||id.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArgumentException("Invalid optimizer policy ID.");
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT payload FROM pilot_optimizer_published WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);
        var payload=cmd.ExecuteScalar() as string??throw new KeyNotFoundException("Optimizer policy has not been published.");
        var policy=Json.Decode<OptimizerPolicy>(payload);
        if(policy.Version!=1||policy.Id!=id)throw new InvalidDataException("Optimizer policy identity mismatch.");
        return policy;
    }
    public SimulationRun? ExistingSession(RunConfig config)=>Runs().Where(run=>CompatibleSession(run.Config,config)).OrderByDescending(run=>run.Config.GamesPerPair).FirstOrDefault();
    public static CachedGameKey CacheKey(GameSlot slot) => new(slot.DeckA,slot.DeckB,slot.Starter,slot.Seed,slot.Index);
    private static SqliteCommand CacheQuery(SqliteConnection db, RunConfig config, bool keysOnly)
    {
        var cmd=db.CreateCommand();
        var fields=keysOnly ? "json_extract(s.payload,'$.deckA'),json_extract(s.payload,'$.deckB'),json_extract(s.payload,'$.starter'),json_extract(s.payload,'$.seed'),json_extract(s.payload,'$.index')" : "s.payload";
        cmd.CommandText=$"""
            SELECT {fields} FROM slots s JOIN runs r ON r.id=s.run_id
            WHERE json_extract(r.payload,'$.config.rootSeed')=$seed
                AND json_extract(r.payload,'$.config.engineHash')=$engine
                AND json_extract(r.payload,'$.config.adapterHash')=$adapter
                AND coalesce(json_extract(r.payload,'$.config.performanceMode'),0)=$performance
                AND json_extract(r.payload,'$.config.pilot')=$pilot
                AND (($profiles='null' AND json_type(r.payload,'$.config.pilotProfiles') IS NULL)
                    OR (json_extract(r.payload,'$.config.pilotProfiles."' || json_extract(s.payload,'$.deckA') || '"')
                            = json_extract($profiles,'$."' || json_extract(s.payload,'$.deckA') || '"')
                        AND json_extract(r.payload,'$.config.pilotProfiles."' || json_extract(s.payload,'$.deckB') || '"')
                            = json_extract($profiles,'$."' || json_extract(s.payload,'$.deckB') || '"')))
                AND json_extract(r.payload,'$.config.openingPolicy')=$opening
                AND json_extract(r.payload,'$.config.seedScheme')=$scheme
                AND coalesce(json_extract(r.payload,'$.config.attributesHash'),'none')=$attributes
                AND coalesce(json_extract(r.payload,'$.config.optimizerPolicy.id'),'none')=$optimizer
                AND coalesce(json_extract(r.payload,'$.config.protocolSet.hash'),'none')=$protocols
                AND json_extract(s.payload,'$.status')='completed'
                AND json_extract(s.payload,'$.result') IS NOT NULL
                AND json_extract(s.payload,'$.attempts[#-1].status')='completed'
                AND json_extract(s.payload,'$.attempts[#-1].replay') IS NOT NULL
            ORDER BY json_extract(r.payload,'$.createdAt') DESC
            """;
        cmd.Parameters.AddWithValue("$seed",config.RootSeed);cmd.Parameters.AddWithValue("$engine",config.EngineHash);
        cmd.Parameters.AddWithValue("$adapter",config.AdapterHash);cmd.Parameters.AddWithValue("$performance",config.PerformanceMode?1:0);cmd.Parameters.AddWithValue("$pilot",config.Pilot);
        cmd.Parameters.AddWithValue("$profiles",config.PilotProfiles is null?"null":Json.Encode(config.PilotProfiles));
        cmd.Parameters.AddWithValue("$attributes",config.AttributesHash??"none");cmd.Parameters.AddWithValue("$opening",config.OpeningPolicy);cmd.Parameters.AddWithValue("$scheme",config.SeedScheme);
        cmd.Parameters.AddWithValue("$optimizer",config.OptimizerPolicy?.Id??"none");
        cmd.Parameters.AddWithValue("$protocols",config.ProtocolSet?.Hash??"none");
        return cmd;
    }
    public HashSet<CachedGameKey> CachedGameKeys(RunConfig config)
    {
        using var db=Open();using var cmd=CacheQuery(db,config,true);using var reader=cmd.ExecuteReader();
        var result=new HashSet<CachedGameKey>();
        while(reader.Read())result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetInt64(3),reader.GetInt32(4)));
        return result;
    }
    public Dictionary<CachedGameKey,GameSlot> CachedGames(RunConfig config)
    {
        using var db=Open();using var cmd=CacheQuery(db,config,false);using var reader=cmd.ExecuteReader();
        var result=new Dictionary<CachedGameKey,GameSlot>();
        while(reader.Read()) { var slot=Json.Decode<GameSlot>(reader.GetString(0));if(config.DeckIds.Contains(slot.DeckA)&&config.DeckIds.Contains(slot.DeckB))result.TryAdd(CacheKey(slot),slot); }
        return result;
    }
    public List<GameSlot> Slots(string run) => Read<GameSlot>("slots", run);
    public List<GameSlot> MatchupSlots(string run,string deck,string opponent)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT payload FROM slots WHERE run_id=$run AND (
                (json_extract(payload,'$.deckA')=$deck AND json_extract(payload,'$.deckB')=$opponent) OR
                (json_extract(payload,'$.deckA')=$opponent AND json_extract(payload,'$.deckB')=$deck))
            """;
        cmd.Parameters.AddWithValue("$run",run);cmd.Parameters.AddWithValue("$deck",deck);cmd.Parameters.AddWithValue("$opponent",opponent);
        using var reader=cmd.ExecuteReader();var slots=new List<GameSlot>();
        while(reader.Read())slots.Add(Json.Decode<GameSlot>(reader.GetString(0)));
        return slots;
    }
    public SessionTurnStats? SessionTurnStatistics()
    {
        using var db=Open();using var cmd=db.CreateCommand();
        // Only completed games establish an observed finished-game length.
        cmd.CommandText="""
            SELECT count(DISTINCT run_id),count(*),max(json_extract(payload,'$.result.actualTurns'))
            FROM slots
            WHERE json_extract(payload,'$.status')='completed'
                AND json_extract(payload,'$.cachedFromRunId') IS NULL
                AND json_extract(payload,'$.result.actualTurns')>0
            """;
        using var reader=cmd.ExecuteReader();
        return reader.Read()&&!reader.IsDBNull(2)?new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2)):null;
    }
    public List<TimingSample> TimingSamples(string engineHash,string adapterHash)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        // Read only timing fields, rather than loading every stored game's turn samples.
        cmd.CommandText="""
            SELECT json_extract(s.payload,'$.deckA'),json_extract(s.payload,'$.deckB'),
                json_extract(r.payload,'$.config.workers'),json_extract(r.payload,'$.config.timeoutSeconds'),
                json_extract(r.payload,'$.config.turnLimit'),
                (julianday(json_extract(s.payload,'$.attempts[#-1].finishedAt'))-
                 julianday(json_extract(s.payload,'$.attempts[#-1].startedAt')))*86400,
                coalesce(json_extract(r.payload,'$.config.staggerStarts'),0),
                json_extract(r.payload,'$.config.startupSpacingSeconds'),
                coalesce(json_extract(r.payload,'$.config.performanceMode'),0)
            FROM slots s JOIN runs r ON r.id=s.run_id
            WHERE json_extract(r.payload,'$.config.engineHash')=$engine
                AND json_extract(r.payload,'$.config.adapterHash')=$adapter
                AND json_extract(s.payload,'$.attempts[#-1].status') IN ('completed','failed')
                AND json_extract(r.payload,'$.config.optimizerPolicy.id') IS NULL
                AND json_extract(s.payload,'$.attempts[#-1].finishedAt') IS NOT NULL
                AND json_extract(s.payload,'$.cachedFromRunId') IS NULL
            ORDER BY json_extract(s.payload,'$.attempts[#-1].finishedAt') DESC LIMIT 2000
            """;
        cmd.Parameters.AddWithValue("$engine",engineHash);cmd.Parameters.AddWithValue("$adapter",adapterHash);
        using var reader=cmd.ExecuteReader();var samples=new List<TimingSample>();
        while(reader.Read())if(!reader.IsDBNull(5))samples.Add(new(reader.GetString(0),reader.GetString(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.GetDouble(5),reader.GetBoolean(6),reader.IsDBNull(7)?null:reader.GetDouble(7),reader.GetBoolean(8)));
        return samples;
    }
    public DeckManifest Deck(string id) => ReadDecks(id).SingleOrDefault() ?? throw new KeyNotFoundException("Deck not found.");
    public SimulationRun Run(string id)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT payload FROM runs WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);
        return cmd.ExecuteScalar() is string payload?Json.Decode<SimulationRun>(payload):throw new KeyNotFoundException("Run not found.");
    }
    public GameSlot Slot(string run, string id) => Slots(run).SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Game not found.");
    private static string NewFriendlyName(SqliteConnection db)
    {
        var adjectives = NameLibrary.Adjectives;
        var nouns = NameLibrary.Nouns;
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM decks WHERE friendly_name=$name";
        for (var attempt = 0; attempt < 1000; attempt++) {
            var name = $"{adjectives[System.Security.Cryptography.RandomNumberGenerator.GetInt32(adjectives.Count)]}-{nouns[System.Security.Cryptography.RandomNumberGenerator.GetInt32(nouns.Count)]}";
            cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$name", name);
            if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return name;
        }
        throw new InvalidOperationException("Could not assign a unique deck name.");
    }
    public async Task SaveDeck(DeckManifest deck)
    {
        await writer.WaitAsync(); try {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO decks(id,payload,friendly_name,strategy,subtype) VALUES($id,$payload,$name,$strategy,$subtype) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$id", deck.Id); cmd.Parameters.AddWithValue("$payload", Json.Encode(deck));
            cmd.Parameters.AddWithValue("$name", NewFriendlyName(db));
            cmd.Parameters.AddWithValue("$strategy", (object?)DeckStrategy.ForName(deck.Name) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$subtype", (object?)DeckStrategy.SubtypeForName(deck.Name) ?? DBNull.Value); cmd.ExecuteNonQuery();
        } finally { writer.Release(); }
    }
    public async Task SaveCustomDeck(DeckManifest deck)
    {
        await writer.WaitAsync();try{
            var parent=deck.ParentDeckId is {} parentId?Deck(parentId):null;
            using var db=Open();using var cmd=db.CreateCommand();
            cmd.CommandText="INSERT INTO decks(id,payload,friendly_name,strategy,subtype) VALUES($id,$payload,$name,$strategy,$subtype) ON CONFLICT(id) DO NOTHING";
            cmd.Parameters.AddWithValue("$id",deck.Id);cmd.Parameters.AddWithValue("$payload",Json.Encode(deck));cmd.Parameters.AddWithValue("$name",NewFriendlyName(db));
            cmd.Parameters.AddWithValue("$strategy",(object?)(deck.Strategy??parent?.Strategy??DeckStrategy.ForName(deck.Name))??DBNull.Value);
            cmd.Parameters.AddWithValue("$subtype",(object?)(deck.Subtype??parent?.Subtype??DeckStrategy.SubtypeForName(deck.Name))??DBNull.Value);cmd.ExecuteNonQuery();
        }finally{writer.Release();}
    }
    public async Task SaveRun(SimulationRun run) => await Save("runs", run.Id, Json.Encode(run));
    public async Task SaveSlot(GameSlot slot) => await Save("slots", slot.Id, Json.Encode(slot), slot.RunId);
    public async Task<List<string>> DeleteRun(string id)
    {
        await writer.WaitAsync();
        try {
            using var db=Open();using var tx=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=tx;
            cmd.Parameters.AddWithValue("$id",id);cmd.CommandText="SELECT payload FROM runs WHERE id=$id";
            var payload=cmd.ExecuteScalar() as string??throw new KeyNotFoundException("Session not found.");
            if(Json.Decode<SimulationRun>(payload).Status is "running" or "queued" or "cancelling")
                throw new InvalidOperationException("Pause the session and wait for it to stop before deleting it.");
            cmd.CommandText="SELECT payload FROM slots WHERE run_id=$id";
            var candidates=new HashSet<string>(StringComparer.Ordinal);
            using(var reader=cmd.ExecuteReader())while(reader.Read()){
                var slot=Json.Decode<GameSlot>(reader.GetString(0));
                foreach(var attempt in slot.Attempts){
                    var prefix=$"{slot.Id}-{attempt.Number}";
                    foreach(var suffix in new[]{".jsonl.gz",".jsonl.gz.partial",".incomplete.gz",".log"})candidates.Add(prefix+suffix);
                    if(attempt.Replay is {} replay)candidates.Add(replay);
                }
            }
            foreach(var table in new[]{"idempotency","session_timings","run_matchup_notes","slots","runs"}){
                cmd.CommandText=$"DELETE FROM {table} WHERE {(table=="runs"?"id":"run_id")}=$id";cmd.ExecuteNonQuery();
            }
            // Cached games may still reference the deleted session's replay files.
            cmd.CommandText="SELECT DISTINCT json_extract(a.value,'$.replay') FROM slots s,json_each(s.payload,'$.attempts') a WHERE json_extract(a.value,'$.replay') IS NOT NULL";
            using(var reader=cmd.ExecuteReader())while(reader.Read())candidates.Remove(reader.GetString(0));
            tx.Commit();return candidates.ToList();
        }finally{writer.Release();}
    }
    private async Task Save(string table, string id, string payload, string? run = null)
    {
        await writer.WaitAsync();
        try { using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = run is null ? $"INSERT INTO {table}(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload"
                : "INSERT INTO slots(id,run_id,payload) VALUES($id,$run,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$payload", payload); if (run is not null) cmd.Parameters.AddWithValue("$run", run); cmd.ExecuteNonQuery();
            if (table == "decks") {
                cmd.CommandText = "UPDATE decks SET archetype_tier=json_extract($payload,'$.archetypeTier'),tier_source_url=json_extract($payload,'$.tierSourceUrl'),tier_snapshot_date=json_extract($payload,'$.tierSnapshotDate') WHERE id=$id AND json_extract($payload,'$.archetypeTier') IS NOT NULL";
                cmd.ExecuteNonQuery();
            }
        } finally { writer.Release(); }
    }
    public async Task<string> CreateRun(SimulationRun run, List<GameSlot> slots, string key, string requestHash)
    {
        await writer.WaitAsync();
        try {
            using var db = Open(); using var tx = db.BeginTransaction(); using var lookup = db.CreateCommand(); lookup.Transaction = tx;
            lookup.CommandText = "SELECT request_hash,run_id FROM idempotency WHERE key=$key"; lookup.Parameters.AddWithValue("$key", key);
            using (var reader = lookup.ExecuteReader()) if (reader.Read()) { if (reader.GetString(0) != requestHash) throw new InvalidOperationException("Idempotency key was used for another configuration."); return reader.GetString(1); }
            SimulationRun? existing=null;
            using(var find=db.CreateCommand()){
                find.Transaction=tx;find.CommandText="SELECT payload FROM runs ORDER BY json_extract(payload,'$.config.gamesPerPair') DESC";
                using var reader=find.ExecuteReader();while(reader.Read()){
                    var candidate=Json.Decode<SimulationRun>(reader.GetString(0));
                    if(CompatibleSession(candidate.Config,run.Config)){existing=candidate;break;}
                }
            }
            if(existing is not null){
                if(existing.Status=="cancelling")throw new InvalidOperationException("This session is cancelling. Continue it after cancellation finishes.");
                // A smaller fully cached request opens stored results without restarting
                // unrelated unfinished games beyond the requested count.
                if(run.Config.GamesPerPair<=existing.Config.GamesPerPair&&slots.All(slot=>slot.Status=="completed")){
                    using var remember=db.CreateCommand();remember.Transaction=tx;remember.CommandText="INSERT INTO idempotency VALUES($key,$hash,$run)";
                    remember.Parameters.AddWithValue("$key",key);remember.Parameters.AddWithValue("$hash",requestHash);remember.Parameters.AddWithValue("$run",existing.Id);remember.ExecuteNonQuery();
                    tx.Commit();return existing.Id;
                }
                using var readSlots=db.CreateCommand();readSlots.Transaction=tx;readSlots.CommandText="SELECT payload FROM slots WHERE run_id=$run";readSlots.Parameters.AddWithValue("$run",existing.Id);
                var original=new List<GameSlot>();using(var reader=readSlots.ExecuteReader())while(reader.Read())original.Add(Json.Decode<GameSlot>(reader.GetString(0)));
                var incoming=slots.ToDictionary(CacheKey);var keys=original.Select(CacheKey).ToHashSet();var merged=new List<GameSlot>();
                foreach(var slot in original){
                    if(slot.Status=="completed"||slot.Status=="running")merged.Add(slot);
                    else if(incoming.TryGetValue(CacheKey(slot),out var reusable)&&reusable.Status=="completed")merged.Add(reusable with{Id=slot.Id,RunId=existing.Id});
                    else merged.Add(AttemptPolicy.CanResume(slot)?slot with{Status="pending"}:slot);
                }
                var additions=slots.Where(slot=>!keys.Contains(CacheKey(slot))).Select(slot=>slot with{RunId=existing.Id}).ToList();merged.AddRange(additions);
                var target=Math.Max(existing.Config.GamesPerPair,run.Config.GamesPerPair);
                var extensions=existing.Extensions?.ToList()??[];
                if(target>existing.Config.GamesPerPair)extensions.Add(new(DateTimeOffset.UtcNow,existing.Config.GamesPerPair,target,additions.Count,additions.Count(slot=>slot.CachedFromRunId is not null)));
                var status=existing.Status is "running" or "queued"?existing.Status:merged.Any(slot=>slot.Status=="pending")?"queued":merged.Any(slot=>slot.Result is null)?"failed":"completed";
                var updated=existing with{Config=existing.Config with{GamesPerPair=target},Total=merged.Count,Status=status,Error=status=="failed"?"Unfinished games have exhausted their attempt allowance.":null,Extensions=extensions,UpdatedAt=DateTimeOffset.UtcNow};
                using var save=db.CreateCommand();save.Transaction=tx;save.CommandText="UPDATE runs SET payload=$payload WHERE id=$id";save.Parameters.AddWithValue("$id",existing.Id);save.Parameters.AddWithValue("$payload",Json.Encode(updated));save.ExecuteNonQuery();
                save.CommandText="INSERT INTO slots(id,run_id,payload) VALUES($id,$run,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";save.Parameters.AddWithValue("$run",existing.Id);
                foreach(var slot in merged){save.Parameters["$id"].Value=slot.Id;save.Parameters["$payload"].Value=Json.Encode(slot);save.ExecuteNonQuery();}
                save.Parameters.Clear();save.CommandText="INSERT INTO idempotency VALUES($key,$hash,$run)";save.Parameters.AddWithValue("$key",key);save.Parameters.AddWithValue("$hash",requestHash);save.Parameters.AddWithValue("$run",existing.Id);save.ExecuteNonQuery();
                tx.Commit();return existing.Id;
            }
            using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO runs VALUES($id,$payload)";
            cmd.Parameters.AddWithValue("$id", run.Id); cmd.Parameters.AddWithValue("$payload", Json.Encode(run)); cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO slots VALUES($id,$run,$payload)"; cmd.Parameters.AddWithValue("$run", run.Id);
            foreach(var slot in slots) { cmd.Parameters["$id"].Value = slot.Id; cmd.Parameters["$payload"].Value = Json.Encode(slot); cmd.ExecuteNonQuery(); }
            cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO idempotency VALUES($key,$hash,$run)";
            cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$hash", requestHash); cmd.Parameters.AddWithValue("$run", run.Id); cmd.ExecuteNonQuery(); tx.Commit(); return run.Id;
        } finally { writer.Release(); }
    }
}
