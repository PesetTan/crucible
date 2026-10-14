namespace DeckTester;

// Both coordinators acquire this for their entire job, including child JVMs.
public sealed class SimulationExecutionGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);
    public async Task<IDisposable> Acquire(CancellationToken cancellation)
    {
        await semaphore.WaitAsync(cancellation);
        return new Lease(semaphore);
    }
    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) semaphore.Release(); }
    }
}
