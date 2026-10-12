using System.Collections.Concurrent;
using DeckTester;

static void Check(bool condition,string message){if(!condition)throw new Exception(message);}

// A fast worker must replace games while other workers are still in their initial delay.
var replacements=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var seen=new ConcurrentDictionary<int,int>();
var fast=GameScheduler.RunAsync(Enumerable.Range(0,12),4,TimeSpan.FromSeconds(1),(game,ct)=>{
    seen.AddOrUpdate(game,1,(_,count)=>count+1);
    if(game==1)replacements.TrySetResult();
    return Task.CompletedTask;
},CancellationToken.None);
await replacements.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
await fast.WaitAsync(TimeSpan.FromMilliseconds(500));
Check(seen.Count==12&&seen.Values.All(n=>n==1),"Every queued game must execute once, without waiting for unused startup offsets.");

// No more than the selected number of games may be active, even with immediate refills.
var firstWave=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var entered=0;var active=0;var maximum=0;var sync=new object();
var capped=GameScheduler.RunAsync(Enumerable.Range(0,20),4,TimeSpan.FromMilliseconds(30),async(game,ct)=>{
    lock(sync){active++;maximum=Math.Max(maximum,active);entered++;if(entered==4)firstWave.TrySetResult();}
    if(game<4)await release.Task.WaitAsync(ct);
    await Task.Yield();
    lock(sync){active--;}
},CancellationToken.None);
await firstWave.Task.WaitAsync(TimeSpan.FromSeconds(2));
Check(maximum==4,"Initial stagger must fill all four slots while games are busy.");
release.SetResult();await capped;
Check(entered==20&&maximum==4&&active==0,"Immediate refills must respect the concurrency cap.");

// Cancelling a run must cancel both a running game and workers waiting for initial startup.
using(var cancel=new CancellationTokenSource()){
    var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var launches=0;
    var pending=GameScheduler.RunAsync(Enumerable.Range(0,20),4,TimeSpan.FromSeconds(5),async(game,ct)=>{
        Interlocked.Increment(ref launches);started.TrySetResult();await Task.Delay(Timeout.Infinite,ct);
    },cancel.Token);
    await started.Task;cancel.Cancel();
    try{await pending.WaitAsync(TimeSpan.FromSeconds(1));throw new Exception("Cancellation was ignored.");}
    catch(OperationCanceledException){}
    Check(launches==1,"Cancellation must prevent delayed worker launches.");
}

// Empty runs and fatal callback errors must not wait for startup offsets.
await GameScheduler.RunAsync(Array.Empty<int>(),4,TimeSpan.FromSeconds(5),(_,_)=>throw new Exception("Empty queue launched a game."),CancellationToken.None);
try{
    await GameScheduler.RunAsync(Enumerable.Range(0,20),4,TimeSpan.FromSeconds(5),(_,_)=>throw new InvalidOperationException("expected"),CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    throw new Exception("Fatal game failure was ignored.");
}catch(InvalidOperationException error)when(error.Message=="expected"){}

// Estimate startup offsets once; a long run must not pay the spacing on every game.
var normal=Enumerable.Range(0,5).Select(_=>new TimingSample("A","B",4,180,150,10)).ToList();
var spaced=normal.Select(s=>s with{StaggerStarts=true,StartupSpacingSeconds=1}).ToList();
var plain=RuntimeEstimate.Calculate(["A","B"],100,4,180,150,false,15,5,normal)!;
var stagger=RuntimeEstimate.Calculate(["B","A"],100,4,180,150,true,15,5,spaced,1)!;
Check(stagger.LowerSeconds-plain.LowerSeconds==3&&stagger.UpperSeconds-plain.UpperSeconds==3,"Initial staggering adds only the startup ramp, not a run-wide throttle.");
Check(!stagger.Extrapolated&&stagger.Observations==5,"Matching stagger measurements must count each attempt.");
var old=RuntimeEstimate.Calculate(["A","B"],100,4,180,150,true,15,5,spaced.Select(s=>s with{StartupSpacingSeconds=null}).ToList(),null)!;
Check(old.LowerSeconds>900,"Old saved scheduling must retain its continuous-pacing estimate.");
Check(RuntimeEstimate.Calculate(["A","B"],100,4,180,150,true,15,5,normal,double.NaN)==null,"Invalid startup spacing must be rejected.");
Check(RuntimeEstimate.Calculate(["A","B"],3,4,180,150,false,15,5,normal)==null,"Odd game counts must be rejected.");
Check(RuntimeEstimate.Duration(90000)=="25 hr","Long estimates must not wrap at 24 hours.");

// Omitted settings distinguish old stored configurations from new requests.
var config=new RunConfig(["A","B"],10,2,180,150,42,true,"engine","adapter",StaggerStarts:true);
var oldJson=Json.Encode(config).Replace(",\"startupSpacingSeconds\":null","");
Check(Json.Decode<RunConfig>(oldJson).StartupSpacingSeconds is null,"Old sessions must deserialize with the original scheduling policy.");
var defaultRequest=Json.Decode<RunRequest>("{\"deckIds\":[\"A\",\"B\"]}");
Check(defaultRequest.StaggerStarts&&defaultRequest.Workers==2&&defaultRequest.StartupSpacingSeconds==1,"New requests must start two workers one second apart by default.");
Console.WriteLine("Scheduling checks passed: immediate refill, concurrency cap, startup cancellation, short/empty queues, errors, runtime estimates, and saved-session compatibility.");

// Session wall time calibrates only matching settings and deck versions.
var calibrationConfig=new RunConfig(["B","A"],100,4,180,150,42,true,"engine","adapter");
var sessions=new List<SessionTimingSample>{new(calibrationConfig,500,250,100)};
var calibrated=RuntimeEstimate.Calculate(["A","B"],100,4,180,150,false,15,5,normal,sessions:sessions)!;
Check(calibrated.SessionObservations==1&&calibrated.LowerSeconds==plain.LowerSeconds*2&&calibrated.UpperSeconds==plain.UpperSeconds*2,"Measured whole-session time must adjust the next comparable estimate.");
var foreign=RuntimeEstimate.Calculate(["A","C"],100,4,180,150,false,15,5,normal,sessions:sessions)!;
Check(foreign.SessionObservations==0,"A different combination must not inherit session calibration.");
var wrongWorkers=RuntimeEstimate.Calculate(["A","B"],100,2,180,150,false,15,5,normal,sessions:sessions)!;
Check(wrongWorkers.SessionObservations==0,"A different concurrency setting must not inherit calibration.");
var tooSmall=RuntimeEstimate.Calculate(["A","B"],100,4,180,150,false,15,5,normal,sessions:[new(calibrationConfig,500,250,2)])!;
Check(tooSmall.SessionObservations==0,"Cached-only or tiny execution periods must not calibrate estimates.");
Check(RuntimeEstimate.MeasuredSchedule([10,10,10,10,10,10,10,10],calibrationConfig)==20,"Measured scheduling must account for parallel work rather than summing engine seconds.");
Check(Json.Decode<SimulationRun>(Json.Encode(new SimulationRun("old",DateTimeOffset.UtcNow,"completed",calibrationConfig,100))).Id=="old","Older sessions remain readable without timing records.");
var stopped=new SessionTiming("timing","run",DateTimeOffset.UtcNow.AddDays(-3),DateTimeOffset.UtcNow.AddDays(-2),DateTimeOffset.UtcNow.AddDays(-2),120,10,100,"completed");
Check(RuntimeEstimate.SessionDuration([stopped])=="Active Runtime: 2 min","Time while paused must not inflate stored runtime.");
Console.WriteLine("Session timing checks passed: calibration, setting isolation, minimum samples, parallel scheduling, and pause exclusion.");

var pausedAttempts=Enumerable.Range(1,10).Select(n=>new Attempt(n,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,n%2==0?"interrupted":"cancelled",null,null,null)).ToList();
var pausedGame=new GameSlot("game","run","A","B","A",1,0,"cancelled",pausedAttempts);
Check(AttemptPolicy.CanResume(pausedGame),"Repeated pauses and service interruptions must not exhaust failure retries.");
var failures=Enumerable.Range(11,3).Select(n=>new Attempt(n,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"failed",null,null,null)).ToList();
Check(AttemptPolicy.CanResume(pausedGame with{Attempts=pausedAttempts.Concat(failures).ToList()}),"Three genuine failures still permit a retry after any number of pauses.");
Check(!AttemptPolicy.CanResume(pausedGame with{Attempts=pausedAttempts.Concat(failures).Append(failures[0] with{Number=14}).ToList()}),"Four genuine failures must stop retries.");
Console.WriteLine("Pause retry checks passed: pauses and interruptions do not consume the four-failure allowance.");
