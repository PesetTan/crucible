using DeckTester;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
if(args.Length<3)throw new ArgumentException("result.json metric-definitions.json controller-metrics.json [second-starter.json]");
var payload=File.ReadAllText(args[0]);var defs=File.ReadAllText(args[1]);var controllers=File.ReadAllText(args[2]);
var root=Path.Combine(Path.GetTempPath(),"crucible-pyre-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"docs","specs"));
var paths=new AppPaths(root);var store=new Store(paths);
var parsed=PyreRecord.Parse(payload,defs,controllers);var receipt=await store.ImportPyreResult(payload,defs,controllers);var duplicate=await store.ImportPyreResult(payload,defs,controllers);
Check(!receipt.Duplicate&&duplicate.Duplicate,"Exact duplicate must acknowledge without inserting another game.");
Check(store.Slots(receipt.RunId).Count==1,"Duplicate inflates denominator.");
Check(store.PyreRawRecord(receipt.RecordId)==payload,"Original raw values not retained byte for byte.");
Check(store.Run(receipt.RunId).Config.ProviderId=="pyre","Provider provenance missing.");
Check(store.Run(receipt.RunId).Config.Provider!.MetricDefinitionHash==parsed.MetricHash,"Accepted metric definition identity lost.");
static void RejectDefinition(string result,string metrics,string controller,string message){
    try{PyreRecord.Parse(result,metrics,controller);throw new Exception(message);}
    catch(InvalidDataException error){Check(error.Message.Contains("Metric definition mismatch"),"Expected the definition identity guard: "+error.Message);}
}
RejectDefinition(payload,defs+" ",controllers,"Modified metric bytes accepted.");
RejectDefinition(payload,defs,controllers+" ","Modified controller bytes accepted.");
// Rehash all enclosing identities as well as the definitions. Consistency alone
// must not admit unreviewed semantics under an existing version label.
var unknownDefinitions=defs+" ";
var unknownHash=PyreRecord.SemanticHash("k27-standard-metrics-v1",Encoding.UTF8.GetBytes(unknownDefinitions));
var unknown=JsonNode.Parse(WithManifestChange(payload,m=>m["metric_definition_hash"]=unknownHash))!;
unknown["raw_metrics"]!["definition_set_hash"]=new JsonArray(Convert.FromHexString(unknownHash).Select(b=>JsonValue.Create(b) as JsonNode).ToArray());
unknown["payload_hash"]="";unknown["payload_hash"]=Hash("spark-result-payload-v1",unknown);
RejectDefinition(unknown.ToJsonString(),unknownDefinitions,controllers,"Self-consistent unreviewed definition accepted.");
var unknownCohort=WithManifestChange(payload,m=>m["cohort"]="unreviewed-cohort");
try{PyreRecord.Parse(unknownCohort,defs,controllers);throw new Exception("Unknown cohort inherited White/Boros metadata.");}
catch(InvalidDataException error){Check(error.Message.Contains("Unsupported frozen Pyre cohort"),"Expected explicit unsupported cohort guard.");}
var summary=Metrics.Summarize(store.Run(receipt.RunId),store.Slots(receipt.RunId));Check(summary.Valid==1&&summary.Matchups[0].Valid==1,"Existing summary mapping failed.");
Check(summary.Mulligans.All(m=>m.RecordedPlayers==1),"Opening metrics not available to existing analyzer.");
var slot=store.Slots(receipt.RunId).Single();Check(slot.Result!.Samples.Any(s=>s.Artifacts>=0&&s.HandLands is not null&&s.HandSpells is not null),"Chart field mapping incomplete.");
Check(Metrics.WinRatesByLength([slot],slot.DeckA).Count==1,"Existing length chart did not consume Pyre game.");
Check(slot.Attempts.Single().Replay is null,"Summary replay must remain unavailable.");
var forged=JsonNode.Parse(payload)!.AsObject();forged["winner"]=forged["winner"]?.GetValue<string>()=="A"?"B":"A";
try{await store.ImportPyreResult(forged.ToJsonString(),defs,controllers);throw new Exception("Checksum forgery accepted.");}catch(InvalidDataException){}
var conflict=JsonNode.Parse(payload)!.AsObject();conflict["elapsed_nanoseconds"]=1234;conflict["payload_hash"]="";using(var d=JsonDocument.Parse(conflict.ToJsonString()))conflict["payload_hash"]=PyreRecord.SemanticHash("spark-result-payload-v1",PyreRecord.Canonical(d.RootElement));
try{await store.ImportPyreResult(conflict.ToJsonString(),defs,controllers);throw new Exception("Immutable record conflict accepted.");}catch(InvalidDataException){}
Check(store.Slots(receipt.RunId).Count==1,"Conflict changed existing result.");
var forge=store.Run(receipt.RunId).Config with{Provider=null};Check(forge.ProviderId=="forge"&&!Json.Encode(forge).Contains("\"provider\""),"Legacy Forge serialization changed.");Check(!Store.CompatibleSession(forge,store.Run(receipt.RunId).Config),"Cross-provider cache contamination.");
var otherExperiment=store.Run(receipt.RunId).Config with{Provider=store.Run(receipt.RunId).Config.Provider! with{ExperimentId="different"}};Check(!Store.CompatibleSession(otherExperiment,store.Run(receipt.RunId).Config),"Cross-experiment Pyre cache contamination.");
await using(var engine=new ForgeEngine(paths,store)){try{await engine.Play(store.Run(receipt.RunId),slot,1,CancellationToken.None);throw new Exception("Pyre dispatched through Forge.");}catch(InvalidOperationException error){Check(error.Message.Contains("Pyre"),"Forge dispatch guard must explain the incompatible provider.");}}
if(args.Length>3){var other=await store.ImportPyreResult(File.ReadAllText(args[3]),defs,controllers);Check(other.RunId==receipt.RunId,"Same experiment split into unrelated runs.");summary=Metrics.Summarize(store.Run(receipt.RunId),store.Slots(receipt.RunId));Check(summary.Valid==2&&summary.Matchups[0].PlayA==1&&summary.Matchups[0].PlayB==1,"Balanced starter mapping failed.");}
// Contract fixtures use rehashed synthetic identities to exercise delivery behavior;
// the baseline records above are actual native Pyre output.
static string Hash(string domain,JsonNode node){using var d=JsonDocument.Parse(node.ToJsonString());return PyreRecord.SemanticHash(domain,PyreRecord.Canonical(d.RootElement));}
static string WithManifestChange(string original,Action<JsonObject> change){
    var n=JsonNode.Parse(original)!.AsObject();var m=n["manifest"]!.AsObject();change(m);
    m["experiment_id"]="";m["experiment_id"]=Hash("spark-experiment-v1",m);n["experiment_id"]=m["experiment_id"]!.GetValue<string>();
    n["attempt_id"]=Hash("spark-attempt-v1",new JsonArray(n["experiment_id"]!.GetValue<string>(),n["slot_id"]!.GetValue<string>(),n["attempt"]!.GetValue<uint>()));
    n["record_id"]=Hash("spark-record-v1",new JsonArray(n["attempt_id"]!.GetValue<string>(),"spark-result-v1"));
    n["payload_hash"]="";n["payload_hash"]=Hash("spark-result-payload-v1",n);return n.ToJsonString();
}
static string Variant(string original,uint attempt,bool failed,bool otherExperiment=false){
    var n=JsonNode.Parse(original)!.AsObject();var m=n["manifest"]!.AsObject();
    if(otherExperiment){m["artifact_sha256"]=new string('a',64);m["experiment_id"]="";m["experiment_id"]=Hash("spark-experiment-v1",m);n["experiment_id"]=m["experiment_id"]!.GetValue<string>();}
    n["attempt"]=attempt;n["attempt_id"]=Hash("spark-attempt-v1",new JsonArray(n["experiment_id"]!.GetValue<string>(),n["slot_id"]!.GetValue<string>(),attempt));
    n["record_id"]=Hash("spark-record-v1",new JsonArray(n["attempt_id"]!.GetValue<string>(),"spark-result-v1"));
    if(failed){n["valid_for_analysis"]=false;n["had_pilot_failure"]=true;n["controller_metrics"]!["had_pilot_failure"]=true;var counters=n["controller_metrics"]!["players"]![0]!["selected"]!;counters["illegal_play_attempts"]=1;counters["submitted"]=counters["submitted"]!.GetValue<ulong>()+1;}
    n["payload_hash"]="";n["payload_hash"]=Hash("spark-result-payload-v1",n);return n.ToJsonString();
}
var otherPayload=Variant(payload,0,false,true);var otherRecord=await store.ImportPyreResult(otherPayload,defs,controllers);
Check(otherRecord.RunId!=receipt.RunId&&otherRecord.SlotId!=receipt.SlotId,"Paired slots from different experiments collided in the global Store.");
Check(store.Slots(receipt.RunId).Count==(args.Length>3?2:1),"Another experiment overwrote an existing slot.");
var retryRoot=Path.Combine(Path.GetTempPath(),"crucible-pyre-retry-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(retryRoot,"docs","specs"));var retryStore=new Store(new AppPaths(retryRoot));
var failedReceipt=await retryStore.ImportPyreResult(Variant(payload,1,true),defs,controllers);
Check(retryStore.Slots(failedReceipt.RunId).Single().Result is null,"Pilot failure entered analysis.");
var successReceipt=await retryStore.ImportPyreResult(payload,defs,controllers);
await retryStore.ImportPyreResult(Variant(payload,2,true),defs,controllers);
var retrySlot=retryStore.Slots(successReceipt.RunId).Single();Check(retrySlot.Status=="completed"&&retrySlot.Attempts.Count==3&&retrySlot.Result is not null,"Retry/late failure changed accepted denominator or erased attempts.");
try{await retryStore.ImportPyreResult(Variant(payload,3,false),defs,controllers);throw new Exception("Second terminal accepted for same slot.");}catch(InvalidDataException){}
Check(retryStore.Slots(successReceipt.RunId).Single().Attempts.Count==3,"Rejected second terminal mutated the slot.");
await store.DeleteRun(receipt.RunId);Check(store.PyreRawRecord(receipt.RecordId) is null,"Deleting a Pyre run left a stale duplicate receipt/raw record.");
var restored=await store.ImportPyreResult(payload,defs,controllers);Check(!restored.Duplicate&&store.Slots(restored.RunId).Count==1,"Explicit reimport after deletion must restore a consistent run.");
Console.WriteLine(Json.Encode(receipt));
Console.WriteLine("Isolated store: "+root);
Console.WriteLine("PASS: idempotence, conflicts, raw preservation, provider/experiment/cache isolation, failed attempts, retries, late delivery, summaries, openings, charts and deletion/reimport.");
