using DeckTester;
using System.Diagnostics;
try {
    if(args.Length==0||args[0]=="--help") {Console.WriteLine("PyreImport --root CRUCIBLE_ROOT (--result FILE | --results-jsonl FILE) --metrics FILE --controller-metrics FILE");return 0;}
    if(args.Length%2!=0)throw new ArgumentException("Expected named arguments with values.");
    var options=new Dictionary<string,string>();for(var i=0;i<args.Length;i+=2)if(!options.TryAdd(args[i],args[i+1]))throw new ArgumentException("Duplicate argument.");
    foreach(var key in options.Keys)if(key is not ("--root" or "--result" or "--results-jsonl" or "--metrics" or "--controller-metrics"))throw new ArgumentException("Unknown argument: "+key);
    string Required(string key)=>options.GetValueOrDefault(key)??throw new ArgumentException("Required "+key);
    if(options.ContainsKey("--result")==options.ContainsKey("--results-jsonl"))throw new ArgumentException("Select exactly one result file or JSONL batch.");
    var clock=Stopwatch.StartNew();
    var metrics=File.ReadAllText(Required("--metrics"));var controllers=File.ReadAllText(Required("--controller-metrics"));
    IEnumerable<string> Payloads()=>options.ContainsKey("--result")?[File.ReadAllText(Required("--result"))]:File.ReadLines(Required("--results-jsonl"));
    var expected=0;foreach(var payload in Payloads()){_=PyreRecord.Parse(payload,metrics,controllers);expected++;} // Validate the entire input before opening caller-owned storage.
    if(expected==0)throw new ArgumentException("Empty batch.");
    var store=new Store(new AppPaths(Required("--root")));var inserted=0;var duplicates=0;PyreIngestReceipt? last=null;
    foreach(var payload in Payloads()){last=await store.ImportPyreResult(payload,metrics,controllers);if(last.Duplicate)duplicates++;else inserted++;}
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{contract="spark-import-measurement-v1",records=expected,inserted,duplicates,elapsed_seconds=clock.Elapsed.TotalSeconds,records_per_second=expected/clock.Elapsed.TotalSeconds,last}));return 0;
} catch(Exception error) when(error is ArgumentException or IOException or InvalidOperationException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException) {Console.Error.WriteLine(error.Message);return 2;}
