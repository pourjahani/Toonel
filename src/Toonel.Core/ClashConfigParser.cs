using System.Text.RegularExpressions;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>
/// Parses the "proxies:" section of Clash YAML configs. A pragmatic subset:
/// flow-style entries (- {k: v, ...}) and block-style entries (- key: v + indented keys)
/// covering ss / trojan / vmess / vless.
/// </summary>
public static class ClashConfigParser
{
    private const char CR = (char)13;
    private const char LF = (char)10;
    private const char TT = (char)9;
    private const char Q = (char)39;

    public static List<Profile> Parse(string text)
    {
        var result = new List<Profile>();
        var lines = text.Replace(CR.ToString(), "").Split(LF);
        var pi = Array.FindIndex(lines, l => Regex.IsMatch(l.Trim(), "^proxies\\s*:"));
        if (pi < 0) return result;

        var i = pi + 1;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.Trim().Length > 0 && !line.StartsWith(" ") && !line.StartsWith("-") && Regex.IsMatch(line.Trim(), "^[A-Za-z0-9_-]+\\s*:"))
                break;
            var t = line.TrimStart(new[] { " "[0], TT });
            if (t.Length == 0 || !t.StartsWith("-") || t.StartsWith("#")) { i++; continue; }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int open = t.IndexOf("{");
            if (open >= 0)
            {
                int close = t.LastIndexOf("}");
                if (close > open) ParseFlow(t.Substring(open + 1, close - open - 1), fields);
                i++;
            }
            else
            {
                var inline = t.Substring(1).TrimStart();
                var ci = inline.IndexOf(":");
                if (ci > 0) fields[inline.Substring(0, ci).Trim()] = YamlValue(inline.Substring(ci + 1).Trim());
                i++;
                while (i < lines.Length)
                {
                    var sub = lines[i];
                    if (sub.Trim().Length == 0) { i++; continue; }
                    if (sub.StartsWith("-") || sub.Trim().StartsWith("#")) break;
                    if (sub.StartsWith("    ") || sub.StartsWith("        "))
                    {
                        var si = sub.IndexOf(":");
                        if (si > 0) fields[sub.Substring(0, si).Trim()] = YamlValue(sub.Substring(si + 1).Trim());
                        i++;
                    }
                    else break;
                }
            }
            var p = Map(fields);
            if (p != null) result.Add(p);
        }
        return result;
    }

    private static Profile? Map(Dictionary<string, string> f)
    {
        if (!f.TryGetValue("server", out var host) || !f.TryGetValue("port", out var portStr)) return null;
        if (!int.TryParse(portStr.Trim().Split(",")[0], out var port)) return null;
        f.TryGetValue("name", out var name);
        f.TryGetValue("type", out var type);
        var p = new Profile
        {
            Address = host.Trim(new[] { "["[0], "]"[0] }),
            Port = port,
            Name = string.IsNullOrWhiteSpace(name) ? ((type ?? "node") + ":" + host) : name,
        };

        switch ((type ?? "").ToLowerInvariant())
        {
            case "ss":
                p.Protocol = ProfileProtocol.Shadowsocks;
                if (f.TryGetValue("cipher", out var cipher)) p.SsMethod = cipher;
                if (f.TryGetValue("password", out var pass)) p.Password = pass;
                p.Security = "none"; p.Network = "tcp";
                break;

            case "trojan":
                p.Protocol = ProfileProtocol.Trojan;
                if (f.TryGetValue("password", out var tp)) p.Password = tp;
                p.Security = "tls"; p.Network = "tcp";
                if (f.TryGetValue("sni", out var tsni)) p.Sni = tsni;
                if (f.TryGetValue("skip-cert-verify", out var scv)) p.AllowInsecure = scv == "true";
                if (f.TryGetValue("network", out var tnet) && tnet.Length > 0) p.Network = tnet;
                break;

            case "vmess":
                p.Protocol = ProfileProtocol.Vmess;
                if (f.TryGetValue("uuid", out var vu)) p.Uuid = vu;
                if (f.TryGetValue("alterId", out var aid) && int.TryParse(aid, out var av)) p.AlterId = av;
                string? sec = f.TryGetValue("tls", out var tls) ? tls : null;
                p.Security = (sec == "true" || sec == "tls") ? "tls" : "none";
                if (f.TryGetValue("servername", out var se)) p.Sni = se;
                if (f.TryGetValue("network", out var vnet) && vnet.Length > 0) p.Network = vnet;
                break;

            case "vless":
                p.Protocol = ProfileProtocol.Vless;
                if (f.TryGetValue("uuid", out var lu)) p.Uuid = lu;
                bool ltlsB = f.TryGetValue("tls", out var ltls) && (ltls == "true" || ltls == "tls");
                p.Security = ltlsB ? "tls" : "none";
                if (f.TryGetValue("servername", out var lse) && lse.Length > 0) p.Sni = lse;
                if (f.TryGetValue("flow", out var lflow)) p.Flow = lflow;
                if (f.TryGetValue("network", out var lnet) && lnet.Length > 0) p.Network = lnet;
                if (f.TryGetValue("reality-opts", out var ro))
                {
                    var pk = Regex.Match(ro, "pbk\\s*:\\s*([^,}]+)");
                    var sid = Regex.Match(ro, "sid\\s*:\\s*([^,}]+)");
                    if (pk.Success) p.RealityPublicKey = pk.Groups[1].Value.Trim();
                    if (sid.Success) p.RealityShortId = sid.Groups[1].Value.Trim();
                    p.Security = "reality";
                }
                break;

            default:
                return null;
        }
        return p;
    }

    private static void ParseFlow(string body, Dictionary<string, string> f)
    {
        foreach (Match m in Regex.Matches(body, "([^,]+)"))
        {
            var pair = m.Groups[1].Value;
            var c = pair.IndexOf(":");
            if (c <= 0) continue;
            var k = pair.Substring(0, c).Trim();
            var v = Unquote(pair.Substring(c + 1).Trim());
            if (k.Length > 0) f[k] = v;
        }
    }

    private static string Unquote(string v)
    {
        if (v.Length >= 2)
        {
            char first = v[0];
            char last = v[v.Length - 1];
            if (first == Q && last == Q) return v.Substring(1, v.Length - 2);
        }
        return v;
    }

    private static string YamlValue(string v)
    {
        v = Unquote(v.Trim());
        return v;
    }
}
