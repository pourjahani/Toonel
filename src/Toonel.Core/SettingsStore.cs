using System.Text.Json;
using Toonel.Core.Models;

namespace Toonel.Core;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string DefaultPath
    {
        get
        {
            Directory.CreateDirectory(PortablePaths.DataDir);
            return PortablePaths.SettingsFile;
        }
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts) ?? new AppSettings();
        }
        catch
        {
            // corrupted settings -> start fresh, never crash the app
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        // Atomic write: settings.json holds the profiles' credentials, so a crash or
        // power loss mid-write must never truncate it (temp file + atomic rename).
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }
}
