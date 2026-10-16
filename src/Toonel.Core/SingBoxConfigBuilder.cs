using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>
/// Generates the sing-box sidecar config used for whole-system traffic (TUN) with
/// Proxifier-grade split tunneling via process_name route rules.
/// Rule order (first match wins):
///   0. QUIC (udp/443) reject in psiphon modes (psiphon local proxy is TCP-only)
///   1. our own engine binaries -> direct (anti-loop: their traffic must bypass the TUN)
///   2. always-direct exceptions (private IPs, user IP/domain/port) -> direct
///   3. always-proxy exceptions (user IP/domain/port) -> xray
///   4. per-process rule according to include/exclude mode
///   5. catch-all according to include/exclude mode (include => direct, exclude => xray)
/// </summary>
public static class SingBoxConfigBuilder
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public const string OutXray = "xray";
    public const string OutDirect = "direct";

    public static string Build(AppSettings s, ConnectionMode mode, string? logFilePath = null)
    {
        var st = s.SplitTunnel;
        var psiphonMode = mode is ConnectionMode.PsiphonOnly or ConnectionMode.PsiphonAfter or ConnectionMode.PsiphonBefore;

        // Catch-all destination: include mode tunnels only listed apps (rest direct);
        // exclude mode tunnels everything except listed apps.
        string catchAll = st.Mode switch
        {
            SplitTunnelMode.Include when st.Processes.Count > 0 => OutDirect,
            SplitTunnelMode.Include => OutDirect, // include with empty list => nothing tunnels
            SplitTunnelMode.Exclude when st.Processes.Count > 0 => OutXray,
            SplitTunnelMode.Exclude => OutXray,
            _ => OutXray, // Off => everything tunnels
        };

        var rules = new JsonArray();

        // Sniffing must be a rule action in sing-box 1.11+ (inbound "sniff" field was
        // removed in 1.13). Runs first so later rules can match sniffed domains.
        rules.Add(new JsonObject { ["inbound"] = new JsonArray("tun-in"), ["action"] = "sniff" });

        // 1. Anti-loop first: our engines' own traffic must bypass the TUN *and* must not
        //    be hit by the QUIC reject (psiphon may legitimately use QUIC-OSSH tunnels).
        rules.Add(ProcessRule(new[] { PsiphonConfigBuilder.PsiphonBinaryName, "xray.exe", "sing-box.exe" }, OutDirect));

        // 2. QUIC reject for everything else: pushes browsers/apps to TCP through the chain.
        if (s.BlockQuic && psiphonMode)
            rules.Add(new JsonObject { ["network"] = "udp", ["port"] = 443, ["action"] = "reject" });

        // 2. Always-direct exceptions.
        if (st.BypassPrivateIps)
            rules.Add(DestRule(OutDirect, "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
                "127.0.0.0/8", "169.254.0.0/16", "::1/128", "fc00::/7", "fe80::/10"));
        AddDestRules(rules, OutDirect, st.DirectDomains, st.DirectIps, st.DirectPorts);

        // 3. Always-proxy exceptions.
        AddDestRules(rules, OutXray, st.ProxyDomains, st.ProxyIps, st.ProxyPorts);

        // 4. Per-process rule.
        if (st.Mode == SplitTunnelMode.Include && st.Processes.Count > 0)
            rules.Add(ProcessRule(st.Processes, OutXray));
        else if (st.Mode == SplitTunnelMode.Exclude && st.Processes.Count > 0)
            rules.Add(ProcessRule(st.Processes, OutDirect));

        // 5. Catch-all (also set as route.final).
        rules.Add(new JsonObject { ["outbound"] = catchAll });

        var inbounds = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["interface_name"] = "Toonel",
                ["address"] = new JsonArray("172.19.0.1/30"),
                ["mtu"] = 9000,
                ["auto_route"] = true,
                ["strict_route"] = true,
                ["stack"] = "mixed",
            },
        };

        var outbounds = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = OutXray,
                ["server"] = "127.0.0.1",
                // Psiphon-only has no xray front-router: TUN dials psiphon's local SOCKS.
                ["server_port"] = mode == ConnectionMode.PsiphonOnly ? s.Ports.PsiphonSocksPort : s.Ports.MainSocksPort,
            },
            new JsonObject { ["type"] = "direct", ["tag"] = OutDirect },
        };

        var log = new JsonObject { ["level"] = "info" };
        if (!string.IsNullOrWhiteSpace(logFilePath)) log["output"] = logFilePath;

        var cfg = new JsonObject
        {
            ["log"] = log,
            ["dns"] = new JsonObject
            {
                // sing-box 1.12+ removed the legacy "address" DNS format; the new
                // format uses a "type" field instead.
                ["servers"] = new JsonArray(new JsonObject { ["type"] = "local", ["tag"] = "local" }),
            },
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["route"] = new JsonObject
            {
                ["rules"] = rules,
                ["auto_detect_interface"] = true,
                ["final"] = catchAll,
            },
        };
        return cfg.ToJsonString(Indented);
    }

    private static JsonObject ProcessRule(IEnumerable<string> processes, string outbound)
    {
        var arr = new JsonArray();
        foreach (var p in processes)
        {
            var name = p.Trim();
            if (name.Length == 0) continue;
            arr.Add(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
        }
        return new JsonObject { ["process_name"] = arr, ["outbound"] = outbound };
    }

    private static void AddDestRules(JsonArray rules, string outbound, List<string> domains, List<string> ips, List<string> ports)
    {
        if (domains.Count > 0)
        {
            var suffix = new JsonArray();
            foreach (var d in domains)
            {
                var v = d.Trim().ToLowerInvariant();
                if (v.Length == 0) continue;
                suffix.Add(v.StartsWith("*.") ? v[2..] : v); // domain_suffix matches subdomains too
            }
            if (suffix.Count > 0)
                rules.Add(new JsonObject { ["domain_suffix"] = suffix, ["outbound"] = outbound });
        }

        if (ips.Count > 0)
        {
            var cidr = new JsonArray();
            foreach (var ip in ips) if (!string.IsNullOrWhiteSpace(ip)) cidr.Add(ip.Trim());
            if (cidr.Count > 0)
                rules.Add(new JsonObject { ["ip_cidr"] = cidr, ["outbound"] = outbound });
        }

        if (ports.Count > 0)
        {
            var pr = new JsonArray();
            foreach (var port in ports) if (!string.IsNullOrWhiteSpace(port)) pr.Add(port.Trim());
            if (pr.Count > 0)
                rules.Add(new JsonObject { ["port"] = pr, ["outbound"] = outbound });
        }
    }

    private static JsonObject DestRule(string outbound, params string[] ipCidr)
    {
        var arr = new JsonArray();
        foreach (var c in ipCidr) arr.Add(c);
        return new JsonObject { ["ip_cidr"] = arr, ["outbound"] = outbound };
    }
}
