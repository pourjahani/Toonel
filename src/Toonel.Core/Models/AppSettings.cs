namespace Toonel.Core.Models;

using System.Collections.ObjectModel;

public enum ConnectionMode
{
    /// <summary>Traffic goes straight into the selected VLESS/VMess/Trojan profile.</summary>
    V2RayOnly,
    /// <summary>No V2Ray profile: Xray front-router sends everything to Psiphon's local SOCKS.</summary>
    PsiphonOnly,
    /// <summary>Psiphon over V2Ray: psiphon dials its servers through the dedicated Xray inbound (UpstreamProxyURL).</summary>
    PsiphonAfter,
    /// <summary>V2Ray over Psiphon: the Xray proxy outbound dials through Psiphon's local SOCKS (dialerProxy).</summary>
    PsiphonBefore,
    /// <summary>V2Ray over WARP: the Xray proxy outbound dials through the WireGuard/WARP hop (dialerProxy).</summary>
    WarpBefore,
}

public sealed class PortSettings
{
    public int MainSocksPort { get; set; } = 10808;      // apps -> xray (mode-aware router)
    public int MainHttpPort { get; set; } = 10810;       // apps -> xray (http variant)
    public int VlessOnlySocksPort { get; set; } = 10809; // dedicated inbound -> proxy outbound ONLY (anti-loop)
    public int PsiphonSocksPort { get; set; } = 10811;   // psiphon local socks
    public int PsiphonHttpPort { get; set; } = 10812;    // psiphon local http
}

/// <summary>
/// Values below are the public examples used by psiphon-tunnel-core's README and community
/// standalone clients. They can be overridden in Settings; power users can also paste a full
/// custom client.config JSON (merged on top).
/// </summary>
public static class PsiphonDefaults
{
    // Values verified against the community-maintained "free network" client configs
    // (psiphon-linux-cli-conduit, PsiphonLinux): this signing public key authenticates
    // Psiphon's official remote server list at the URL below. Note the key is
    // bucket-specific — it must stay paired with this RemoteServerListUrl.
    public const string SponsorId = "FFFFFFFFFFFFFFFF";
    public const string PropagationChannelId = "FFFFFFFFFFFFFFFF";
    public const string RemoteServerListUrl = "https://s3.amazonaws.com/psiphon/web/mjr4-p23r-puwl/server_list_compressed";
    /// <summary>Base64 signing public key for the remote server list (verify server lists out-of-band).</summary>
    public const string RemoteServerListSignaturePublicKey =
        "MIICIDANBgkqhkiG9w0BAQEFAAOCAg0AMIICCAKCAgEAt7Ls+/39r+T6zNW7GiVpJfzq/xvL9SBH5rIFnk0RXYEYavax3WS6HOD35eTAqn8AniOwiH+DOkvgSKF2caqk/y1dfq47Pdymtwzp9ikpB1C5OfAysXzBiwVJlCdajBKvBZDerV1cMvRzCKvKwRmvDmHgphQQ7WfXIGbRbmmk6opMBh3roE42KcotLFtqp0RRwLtcBRNtCdsrVsjiI1Lqz/lH+T61sGjSjQ3CHMuZYSQJZo/KrvzgQXpkaCTdbObxHqb6/+i1qaVOfEsvjoiyzTxJADvSytVtcTjijhPEV6XskJVHE1Zgl+7rATr/pDQkw6DPCNBS1+Y6fy7GstZALQXwEDN/qhQI9kWkHijT8ns+i1vGg00Mk/6J75arLhqcodWsdeG/M/moWgqQAnlZAGVtJI1OgeF5fsPpXu4kctOfuZlGjVZXQNW34aOzm8r8S0eVZitPlbhcPiR4gT/aSMz/wd8lZlzZYsje/Jr8u/YtlwjjreZrGRmG8KMOzukV3lLmMppXFMvl4bxv6YFEmIuTsOhbLTwFgh7KYNjodLj/LsqRVfwz31PgWQFTEPICV7GCvgVlPRxnofqKSjgTWI4mxDhBpVcATvaoBl1L/6WLbFvBsoAUBItWwctO2xalKxF5szhGm8lccoc5MZr8kfE0uxMgsxz4er68iCID+rsCAQM=";
}

public sealed class PsiphonSettings
{
    public string SponsorId { get; set; } = PsiphonDefaults.SponsorId;
    public string PropagationChannelId { get; set; } = PsiphonDefaults.PropagationChannelId;
    public string RemoteServerListUrl { get; set; } = PsiphonDefaults.RemoteServerListUrl;
    public string RemoteServerListSignaturePublicKey { get; set; } = PsiphonDefaults.RemoteServerListSignaturePublicKey;
    /// <summary>"" = any region. ISO country code otherwise.</summary>
    public string EgressRegion { get; set; } = "";
    /// <summary>Free-form JSON merged last into client.config (power-user overrides).</summary>
    public string CustomConfigJson { get; set; } = "";
}

/// <summary>
/// Cloudflare WARP hop credentials, produced by wgcf (register + generate) or any WARP
/// WireGuard account. Peer key/endpoint are the fixed Cloudflare WARP values; only the
/// account-issued private key, interface address and reserved bytes differ per user.
/// </summary>
public static class WarpDefaults
{
    public const string PeerPublicKey = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo=";
    public const string EndpointHost = "engage.cloudflareclient.com";
    public const int EndpointPort = 2408;
    public const int Mtu = 1280;
}

public sealed class WarpSettings
{
    public string PrivateKey { get; set; } = "";   // base64 private key from the WARP account
    public string Address { get; set; } = "";      // e.g. 172.16.0.2/32
    public string Reserved { get; set; } = "";     // 3 bytes (base64 or "1,2,3") from the account
}

/// <summary>A configured subscription URL with the traffic/expire info the server reports.</summary>
public sealed class SubscriptionInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public long UploadBytes { get; set; } = -1;
    public long DownloadBytes { get; set; } = -1;
    public long TotalBytes { get; set; } = -1;
    public long ExpiresUnix { get; set; } = -1;
    public string LastUpdated { get; set; } = "";
    public int ProfileCount { get; set; }
}

public sealed class AppSettings
{
    public PortSettings Ports { get; set; } = new();
    public PsiphonSettings Psiphon { get; set; } = new();
    public SplitTunnelSettings SplitTunnel { get; set; } = new();

    /// <summary>Whole-system traffic through the sing-box TUN adapter (covers terminals; needs admin).</summary>
    public bool TunEnabled { get; set; } = true;
    /// <summary>Legacy WinINET system proxy (no admin needed; some apps bypass it).</summary>
    public bool SystemProxyEnabled { get; set; } = false;
    /// <summary>Block UDP 443 (QUIC) in psiphon modes so browsers fall back to TCP via the proxy.</summary>
    public bool BlockQuic { get; set; } = true;

    public ConnectionMode Mode { get; set; } = ConnectionMode.V2RayOnly;
    public string SelectedProfileId { get; set; } = "";
    public bool AutoConnectOnStart { get; set; }
    /// <summary>When off, closing the window quits the app and fully resets proxy/TUN/engines.</summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>UI language: "en" or "fa" (Persian, RTL).</summary>
    public string Language { get; set; } = "en";

    /// <summary>UI theme: "light" or "dark".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Show the one-line compact status bar instead of the full window at startup.</summary>
    public bool CompactOnStart { get; set; }

    public ObservableCollection<Profile> Profiles { get; set; } = new();
    public List<SubscriptionInfo> Subscriptions { get; set; } = new();
    public WarpSettings Warp { get; set; } = new();

    /// <summary>DNS resolver: "auto" (system/local), plain UDP ("1.1.1.1","8.8.8.8","9.9.9.9")
    /// or DoH ("doh:1.1.1.1","doh:8.8.8.8","doh:9.9.9.9").</summary>
    public string DnsChoice { get; set; } = "auto";

    /// <summary>Auto-refresh subscriptions on app start and every N hours (0 = off).</summary>
    public int SubscriptionUpdateHours { get; set; } = 12;

    public Profile? SelectedProfile =>
        Profiles.FirstOrDefault(p => p.Id == SelectedProfileId) ?? Profiles.FirstOrDefault();
}
