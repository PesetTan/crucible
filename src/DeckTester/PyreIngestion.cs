using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace DeckTester;

/// <summary>Narrow Spark importer; no service startup, Forge dispatch or engine database writes.</summary>
public sealed partial class Store
{
    public const string PyreAdapterVersion="spark-ingestion-v1";
    private static readonly Lazy<string> pyreAdapterHash=new(()=>{
        var path=Path.Combine(AppContext.BaseDirectory,"Crucible.Core.dll");
        if(!File.Exists(path))path=Environment.ProcessPath??throw new InvalidOperationException("Adapter artifact unavailable.");
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    });
    public static string PyreAdapterHash=>pyreAdapterHash.Value;
    public async Task<PyreIngestReceipt> ImportPyreResult(string payload,string metricDefinitions,string controllerDefinitions)
    {
        var parsed=PyreRecord.Parse(payload,metricDefinitions,controllerDefinitions);
        await writer.WaitAsync();
        try {
            using var db=Open(); using var tx=db.BeginTransaction(); using var cmd=db.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="""
                CREATE TABLE IF NOT EXISTS pyre_records(record_id TEXT PRIMARY KEY,attempt_id TEXT NOT NULL UNIQUE,slot_id TEXT NOT NULL,experiment_id TEXT NOT NULL,payload_hash TEXT NOT NULL,payload TEXT NOT NULL,eligible INTEGER NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS pyre_one_terminal_per_slot ON pyre_records(experiment_id,slot_id) WHERE eligible=1;
                CREATE TABLE IF NOT EXISTS pyre_definitions(hash TEXT PRIMARY KEY,payload TEXT NOT NULL);
                """;cmd.ExecuteNonQuery();
            cmd.CommandText="SELECT payload_hash FROM pyre_records WHERE record_id=$id";cmd.Parameters.AddWithValue("$id",parsed.RecordId);
            if(cmd.ExecuteScalar() is string prior){
                if(prior!=parsed.PayloadHash)throw new InvalidDataException("Conflicting payload under the same immutable Pyre record identity.");
                tx.Commit();return new(parsed.RecordId,prior,parsed.Run.Id,parsed.Slot.Id,true);
            }
            cmd.Parameters.Clear();cmd.CommandText="SELECT record_id FROM pyre_records WHERE experiment_id=$experiment AND slot_id=$slot AND eligible=1";
            cmd.Parameters.AddWithValue("$experiment",parsed.ExperimentId);cmd.Parameters.AddWithValue("$slot",parsed.SlotId);
            if(cmd.ExecuteScalar() is not null && parsed.Slot.Status=="completed")throw new InvalidDataException("Pyre slot already has an accepted terminal record; attempts cannot inflate its denominator.");
            foreach(var definition in new[]{(parsed.MetricHash,metricDefinitions),(parsed.ControllerHash,controllerDefinitions)}){
                cmd.Parameters.Clear();cmd.CommandText="SELECT payload FROM pyre_definitions WHERE hash=$hash";cmd.Parameters.AddWithValue("$hash",definition.Item1);
                if(cmd.ExecuteScalar() is string existing && existing!=definition.Item2)throw new InvalidDataException("Definition identity conflict.");
                cmd.CommandText="INSERT OR IGNORE INTO pyre_definitions VALUES($hash,$payload)";cmd.Parameters.AddWithValue("$payload",definition.Item2);cmd.ExecuteNonQuery();
            }
            foreach(var deck in parsed.Decks){
                cmd.Parameters.Clear();cmd.CommandText="SELECT payload FROM decks WHERE id=$id";cmd.Parameters.AddWithValue("$id",deck.Id);
                if(cmd.ExecuteScalar() is string existing){if(Json.Decode<DeckManifest>(existing).ListHash!=deck.ListHash)throw new InvalidDataException("Pyre deck identity conflict.");}
                else {
                    cmd.CommandText="INSERT INTO decks(id,payload,friendly_name) VALUES($id,$payload,$friendly)";cmd.Parameters.AddWithValue("$payload",Json.Encode(deck));cmd.Parameters.AddWithValue("$friendly","Pyre "+deck.Id);cmd.ExecuteNonQuery();
                }
            }
            cmd.Parameters.Clear();cmd.CommandText="SELECT payload FROM runs WHERE id=$id";cmd.Parameters.AddWithValue("$id",parsed.Run.Id);
            var priorRun=cmd.ExecuteScalar() as string;
            if(priorRun is not null && Json.Decode<SimulationRun>(priorRun).Config.Provider?.ExperimentId!=parsed.ExperimentId)throw new InvalidDataException("Provider/experiment identity conflict.");
            cmd.CommandText="INSERT OR IGNORE INTO runs(id,payload) VALUES($id,$payload)";cmd.Parameters.AddWithValue("$payload",Json.Encode(parsed.Run));cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();cmd.CommandText="SELECT payload FROM slots WHERE id=$id";cmd.Parameters.AddWithValue("$id",parsed.Slot.Id);
            var previousSlot=cmd.ExecuteScalar() as string;
            var normalizedSlot=parsed.Slot;
            if(previousSlot is not null){var oldSlot=Json.Decode<GameSlot>(previousSlot);normalizedSlot=(oldSlot.Status=="completed"?oldSlot:parsed.Slot) with{Attempts=oldSlot.Attempts.Concat(parsed.Slot.Attempts).OrderBy(a=>a.Number).ToList()};}
            cmd.Parameters.Clear();cmd.CommandText="INSERT INTO slots(id,run_id,payload) VALUES($id,$run,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$id",parsed.Slot.Id);cmd.Parameters.AddWithValue("$run",parsed.Run.Id);cmd.Parameters.AddWithValue("$payload",Json.Encode(normalizedSlot));cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();cmd.CommandText="INSERT INTO pyre_records VALUES($record,$attempt,$slot,$experiment,$hash,$payload,$eligible)";
            cmd.Parameters.AddWithValue("$record",parsed.RecordId);cmd.Parameters.AddWithValue("$attempt",parsed.AttemptId);cmd.Parameters.AddWithValue("$slot",parsed.SlotId);cmd.Parameters.AddWithValue("$experiment",parsed.ExperimentId);cmd.Parameters.AddWithValue("$hash",parsed.PayloadHash);cmd.Parameters.AddWithValue("$payload",payload);cmd.Parameters.AddWithValue("$eligible",parsed.Slot.Status=="completed"?1:0);cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();cmd.CommandText="SELECT COUNT(*) FROM slots WHERE run_id=$run";cmd.Parameters.AddWithValue("$run",parsed.Run.Id);
            var slotCount=Convert.ToInt32(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
            cmd.Parameters.Clear();cmd.CommandText="SELECT COUNT(*) FROM pyre_records WHERE experiment_id=$experiment AND eligible=1";cmd.Parameters.AddWithValue("$experiment",parsed.ExperimentId);
            var validCount=Convert.ToInt32(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
            var original=priorRun is null?parsed.Run:Json.Decode<SimulationRun>(priorRun);
            var updated=original with{CreatedAt=original.CreatedAt<parsed.Run.CreatedAt?original.CreatedAt:parsed.Run.CreatedAt,UpdatedAt=DateTimeOffset.UtcNow,Total=slotCount,Status=validCount==slotCount?"completed":"failed",Config=original.Config with{GamesPerPair=slotCount}};
            cmd.Parameters.Clear();cmd.CommandText="UPDATE runs SET payload=$payload WHERE id=$id";cmd.Parameters.AddWithValue("$payload",Json.Encode(updated));cmd.Parameters.AddWithValue("$id",parsed.Run.Id);cmd.ExecuteNonQuery();
            tx.Commit();return new(parsed.RecordId,parsed.PayloadHash,parsed.Run.Id,parsed.Slot.Id,false);
        } finally {writer.Release();}
    }
    public string? PyreRawRecord(string recordId){using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT payload FROM pyre_records WHERE record_id=$id";cmd.Parameters.AddWithValue("$id",recordId);return cmd.ExecuteScalar() as string;}
}

public sealed record PyreRecord(string RecordId,string PayloadHash,string AttemptId,string SlotId,string ExperimentId,string MetricHash,string ControllerHash,SimulationRun Run,GameSlot Slot,List<DeckManifest> Decks)
{
    static JsonElement Prop(JsonElement e,string name)=>e.GetProperty(name);
    static string Str(JsonElement e,string name)=>Prop(e,name).GetString()??throw new InvalidDataException("Missing "+name);
    static int Int(JsonElement e,string name)=>Prop(e,name).GetInt32();
    static string HexArray(JsonElement e)=>Convert.ToHexStringLower(e.EnumerateArray().Select(v=>v.GetByte()).ToArray());
    static int? Observed(JsonElement e)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty("Observed",out var n)?n.GetInt32():null;
    static string DeckId(JsonElement e)=>"pyre-deck-"+SemanticHash("spark-deck-v1",Canonical(e));
    public static PyreRecord Parse(string payload,string metricDefinitions,string controllerDefinitions)
    {
        if(Encoding.UTF8.GetByteCount(payload)>32*1024*1024)throw new InvalidDataException("Pyre result exceeds bounded ingestion size.");
        using var document=JsonDocument.Parse(payload,new JsonDocumentOptions{MaxDepth=96});var e=document.RootElement;
        if(Int(e,"schema_version")!=1||Str(e,"contract")!="spark-result-v1")throw new InvalidDataException("Unsupported Pyre result contract.");
        var root=JsonNode.Parse(payload)!.AsObject();var claimed=Str(e,"payload_hash");root["payload_hash"]="";
        using(var hashDoc=JsonDocument.Parse(root.ToJsonString()))if(SemanticHash("spark-result-payload-v1",Canonical(hashDoc.RootElement))!=claimed)throw new InvalidDataException("Pyre payload checksum mismatch.");
        var gameId=Prop(e,"game_id");if(gameId.GetArrayLength()!=16)throw new InvalidDataException("Invalid opaque game identity.");foreach(var b in gameId.EnumerateArray())_=b.GetByte();
        var m=Prop(e,"manifest");if(Int(m,"schema_version")!=1||Str(m,"contract")!="spark-run-v1")throw new InvalidDataException("Unsupported manifest contract.");
        var experiment=Str(e,"experiment_id");var manifestNode=JsonNode.Parse(m.GetRawText())!.AsObject();manifestNode["experiment_id"]="";
        using(var md=JsonDocument.Parse(manifestNode.ToJsonString()))if(SemanticHash("spark-experiment-v1",Canonical(md.RootElement))!=experiment||Str(m,"experiment_id")!=experiment)throw new InvalidDataException("Experiment identity mismatch.");
        var metricHash=Str(m,"metric_definition_hash");var controllerHash=Str(m,"controller_definition_hash");
        // Both reviewed definitions use the same fields mapped below. The newer
        // definition clarifies copied-source damage attribution; those richer
        // counters remain in the exact raw record, not Forge snapshot counters.
        // Keep explicit byte identities: a matching semantic label alone is insufficient.
        if(metricHash is not ("93f9a3ecca5db0f4eead790ba17bcb405e312caa75d9d2d59decec004976e204" or "98868ac750ab63a1b4113b3ff02374679d05a024ccc699aa639dfea5fd236895")||controllerHash!="06b078a6a5e8898e46c74a29712d39f8320d8dba3b8eeba47871709817f057d1"||SemanticHash("k27-standard-metrics-v1",Encoding.UTF8.GetBytes(metricDefinitions))!=metricHash||SemanticHash("spark-controller-attempts-v1",Encoding.UTF8.GetBytes(controllerDefinitions))!=controllerHash)throw new InvalidDataException("Metric definition mismatch.");
        using(var defs=JsonDocument.Parse(metricDefinitions))if(Str(defs.RootElement,"semanticVersion")!="k27-standard-metrics-v1")throw new InvalidDataException("Unsupported gameplay metric semantics.");
        using(var defs=JsonDocument.Parse(controllerDefinitions))if(Str(defs.RootElement,"semanticVersion")!="spark-controller-attempts-v1")throw new InvalidDataException("Unsupported controller metric semantics.");
        if(Str(m,"cohort")!="survey-white-boros-2026-10-15")throw new InvalidDataException("Unsupported frozen Pyre cohort in this MVP 1 importer.");
        var draft=Prop(m,"draft");var selections=Prop(m,"pilots").EnumerateArray().ToArray();var lists=Prop(draft,"decks").EnumerateArray().ToArray();if(lists.Length!=2||selections.Length!=2)throw new InvalidDataException("Two players required.");
        var starter=Int(e,"starter");if(starter is not (0 or 1))throw new InvalidDataException("Invalid starter.");
        var replicate=Prop(e,"replicate").GetUInt64();var attempt=Prop(e,"attempt").GetUInt32();
        var matchup=SemanticHash("spark-matchup-v1",Canonical(Prop(draft,"decks")));
        var slotValue=new JsonObject{["namespace"]=Str(draft,"namespace"),["master"]=Str(draft,"master_seed"),["matchup"]=new JsonArray(Convert.FromHexString(matchup).Select(v=>JsonValue.Create(v) as JsonNode).ToArray()),["starter"]=starter,["replicate"]=replicate};
        using var sd=JsonDocument.Parse(slotValue.ToJsonString());var slotId=SemanticHash("spark-slot-v1",Canonical(sd.RootElement));
        var attemptId=HashArray("spark-attempt-v1",experiment,slotId,attempt);var recordId=HashArray("spark-record-v1",attemptId,"spark-result-v1");
        if(Str(e,"slot_id")!=slotId||Str(e,"attempt_id")!=attemptId||Str(e,"record_id")!=recordId)throw new InvalidDataException("Slot/attempt/record identity mismatch.");
        var status=Str(e,"status");if(status is not ("rules_win" or "rules_draw" or "stopped" or "technical_failure"))throw new InvalidDataException("Unknown termination status.");
        if(status=="technical_failure"&&Prop(e,"technical_failure").ValueKind==JsonValueKind.Null)throw new InvalidDataException("Missing technical-failure diagnostic.");
        var raw=Prop(e,"raw_metrics");var controllers=Prop(e,"controller_metrics");
        if(HexArray(Prop(controllers,"definition_set_hash"))!=controllerHash||Str(controllers,"contract")!="spark-controller-attempts-v1")throw new InvalidDataException("Controller definition identity mismatch.");
        var failure=Prop(e,"had_pilot_failure").GetBoolean();if(Prop(controllers,"had_pilot_failure").GetBoolean()!=failure)throw new InvalidDataException("Pilot failure flag mismatch.");
        if(Prop(controllers,"players").GetArrayLength()!=2)throw new InvalidDataException("Two controller metric players required.");
        if(!Canonical(Prop(controllers,"selected_opponent_protocols")).AsSpan().SequenceEqual(Canonical(Prop(m,"opponent_protocols"))))throw new InvalidDataException("Opponent protocol configuration mismatch.");
        var totalFailures=0UL;
        var selectedNames=Prop(controllers,"selected_pilots").EnumerateArray().Select(v=>v.GetString()).ToArray();
        if(selectedNames.Length!=2||selectedNames.Where((name,i)=>name!=Str(selections[i],"id")).Any()||Str(controllers,"recovery_policy")!=Str(m,"recovery_policy"))throw new InvalidDataException("Pilot identity/recovery mismatch.");
        foreach(var player in Prop(controllers,"players").EnumerateArray())foreach(var source in new[]{"selected","default"}){
            var c=Prop(player,source);var illegal=Prop(c,"illegal_play_attempts").GetUInt64();var stale=Prop(c,"stale_responses").GetUInt64();var errors=Prop(c,"engine_errors").GetUInt64();
            if(Prop(c,"submitted").GetUInt64()!=checked(Prop(c,"accepted").GetUInt64()+illegal+stale+errors))throw new InvalidDataException("Controller counter inconsistency.");totalFailures=checked(totalFailures+illegal+stale);
        }
        if(failure!=(totalFailures>0))throw new InvalidDataException("Illegal/stale attempts must retain the pilot-failure flag.");
        var valid=Prop(e,"valid_for_analysis").GetBoolean();var complete=Str(e,"metric_capture_status")=="complete";
        if(valid&&(!(status is "rules_win" or "rules_draw")||failure||!complete))throw new InvalidDataException("Invalid analysis eligibility.");
        var turns=Int(e,"game_turn_number");var personal=Prop(e,"player_turn_number").EnumerateArray().Select(v=>v.GetInt32()).ToArray();if(personal.Length!=2||personal.Sum()!=turns||turns<0)throw new InvalidDataException("Invalid turn axes.");
        var ids=lists.Select(DeckId).ToArray();var names=new[]{"White Aggro","Boros Aggro"};
        var decks=new List<DeckManifest>();var provenance=Prop(m,"deck_provenance").EnumerateArray().ToArray();
        if(provenance.Length!=2)throw new InvalidDataException("Missing frozen deck provenance.");
        for(var i=0;i<2;i++){
            var cards=Json.Decode<List<CardEntry>>(Prop(provenance[i],"cards").GetRawText());
            if(cards.Sum(c=>c.Quantity)!=60||!cards.All(c=>c.LocallyLegal)||cards.GroupBy(c=>c.Name).ToDictionary(g=>g.Key,g=>g.Sum(c=>c.Quantity)).OrderBy(k=>k.Key,StringComparer.Ordinal).Select(k=>(k.Key,k.Value)).SequenceEqual(lists[i].EnumerateArray().Select(c=>(Str(c,"name"),Int(c,"count"))).OrderBy(k=>k.Item1,StringComparer.Ordinal))==false)throw new InvalidDataException("Deck provenance differs from frozen list.");
            decks.Add(new(ids[i],names[i],"pyre-frozen","snapshot-2026-10-15",ids[i],"2026-10-11",null,ids[i],SemanticHash("spark-deck-provenance-v1",Canonical(provenance[i])),DateTimeOffset.UnixEpoch,cards,"Frozen Pyre cohort; legality snapshot 2026-10-15, not current Arena ownership."));
        }
        var samples=new List<TurnSample>();var openings=new List<OpeningHand>();
        if(raw.ValueKind==JsonValueKind.Object){
            if(!Canonical(Prop(raw,"game")).AsSpan().SequenceEqual(Canonical(Prop(e,"game_id")))||HexArray(Prop(raw,"definition_set_hash"))!=metricHash||Int(raw,"game_turn_number")!=turns||!Prop(raw,"player_turn_number").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(personal))throw new InvalidDataException("Raw gameplay identity/turn mismatch.");
            var inputs=Prop(raw,"inputs");if(Str(inputs,"cohort")!=Str(m,"cohort")||HexArray(Prop(inputs,"catalog_sha256"))!=Str(m,"catalog_sha256")||HexArray(Prop(inputs,"rules_sha256"))!=Str(m,"rules_sha256"))throw new InvalidDataException("Frozen rules/catalog mismatch.");
            if(complete!=(Prop(raw,"capture_status").ValueKind==JsonValueKind.String&&Prop(raw,"capture_status").GetString()=="Complete"))throw new InvalidDataException("Capture status mismatch.");
            var rawStatus=Prop(raw,"status");
            if(status=="rules_win"&&(!rawStatus.TryGetProperty("Rules",out var rules)||rules.ValueKind!=JsonValueKind.Object||Str(rules,"Winner")!=Prop(e,"winner").GetString()))throw new InvalidDataException("Raw winner mismatch.");
            if(status=="rules_draw"&&(!rawStatus.TryGetProperty("Rules",out rules)||rules.ValueKind!=JsonValueKind.String||rules.GetString()!="Draw"))throw new InvalidDataException("Raw draw mismatch.");
            if(status=="stopped"&&!rawStatus.TryGetProperty("Stopped",out _))throw new InvalidDataException("Raw administrative stop mismatch.");
            if(status=="technical_failure"&&Prop(e,"technical_failure").ValueKind==JsonValueKind.Null)throw new InvalidDataException("Missing technical-failure diagnostic.");
            if(raw.TryGetProperty("data",out var data)&&data.TryGetProperty("Observed",out var facts)){
                if(!Canonical(Prop(facts,"samples")).AsSpan().SequenceEqual(Canonical(Prop(e,"turn_samples"))))throw new InvalidDataException("Projected samples differ from raw metric rows.");
                var previous=-1L;foreach(var row in Prop(e,"turn_samples").EnumerateArray()){
                    var sequence=Prop(row,"sequence").GetInt64();if(sequence<=previous)throw new InvalidDataException("Nonmonotonic sample sequence.");previous=sequence;
                    var player=Str(row,"player");if(player is not ("A" or "B"))throw new InvalidDataException("Invalid sample player.");
                    if(Str(row,"point")=="Initial")continue;
                    samples.Add(new(Int(row,"game_turn_number"),Int(row,"player_turn_number"),player,Int(row,"life"),Int(row,"hand_cards"),Int(row,"lands"),Int(row,"creatures"),checked((int)Prop(row,"total_creature_power").GetInt64()),Int(row,"graveyard_cards"),Int(row,"library_cards"),Int(row,"artifacts"),Int(row,"enchantments"),Int(row,"hand_lands"),Int(row,"hand_nonlands")));
                }
                var opening=Prop(facts,"opening");var mulligans=Prop(opening,"mulligans").EnumerateArray().ToArray();var kept=Prop(opening,"kept_hand_size").EnumerateArray().ToArray();
                for(var i=0;i<2;i++)if(Observed(mulligans[i]) is int mull&&Observed(kept[i]) is int size)openings.Add(new(i==0?"A":"B",mull,size,null,"Pyre committed opening metrics; no inferred policy-acceptance flag."));
            }else if(valid)throw new InvalidDataException("Valid result lacks observed metric data.");
        }else if(valid)throw new InvalidDataException("Valid result lacks gameplay metrics.");
        // Existing chart records have one row per player/turn. Keep the last actual
        // capture point; preserve every raw point, including initial, above.
        samples=samples.GroupBy(s=>(s.ActualTurn,s.Player)).Select(g=>g.Last()).OrderBy(s=>s.ActualTurn).ThenBy(s=>s.Player,StringComparer.Ordinal).ToList();
        var winner=Prop(e,"winner").ValueKind==JsonValueKind.Null?null:Prop(e,"winner").GetString();if(status=="rules_win"&&winner is not ("A" or "B")||status!="rules_win"&&winner is not null)throw new InvalidDataException("Winner/status mismatch.");
        var result=new GameResult(winner=="A"?ids[0]:winner=="B"?ids[1]:null,status=="rules_draw",status,turns,new(){{"A",personal[0]},{"B",personal[1]}},checked((long)(Prop(e,"elapsed_nanoseconds").GetUInt64()/1_000_000)),samples,null,null,openings,"pyre-k27-opening-v1");
        var profiles=new Dictionary<string,string>{{ids[0],Str(selections[0],"id")},{ids[1],Str(selections[1],"id")}};
        var limits=Prop(draft,"limits");
        var timeout=Prop(limits,"max_elapsed_ms").ValueKind==JsonValueKind.Null?0:checked((int)((Prop(limits,"max_elapsed_ms").GetUInt64()+999)/1000));
        var config=new RunConfig(ids.ToList(),1,1,timeout,Int(limits,"max_game_turns"),BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(SemanticHash("spark-legacy-root-label-v1",Encoding.UTF8.GetBytes(Str(draft,"master_seed"))))),Str(draft,"capture")=="full",Str(m,"artifact_sha256"),Store.PyreAdapterHash,"pyre-selected","London; no Arena smoothing","pyre-full-seed-in-raw;legacy-int64-label-v1",PilotProfiles:profiles){Provider=new("pyre",Store.PyreAdapterVersion,metricHash,controllerHash,experiment)};
        var started=DateTimeOffset.FromUnixTimeMilliseconds(checked((long)Prop(e,"driver_started_at_unix_ms").GetUInt64()));
        var runId="pyre-"+experiment;var run=new SimulationRun(runId,started,valid?"completed":"failed",config,1);
        var slot=new GameSlot("pyre-"+SemanticHash("spark-crucible-slot-v1",Encoding.UTF8.GetBytes(experiment+":"+slotId)),runId,ids[0],ids[1],ids[starter],BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(slotId)),checked((int)replicate),valid?"completed":"failed",[new(checked((int)attempt)+1,started,DateTimeOffset.FromUnixTimeMilliseconds(checked((long)Prop(e,"finished_at_unix_ms").GetUInt64())),valid?"completed":"failed",valid?null:"Pyre non-analysis result; inspect immutable raw record.",null,null)],valid?result:null);
        return new(recordId,claimed,attemptId,slotId,experiment,metricHash,controllerHash,run,slot,decks);
    }
    static string HashArray(string domain,params object[] fields){var b=new StringBuilder("[");for(var i=0;i<fields.Length;i++){if(i>0)b.Append(',');if(fields[i] is string text)Quote(b,text);else if(fields[i] is uint n)b.Append(n.ToString(CultureInfo.InvariantCulture));else throw new InvalidDataException("Unsupported identity field.");}b.Append(']');return SemanticHash(domain,Encoding.UTF8.GetBytes(b.ToString()));}
    public static string SemanticHash(string domain,byte[] bytes){using var stream=new MemoryStream();Span<byte> length=stackalloc byte[8];foreach(var part in new[]{Encoding.UTF8.GetBytes(domain),bytes}){BinaryPrimitives.WriteUInt64BigEndian(length,(ulong)part.Length);stream.Write(length);stream.Write(part);}return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));}
    public static byte[] Canonical(JsonElement e){var b=new StringBuilder();Append(b,e);return Encoding.UTF8.GetBytes(b.ToString());}
    static void Quote(StringBuilder b,string text){b.Append('"');foreach(var c in text)switch(c){case '"':b.Append("\\\"");break;case '\\':b.Append("\\\\");break;case '\n':b.Append("\\n");break;case '\r':b.Append("\\r");break;case '\t':b.Append("\\t");break;case '\b':b.Append("\\b");break;case '\f':b.Append("\\f");break;default:if(c<32)b.Append("\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture));else b.Append(c);break;}b.Append('"');}
    static void Append(StringBuilder b,JsonElement e){switch(e.ValueKind){case JsonValueKind.Object:b.Append('{');var first=true;var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject().OrderBy(p=>p.Name,Comparer<string>.Create((a,z)=>Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(z))))){if(!seen.Add(p.Name))throw new InvalidDataException("Duplicate JSON field.");if(!first)b.Append(',');first=false;Quote(b,p.Name);b.Append(':');Append(b,p.Value);}b.Append('}');break;case JsonValueKind.Array:b.Append('[');first=true;foreach(var v in e.EnumerateArray()){if(!first)b.Append(',');first=false;Append(b,v);}b.Append(']');break;case JsonValueKind.String:Quote(b,e.GetString()!);break;default:b.Append(e.GetRawText());break;}}
}
