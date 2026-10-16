using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>
/// Builds the psiphon-tunnel-core client.config JSON. The key field for chain mode
/// "Psiphon after V2Ray" is UpstreamProxyURL (officially supported by tunnel-core),
/// pointed at the dedicated Xray inbound that routes only to the VLESS outbound.
/// </summary>
public static class PsiphonConfigBuilder
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public const string PsiphonBinaryName = "psiphon-core.exe";

    public static string Build(AppSettings settings, ConnectionMode mode)
    {
        var cfg = new JsonObject
        {
            // Local listeners
            ["LocalSocksProxyPort"] = settings.Ports.PsiphonSocksPort,
            ["LocalHttpProxyPort"] = settings.Ports.PsiphonHttpPort,

            // Identity / propagation (public example values; overridable in Settings)
            ["PropagationChannelId"] = settings.Psiphon.PropagationChannelId,
            ["SponsorId"] = settings.Psiphon.SponsorId,

            // Server discovery via the official remote server list
            ["RemoteServerListUrl"] = settings.Psiphon.RemoteServerListUrl,
            ["EmitDiagnosticNotices"] = false,
            // NOTE: UseNoticeFiles is a struct {RotatingFileSize, RotatingSyncFrequency}
            // in tunnel-core — NOT a bool/string. Omitted here (optional field); users
            // who want notice files can add it via the custom config JSON.
        };

        if (!string.IsNullOrWhiteSpace(settings.Psiphon.RemoteServerListSignaturePublicKey))
            cfg["RemoteServerListSignaturePublicKey"] = settings.Psiphon.RemoteServerListSignaturePublicKey;

        if (!string.IsNullOrWhiteSpace(settings.Psiphon.EgressRegion))
            cfg["EgressRegion"] = settings.Psiphon.EgressRegion;

        // Chain mode "after": dial Psiphon's own server connections through the dedicated
        // Xray inbound (VLESS outbound only) — this is what makes psiphon run "over" v2ray.
        if (mode == ConnectionMode.PsiphonAfter)
            cfg["UpstreamProxyURL"] = $"socks5://127.0.0.1:{settings.Ports.VlessOnlySocksPort}";

        MergeCustom(cfg, settings.Psiphon.CustomConfigJson);

        return cfg.ToJsonString(Indented);
    }

    /// <summary>
    /// Applies the custom-config field on top of the generated config. The field
    /// accepts either a JSON object or a psiphon:// share link.
    /// </summary>
    private static void MergeCustom(JsonObject cfg, string custom)
    {
        if (string.IsNullOrWhiteSpace(custom)) return;

        if (PsiphonLinkParser.IsLink(custom))
        {
            if (PsiphonLinkParser.TryParse(custom, out var fields, out _))
                foreach (var kv in fields)
                    cfg[kv.Key] = kv.Value?.DeepClone();
            return;
        }

        try
        {
            if (JsonNode.Parse(custom) is JsonObject json)
                foreach (var kv in json)
                    cfg[kv.Key] = kv.Value?.DeepClone();
        }
        catch
        {
            // invalid user JSON is ignored; validator surfaces a warning elsewhere
        }
    }

    /// <summary>
    /// Returns a warning message if the custom config (JSON or psiphon:// link) is
    /// invalid, else null.
    /// </summary>
    public static string? ValidateCustomConfig(string custom)
    {
        if (string.IsNullOrWhiteSpace(custom)) return null;

        if (PsiphonLinkParser.IsLink(custom))
            return PsiphonLinkParser.TryParse(custom, out _, out var linkError) ? null : linkError;

        try
        {
            return JsonNode.Parse(custom) is JsonObject
                ? null
                : "Custom Psiphon config must be a JSON object or a psiphon:// link.";
        }
        catch (Exception ex)
        {
            return $"Invalid custom config: {ex.Message}";
        }
    }
}
