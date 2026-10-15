using System.Diagnostics;
using System.IO.Compression;
using System.Formats.Tar;
using ICSharpCode.SharpZipLib.BZip2;
using System.Text.Json;

namespace DeckTester;

public sealed record EngineInstallation(DateTimeOffset InstalledAt,string ForgeVersion,string JarHash,string JavaHome,string BridgeSourceHash);

public static class EngineSetup
{
    public const string ForgeArchiveSha256="4468f182b60f6ca62d89697baa208981085af744f116781af751c611826db600";
    public static Task Install(string root)=>Install(new AppPaths(root,Environment.GetEnvironmentVariable("MTG_ASSET_ROOT")));
    public static async Task Install(AppPaths paths)
    {
        // Setup and simulations must never mutate the same installation concurrently.
        using var ownership=new FileStream(Path.Combine(paths.Data,"service.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        CatalogBootstrap.Ensure(paths);
        using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Crucible-Setup/0.1");
        Directory.CreateDirectory(paths.Vendor);
        var jar=paths.Jar;
        if(!File.Exists(jar)||!Directory.Exists(Path.Combine(paths.Vendor,"res"))){
            Console.WriteLine("Downloading and verifying Forge 2.0.15 (approximately 300 MB)…");
            var archive=Path.Combine(paths.Data,"forge-2.0.15.tar.bz2");
            await DownloadVerified(http,"https://github.com/Card-Forge/forge/releases/download/forge-2.0.15/forge-installer-2.0.15.tar.bz2",archive,ForgeArchiveSha256);
            Console.WriteLine("Extracting and validating Forge resources…");
            var staging=Path.Combine(paths.Data,"forge-setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try{
                if(OperatingSystem.IsWindows())await ExtractForgeArchive(archive,staging);
                else await Command(Tar(),["-xjf",Path.GetFileName(archive),"-C",Path.GetFileName(staging)],paths.Data);
                if(!File.Exists(Path.Combine(staging,Path.GetFileName(jar)))||!Directory.Exists(Path.Combine(staging,"res")))
                    throw new InvalidDataException("Forge archive has an unexpected layout.");
                foreach(var directory in Directory.EnumerateDirectories(staging)){
                    var target=Path.Combine(paths.Vendor,Path.GetFileName(directory));
                    if(Directory.Exists(target))Directory.Move(target,target+".previous-"+Guid.NewGuid().ToString("N"));
                    Directory.Move(directory,target);
                }
                foreach(var file in Directory.EnumerateFiles(staging))File.Move(file,Path.Combine(paths.Vendor,Path.GetFileName(file)),true);
            }finally{Directory.Delete(staging,true);}
            File.Delete(archive);
        }
        var localJdk=Path.Combine(paths.Vendor,"jdk");
        if(!File.Exists(Path.Combine(paths.JavaHome,"bin",AppPaths.JavaTool("java")))||!File.Exists(Path.Combine(paths.JavaHome,"bin",AppPaths.JavaTool("javac")))){
            var os=OperatingSystem.IsMacOS()?"mac":OperatingSystem.IsLinux()?"linux":OperatingSystem.IsWindows()?"windows":throw new PlatformNotSupportedException();
            var arch=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.Arm64?"aarch64":"x64";
            Console.WriteLine("Downloading and verifying local Temurin Java 17…");
            using var metadata=JsonDocument.Parse(await http.GetStringAsync($"https://api.adoptium.net/v3/assets/latest/17/hotspot?architecture={arch}&image_type=jdk&os={os}"));
            var package=metadata.RootElement[0].GetProperty("binary").GetProperty("package");
            var archive=Path.Combine(paths.Data,OperatingSystem.IsWindows()?"jdk.zip":"jdk.tar.gz");
            await DownloadVerified(http,package.GetProperty("link").GetString()!,archive,package.GetProperty("checksum").GetString()!);
            Console.WriteLine("Extracting the local Java runtime…");
            var staging=Path.Combine(paths.Data,"jdk-setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try{
                if(OperatingSystem.IsWindows())ZipFile.ExtractToDirectory(archive,staging);
                else await Command(Tar(),["-xzf",archive,"-C",staging],paths.Root);
                var extracted=Directory.GetDirectories(staging).Single();
                var bin=OperatingSystem.IsMacOS()?Path.Combine(extracted,"Contents","Home","bin"):Path.Combine(extracted,"bin");
                if(!File.Exists(Path.Combine(bin,AppPaths.JavaTool("java")))||!File.Exists(Path.Combine(bin,AppPaths.JavaTool("javac"))))
                    throw new InvalidDataException("Java archive has an unexpected layout.");
                if(Directory.Exists(localJdk))Directory.Move(localJdk,localJdk+".previous-"+Guid.NewGuid().ToString("N"));
                Directory.Move(extracted,localJdk);
            }finally{Directory.Delete(staging,true);}
            File.Delete(archive);
        }
        await File.WriteAllTextAsync(Path.Combine(paths.Vendor,"forge.profile.properties"),$"userDir={Property(Path.Combine(paths.Data,"forge-user"))}\ncacheDir={Property(Path.Combine(paths.Data,"forge-cache"))}\n");
        Console.WriteLine("Compiling the pilot bridge and publishing the immutable engine…");
        var bundle=await new EngineBundleManager(paths.Root,paths.Assets).Publish();
        await File.WriteAllTextAsync(Path.Combine(paths.Data,"engine-installation.json"),Json.Encode(new EngineInstallation(DateTimeOffset.UtcNow,"2.0.15",ForgeEngine.FileHash(jar),paths.JavaHome,ForgeEngine.FileHash(Path.Combine(paths.Assets,"engine","ForgeBridge.java")))));
        Console.WriteLine($"Setup complete. Active bundle: {bundle.Id}. No simulations have been run.");
    }
    public static async Task ExtractForgeArchive(string archive,string destination)
    {
        // Windows tar builds vary in bzip2 support and handling of Unicode arguments.
        // Managed decompression also keeps the .NET traversal checks around extraction.
        await using var file=File.OpenRead(archive);
        using var bzip=new BZip2InputStream(file);
        await TarFile.ExtractToDirectoryAsync(bzip,destination,overwriteFiles:false);
    }
    private static string Tar()=>OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe"):"tar";
    private static string Property(string path)=>string.Concat(path.Select(c=>c switch {
        '\\'=>"\\\\", ':'=>"\\:", '\r'=>"\\r", '\n'=>"\\n", '\t'=>"\\t",
        > '\u007e'=>"\\u"+((int)c).ToString("x4",System.Globalization.CultureInfo.InvariantCulture), _=>c.ToString()
    }));
    public static async Task DownloadVerified(HttpClient http,string url,string destination,string expected)
    {
        if(expected.Length!=64||!expected.All(Uri.IsHexDigit))throw new InvalidDataException("A SHA-256 checksum is required for setup downloads.");
        var temporary=destination+".partial";
        try{
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
            await using(var stream=await response.Content.ReadAsStreamAsync())await using(var file=File.Create(temporary))await stream.CopyToAsync(file);
            if(!ForgeEngine.FileHash(temporary).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Downloaded archive checksum mismatch. Retry setup.");
            File.Move(temporary,destination,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static async Task Command(string executable,List<string> args,string directory)
    {
        var info=new ProcessStartInfo(executable){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new InvalidOperationException("Unable to launch setup tool.");
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();var text=await output;var error=await errors;
        if(!string.IsNullOrWhiteSpace(text))Console.WriteLine(text);
        if(!string.IsNullOrWhiteSpace(error))Console.Error.WriteLine(error);
        if(process.ExitCode!=0)throw new InvalidOperationException($"Setup tool failed ({process.ExitCode}): {executable}. {error.Trim()} Retry setup.");
    }
}
