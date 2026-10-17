using System.Text;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>Parses v2rayNG-style share links: vless://, vmess://, trojan://, ss://,
/// hysteria2://, tuic://, wireguard://, plus plain WireGuard .conf text.</summary>
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
            if (url.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase)) return ParseHy2(url["hysteria2://".Length..], profile);
            if (url.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase)) return ParseHy2(url["hy2://".Length..], profile);
            if (url.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase)) return ParseTuic(url["tuic://".Length..], profile);
            if (url.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("wg://", StringComparison.OrdinalIgnoreCase))
                return ParseWireGuard(url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..], profile);
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

        // Plain WireGuard .conf text ([Interface]/[Peer] sections) is one profile.
        if (text.Contains("[Interface]") && text.Contains("[Peer]") && !text.Contains("://"))
        {
            if (TryParseWireGuardConf(text, out var wg)) list.Add(wg);
            return list;
        }

        // Plain WireGuard .conf text ([Interface]/[Peer] sections) is one profile.
        if (text.Contains("[Interface]") && text.Contains("[Peer]") && !text.Contains("://"))
        {
            if (TryParseWireGuardConf(text, out var wg)) list.Add(wg);
            return list;
        }

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

    /// <summary>
    /// hysteria2://PASSWORD@host:port?sni=X&obfs=salamander&obfs-password=Y&insecure=1#name
    /// (the hy2:// alias is accepted too). Port ranges in ?mport= are ignored for now.
    /// </summary>
    private static bool ParseHy2(string body, Profile p)
    {
        p.Protocol = ProfileProtocol.Hysteria2;
        string name = "";
        var hashIdx = body.IndexOf('#');
        if (hashIdx >= 0)
        {
            name = Uri.UnescapeDataString(body[(hashIdx + 1)..]);
            body = body[..hashIdx];
        }
        var qIdx = body.IndexOf('?');
        var q = qIdx >= 0 ? System.Web.HttpUtility.ParseQueryString(body[(qIdx + 1)..]) : null;
        if (qIdx >= 0) body = body[..qIdx];

        var at = body.LastIndexOf('@');
        if (at < 0) return false;
        p.Password = Uri.UnescapeDataString(body[..at]);
        var hostPart = body[(at + 1)..];
        var colon = hostPart.LastIndexOf(':');
        if (colon < 0) return false;
        p.Address = hostPart[..colon].Trim('[', ']');
        p.Port = int.Parse(hostPart[(colon + 1)..]);

        p.Security = "tls";
        p.Sni = q?["sni"] ?? "";
        p.AllowInsecure = q?["insecure"] == "1" || q?["allowInsecure"] == "1";
        var obfs = q?["obfs"] ?? "";
        if (!string.IsNullOrEmpty(obfs) && obfs != "none")
        {
            p.Network = "udp+obfs";
            p.ObfsPassword = Uri.UnescapeDataString(q?["obfs-password"] ?? q?["obfsParam"] ?? "");
        }
        else
        {
            p.Network = "udp";
        }
        p.Name = string.IsNullOrWhiteSpace(name) ? $"hy2:{p.Address}" : name;
        return !string.IsNullOrWhiteSpace(p.Address);
    }

    /// <summary>tuic://UUID:PASSWORD@host:port?congestion_control=bbr&alpn=h3&sni=X&allow_insecure=1#name</summary>
    private static bool ParseTuic(string body, Profile p)
    {
        p.Protocol = ProfileProtocol.Tuic;
        string name = "";
        var hashIdx = body.IndexOf('#');
        if (hashIdx >= 0)
        {
            name = Uri.UnescapeDataString(body[(hashIdx + 1)..]);
            body = body[..hashIdx];
        }
        var qIdx = body.IndexOf('?');
        var q = qIdx >= 0 ? System.Web.HttpUtility.ParseQueryString(body[(qIdx + 1)..]) : null;
        if (qIdx >= 0) body = body[..qIdx];

        var at = body.LastIndexOf('@');
        if (at < 0) return false;
        var userInfo = Uri.UnescapeDataString(body[..at]);
        var colon = userInfo.IndexOf(':');
        p.Uuid = colon >= 0 ? userInfo[..colon] : userInfo;
        p.Password = colon >= 0 ? userInfo[(colon + 1)..] : "";
        var hostPart = body[(at + 1)..];
        var c2 = hostPart.LastIndexOf(':');
        if (c2 < 0) return false;
        p.Address = hostPart[..c2].Trim('[', ']');
        p.Port = int.Parse(hostPart[(c2 + 1)..]);

        p.Security = "tls";
        p.CongestionControl = q?["congestion_control"] ?? q?["congestioncontrol"] ?? "bbr";
        p.Alpn = q?["alpn"] ?? "h3";
        p.Sni = q?["sni"] ?? "";
        p.AllowInsecure = q?["allow_insecure"] == "1" || q?["allowInsecure"] == "1" || q?["insecure"] == "1";
        p.Network = "udp";
        p.Name = string.IsNullOrWhiteSpace(name) ? $"tuic:{p.Address}" : name;
        return !string.IsNullOrWhiteSpace(p.Address);
    }

    /// <summary>
    /// wireguard://BASE64_PRIVATE_KEY@peerHost:port?address=172.16.0.2%2F32&peer=BASE64_PUB
    /// &reserved=BASE64-or-linear (wg:// alias). Non-standard scheme; Toonel's own format
    /// (same shape as ProfileShareLink.Build for wireguard).
    /// </summary>
    private static bool ParseWireGuard(string body, Profile p)
    {
        p.Protocol = ProfileProtocol.WireGuard;
        string name = "";
        var hashIdx = body.IndexOf('#');
        if (hashIdx >= 0)
        {
            name = Uri.UnescapeDataString(body[(hashIdx + 1)..]);
            body = body[..hashIdx];
        }
        var qIdx = body.IndexOf('?');
        var q = qIdx >= 0 ? System.Web.HttpUtility.ParseQueryString(body[(qIdx + 1)..]) : null;
        if (qIdx >= 0) body = body[..qIdx];

        var at = body.LastIndexOf('@');
        if (at < 0) return false;
        p.WgPrivateKey = body[..at];
        var hp = body[(at + 1)..];
        var colon = hp.LastIndexOf(':');
        if (colon < 0) return false;
        p.Address = hp[..colon].Trim('[', ']');
        p.Port = int.Parse(hp[(colon + 1)..]);

        p.WgPeerPublicKey = q?["peer"] ?? q?["publickey"] ?? "";
        p.WgAddress = Uri.UnescapeDataString(q?["address"] ?? "");
        p.Reserved = Uri.UnescapeDataString(q?["reserved"] ?? "");
        p.Security = "none";
        p.Network = "udp";
        p.Name = string.IsNullOrWhiteSpace(name) ? $"wg:{p.Address}" : name;
        return !string.IsNullOrWhiteSpace(p.Address) && p.WgPrivateKey.Length > 0 && p.WgPeerPublicKey.Length > 0;
    }

    /// <summary>Parses plain WireGuard .conf text into a wireguard profile.</summary>
    public static bool TryParseWireGuardConf(string conf, out Profile p)
    {
        p = new Profile { Protocol = ProfileProtocol.WireGuard, Security = "none", Network = "udp" };
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in conf.Split(new[] { '\r', '\n' }))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        if (!map.TryGetValue("PrivateKey", out var priv)) return false;
        if (!map.TryGetValue("PublicKey", out var pub)) return false;
        if (string.IsNullOrWhiteSpace(priv) || string.IsNullOrWhiteSpace(pub)) return false;
        p.WgPrivateKey = priv.Replace(" ", "");
        p.WgPeerPublicKey = pub;
        if (map.TryGetValue("Address", out var addr)) p.WgAddress = addr;
        if (map.TryGetValue("Reserved", out var res)) p.Reserved = res;
        if (map.TryGetValue("Endpoint", out var ep))
        {
            var colon = ep.LastIndexOf(':');
            if (colon < 0) return false;
            p.Address = ep[..colon].Trim('[', ']');
            if (!int.TryParse(ep[(colon + 1)..].Trim(), out var portNum) || portNum <= 0) portNum = Models.WarpDefaults.EndpointPort;
p.Port = portNum;
        }
        else
        {
            p.Address = Models.WarpDefaults.EndpointHost;
            p.Port = Models.WarpDefaults.EndpointPort;
        }
        var hostIdx = p.Address.IndexOf('/');
        p.Name = $"wg:{(hostIdx > 0 ? p.Address[..hostIdx] : p.Address)}";
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
