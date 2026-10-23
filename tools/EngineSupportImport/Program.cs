using DeckTester;
using Microsoft.Data.Sqlite;

try{
    if(args.Length==0||args.Contains("--help")){
        Console.WriteLine("EngineSupportImport --prepare INPUT --output PACK | --root CRUCIBLE_ROOT --pack PACK");return 0;
    }
    if(args.Length!=4)throw new ArgumentException("Expected two named arguments with values.");
    var options=new Dictionary<string,string>();for(var i=0;i<args.Length;i+=2)options.Add(args[i],args[i+1]);
    if(options.TryGetValue("--prepare",out var input)&&options.TryGetValue("--output",out var output)){
        var pack=Json.Decode<EngineSupportPack>(File.ReadAllText(input));
        var mapping=new Dictionary<string,string>(StringComparer.Ordinal);
        var decks=pack.Decks.Select(d=>{
            var pairs=d.Cards.Where(c=>c.Zone=="MainDeck").GroupBy(c=>c.Name).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new object[]{g.Key,g.Sum(c=>c.Quantity)}).ToList();
            var hash=DeckImporter.Hash(Json.Encode(new DeckFingerprint(d.Name,d.SourceType,d.SourceDeckId,d.Snapshot,d.SourceUrl,d.Cards)));
            mapping.Add(d.SourceDeckId,hash);return d with{Id=hash,ManifestHash=hash,ListHash=DeckImporter.Hash(Json.EncodeRelaxed(pairs))};
        }).ToList();
        pack=pack with{Decks=decks,Matchups=pack.Matchups.Select(m=>m with{DeckA=mapping[m.DeckA],DeckB=mapping[m.DeckB]}).ToList()};
        EngineSupportPack.Validate(pack);File.WriteAllText(output,Json.Encode(pack));Console.WriteLine("Prepared "+pack.Id);return 0;
    }
    if(!options.TryGetValue("--root",out var root)||!options.TryGetValue("--pack",out var file))throw new ArgumentException("Expected --root and --pack.");
    var catalog=Json.Decode<EngineSupportPack>(File.ReadAllText(file));EngineSupportPack.Validate(catalog);
    var paths=new AppPaths(root);using var ownership=HostControl.AcquireDataOwnership(paths);
    var database=Path.Combine(paths.Data,"simulator.sqlite");
    if(File.Exists(database)){
        var backups=Path.Combine(paths.Data,"backups");Directory.CreateDirectory(backups);
        var destination=Path.Combine(backups,"before-engine-support-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+".sqlite");
        using var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Mode=SqliteOpenMode.ReadOnly}.ToString());source.Open();
        using var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination,Mode=SqliteOpenMode.ReadWriteCreate}.ToString());backup.Open();source.BackupDatabase(backup);
        Console.Error.WriteLine("Database backup: "+destination);
    }
    Console.WriteLine(Json.Encode(await new Store(paths).ImportEngineSupport(catalog)));return 0;
}catch(Exception e)when(e is ArgumentException or IOException or InvalidOperationException or System.Text.Json.JsonException or SqliteException){Console.Error.WriteLine(e.Message);return 2;}
