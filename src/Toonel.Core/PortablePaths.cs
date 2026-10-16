namespace Toonel.Core;

/// <summary>
/// Portable layout: all writable data (settings, logs, runtime configs) lives in a
/// <c>data</c> folder next to the exe, and engine binaries in <c>cores</c>. Copy the
/// folder, move the app — nothing is stored in %APPDATA% or the registry.
///
///   Toonel.exe
///   app.ico
///   cores\          (xray.exe, sing-box.exe, psiphon, wintun.dll)
///   data\           (settings.json, crash.log, runtime\…)
/// </summary>
public static class PortablePaths
{
    /// <summary>Directory containing the executable (works when launched via a shortcut).</summary>
    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>Root of all writable data — created on demand.</summary>
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(AppDir, "data");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string CrashLogFile => Path.Combine(DataDir, "crash.log");
    public static string RuntimeDir => Path.Combine(DataDir, "runtime");
    public static string CoresDir => Path.Combine(AppDir, "cores");

    /// <summary>
    /// One-time migration: if a legacy %APPDATA%\FusionVPN\settings.json exists and the
    /// portable one doesn't yet, copy it over so an existing user keeps their profiles,
    /// theme and language. The legacy folder is left in place (never deleted).
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FusionVPN");
            var legacySettings = Path.Combine(legacy, "settings.json");
            if (!File.Exists(legacySettings)) return;
            if (File.Exists(SettingsFile)) return;

            var data = DataDir;
            File.Copy(legacySettings, SettingsFile);
            var legacyRuntime = Path.Combine(legacy, "runtime");
            if (Directory.Exists(legacyRuntime))
            {
                foreach (var file in Directory.GetFiles(legacyRuntime))
                {
                    try { File.Copy(file, Path.Combine(data, "runtime", Path.GetFileName(file))); }
                    catch { /* best effort */ }
                }
            }
        }
        catch
        {
            // Migration is best-effort; a fresh portable install is always fine.
        }
    }
}
