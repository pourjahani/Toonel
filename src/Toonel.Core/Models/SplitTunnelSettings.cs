namespace Toonel.Core.Models;

public enum SplitTunnelMode { Off, Include, Exclude }

/// <summary>
/// Per-application routing policy enforced by the sing-box TUN layer (process_name rules),
/// with destination exceptions that override the per-app decision:
///  - Proxy* exceptions: always send through the tunnel (even for direct apps / fallback).
///  - Direct* exceptions: always send direct (even for tunneled apps).
/// </summary>
public sealed class SplitTunnelSettings
{
    public SplitTunnelMode Mode { get; set; } = SplitTunnelMode.Off;

    /// <summary>Executable names, e.g. "chrome.exe". Meaning depends on Mode.</summary>
    public List<string> Processes { get; set; } = new();

    // Always-direct exceptions
    public List<string> DirectDomains { get; set; } = new();  // suffix match, e.g. "bank.ir"
    public List<string> DirectIps { get; set; } = new();      // CIDR, e.g. "10.0.0.0/8"
    public List<string> DirectPorts { get; set; } = new();    // "80" or "1000-2000"

    // Always-proxy exceptions
    public List<string> ProxyDomains { get; set; } = new();
    public List<string> ProxyIps { get; set; } = new();
    public List<string> ProxyPorts { get; set; } = new();

    /// <summary>Local/private ranges always go direct (router, LAN, loopback).</summary>
    public bool BypassPrivateIps { get; set; } = true;

    public bool HasAnyRule =>
        Mode != SplitTunnelMode.Off && Processes.Count > 0;
}
