using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using DeckTester;
using Microsoft.Data.Sqlite;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var source=Path.GetFullPath(args.Single());
var sandbox=Path.Combine(Path.GetTempPath(),"Crucible beta Ω "+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
try{
    var assets=Path.Combine(sandbox,"read-only assets");Directory.CreateDirectory(Path.Combine(assets,"starter"));
    foreach(var file in Directory.GetFiles(source))File.Copy(file,Path.Combine(assets,"starter",Path.GetFileName(file)));
    var root=Path.Combine(sandbox,"state Ω");
    var paths=new AppPaths(root,assets);
    CatalogBootstrap.Ensure(paths);
    Check(File.Exists(paths.Source)&&paths.Source.StartsWith(root),"Catalog must live in writable state.");
    using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=paths.Source}.ToString())){
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT count(*) FROM decks";Check(Convert.ToInt32(cmd.ExecuteScalar())==0,"No personal decks in starter.");
        cmd.CommandText="CREATE TABLE user_sentinel(value TEXT); INSERT INTO user_sentinel VALUES('keep')";cmd.ExecuteNonQuery();
    }
    var before=ForgeEngine.FileHash(paths.Source);
    CatalogBootstrap.Ensure(paths);Check(ForgeEngine.FileHash(paths.Source)==before,"Upgrade must not replace a modified catalog.");
    var corrupt=new AppPaths(Path.Combine(sandbox,"corrupt"),assets);
    File.WriteAllText(Path.Combine(assets,"starter","manifest.json"),"{\"sha256\":\"bad\"}");
    try{CatalogBootstrap.Ensure(corrupt);throw new Exception("Corrupt starter accepted.");}catch(InvalidDataException){}
    Check(!File.Exists(corrupt.Source)&&!Directory.GetFiles(corrupt.Root,"*.partial").Any(),"Failed seed must leave no catalog or partial.");
    // Legacy checkout paths and roots remain independent of installed mode.
    var checkout=Path.Combine(sandbox,"checkout");Directory.CreateDirectory(Path.Combine(checkout,"docs","specs"));
    Check(new AppPaths(checkout).Source==Path.GetFullPath(Path.Combine(checkout,"..","..","standard_cards.sqlite")),"Legacy catalog path changed.");
    using var ownership=new FileStream(Path.Combine(paths.Data,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    try{using var duplicate=new FileStream(Path.Combine(paths.Data,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);throw new Exception("Duplicate lock accepted.");}catch(IOException){}
    var compressed=Path.Combine(sandbox,"engine Ω.tar.bz2");
    File.WriteAllBytes(compressed,Convert.FromBase64String("QlpoOTFBWSZTWQwP/SQAAGfflsqYQAH/gsBIUABv4V9AACAIAQACCAggAJKGqaHpNAAAAAMgBKJT0mNJiZM1HqA0Gho09SPnHKlqizqAGEqIghdfMXUIT3VDJhBBUClg7D49rWlt8JXjGPoK1EHCghx56BCFUgyxkN8kXNqQTaN3xHcWBlqKQdOdxLa9qGzajQDE5wRgXKXVCVofZVlq60XMUeZg3EzHYTyWEYq1OAiB/F3JFOFCQDA/9JA="));
    var extraction=Path.Combine(sandbox,"managed extraction Ω");Directory.CreateDirectory(extraction);
    await EngineSetup.ExtractForgeArchive(compressed,extraction);
    Check(File.ReadAllText(Path.Combine(extraction,"nested Ω","engine é.txt"))=="verified Ω archive","Managed bzip2/tar extraction must retain Unicode paths and contents.");
    var destination=Path.Combine(sandbox,"download");File.WriteAllText(destination,"existing");
    var bytes=System.Text.Encoding.UTF8.GetBytes("verified archive");var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
    using var valid=new HttpClient(new FixtureHandler(bytes));
    await EngineSetup.DownloadVerified(valid,"https://fixture.invalid/archive",destination,hash);
    Check(File.ReadAllBytes(destination).SequenceEqual(bytes),"Verified archive not committed.");
    using var interrupted=new HttpClient(new FixtureHandler(bytes,true));
    try{await EngineSetup.DownloadVerified(interrupted,"https://fixture.invalid/archive",destination,hash);throw new Exception("Interrupted download accepted.");}catch(IOException){}
    Check(File.ReadAllBytes(destination).SequenceEqual(bytes)&&!File.Exists(destination+".partial"),"Interrupted download damaged existing archive.");
    try{await EngineSetup.DownloadVerified(valid,"https://fixture.invalid/archive",destination,new string('0',64));throw new Exception("Bad checksum accepted.");}catch(InvalidDataException){}
    Check(!File.Exists(destination+".partial"),"Bad checksum left partial archive.");
    Console.WriteLine("Windows beta shared checks passed: starter privacy/integrity, Unicode paths, legacy defaults, upgrade retention, exclusive lock, Unicode bzip2/tar extraction, interrupted downloads, checksums.");
}finally{SqliteConnection.ClearAllPools();Directory.Delete(sandbox,true);}

sealed class FixtureHandler(byte[] bytes,bool interrupted=false):HttpMessageHandler{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=interrupted?new StreamContent(new BrokenStream(bytes)):new ByteArrayContent(bytes)});
}
sealed class BrokenStream(byte[] bytes):MemoryStream(bytes){
    public override Task CopyToAsync(Stream destination,int size,CancellationToken token)=>throw new IOException("Fixture connection interrupted.");
}
