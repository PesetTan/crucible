using Microsoft.Data.Sqlite;

namespace DeckTester;

public sealed record UiRead<T>(T Value,string Tag);

// Share prepared reads across UI circuits. Compare data_version on the same
// read-only connections: commits from Store or an external writer invalidate it.
// No transaction remains open between requests, so WAL checkpoints can proceed.
public sealed class UiReadCache(AppPaths paths):IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,object> entries=[];
    private SqliteConnection? simulation;
    private SqliteConnection? source;
    private string? revision;
    private const int MaximumEntries=16;

    private static long Version(ref SqliteConnection? connection,string path)
    {
        if(!File.Exists(path))return -1;
        if(connection is null){
            connection=new(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=1}.ToString());
            connection.Open();
        }
        using var command=connection.CreateCommand();command.CommandText="PRAGMA data_version";
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static string Stamp(string path)
    {
        var file=new FileInfo(path);
        return file.Exists?$"{file.Length}:{file.LastWriteTimeUtc.Ticks}":"missing";
    }
    private string Revision()=>string.Join('|',Version(ref simulation,Path.Combine(paths.Data,"simulator.sqlite")),
        Version(ref source,paths.Source),Stamp(Path.Combine(paths.Data,"engine-bundles","active.json")),Stamp(paths.Java));

    public async Task<UiRead<T>> Read<T>(string key,Func<CancellationToken,Task<T>> build,CancellationToken cancellation=default)
    {
        await gate.WaitAsync(cancellation);
        try{
            var before=Revision();
            if(before!=revision){entries.Clear();revision=before;}
            if(entries.TryGetValue(key,out var cached))return (UiRead<T>)cached;
            var value=await build(cancellation);
            cancellation.ThrowIfCancellationRequested();
            var result=new UiRead<T>(value,"\""+DeckImporter.Hash(Json.Encode(value))+"\"");
            // A game can finish while a read is being assembled. Serve that read,
            // but never retain it as a snapshot of the newer database revision.
            if(Revision()==before){
                if(entries.Count>=MaximumEntries)entries.Remove(entries.Keys.First());
                entries[key]=result;
            }
            return result;
        }finally{gate.Release();}
    }
    public void Dispose(){simulation?.Dispose();source?.Dispose();gate.Dispose();}
}
