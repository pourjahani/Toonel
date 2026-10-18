using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>
/// Generates the Xray-core config. Xray is always the "front router" (main socks/http inbounds),
/// so split tunneling and the four connection modes stay consistent:
///   A V2RayOnly      : main inbounds -> proxy outbound (VLESS/VMess/Trojan/SS)
///   B PsiphonOnly    : main inbounds -> socks outbound -> Psiphon local SOCKS
///   C PsiphonAfter   : main inbounds -> Psiphon local SOCKS; dedicated inbound -> proxy
///                      (psiphon's UpstreamProxyURL points at the dedicated inbound)
///   D PsiphonBefore  : main inbounds -> proxy, proxy dials through Psiphon (dialerProxy)
/// </summary>
public static class XrayConfigBuilder
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public const string OutProxy = "proxy-out";
    public const string OutPsiphon = "psiphon-out";
    public const string OutDirect = "direct-out";
    public const string OutBlock = "block-out";
    public const string InMainSocks = "in-main-socks";
    public const string InMainHttp = "in-main-http";
    public const string InVlessOnly = "in-vless-only";
    public const string OutWarp = "warp-out";
    public const string OutFragment = "fragment-out";

    public static string Build(AppSettings s, ConnectionMode mode)
    {
        var inbounds = new JsonArray
        {
            SocksInbound(InMainSocks, s.Ports.MainSocksPort, udp: true),
            HttpInbound(InMainHttp, s.Ports.MainHttpPort),
            SocksInbound(InVlessOnly, s.Ports.VlessOnlySocksPort, udp: false),
        };

        var outbounds = new JsonArray();
        string mainOut;

        if (mode == ConnectionMode.PsiphonOnly)
        {
            outbounds.Add(PsiphonOutbound(s));
            mainOut = OutPsiphon;
        }
        else
        {
            // What the proxy outbound dials its server through (null = directly).
            var dialer = mode switch
            {
                ConnectionMode.PsiphonBefore => OutPsiphon,
                ConnectionMode.WarpBefore => OutWarp,
                _ => null as string,
            };
            // Fragment threading: the server connection is dialed through a freedom
            // outbound that fragments the TLS handshake / TCP writes; that freedom
            // outbound in turn dials through the chain dialer (psiphon / warp), so
            // fragmentation composes with every mode.
            if (s.Dpi.UseFragment)
            {
                outbounds.Add(FragmentOutbound(s, dialer));
                dialer = OutFragment;
            }
            var proxy = ProxyOutbound(s.SelectedProfile, dialer, s.Dpi.CipherSuites, s.Dpi.FinalMaskJson);
            if (mode == ConnectionMode.WarpBefore)
                outbounds.Add(WarpOutbound(s));
            outbounds.Add(proxy);
            mainOut = OutProxy;
        }

        if (mode == ConnectionMode.PsiphonAfter || mode == ConnectionMode.PsiphonBefore || mode == ConnectionMode.PsiphonOnly)
            outbounds.Add(PsiphonOutbound(s));

        outbounds.Add(new JsonObject { ["tag"] = OutDirect, ["protocol"] = "freedom", ["settings"] = new JsonObject() });
        outbounds.Add(new JsonObject { ["tag"] = OutBlock, ["protocol"] = "blackhole", ["settings"] = new JsonObject() });

        var routing = new JsonObject
        {
            ["domainStrategy"] = "AsIs",
            ["rules"] = BuildRoutingRules(s, mode, mainOut),
        };

        var cfg = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["dns"] = DnsEntry(s.DnsChoice),
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["routing"] = routing,
        };
        return cfg.ToJsonString(Indented);
    }

    private static JsonArray BuildRoutingRules(AppSettings s, ConnectionMode mode, string mainOut)
    {
        var rules = new JsonArray();

        // Dedicated inbound always routes ONLY to the VLESS/VMess outbound (never to psiphon):
        // Psiphon's upstream in "after" mode enters here, so the chain cannot loop back.
        rules.Add(Rule(inboundTag: InVlessOnly, outbound: mode == ConnectionMode.PsiphonOnly ? OutPsiphon : OutProxy));

        if (mode == ConnectionMode.PsiphonOnly || mode == ConnectionMode.PsiphonAfter)
        {
            rules.Add(Rule(inboundTag: $"{InMainSocks},{InMainHttp}", outbound: OutPsiphon));
        }
        else
        {
            rules.Add(Rule(inboundTag: $"{InMainSocks},{InMainHttp}", outbound: mainOut));
        }

        return rules;
    }

    private static JsonObject Rule(string inboundTag, string outbound)
    {
        var tags = inboundTag.Split(',');
        return new JsonObject
        {
            ["inboundTag"] = new JsonArray(tags.Select(t => JsonValue.Create(t)).ToArray()),
            ["outboundTag"] = outbound,
        };
    }

    private static JsonObject SocksInbound(string tag, int port, bool udp) => new()
    {
        ["tag"] = tag,
        ["listen"] = "127.0.0.1",
        ["port"] = port,
        ["protocol"] = "socks",
        ["settings"] = new JsonObject { ["udp"] = udp, ["auth"] = "noauth" },
        ["sniffing"] = new JsonObject
        {
            ["enabled"] = true,
            ["destOverride"] = new JsonArray("http", "tls"),
            ["routeOnly"] = false,
        },
    };

    private static JsonObject HttpInbound(string tag, int port) => new()
    {
        ["tag"] = tag,
        ["listen"] = "127.0.0.1",
        ["port"] = port,
        ["protocol"] = "http",
        ["settings"] = new JsonObject(),
    };

        /// <summary>Xray dns block for the chosen resolver (auto keeps system DNS).</summary>
    public static JsonObject? DnsEntry(string choice)
    {
        var addr = choice switch
        {
            "doh:1.1.1.1" => "https://cloudflare-dns.com/dns-query",
            "doh:8.8.8.8" => "https://dns.google/dns-query",
            "doh:9.9.9.9" => "https://dns.quad9.net/dns-query",
            "1.1.1.1" => "1.1.1.1",
            "8.8.8.8" => "8.8.8.8",
            "9.9.9.9" => "9.9.9.9",
            _ => null,
        };
        if (addr is null) return null;
        return new JsonObject { ["servers"] = new JsonArray(addr) };
    }

    /// <summary>Xray wireguard settings (keys in hex; accepts base64 input).</summary>
    public static JsonObject WireGuardSettings(Profile p)
    {
        var peer = new JsonObject
        {
            ["publicKey"] = B64ToHex(p.WgPeerPublicKey),
            ["endpoint"] = $"{p.Address}:{p.Port}",
        };
        if (TryReservedBytes(p.Reserved, out var reserved))
            peer["reserved"] = new JsonArray(reserved.Select(b => JsonValue.Create((int)b)).ToArray());
        var settings = new JsonObject
        {
            ["secretKey"] = B64ToHex(p.WgPrivateKey),
            ["mtu"] = Models.WarpDefaults.Mtu,
            ["peers"] = new JsonArray(peer),
        };
        if (!string.IsNullOrWhiteSpace(p.WgAddress))
            settings["address"] = new JsonArray(p.WgAddress);
        return settings;
    }

    /// <summary>Cloudflare WARP hop outbound built from the saved WarpSettings.</summary>
    public static JsonObject WarpOutbound(AppSettings s)
    {
        var p = new Profile
        {
            Protocol = ProfileProtocol.WireGuard,
            Address = Models.WarpDefaults.EndpointHost,
            Port = Models.WarpDefaults.EndpointPort,
            WgPrivateKey = s.Warp.PrivateKey,
            WgPeerPublicKey = Models.WarpDefaults.PeerPublicKey,
            WgAddress = s.Warp.Address,
            Reserved = s.Warp.Reserved,
        };
        return new JsonObject
        {
            ["tag"] = OutWarp,
            ["protocol"] = "wireguard",
            ["settings"] = WireGuardSettings(p),
        };
    }

    internal static string B64ToHex(string b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return "";
        var trimmed = b64.Trim();
        if (trimmed.Length % 2 == 0 && trimmed.All(Uri.IsHexDigit)) return trimmed.ToLowerInvariant();
        var bytes = Convert.FromBase64String(trimmed);
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    internal static bool TryReservedBytes(string reserved, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(reserved)) return false;
        var r = reserved.Trim();
        if (r.Contains(","))
        {
            var parts = r.Split(",");
            var list = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!byte.TryParse(parts[i].Trim(), out list[i])) return false;
            bytes = list;
            return list.Length == 3;
        }
        try
        {
            bytes = Convert.FromBase64String(r.PadRight(r.Length + (4 - r.Length % 4) % 4, '='));
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Freedom outbound that fragments the outgoing TLS/TCP connection.
    /// `dialer` lets it itself dial through the psiphon/warp hop in chain modes.</summary>
    private static JsonObject FragmentOutbound(AppSettings s, string? dialer)
    {
        var fragment = new JsonObject
        {
            ["packets"] = string.IsNullOrWhiteSpace(s.Dpi.FragmentPackets) ? "tlshello" : s.Dpi.FragmentPackets.Trim(),
            ["length"] = string.IsNullOrWhiteSpace(s.Dpi.FragmentLength) ? "100-200" : s.Dpi.FragmentLength.Trim(),
            ["interval"] = string.IsNullOrWhiteSpace(s.Dpi.FragmentInterval) ? "10-20" : s.Dpi.FragmentInterval.Trim(),
        };
        var outbound = new JsonObject
        {
            ["tag"] = OutFragment,
            ["protocol"] = "freedom",
            ["settings"] = new JsonObject { ["fragment"] = fragment },
        };
        if (dialer is string d)
            outbound["streamSettings"] = new JsonObject
            {
                ["sockopt"] = new JsonObject { ["dialerProxy"] = d },
            };
        return outbound;
    }

    private static JsonObject PsiphonOutbound(AppSettings s) => new()
    {
        ["tag"] = OutPsiphon,
        ["protocol"] = "socks",
        ["settings"] = new JsonObject
        {
            ["servers"] = new JsonArray(new JsonObject
            {
                ["address"] = "127.0.0.1",
                ["port"] = s.Ports.PsiphonSocksPort,
            }),
        },
    };

    private static JsonObject ProxyOutbound(Profile? p, string? dialerOut, string? cipherSuites = null, string? finalMaskJson = null)
    {
        if (p is null)
        {
            // No profile configured: fail closed to direct so xray still starts (UI blocks connect).
            return new JsonObject { ["tag"] = OutProxy, ["protocol"] = "freedom", ["settings"] = new JsonObject() };
        }

        JsonObject users;
        JsonObject settings;
        switch (p.Protocol)
        {
            case ProfileProtocol.Vmess:
                users = new JsonObject
                {
                    ["id"] = p.Uuid,
                    ["alterId"] = p.AlterId,
                    ["security"] = string.IsNullOrWhiteSpace(p.Encryption) || p.Encryption == "none" ? "auto" : p.Encryption,
                    ["level"] = 8,
                };
                settings = new JsonObject
                {
                    ["vnext"] = new JsonArray(new JsonObject { ["address"] = p.Address, ["port"] = p.Port, ["users"] = new JsonArray(users) }),
                };
                break;

            case ProfileProtocol.Trojan:
                settings = new JsonObject
                {
                    ["servers"] = new JsonArray(new JsonObject
                    {
                        ["address"] = p.Address, ["port"] = p.Port, ["password"] = p.Password, ["level"] = 8,
                    }),
                };
                break;

            case ProfileProtocol.Shadowsocks:
                settings = new JsonObject
                {
                    ["servers"] = new JsonArray(new JsonObject
                    {
                        ["address"] = p.Address, ["port"] = p.Port, ["method"] = p.SsMethod, ["password"] = p.Password, ["uot"] = true,
                    }),
                };
                break;

            case ProfileProtocol.Socks:
                settings = new JsonObject
                {
                    ["servers"] = new JsonArray(new JsonObject
                    {
                        ["address"] = p.Address, ["port"] = p.Port,
                        ["users"] = string.IsNullOrEmpty(p.Uuid)
                            ? null
                            : new JsonArray(new JsonObject { ["user"] = p.Uuid, ["pass"] = p.Password }),
                    }),
                };
                break;

            case ProfileProtocol.WireGuard:
                users = new JsonObject();
                settings = WireGuardSettings(p);
                break;

            case ProfileProtocol.Http:
                settings = new JsonObject
                {
                    ["servers"] = new JsonArray(new JsonObject
                    {
                        ["address"] = p.Address, ["port"] = p.Port,
                        ["users"] = string.IsNullOrEmpty(p.Uuid)
                            ? null
                            : new JsonArray(new JsonObject { ["user"] = p.Uuid, ["pass"] = p.Password }),
                    }),
                };
                break;

            default: // Vless
                users = new JsonObject
                {
                    ["id"] = p.Uuid,
                    ["encryption"] = "none",
                    ["level"] = 8,
                };
                if (!string.IsNullOrWhiteSpace(p.Flow)) users["flow"] = p.Flow;
                settings = new JsonObject
                {
                    ["vnext"] = new JsonArray(new JsonObject { ["address"] = p.Address, ["port"] = p.Port, ["users"] = new JsonArray(users) }),
                };
                break;
        }

        var outbound = new JsonObject
        {
            ["tag"] = OutProxy,
            ["protocol"] = p.Protocol switch
            {
                ProfileProtocol.Vless => "vless",
                ProfileProtocol.WireGuard => "wireguard",
                ProfileProtocol.Vmess => "vmess",
                ProfileProtocol.Trojan => "trojan",
                ProfileProtocol.Shadowsocks => "shadowsocks",
                ProfileProtocol.Socks => "socks",
                ProfileProtocol.Http => "http",
                _ => "freedom",
            },
            ["settings"] = settings,
        };

        var stream = BuildStreamSettings(p, cipherSuites);

        // The proxy outbound dials its server through the given outbound tag
        // (psiphon / warp / fragment hop); the hop itself has no recursive dialer.
        if (dialerOut is string dialer)
            stream["sockopt"] = new JsonObject { ["dialerProxy"] = dialer };

        // FinalMask traffic obfuscation (Xray-core v26.2.6+) injected verbatim.
        if (!string.IsNullOrWhiteSpace(finalMaskJson))
        {
            try
            {
                if (JsonNode.Parse(finalMaskJson) is JsonObject finalmask)
                    stream["finalmask"] = finalmask.DeepClone();
            }
            catch
            {
                // invalid snippets are rejected by the UI/tests; never crash generation
            }
        }

        if (stream.Count > 0) outbound["streamSettings"] = stream;
        return outbound;
    }

    public static JsonObject BuildStreamSettings(Profile p, string? cipherSuites = null)
    {
        var stream = new JsonObject();

        switch (p.Network)
        {
            case "ws":
                var ws = new JsonObject { ["path"] = string.IsNullOrWhiteSpace(p.WsPath) ? "/" : p.WsPath };
                if (!string.IsNullOrWhiteSpace(p.WsHost))
                    ws["headers"] = new JsonObject { ["Host"] = p.WsHost };
                stream["network"] = "ws";
                stream["wsSettings"] = ws;
                break;
            case "grpc":
                stream["network"] = "grpc";
                stream["grpcSettings"] = new JsonObject { ["serviceName"] = p.GrpcServiceName };
                break;
            case "h2":
                stream["network"] = "h2";
                var http = new JsonObject { ["path"] = string.IsNullOrWhiteSpace(p.WsPath) ? "/" : p.WsPath };
                http["host"] = new JsonArray(string.IsNullOrWhiteSpace(p.WsHost) ? p.Address : p.WsHost);
                stream["httpSettings"] = http;
                break;
            default:
                stream["network"] = "tcp";
                break;
        }

        if (p.Security == "tls")
        {
            stream["security"] = "tls";
            var tls = new JsonObject
            {
                ["serverName"] = string.IsNullOrWhiteSpace(p.Sni) ? p.Address : p.Sni,
                ["allowInsecure"] = p.AllowInsecure,
                ["fingerprint"] = string.IsNullOrWhiteSpace(p.Fingerprint) ? "chrome" : p.Fingerprint,
            };
            // TLS 1.0–1.2 suites only; a uTLS fingerprint overrides this on the Xray side.
            if (!string.IsNullOrWhiteSpace(cipherSuites)) tls["cipherSuites"] = cipherSuites.Trim();
            stream["tlsSettings"] = tls;
        }
        else if (p.Security == "reality")
        {
            stream["security"] = "reality";
            stream["realitySettings"] = new JsonObject
            {
                ["serverName"] = string.IsNullOrWhiteSpace(p.Sni) ? p.Address : p.Sni,
                ["fingerprint"] = string.IsNullOrWhiteSpace(p.Fingerprint) ? "chrome" : p.Fingerprint,
                ["publicKey"] = p.RealityPublicKey,
                ["shortId"] = p.RealityShortId,
                ["spiderX"] = "/",
            };
        }

        return stream;
    }
}
