using System.Text.Json;
using System.Text.Json.Serialization;

namespace Totp;

public sealed class AppEntry
{
    public string Name { get; set; } = "";
    public string? Account { get; set; }
    public string Secret { get; set; } = "";
    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;
    public string Algorithm { get; set; } = "SHA1";
}

public sealed class TotpConfig
{
    public List<AppEntry> Apps { get; set; } = [];

    public AppEntry? Find(string name)
    {
        // Exact (case-insensitive) match first, then unique prefix match.
        var exact = Apps.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var prefix = Sorted().Where(a => a.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return prefix.Count == 1 ? prefix[0] : null;
    }

    /// <summary>Apps in display order (used by list and the -g menu, so indexes match).</summary>
    public List<AppEntry> Sorted() => Apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public bool NameTaken(string name) => Apps.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(TotpConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext;

public static class ConfigStore
{
    /// <summary>
    /// Config path: $TOTP_CONFIG if set, otherwise ~/.totp/config.json
    /// (e.g. C:\Users\you\.totp\config.json on Windows).
    /// </summary>
    public static string Path
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TOTP_CONFIG");
            if (!string.IsNullOrWhiteSpace(env)) return System.IO.Path.GetFullPath(env);

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return System.IO.Path.Combine(home, ".totp", "config.json");
        }
    }

    /// <summary>Creates an empty config file (and its folder) on first run. Returns true if it was created.</summary>
    public static bool EnsureExists()
    {
        if (File.Exists(Path)) return false;
        Save(new TotpConfig());
        return true;
    }

    public static TotpConfig Load()
    {
        var path = Path;
        if (!File.Exists(path)) return new TotpConfig();

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return new TotpConfig();

        return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.TotpConfig) ?? new TotpConfig();
    }

    public static void Save(TotpConfig config)
    {
        var path = Path;
        var dir = System.IO.Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(config, ConfigJsonContext.Default.TotpConfig));

        // Secrets live here — restrict to the current user on Unix-like systems.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
