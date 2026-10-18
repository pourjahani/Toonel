using System.Text.Json.Nodes;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>
/// Parses "psiphon://" share links pasted into the custom-config field into
/// client.config overrides. A link carries the same knobs users would otherwise
/// hand-write as JSON:
///
///   psiphon://?region=DE&amp;protocol=QUIC-OSSH&amp;sponsor=FFFFFFFFFFFFFFFF
///             &amp;channel=FFFFFFFFFFFFFFFF&amp;upstream=socks5://127.0.0.1:10809
///             &amp;tls=1&amp;timeout=30&amp;serverlist=&lt;url&gt;&amp;pubkey=&lt;base64&gt;
///
/// The host part of the link (psiphon://tunnel?... ) is ignored, unknown
/// parameters are ignored, and repeated parameters keep the last value.
/// </summary>
public static class PsiphonLinkParser
{
    public const string Scheme = "psiphon://";

    public static bool IsLink(string? text) =>
        text is not null && text.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a link into a JSON object of client.config fields.
    /// Returns false with a human-readable <paramref name="error"/> when the link
    /// is malformed or carries a value psiphon cannot use.
    /// </summary>
    public static bool TryParse(string? link, out JsonObject fields, out string? error)
    {
        fields = new JsonObject();
        error = null;

        if (!IsLink(link))
        {
            error = "Not a psiphon:// link.";
            return false;
        }

        var text = link!.Trim();
        var queryStart = text.IndexOf('?');
        if (queryStart < 0)
        {
            error = "psiphon:// link has no ?query parameters.";
            return false;
        }

        var query = text[(queryStart + 1)..];
        var fragment = query.IndexOf('#');
        if (fragment >= 0) query = query[..fragment];

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Unescape(eq < 0 ? pair : pair[..eq]).Trim().ToLowerInvariant();
            var value = eq < 0 ? "" : Unescape(pair[(eq + 1)..]).Trim();
            if (key.Length == 0) continue;

            switch (key)
            {
                case "region" or "egress" or "egressregion" or "country":
                    if (IsAutomatic(value)) break;
                    var code = PsiphonCountries.Normalize(value);
                    if (code.Length == 0)
                    {
                        error = $"Unrecognized region '{value}'.";
                        return false;
                    }
                    fields["EgressRegion"] = code;
                    break;

                case "protocol" or "protocols" or "limittunnelprotocols":
                    var protocols = value
                        .Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim().ToUpperInvariant())
                        .Where(p => p.Length > 0 && !IsAutomatic(p))
                        .ToArray();
                    if (protocols.Length > 0)
                    {
                        var arr = new JsonArray();
                        foreach (var p in protocols) arr.Add(p);
                        fields["LimitTunnelProtocols"] = arr;
                    }
                    break;

                case "sponsor" or "sponsorid":
                    if (value.Length > 0) fields["SponsorId"] = value.ToUpperInvariant();
                    break;

                case "channel" or "propagationchannel" or "propagationchannelid":
                    if (value.Length > 0) fields["PropagationChannelId"] = value.ToUpperInvariant();
                    break;

                case "upstream" or "proxy" or "upstreamproxy" or "upstreamproxyurl":
                    if (value.Length == 0) break;
                    if (!HasSupportedProxyScheme(value))
                    {
                        error = $"Unsupported upstream proxy '{value}' (use http://, https:// or socks5://).";
                        return false;
                    }
                    fields["UpstreamProxyURL"] = value;
                    break;

                case "tls" or "useindistinguishabletls":
                    if (!TryParseBool(value, out var tls))
                    {
                        error = $"'{value}' is not a valid on/off value for tls.";
                        return false;
                    }
                    fields["UseIndistinguishableTLS"] = tls;
                    break;

                case "timeout" or "establishtunneltimeout" or "establishtunneltimeoutseconds":
                    if (IsDisabled(value)) { fields["EstablishTunnelTimeoutSeconds"] = 0; break; }
                    if (!int.TryParse(value, out var seconds) || seconds < 0)
                    {
                        error = $"'{value}' is not a valid tunnel timeout in seconds.";
                        return false;
                    }
                    fields["EstablishTunnelTimeoutSeconds"] = seconds;
                    break;

                case "serverlist" or "rsl" or "remoteserverlisturl":
                    if (value.Length > 0) fields["RemoteServerListUrl"] = value;
                    break;

                case "pubkey" or "signaturekey" or "remoteserverlistsignaturepublickey":
                    if (value.Length == 0) break;
                    if (!IsBase64(value))
                    {
                        error = "Signature public key must be base64.";
                        return false;
                    }
                    fields["RemoteServerListSignaturePublicKey"] = value;
                    break;

                default:
                    // Unknown parameters are ignored so links from other clients
                    // (extra ports, LAN flags, ...) still import what we understand.
                    break;
            }
        }

        return true;
    }

    private static bool IsAutomatic(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        return v is "" or "auto" or "any" or "fastest" or "default" or "none";
    }

    private static bool IsDisabled(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        return v is "0" or "off" or "none" or "disabled" or "false";
    }

    private static bool TryParseBool(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "1" or "true" or "yes" or "on" or "enable" or "enabled": result = true; return true;
            case "0" or "false" or "no" or "off" or "disable" or "disabled": result = false; return true;
            default: result = false; return false;
        }
    }

    private static bool HasSupportedProxyScheme(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase);

    private static bool IsBase64(string value)
    {
        // Stack-allocate only for small values; a long pasted key must not stack-overflow
        // the parser (share links on one line can be arbitrarily long).
        if (value.Length <= 256)
        {
            Span<byte> buffer = stackalloc byte[value.Length];
            return Convert.TryFromBase64String(value, buffer, out var written) && written >= 32;
        }
        try
        {
            return Convert.FromBase64String(value) is { Length: >= 32 };
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Unescape(string value)
    {
        try { return Uri.UnescapeDataString(value); }
        catch { return value; }
    }
}
