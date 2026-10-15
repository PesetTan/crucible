using System.Diagnostics;
using System.Text;

namespace DeckTester;

// A pool belongs to one session and one immutable build/rules mode. A JVM never
// runs concurrent games. Failed attempts are discarded rather than returned.
internal sealed class ForgeWorkerPool(AppPaths paths, EngineBundle bundle, int capacity) : IAsyncDisposable
{
    private readonly SemaphoreSlim slots = new(capacity, capacity);
    private readonly object gate = new();
    private readonly Stack<ForgeWorker> idle = new();
    private readonly HashSet<ForgeWorker> workers = new();
    private bool disposed;

    public async Task<ForgeWorker> Rent(CancellationToken cancellation)
    {
        await slots.WaitAsync(cancellation);
        try {
            while (true) {
                ForgeWorker available;
                lock (gate) {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (!idle.TryPop(out available!)) {
                        var worker = new ForgeWorker(paths, bundle, persistent: true);
                        workers.Add(worker);
                        return worker;
                    }
                    if (available.CanReuse) return available;
                    workers.Remove(available);
                }
                await available.DisposeAsync();
            }
        } catch { slots.Release(); throw; }
    }

    public async Task Return(ForgeWorker worker, bool healthy)
    {
        var retain = false;
        lock (gate) {
            retain = !disposed && healthy && worker.CanReuse;
            if (retain) idle.Push(worker);
            else workers.Remove(worker);
        }
        try { if (!retain) await worker.DisposeAsync(); }
        finally { slots.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        ForgeWorker[] remaining;
        lock (gate) {
            if (disposed) return;
            disposed = true;
            remaining = workers.ToArray();
            workers.Clear(); idle.Clear();
        }
        foreach (var worker in remaining) await worker.DisposeAsync();
    }
}

internal sealed class ForgeWorker : IAsyncDisposable
{
    public Process Process { get; }
    public bool Persistent { get; }
    public int CompletedGames { get; set; }
    public long HeapUsedBytes { get; set; }
    public bool CanReuse => CompletedGames < 32 && HeapUsedBytes < 900L * 1024 * 1024 && !Process.HasExited;
    private readonly StringBuilder errors = new();
    private readonly Task readErrors;
    public Task DrainErrors => readErrors;
    private int disposed;

    public ForgeWorker(AppPaths paths, EngineBundle bundle, bool persistent)
    {
        Persistent = persistent;
        var info = new ProcessStartInfo(paths.Java) {
            WorkingDirectory = bundle.Directory, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-Xmx1200m", "-Djava.awt.headless=true", "-Dfile.encoding=UTF-8", "-Dsun.stdout.encoding=UTF-8", "-Dsun.stderr.encoding=UTF-8", "-Duser.home=" + Path.Combine(paths.Data, "engine-home"),
            "-cp", bundle.Bridge + Path.PathSeparator + bundle.Jar, "ForgeBridge" }) info.ArgumentList.Add(arg);
        if (persistent) info.ArgumentList.Add("--worker");
        Process = new Process { StartInfo = info };
        try {
            if (!Process.Start()) throw new InvalidOperationException("Could not launch the rules engine.");
        } catch { Process.Dispose(); throw; }
        readErrors = Task.Run(async () => {
            while (await Process.StandardError.ReadLineAsync() is { } line) {
                lock (errors) {
                    errors.AppendLine(line);
                    if (errors.Length > 16000) errors.Remove(0, errors.Length - 16000);
                }
            }
        });
    }

    public void ClearErrors() { lock (errors) errors.Clear(); }
    public string Errors { get { lock (errors) return errors.ToString(); } }
    public void Kill()
    {
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Kill();
        try { await Process.WaitForExitAsync(); await readErrors; }
        finally { Process.Dispose(); }
    }
}
