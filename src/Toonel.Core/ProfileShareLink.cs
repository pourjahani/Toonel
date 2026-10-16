using System.Text;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>
/// Builds v2rayNG-style share links from profiles (the inverse of ProfileUrlParser),
/// used by the copy/paste commands on the Profiles page.
/// </summary>
public static class ProfileShareLink
{
    /// <summary>Serializes one profile to its standard share URL.</summary>
    public static string Build(Profile p)
    {
        return p.Protocol switch
        {
            ProfileProtocol.Vless => BuildStandard(p, "vless"),
            ProfileProtocol.Trojan => BuildStandard(p, "trojan"),
            ProfileProtocol.Vmess => BuildVmess(p),
            ProfileProtocol.Shadowsocks => BuildSs(p),
            _ => "",
        };
    }

    /// <summary>Serializes many profiles, one link per line (skips unserializable ones).</summary>
    public static string BuildMany(IEnumerable<Profile> profiles)
    {
        var sb = new StringBuilder();
        foreach (var p in profiles)
        {
            var link = Build(p);
            if (link.Length > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(link);
            }
        }
        return sb.ToString();
    }

    private static string BuildStandard(Profile p, string scheme)
    {
        var sb = new StringBuilder();
        sb.Append(scheme).Append("://");
        sb.Append(Uri.EscapeDataString(p.Protocol == ProfileProtocol.Trojan ? p.Password : p.Uuid));
        sb.Append('@').Append(p.Address).Append(':').Append(p.Port);
        sb.Append("?security=").Append(p.Security);
        if (!string.IsNullOrEmpty(p.Sni)) sb.Append("&sni=").Append(Uri.EscapeDataString(p.Sni));
        if (!string.IsNullOrEmpty(p.Flow)) sb.Append("&flow=").Append(Uri.EscapeDataString(p.Flow));
        if (!string.IsNullOrEmpty(p.Fingerprint)) sb.Append("&fp=").Append(Uri.EscapeDataString(p.Fingerprint));
        if (!string.IsNullOrEmpty(p.RealityPublicKey)) sb.Append("&pbk=").Append(Uri.EscapeDataString(p.RealityPublicKey));
        if (!string.IsNullOrEmpty(p.RealityShortId)) sb.Append("&sid=").Append(Uri.EscapeDataString(p.RealityShortId));
        if (p.AllowInsecure) sb.Append("&allowInsecure=1");

        var network = string.IsNullOrEmpty(p.Network) ? "tcp" : p.Network;
        sb.Append("&type=").Append(network);
        if (network == "ws" || network == "h2")
        {
            if (!string.IsNullOrEmpty(p.WsPath)) sb.Append("&path=").Append(Uri.EscapeDataString(p.WsPath));
            if (!string.IsNullOrEmpty(p.WsHost)) sb.Append("&host=").Append(Uri.EscapeDataString(p.WsHost));
        }
        else if (network == "grpc" && !string.IsNullOrEmpty(p.GrpcServiceName))
        {
            sb.Append("&serviceName=").Append(Uri.EscapeDataString(p.GrpcServiceName));
        }

        var name = string.IsNullOrWhiteSpace(p.Name) ? $"{scheme}:{p.Address}" : p.Name;
        sb.Append('#').Append(Uri.EscapeDataString(name));
        return sb.ToString();
    }

    private static string BuildVmess(Profile p)
    {
        // v2rayNG vmess JSON, base64url-encoded.
        var json = BuildVmessJson(
            ("v", "2"), ("ps", p.Name), ("add", p.Address), ("port", p.Port.ToString()),
            ("id", p.Uuid), ("aid", p.AlterId.ToString()), ("scy", p.Encryption is "none" ? "auto" : p.Encryption),
            ("net", string.IsNullOrEmpty(p.Network) ? "tcp" : p.Network),
            ("type", "none"),
            ("host", p.WsHost ?? ""), ("path", p.WsPath ?? "/"),
            ("tls", p.Security == "tls" || p.Security == "reality" ? "tls" : ""),
            ("sni", p.Sni ?? ""), ("fp", p.Fingerprint ?? "chrome"));
        return "vmess://" + ToBase64Url(Encoding.UTF8.GetBytes(json));
    }

    private static string BuildSs(Profile p)
    {
        var userInfo = ToBase64Url(Encoding.UTF8.GetBytes($"{p.SsMethod}:{p.Password}"));
        var name = Uri.EscapeDataString(string.IsNullOrWhiteSpace(p.Name) ? $"ss:{p.Address}" : p.Name);
        return $"ss://{userInfo}@{p.Address}:{p.Port}#{name}";
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string BuildVmessJson(params (string Key, string Value)[] pairs)
    {
        var obj = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in pairs) obj[key] = value;
        return obj.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
