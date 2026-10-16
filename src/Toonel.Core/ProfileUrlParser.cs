using System.Text;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>Parses v2rayNG-style share links: vless://, vmess://, trojan://, ss://.</summary>
public static class ProfileUrlParser
{
    public static bool TryParse(string url, out Profile profile)
    {
        profile = new Profile();
        url = url.Trim();
        try
        {
            if (url.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return ParseVmess(url[8..], profile);
            if (url.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) return ParseStandard(new Uri(url), profile, ProfileProtocol.Vless);
            if (url.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase)) return ParseStandard(new Uri(url), profile, ProfileProtocol.Trojan);
            if (url.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) return ParseSs(url[5..], profile);
        }
        catch
        {
            return false;
        }
        return false;
    }

    public static List<Profile> ParseMany(string text)
    {
        var list = new List<Profile>();
        var candidates = new List<string>();

        // Whole text might be one base64 blob (subscription style).
        var decoded = TryBase64Decode(text.Trim());
        if (decoded is not null && decoded.Contains("://"))
            candidates.AddRange(decoded.Split('\n'));
        candidates.AddRange(text.Split('\n'));

        foreach (var raw in candidates)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (TryParse(line, out var p)) list.Add(p);
        }
        return list;
    }

    private static bool ParseVmess(string b64, Profile p)
    {
        var json = TryBase64Decode(b64);
        if (json is null) return false;
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        if (node is null) return false;

        p.Protocol = ProfileProtocol.Vmess;
        p.Name = node["ps"]?.GetValue<string>() ?? node["remark"]?.GetValue<string>() ?? "vmess";
        p.Address = node["add"]?.GetValue<string>() ?? "";
        // port/aid may arrive as JSON numbers or strings — ToString() covers both.
        p.Port = int.TryParse(node["port"]?.ToString(), out var pt) ? pt : 443;
        p.Uuid = node["id"]?.GetValue<string>() ?? "";
        p.AlterId = int.TryParse(node["aid"]?.ToString(), out var a) ? a : 0;
        p.Network = node["net"]?.GetValue<string>() ?? "tcp";
        p.Security = node["tls"]?.GetValue<string>() switch { "tls" => "tls", "reality" => "reality", _ => "none" };
        p.Sni = node["sni"]?.GetValue<string>() ?? "";
        p.WsPath = node["path"]?.GetValue<string>() ?? "/";
        p.WsHost = node["host"]?.GetValue<string>() ?? "";
        var fp = node["fp"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(fp)) p.Fingerprint = fp;
        return !string.IsNullOrWhiteSpace(p.Address);
    }

    private static bool ParseStandard(Uri uri, Profile p, ProfileProtocol proto)
    {
        p.Protocol = proto;
        p.Name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = $"{proto.ToString().ToLowerInvariant()}:{uri.Host}";
        p.Address = uri.Host;
        p.Port = uri.Port > 0 ? uri.Port : 443;
        if (proto == ProfileProtocol.Trojan)
        {
            p.Password = uri.UserInfo; // trojan share links carry the password as userinfo
            p.Uuid = "";
        }
        else
        {
            p.Uuid = uri.UserInfo;
        }

        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        p.Network = q["type"] is { Length: > 0 } t ? t : "tcp";
        p.Security = q["security"] is { Length: > 0 } sec ? sec : proto == ProfileProtocol.Trojan ? "tls" : "none";
        p.Sni = q["sni"] ?? q["peer"] ?? "";
        p.Flow = q["flow"] ?? "";
        p.Fingerprint = q["fp"] is { Length: > 0 } fp ? fp : "chrome";
        p.RealityPublicKey = q["pbk"] ?? "";
        p.RealityShortId = q["sid"] ?? "";
        p.WsPath = q["path"] is { Length: > 0 } path ? Uri.UnescapeDataString(path) : "/";
        p.WsHost = q["host"] ?? "";
        p.GrpcServiceName = q["serviceName"] ?? "";
        p.AllowInsecure = q["allowInsecure"] == "1";
        return true;
    }

    private static bool ParseSs(string body, Profile p)
    {
        p.Protocol = ProfileProtocol.Shadowsocks;

        // ss://base64(method:password)@host:port#name  OR  ss://base64(method:password@host:port)#name
        string name = "";
        var hashIdx = body.IndexOf('#');
        if (hashIdx >= 0)
        {
            name = Uri.UnescapeDataString(body[(hashIdx + 1)..]);
            body = body[..hashIdx];
        }

        if (body.Contains('@'))
        {
            var at = body.LastIndexOf('@');
            var userInfo = TryBase64Decode(body[..at]) ?? body[..at];
            var hostPart = body[(at + 1)..];
            var sepIdx = userInfo.IndexOf(':');
            if (sepIdx < 0) return false;
            p.SsMethod = userInfo[..sepIdx];
            p.Password = userInfo[(sepIdx + 1)..];
            var colon = hostPart.LastIndexOf(':');
            if (colon < 0) return false;
            p.Address = hostPart[..colon].Trim('[', ']');
            p.Port = int.Parse(hostPart[(colon + 1)..]);
        }
        else
        {
            var decoded = TryBase64Decode(body);
            if (decoded is null) return false;
            var sepIdx = decoded.IndexOf(':');
            var colon = decoded.LastIndexOf(':');
            if (sepIdx < 0 || colon < 0 || colon <= sepIdx) return false;
            p.SsMethod = decoded[..sepIdx];
            var hostPart = decoded[(sepIdx + 1)..];
            var at = hostPart.LastIndexOf('@');
            p.Password = hostPart[..at];
            var addr = hostPart[(at + 1)..];
            var c2 = addr.LastIndexOf(':');
            p.Address = addr[..c2].Trim('[', ']');
            p.Port = int.Parse(addr[(c2 + 1)..]);
        }

        p.Name = string.IsNullOrWhiteSpace(name) ? $"ss:{p.Address}" : name;
        return !string.IsNullOrWhiteSpace(p.Address);
    }

    private static string? TryBase64Decode(string s)
    {
        s = s.Trim().Replace('-', '+').Replace('_', '/').TrimEnd('=');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try
        {
            var bytes = Convert.FromBase64String(s);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
