using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DeckTester;

public sealed record ResourceSnapshot(int LogicalProcessors, long? AvailableMemoryBytes);
public sealed record CapacityBadge(string Status, string Label, string Explanation);

// Advisory capacity estimate, independent of engine/rules preflight. Never blocks a run.
public sealed class ResourceCapacity
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ResourceSnapshot? cached;
    private DateTimeOffset sampledAt;

    public async Task<ResourceSnapshot> Sample(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            if (cached is not null && DateTimeOffset.UtcNow - sampledAt < TimeSpan.FromSeconds(10)) return cached;
            long? available = null;
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    using var process = Process.Start(new ProcessStartInfo("/usr/bin/vm_stat")
                    {
                        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
                    });
                    if (process is not null)
                    {
                        try
                        {
                            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                            await process.WaitForExitAsync(timeout.Token);
                            if (process.ExitCode == 0) available = MacAvailableMemory(output);
                        }
                        finally { if (!process.HasExited) process.Kill(); }
                    }
                }
                else if (OperatingSystem.IsLinux())
                {
                    var output = await File.ReadAllTextAsync("/proc/meminfo", cancellation);
                    var match = Regex.Match(output, @"(?m)^MemAvailable:\s+(\d+)\s+kB");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out var kb)) available = checked(kb * 1024);
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or OverflowException) { }
            cancellation.ThrowIfCancellationRequested();
            cached = new(Environment.ProcessorCount, available);
            sampledAt = DateTimeOffset.UtcNow;
            return cached;
        }
        finally { gate.Release(); }
    }

    public static long? MacAvailableMemory(string output)
    {
        var page = Regex.Match(output, @"page size of (\d+) bytes");
        if (!page.Success || !long.TryParse(page.Groups[1].Value, out var pageSize)) return null;
        long pages = 0;
        // Inactive and speculative pages are reclaimable estimates, not guaranteed free RAM.
        foreach (var name in new[] { "free", "inactive", "speculative" })
        {
            var match = Regex.Match(output, @"(?m)^Pages " + name + @":\s+(\d+)\.");
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out var count)) return null;
            pages = checked(pages + count);
        }
        return checked(pages * pageSize);
    }

    public static CapacityBadge Assess(ResourceSnapshot? snapshot, int workers)
    {
        if (snapshot is null)
            return new("unknown", "Checking…", "Checking memory and CPU capacity for this setting.");
        if (snapshot.AvailableMemoryBytes is not {} memory)
            return new("unknown", "Check Unavailable", "Available memory could not be read. Try fewer parallel games first.");
        // 1,200 MiB Java heap plus an estimated 400 MiB overhead per game; leave 1 GiB spare.
        var required = (workers * 1600L + 1024) * 1024 * 1024;
        var fits = memory >= required && snapshot.LogicalProcessors >= workers;
        var details = string.Create(CultureInfo.InvariantCulture, $"{workers} {(workers == 1 ? "game" : "games")}: ~{required / 1073741824.0:0.0} GB budget; ~{memory / 1073741824.0:0.0} GB available, {snapshot.LogicalProcessors} CPU threads.");
        return fits
            ? new("fits", "Likely Fits", details + " Estimate only; speed varies.")
            : new("tight", "Limited Headroom", details + " Estimate; try fewer games.");
    }
}
