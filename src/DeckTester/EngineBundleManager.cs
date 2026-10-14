using System.Diagnostics;
using System.Text;

namespace DeckTester;

public sealed record EngineBundle(string Id, string EngineHash, string AdapterHash, string Directory, string JarName, int WorkerProtocolVersion = 0)
{
    public string Jar => Path.Combine(Directory, JarName);
    public string EngineName => "Forge " + JarName.Replace("forge-gui-desktop-", "", StringComparison.Ordinal)
        .Replace("-jar-with-dependencies.jar", "", StringComparison.Ordinal);
    public string Bridge => Path.Combine(Directory, "bridge");
    public string Resources => Path.Combine(Directory, "res");
}

// Publishing is the only operation that touches mutable vendor/ and engine/build files.
// Games and coverage always read a complete, versioned bundle.
public sealed class EngineBundleManager(string root)
{
    private string Base => Path.Combine(root, "data", "engine-bundles");
    private string ActiveFile => Path.Combine(Base, "active.json");
    private string Vendor => Path.Combine(root, "vendor");
    private string Build => Path.Combine(root, "engine", "build");
    private static string Id(string engine, string adapter) => engine + "-" + adapter;

    public EngineBundle? Active()
    {
        if (!File.Exists(ActiveFile)) return null;
        var id = Json.Decode<ActivePointer>(File.ReadAllText(ActiveFile)).Id;
        return Read(id);
    }

    public EngineBundle? Find(string engineHash, string adapterHash)
    {
        if (engineHash.Length != 64 || adapterHash.Length != 64 ||
            !engineHash.All(Uri.IsHexDigit) || !adapterHash.All(Uri.IsHexDigit)) return null;
        var bundle = Read(Id(engineHash, adapterHash));
        return bundle is not null && bundle.EngineHash == engineHash && bundle.AdapterHash == adapterHash ? bundle : null;
    }

    private EngineBundle? Read(string id)
    {
        if (id.Length != 129 || id[64] != '-' || !id[..64].All(Uri.IsHexDigit) || !id[65..].All(Uri.IsHexDigit)) return null;
        var folder = Path.Combine(Base, id);
        var manifestPath = Path.Combine(folder, "bundle.json");
        if (!File.Exists(manifestPath)) return null;
        var manifest = Json.Decode<BundleManifest>(File.ReadAllText(manifestPath));
        if (manifest.EngineHash + "-" + manifest.AdapterHash != id) return null;
        var bundle = new EngineBundle(id, manifest.EngineHash, manifest.AdapterHash, folder, manifest.JarName, manifest.WorkerProtocolVersion);
        return File.Exists(bundle.Jar) && File.Exists(Path.Combine(bundle.Bridge, "ForgeBridge.class")) && Directory.Exists(bundle.Resources) ? bundle : null;
    }

    private static string EngineHash(string folder, string jarName)
    {
        var files = new[] { Path.Combine(folder, jarName) }
            .Concat(new[] { "cardsfolder", "tokenscripts", "editions", "ai", "formats", "effects", "defaults" }
                .Select(name => Path.Combine(folder, "res", name)).Where(Directory.Exists)
                .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)))
            .Where(File.Exists).Order(StringComparer.Ordinal);
        return DeckImporter.Hash(string.Join("\n", files.Select(file => Path.GetRelativePath(folder, file) + ":" + ForgeEngine.FileHash(file))));
    }

    private static string AdapterHash(string bridge) => DeckImporter.Hash(string.Join("\n",
        Directory.EnumerateFiles(bridge, "*.class").Order(StringComparer.Ordinal).Select(ForgeEngine.FileHash)));

    public async Task<EngineBundle> Publish()
    {
        Directory.CreateDirectory(Base);
        await using var gate = new FileStream(Path.Combine(Base, "publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var configuredJar = Environment.GetEnvironmentVariable("MTG_FORGE_JAR");
        var candidates = configuredJar is null
            ? Directory.EnumerateFiles(Vendor, "forge-gui-desktop-*-jar-with-dependencies.jar", SearchOption.TopDirectoryOnly).ToArray()
            : [Path.GetFullPath(configuredJar, root)];
        if (candidates.Length != 1) throw new InvalidOperationException("Select one Forge jar with MTG_FORGE_JAR before activation.");
        var jar = candidates[0];
        if (Path.GetDirectoryName(jar) != Vendor || !Path.GetFileName(jar).StartsWith("forge-gui-desktop-", StringComparison.Ordinal)
            || !Path.GetFileName(jar).EndsWith("-jar-with-dependencies.jar", StringComparison.Ordinal))
            throw new InvalidOperationException("MTG_FORGE_JAR must name a Forge desktop jar directly inside vendor/.");
        var jarName = Path.GetFileName(jar);
        var javac = Path.Combine(Vendor, "jdk", OperatingSystem.IsMacOS() ? "Contents/Home/bin/javac" : "bin/javac");
        if (!File.Exists(jar) || !File.Exists(javac) || !Directory.Exists(Path.Combine(Vendor, "res")))
            throw new InvalidOperationException("Forge jar, Java compiler, or card resources are missing; run setup first.");
        Directory.CreateDirectory(Build);
        var process = new ProcessStartInfo(javac) { WorkingDirectory = root, UseShellExecute = false };
        foreach (var arg in new[] { "-cp", jar, "-d", Build, Path.Combine(root, "engine", "ForgeBridge.java") }) process.ArgumentList.Add(arg);
        using (var compiler = Process.Start(process) ?? throw new InvalidOperationException("Could not start javac.")) {
            await compiler.WaitForExitAsync();
            if (compiler.ExitCode != 0) throw new InvalidOperationException("Forge bridge compilation failed; active bundle was not changed.");
        }
        var sourceEngine = EngineHash(Vendor, jarName);
        var sourceAdapter = AdapterHash(Build);
        var id = Id(sourceEngine, sourceAdapter);
        var existing = Read(id);
        if (existing is null) {
            var staging = Path.Combine(Base, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try {
                File.Copy(jar, Path.Combine(staging, Path.GetFileName(jar)));
                var sourceProfile = Path.Combine(Vendor, "forge.profile.properties");
                if (File.Exists(sourceProfile)) File.Copy(sourceProfile, Path.Combine(staging, "forge.profile.properties"));
                CopyTree(Path.Combine(Vendor, "res"), Path.Combine(staging, "res"));
                CopyTree(Build, Path.Combine(staging, "bridge"));
                if (EngineHash(staging, jarName) != sourceEngine || AdapterHash(Path.Combine(staging, "bridge")) != sourceAdapter)
                    throw new InvalidDataException("Forge files changed during publication; active bundle was not changed.");
                // Older bridges do not understand worker requests. Detect the capability
                // in the compiled class so optimizer snapshots retain their actual protocol.
                var workerProtocol = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(staging, "bridge", "ForgeBridge.class")))
                    .Contains("worker_complete", StringComparison.Ordinal) ? 1 : 0;
                File.WriteAllText(Path.Combine(staging, "bundle.json"), Json.Encode(new BundleManifest(sourceEngine, sourceAdapter, jarName, workerProtocol)));
                Directory.Move(staging, Path.Combine(Base, id));
            } finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            existing = Read(id) ?? throw new InvalidDataException("Published bundle is incomplete.");
        }
        var pointer = ActiveFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(pointer, Json.Encode(new ActivePointer(id))); File.Move(pointer, ActiveFile, true); }
        finally { if (File.Exists(pointer)) File.Delete(pointer); }
        return existing;
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private sealed record ActivePointer(string Id);
    private sealed record BundleManifest(string EngineHash, string AdapterHash, string JarName = "forge-gui-desktop-2.0.15-jar-with-dependencies.jar", int WorkerProtocolVersion = 0);
}
