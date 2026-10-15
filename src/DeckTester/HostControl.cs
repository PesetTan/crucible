using System.Security.Cryptography;
using System.Text;

namespace DeckTester;

public static class HostControl
{
    public static void WaitForLauncher()
    {
        if(!OperatingSystem.IsWindows()||Environment.GetEnvironmentVariable("CRUCIBLE_LAUNCH_EVENT") is not {} name)return;
        using var gate=EventWaitHandle.OpenExisting(name);
        if(!gate.WaitOne(TimeSpan.FromSeconds(30)))throw new InvalidOperationException("Desktop launcher did not establish process ownership.");
    }
    public static void Map(WebApplication app)
    {
        // Separate from browser credentials. Only the launcher that started this host has this secret.
        var owner=Environment.GetEnvironmentVariable("CRUCIBLE_HOST_OWNER");
        app.MapPost("/api/v1/host/shutdown",(HttpContext context,bool stopWork=false)=>{
            var supplied=context.Request.Headers["X-Crucible-Owner"].ToString();
            if(owner is null||owner.Length<32||!System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress??System.Net.IPAddress.None)
                ||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(owner),Encoding.UTF8.GetBytes(supplied)))
                return Results.StatusCode(403);
            var runs=app.Services.GetService<RunCoordinator>();
            var optimizer=app.Services.GetService<PilotOptimizer>();
            if(!stopWork&&(runs?.ActiveCount>0||runs?.QueuedCount>0||optimizer?.ActiveCount>0||optimizer?.QueuedCount>0))
                return Results.Conflict();
            // Finish the HTTP reply before stopping Kestrel and cancelling hosted workers.
            context.Response.OnCompleted(()=>{app.Lifetime.StopApplication();return Task.CompletedTask;});
            return Results.Accepted();
        });
    }
}
