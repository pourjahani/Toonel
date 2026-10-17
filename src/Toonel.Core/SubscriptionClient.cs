using System.Text.Json;
using System.Text.Json.Nodes;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>The result of fetching one subscription URL.</summary>
public sealed class SubscriptionFetchResult
{
    public List<Profile> Profiles { get; set; } = new();
    public long UploadBytes { get; set; } = -1;
    public long DownloadBytes { get; set; } = -1;
    public long TotalBytes { get; set; } = -1;
    public long ExpiresUnix { get; set; } = -1;
    public string Format { get; set; } = "links";
}

/// <summary>
/// Downloads subscription URLs and turns them into profiles. Formats supported:
/// plain/share-link lists, base64 blobs, Clash YAML (proxies:) and sing-box JSON
/// outbounds. Traffic/expire metadata comes from the Subscription-Userinfo header.
/// </summary>
public sealed class SubscriptionClient
{
    private readonly HttpClient _http;

    public SubscriptionClient(HttpClient? http = null)
        => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<SubscriptionFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct);
        var userinfo = resp.Headers.TryGetValues("Subscription-Userinfo", out var vals)
            ? string.Join("; ", vals) : null;

        var parse = ParseSubscriptionText(text);
        var r = new SubscriptionFetchResult
        {
            Profiles = parse.Profiles,
            Format = parse.Format,
        };
        if (userinfo != null) ApplyUserinfo(r, userinfo);
        return r;
    }

    public static (List<Profile> Profiles, string Format) ParseSubscriptionText(string text)
    {
        var trimmed = text.TrimStart();

        var clash = ClashConfigParser.Parse(text);
        if (clash.Count > 0) return (clash, "clash");

        if (trimmed.StartsWith("{@") || trimmed.StartsWith("["))
        {
            try
            {
                var node = JsonNode.Parse(text);
                var outbounds = node?["outbounds"] as JsonArray;
                if (outbounds != null)
                {
                    var list = new List<Profile>();
                    foreach (var ob in outbounds)
                    {
                        if (ob is not JsonObject obj) continue;
                        var type = obj["type"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(type)) continue;
                        var p = MapSingBoxOutbound(obj, type);
                        if (p != null) list.Add(p);
                    }
                    if (list.Count > 0) return (list, "sing-box");
                }
            }
            catch { /* not JSON, fall through */ }
        }

        return (ProfileUrlParser.ParseMany(text), "links");
    }

    private static Profile? MapSingBoxOutbound(JsonObject ob, string type)
    {
        string? Server() => ob["server"]?.GetValue<string>();
        int? Port() => ob["server_port"] is JsonValue v ? v.GetValue<int>() : null;
        var server = ob["server"]?.ToString();
        int port = ob["server_port"] != null ? int.Parse(ob["server_port"]!.ToString()) : 443;
        if (string.IsNullOrWhiteSpace(server)) return null;
        var p = new Profile { Address = server, Port = port, Protocol = type switch
        {
            "vless" => ProfileProtocol.Vless,
            "vmess" => ProfileProtocol.Vmess,
            "trojan" => ProfileProtocol.Trojan,
            "shadowsocks" => ProfileProtocol.Shadowsocks,
            "hysteria2" => ProfileProtocol.Hysteria2,
            "tuic" => ProfileProtocol.Tuic,
            "wireguard" => ProfileProtocol.WireGuard,
            _ => (ProfileProtocol)0,
        }, Name = ob["tag"]?.ToString() ?? type };
        if (p.Protocol == default && type != "vless") return null;

        switch (p.Protocol)
        {
            case ProfileProtocol.Vless or ProfileProtocol.Vmess:
                p.Uuid = ob["uuid"]?.ToString() ?? "";
                p.Security = ob["tls"] is JsonObject t && t["enabled"]?.GetValue<bool>() == true ? "tls" : "none";
                p.Sni = (ob["tls"] as JsonObject)?["server_name"]?.ToString() ?? "";
                p.Network = ob["transport"] is JsonObject tr ? tr["type"]?.ToString() ?? "tcp" : "tcp";
                break;
            case ProfileProtocol.Trojan or ProfileProtocol.Shadowsocks:
                p.Password = ob["password"]?.ToString() ?? "";
                if (p.Protocol == ProfileProtocol.Shadowsocks) p.SsMethod = ob["method"]?.ToString() ?? "aes-256-gcm";
                break;
            case ProfileProtocol.Hysteria2:
                p.Password = ob["password"]?.ToString() ?? "";
                break;
            case ProfileProtocol.Tuic:
                p.Uuid = ob["uuid"]?.ToString() ?? "";
                p.Password = ob["password"]?.ToString() ?? "";
                p.CongestionControl = ob["congestion_control"]?.ToString() ?? "bbr";
                break;
            case ProfileProtocol.WireGuard:
                p.WgPrivateKey = ob["private_key"]?.ToString() ?? "";
                break;
        }
        return p;
    }

    private static void ApplyUserinfo(SubscriptionFetchResult r, string userinfo)
    {
        foreach (var part in userinfo.Split(new[] { ";", " " }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf("=");
            if (eq <= 0) continue;
            var key = part[..eq].Trim();
            if (!long.TryParse(part[(eq+1)..].Trim(), out var val)) continue;
            switch (key)
            {
                case "upload": r.UploadBytes = val; break;
                case "download": r.DownloadBytes = val; break;
                case "total": r.TotalBytes = val; break;
                case "expire": r.ExpiresUnix = val; break;
            }
        }
    }

    /// <summary>Human-readable traffic/expiry line from subscription-userinfo values.</summary>
    public static string FormatTraffic(long downloaded, long uploaded, long total, long expiresUnix)
    {
        var parts = new List<string>();
        if (downloaded >= 0 && uploaded >= 0) parts.Add("down " + HumanBytes(downloaded) + " up " + HumanBytes(uploaded));
        if (total >= 0) parts.Add("of " + HumanBytes(total));
        if (expiresUnix > 0)
        {
            var left = expiresUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (left > 0)
            {
                var days = (int)(left / 86400);
                parts.Add(days + " days left");
            }
            else parts.Add("expired");
        }
        return string.Join(" · ", parts);
    }

    private static string HumanBytes(long b)
    {
        double v = b;
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return Math.Round(v, 1) + " " + u[i];
    }

    public static string? ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Subscription URL is empty.";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return "Subscription must be a valid http(s) URL.";
        return null;
    }
}
