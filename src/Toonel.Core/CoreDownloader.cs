using System.IO.Compression;

namespace Toonel.Core;

/// <summary>
/// Downloads the pinned engine binaries into the app's cores directory:
///   xray.exe        (XTLS/Xray-core, VLESS/VMess/Trojan/SS core)
///   sing-box.exe    (SagerNet/sing-box, TUN sidecar) + wintun.dll
///   psiphon-core.exe (official psiphon-tunnel-core standalone client)
/// Users can also drop their own binaries into .\cores\ — missing files only are fetched.
/// </summary>
public sealed class CoreDownloader
{
    // Pinned versions (bump deliberately, after verifying upstream releases).
    // v26.3.27 is required for streamSettings.finalmask (incl. the TCP fragment mask
    // type); see Xray-core release notes for v26.2.6/v26.3.27.
    public const string XrayVersion = "v26.3.27";
    public const string SingBoxVersion = "v1.14.2";

    public static string XrayUrl =>
        $"https://github.com/XTLS/Xray-core/releases/download/{XrayVersion}/Xray-windows-64.zip";
    public static string SingBoxUrl =>
        $"https://github.com/SagerNet/sing-box/releases/download/{SingBoxVersion}/sing-box-1.14.2-windows-amd64.zip";
    // Psiphon tunnel-core: pinned to an immutable commit of the official binaries repo.
    // The binary runs elevated, so the URL must never come from a mutable branch like
    // "master" — bump the SHA (and verify the URL still responds) when adopting a new build.
    public const string PsiphonCommit = "d95d7f964c043a5a0ebd25cca801bc2753e14f74";
    public static string PsiphonUrl =>
        $"https://raw.githubusercontent.com/Psiphon-Labs/psiphon-tunnel-core-binaries/{PsiphonCommit}/windows/psiphon-tunnel-core-i686.exe";

    private readonly string _coresDir;
    private readonly HttpClient _http;

    public CoreDownloader(string? coresDir = null, HttpClient? http = null)
    {
        _coresDir = coresDir ?? Path.Combine(AppContext.BaseDirectory, "cores");
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        Directory.CreateDirectory(_coresDir);
    }

    public static string ExeNameXray => "xray.exe";
    public static string ExeNameSingBox => "sing-box.exe";
    public static string ExeNamePsiphon => PsiphonConfigBuilder.PsiphonBinaryName;

    public bool XrayPresent => File.Exists(Path.Combine(_coresDir, ExeNameXray));
    public bool SingBoxPresent => File.Exists(Path.Combine(_coresDir, ExeNameSingBox));
    public bool PsiphonPresent => File.Exists(Path.Combine(_coresDir, ExeNamePsiphon));
    public bool WintunPresent => File.Exists(Path.Combine(_coresDir, "wintun.dll"));

    public string CoresDir => _coresDir;

    /// <summary>Downloads every missing engine binary. Reports progress lines through <paramref name="progress"/>.</summary>
    public async Task DownloadMissingAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!XrayPresent)
        {
            progress?.Report($"Downloading Xray-core {XrayVersion}…");
            await DownloadZipFlatAsync(XrayUrl, keep: new[] { "xray.exe", "geoip.dat", "geosite.dat" }, ct);
        }

        if (!SingBoxPresent || !WintunPresent)
        {
            progress?.Report($"Downloading sing-box {SingBoxVersion} (+wintun.dll)…");
            await DownloadZipFlatAsync(SingBoxUrl, keep: new[] { "sing-box.exe", "wintun.dll" }, ct);
        }

        if (!PsiphonPresent)
        {
            progress?.Report("Downloading Psiphon tunnel-core…");
            await DownloadFileAsync(PsiphonUrl, Path.Combine(_coresDir, ExeNamePsiphon), ct);
        }

        progress?.Report("All cores ready.");
    }

    /// <summary>Force-replaces an engine binary with the pinned version (Ignore if already running).
    /// Used by the "Update Xray to pinned version" button when a user's cores folder holds an
    /// engine from an older pin that lacks newer features (e.g. FinalMask).</summary>
    public async Task<XrayDownloadResult> UpdateXrayAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var target = Path.Combine(_coresDir, ExeNameXray);
        // A running xray cannot be overwritten on Windows: probe with an exclusive open.
        try
        {
            using (File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }
        catch (FileNotFoundException)
        {
            // not installed yet — the download below will create it
        }
        catch (IOException)
        {
            return XrayDownloadResult.RunningLocked;
        }

        try
        {
            progress?.Report($"Downloading Xray-core {XrayVersion} (update)…");
            await DownloadZipFlatAsync(XrayUrl, keep: new[] { "xray.exe", "geoip.dat", "geosite.dat" }, ct);
            return XrayDownloadResult.Updated;
        }
        catch (Exception ex)
        {
            progress?.Report($"Xray update failed: {ex.Message}");
            return XrayDownloadResult.Failed;
        }
    }

    public string MissingSummary() =>
        $"cores dir: {_coresDir}\n" +
        $"xray.exe: {(XrayPresent ? "ok" : "missing")}\n" +
        $"sing-box.exe: {(SingBoxPresent ? "ok" : "missing")}\n" +
        $"wintun.dll: {(WintunPresent ? "ok" : "missing (needed for TUN)")}\n" +
        $"{ExeNamePsiphon}: {(PsiphonPresent ? "ok" : "missing")}";

    private async Task DownloadZipFlatAsync(string url, string[] keep, CancellationToken ct)
    {
        var tmpZip = Path.Combine(Path.GetTempPath(), $"toonel-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(url, tmpZip, ct);
            using var zip = ZipFile.OpenRead(tmpZip);
            foreach (var entry in zip.Entries)
            {
                var name = Path.GetFileName(entry.FullName);
                if (name.Length == 0 || !keep.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(_coresDir, name);
                using var src = entry.Open();
                using var dst = File.Create(target);
                await src.CopyToAsync(dst, ct);
            }
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* temp cleanup */ }
        }
    }

    private async Task DownloadFileAsync(string url, string targetPath, CancellationToken ct)
    {
        var tmp = targetPath + ".download";
        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(tmp);
        await src.CopyToAsync(dst, ct);
        dst.Close();
        File.Move(tmp, targetPath, overwrite: true);
    }
}

public enum XrayDownloadResult
{
    Updated,
    /// <summary>xray.exe is currently executing — overwrite not possible; stop the tunnel first.</summary>
    RunningLocked,
    Failed,
}
