using System.Diagnostics;

namespace MtgSimulator;

// Legacy saved sessions only. New sessions use GameScheduler's initial offsets.
// One shared gate per session spaces launches across all workers. A late launch
// resets the clock, so a busy engine never causes a burst of catch-up launches.
public sealed class StartPacer(TimeSpan interval) : IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    private long? lastStart;

    public async Task WaitAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(interval<=TimeSpan.Zero)return;
        await gate.WaitAsync(cancellation);
        try {
            if(lastStart is {} previous){
                var remaining=interval-Stopwatch.GetElapsedTime(previous);
                if(remaining>TimeSpan.Zero)await Task.Delay(remaining,cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            lastStart=Stopwatch.GetTimestamp();
        }finally{gate.Release();}
    }

    public void Dispose()=>gate.Dispose();
}
