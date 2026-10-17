using System.Net;

namespace Toonel.Core;

/// <summary>
/// Checks what the public IP looks like from the local vantage point, both directly
/// and through the local proxy. If both return the same address the proxy is not
/// actually hopping (possible leak); different addresses = no leak.
/// </summary>
public static class LeakTester
{
    private const string IpService = "https://api.ipify.org/";

    public static async Task<string> GetIpAsync(string? proxyHttpHostPort = null, CancellationToken ct = default)
    {
        using var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxyHttpHostPort))
            handler.Proxy = new WebProxy($"http://{proxyHttpHostPort}");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        var text = (await http.GetStringAsync(IpService, ct)).Trim();
        return text;
    }

    /// <summary>Returns (directIp, proxiedIp, leaked). proxiedIp null when empty.</summary>
    public static async Task<(string direct, string? proxied, bool leaked)> CheckAsync(int httpPort, CancellationToken ct = default)
    {
        var direct = await GetIpAsync(null, ct);
        string? proxied = null;
        try { proxied = await GetIpAsync($"127.0.0.1:{httpPort}", ct); }
        catch { /* proxy not reachable or no engines running */ }
        bool leaked = proxied is null || proxied == direct;
        return (direct, proxied, leaked);
    }
}
