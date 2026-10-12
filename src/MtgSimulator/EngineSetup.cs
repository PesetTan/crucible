using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace MtgSimulator;

public static class EngineSetup
{
    public static async Task Install(string projectRoot)
    {
        var root=Path.GetFullPath(projectRoot);if(!Directory.Exists(Path.Combine(root,"docs","specs")))throw new ArgumentException("Invalid project root.");
        var vendor=Path.Combine(root,"vendor");var data=Path.Combine(root,"data");Directory.CreateDirectory(vendor);Directory.CreateDirectory(data);
        using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};http.DefaultRequestHeaders.UserAgent.ParseAdd("MtgSimulator-Local/1.0");
        var jar=Path.Combine(vendor,"forge-gui-desktop-2.0.15-jar-with-dependencies.jar");
        if(!File.Exists(jar)){
            Console.WriteLine("Downloading pinned Forge 2.0.15 (approximately 300 MB)…");
            var archive=Path.Combine(data,"forge-2.0.15.tar.bz2");
            using var release=JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/Card-Forge/forge/releases/tags/forge-2.0.15"));
            var asset=release.RootElement.GetProperty("assets").EnumerateArray().Single(a=>a.GetProperty("name").GetString()=="forge-installer-2.0.15.tar.bz2");
            var digest=asset.TryGetProperty("digest",out var value)?value.GetString():null;
            await Download(http,asset.GetProperty("browser_download_url").GetString()!,archive,digest?.Replace("sha256:","",StringComparison.Ordinal));
            await Command("tar",["-xjf",archive,"-C",vendor],root);File.Delete(archive);
        }
        var localJdk=Path.Combine(vendor,"jdk");var javaHome=OperatingSystem.IsMacOS()?Path.Combine(localJdk,"Contents","Home"):localJdk;
        if(!File.Exists(Path.Combine(javaHome,"bin","java"))){
            var os=OperatingSystem.IsMacOS()?"mac":OperatingSystem.IsLinux()?"linux":throw new PlatformNotSupportedException("Automatic engine setup supports macOS and Linux.");
            var arch=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.Arm64?"aarch64":"x64";
            Console.WriteLine("Downloading project-local Temurin Java 17…");
            using var metadata=JsonDocument.Parse(await http.GetStringAsync($"https://api.adoptium.net/v3/assets/latest/17/hotspot?architecture={arch}&image_type=jdk&os={os}"));
            var package=metadata.RootElement[0].GetProperty("binary").GetProperty("package");var archive=Path.Combine(data,"jdk.tar.gz");
            await Download(http,package.GetProperty("link").GetString()!,archive,package.GetProperty("checksum").GetString());
            Directory.CreateDirectory(localJdk);await Command("tar",["-xzf",archive,"-C",localJdk,"--strip-components=1"],root);File.Delete(archive);
        }
        var profile=Path.Combine(vendor,"forge.profile.properties");
        await File.WriteAllTextAsync(profile,$"userDir={Property(Path.Combine(data,"forge-user"))}\ncacheDir={Property(Path.Combine(data,"forge-cache"))}\n");
        Directory.CreateDirectory(Path.Combine(root,"engine","build"));
        await Command(Path.Combine(javaHome,"bin","javac"),["-cp",jar,"-d",Path.Combine(root,"engine","build"),Path.Combine(root,"engine","ForgeBridge.java")],root);
        await File.WriteAllTextAsync(Path.Combine(data,"engine-installation.json"),Json.Encode(new{installedAt=DateTimeOffset.UtcNow,forgeVersion="2.0.15",jarHash=ForgeEngine.FileHash(jar),javaHome}));
        Console.WriteLine("Local engine setup complete. No simulations have been run.");
    }
    private static string Property(string path)=>path.Replace("\\","\\\\",StringComparison.Ordinal).Replace(":","\\:",StringComparison.Ordinal);
    private static async Task Download(HttpClient http,string url,string destination,string? expected)
    {
        var temporary=destination+".partial";
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
        await using(var stream=await response.Content.ReadAsStreamAsync())await using(var file=File.Create(temporary))await stream.CopyToAsync(file);
        if(expected is not null&&!ForgeEngine.FileHash(temporary).Equals(expected,StringComparison.OrdinalIgnoreCase)){File.Delete(temporary);throw new InvalidDataException("Downloaded archive checksum mismatch.");}
        File.Move(temporary,destination,true);
    }
    private static async Task Command(string executable,List<string> args,string directory)
    {
        var info=new ProcessStartInfo(executable){WorkingDirectory=directory,UseShellExecute=false};foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new InvalidOperationException("Unable to launch setup tool.");await process.WaitForExitAsync();if(process.ExitCode!=0)throw new InvalidOperationException($"Setup tool failed: {executable}.");
    }
}
