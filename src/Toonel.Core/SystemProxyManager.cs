using Microsoft.Win32;

namespace Toonel.Core;

/// <summary>
/// Sets/restores the WinINET system proxy (per-user registry) and notifies the system.
/// Works for most GUI apps and browsers; terminals need the env snippets from TerminalProxyHelper
/// (or TUN mode, which captures them anyway).
/// </summary>
public static class SystemProxyManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public static void Enable(int httpPort)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? throw new InvalidOperationException("Cannot open Internet Settings registry key.");
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{httpPort}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;192.168.*;<local>", RegistryValueKind.String);
        RefreshWininet();
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key is null) return;
        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
        RefreshWininet();
    }

    /// <summary>Disables the proxy only if it currently points at our local port (never stomps user settings).</summary>
    public static void DisableIfOurs(int httpPort)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue("ProxyEnable") is not 1) return;
            var server = key.GetValue("ProxyServer") as string;
            // Exact host:port comparison — a substring match on ":{port}" would also
            // hit e.g. :10808 when our port is 1080 and stomp a foreign proxy setting.
            if (server is null) return;
            var host = server.AsSpan();
            for (int i = 0; i < host.Length; i++)
                if (host[i] == ';')
                {
                    if (Matches(host[(i + 1)..].TrimEnd())) { Disable(); return; }
                }
            if (Matches(host)) Disable();

            bool Matches(ReadOnlySpan<char> candidate)
            {
                // ProxyServer may be "host:port" or "http=host:port;https=host:port".
                var eq = candidate.IndexOf('=');
                if (eq >= 0) candidate = candidate[(eq + 1)..];
                var colon = candidate.LastIndexOf(':');
                if (colon < 0 || !int.TryParse(candidate[(colon + 1)..], out var port)) return false;
                return port == httpPort;
            }
        }
        catch
        {
            return;
        }
        Disable();
    }

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("ProxyEnable") is 1;
        }
    }

    private static void RefreshWininet()
    {
        try
        {
            _ = InternetSetOption(IntPtr.Zero, 39 /*INTERNET_OPTION_SETTINGS_CHANGED*/, IntPtr.Zero, 0);
            _ = InternetSetOption(IntPtr.Zero, 37 /*INTERNET_OPTION_REFRESH*/, IntPtr.Zero, 0);
        }
        catch
        {
            // best-effort refresh; settings still apply after next WinINET start
        }
    }

    [System.Runtime.InteropServices.DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
