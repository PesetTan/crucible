using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace DeckTester;

public sealed record HostHealthSnapshot(int Version,string Application,string Role,int ProcessId,string InstanceId,
    DateTimeOffset StartedAt,string ProjectRoot,bool Ready,string Database,int ActiveRuns,int QueuedRuns,
    int ActiveOptimizations,int QueuedOptimizations,string? Error)
{
    public string RuntimeMode { get; init; }=System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported?"jit":"native-aot";
    public bool ReflectionJsonEnabled { get; init; }=System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault;
}

public static class HostHealth
{
    // No manifests, game rows, engine capability scans, or subprocesses on this route.
    public static void Map(WebApplication app,string mode)
    {
        var instance=Guid.NewGuid().ToString("N");
        var started=new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime());
        app.MapGet("/api/v1/health",(HttpContext context,AppPaths paths)=>{
            context.Response.Headers.CacheControl="no-store";
            var service=mode!="ui";
            var coordinator=service?app.Services.GetRequiredService<RunCoordinator>():null;
            var optimizer=service?app.Services.GetRequiredService<PilotOptimizer>():null;
            var database=service?CheckDatabase(Path.Combine(paths.Data,"simulator.sqlite")):null;
            var ready=!service||(database is null&&coordinator!.IsReady&&optimizer!.IsReady);
            var result=new HostHealthSnapshot(1,"crucible",mode,Environment.ProcessId,instance,started,paths.Root,ready,
                service?(database is null?"available":"unavailable"):"not-applicable",
                coordinator?.ActiveCount??0,coordinator?.QueuedCount??0,optimizer?.ActiveCount??0,optimizer?.QueuedCount??0,
                database??(ready?null:"Backend is initializing."));
            return Results.Json(result,Json.Info<HostHealthSnapshot>(),statusCode:ready?200:503);
        });
    }

    public static string? CheckDatabase(string path)
    {
        try {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=path,Mode=SqliteOpenMode.ReadOnly,DefaultTimeout=1,Pooling=false
            }.ToString());
            db.Open();using var command=db.CreateCommand();command.CommandTimeout=1;
            command.CommandText="SELECT count(*) FROM sqlite_schema WHERE type='table' AND name IN ('runs','slots','decks')";
            return Convert.ToInt64(command.ExecuteScalar())==3?null:"Database schema is unavailable.";
        }catch(Exception error)when(error is SqliteException or IOException or UnauthorizedAccessException){
            return "Database unavailable: "+error.Message;
        }
    }
}
