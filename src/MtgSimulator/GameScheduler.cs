using System.Collections.Concurrent;

namespace MtgSimulator;

public static class GameScheduler
{
    // Offset workers once at startup. Each worker then drains the queue without
    // pacing its replacements, so a free engine slot is never held idle.
    public static async Task RunAsync<T>(IEnumerable<T> games,int workers,TimeSpan startupSpacing,
        Func<T,CancellationToken,Task> play,CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers,1);
        ArgumentOutOfRangeException.ThrowIfLessThan(startupSpacing,TimeSpan.Zero);
        cancellation.ThrowIfCancellationRequested();
        var pending=new ConcurrentQueue<T>(games);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var ramp=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        async Task Worker(int index)
        {
            if(index>0&&startupSpacing>TimeSpan.Zero){
                try{await Task.Delay(startupSpacing*index,ramp.Token);}
                catch(OperationCanceledException)when(!stop.IsCancellationRequested){return;}
            }
            stop.Token.ThrowIfCancellationRequested();
            while(pending.TryDequeue(out var game)){
                stop.Token.ThrowIfCancellationRequested();
                // Do not wait for unused worker offsets after the queue is empty.
                if(pending.IsEmpty)ramp.Cancel();
                try{await play(game,stop.Token);}
                catch{stop.Cancel();throw;}
                stop.Token.ThrowIfCancellationRequested();
            }
        }
        await Task.WhenAll(Enumerable.Range(0,Math.Min(workers,pending.Count)).Select(Worker));
    }
}
