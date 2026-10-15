using System.Text.Json;

// Executable transport fixture: no shebang/file-association dependency on Windows.
var worker=args.Contains("--worker");
while(Console.ReadLine() is {} line){
    using var request=JsonDocument.Parse(line);
    var r=request.RootElement;var seed=r.GetProperty("seed").GetInt64();var attempt=r.GetProperty("attemptId").GetString();var seq=0;
    var log=JsonSerializer.Serialize(new{pid=Environment.ProcessId,attempt,worker})+"\n";
    // Several worker processes can append concurrently, including on Windows with mandatory sharing.
    using var logGate=new Mutex(false,"forge-fixture-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Directory.GetCurrentDirectory()))));
    logGate.WaitOne();
    try{
    using(var file=new FileStream("requests.log",FileMode.Append,FileAccess.Write,FileShare.ReadWrite)){
        var bytes=System.Text.Encoding.UTF8.GetBytes(log);file.Write(bytes);
    }
    }finally{logGate.ReleaseMutex();}
    void Emit(string kind,params (string,object?)[] values){
        var record=new Dictionary<string,object?>{{"kind",kind},{"sequence",++seq},{"actualTurn",0},{"attemptId",attempt}};
        foreach(var (key,value) in values)record[key]=value;
        Console.WriteLine(JsonSerializer.Serialize(record));
    }
    if(seed is 5 or 6)await Task.Delay(5000);
    if(seed==7)return;
    if(seed==2){Emit("attempt_failed",("reason","Controlled engine failure"));return;}
    if(seed==9)await Task.Delay(100);
    Emit("game_started",("starter",r.GetProperty("starter").Clone()));
    foreach(var player in new[]{"A","B"})Emit("opening_hand",("player",player),("mulligans",0),("keptHandSize",7),("policyAccepted",true));
    Emit("game_finished",("winner","A"),("draw",false),("reason","AllOpponentsLost"),("detail","DurationMs=1"));
    if(seed==3)Emit("game_finished",("winner","A"),("draw",false),("reason","Duplicate"));
    if(worker)Console.WriteLine(JsonSerializer.Serialize(new{kind="worker_complete",attemptId=seed==4?"wrong":attempt,protocolVersion=1,heapUsedBytes=seed==8?1000000000:100}));
    else return;
}
