using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeckTester;

// UI aliases only. Engine identity, resume checks, and caches continue to use full hashes.
public sealed class BuildNames(string path, ILogger<BuildNames>? logger = null)
{
    private readonly object gate = new();
    private Dictionary<string, string> known = new(StringComparer.Ordinal);
    public string Engine(string hash) => Name("engine", hash);
    public string Bridge(string hash) => Name("bridge", hash);

    private string Name(string kind, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Equals("unknown", StringComparison.OrdinalIgnoreCase) || hash.Equals("missing", StringComparison.OrdinalIgnoreCase)) return "Unknown";
        var key = kind + ":" + hash.ToLowerInvariant();
        lock (gate)
        {
            if (known.TryGetValue(key, out var existing)) return existing;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var saved = File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty build name registry.")
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                if (saved.TryGetValue(key, out existing)) { known = saved; return existing; }
                var used = saved.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
                var start = (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(digest) % (uint)NameLibrary.Words.Count);
                var word = Enumerable.Range(0, NameLibrary.Words.Count).Select(offset => NameLibrary.Words[(start + offset) % NameLibrary.Words.Count]).FirstOrDefault(word => !used.Contains(word));
                // Keep an exact, unambiguous identifier if the word library is exhausted.
                if (word is null) return RunBuildIdentity.Short(hash);
                var name = char.ToUpperInvariant(word[0]) + word[1..];
                saved.Add(key, name);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(saved));
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                known = saved;
                return name;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                logger?.LogWarning(error, "Build names unavailable; preserving the build identifier.");
                return RunBuildIdentity.Short(hash);
            }
        }
    }
}
