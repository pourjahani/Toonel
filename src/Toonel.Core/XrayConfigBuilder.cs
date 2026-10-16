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
            var proxy = ProxyOutbound(s.SelectedProfile, mode == ConnectionMode.PsiphonBefore ? s.Ports.PsiphonSocksPort : null);
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

    private static JsonObject ProxyOutbound(Profile? p, int? dialThroughPsiphonSocksPort)
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
                ProfileProtocol.Vmess => "vmess",
                ProfileProtocol.Trojan => "trojan",
                ProfileProtocol.Shadowsocks => "shadowsocks",
                ProfileProtocol.Socks => "socks",
                ProfileProtocol.Http => "http",
                _ => "freedom",
            },
            ["settings"] = settings,
        };

        var stream = BuildStreamSettings(p);

        // "Psiphon before": the proxy outbound dials its server through Psiphon's local SOCKS.
        if (dialThroughPsiphonSocksPort is int portNum)
        {
            var sockopt = new JsonObject { ["dialerProxy"] = OutPsiphon };
            // NOTE: dialerProxy dials the outbound's *server* through the given outbound tag;
            // the psiphon outbound itself must not recursively use a dialer (it doesn't).
            stream["sockopt"] = sockopt;
            _ = portNum; // port is implied by the psiphon outbound definition
        }

        if (stream.Count > 0) outbound["streamSettings"] = stream;
        return outbound;
    }

    public static JsonObject BuildStreamSettings(Profile p)
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
            stream["tlsSettings"] = new JsonObject
            {
                ["serverName"] = string.IsNullOrWhiteSpace(p.Sni) ? p.Address : p.Sni,
                ["allowInsecure"] = p.AllowInsecure,
                ["fingerprint"] = string.IsNullOrWhiteSpace(p.Fingerprint) ? "chrome" : p.Fingerprint,
            };
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
