using System.IO.Compression;
using System.Text.Json;

namespace DeckTester;

public static class CatalogBootstrap
{
    // Called while holding the service ownership lock. Updates never replace a user's catalog.
    public static void Ensure(AppPaths paths)
    {
        if(!paths.Installed||File.Exists(paths.Source))return;
        if(Environment.GetEnvironmentVariable("MTG_CARD_DB") is not null)
            throw new InvalidOperationException("The configured MTG_CARD_DB does not exist.");
        var starter=Path.Combine(paths.Assets,"starter");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(starter,"manifest.json")));
        var expected=manifest.RootElement.GetProperty("sha256").GetString();
        var temporary=paths.Source+"."+Guid.NewGuid().ToString("N")+".partial";
        try{
            using(var source=File.OpenRead(Path.Combine(starter,"catalog.sqlite.gz")))
            using(var gzip=new GZipStream(source,CompressionMode.Decompress))
            using(var destination=File.Create(temporary))gzip.CopyTo(destination);
            if(ForgeEngine.FileHash(temporary)!=expected)throw new InvalidDataException("Starter catalog checksum mismatch. Reinstall Crucible.");
            File.Move(temporary,paths.Source);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
