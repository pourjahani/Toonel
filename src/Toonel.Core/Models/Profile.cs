namespace Toonel.Core.Models;

public enum ProfileProtocol { Vless, Vmess, Trojan, Shadowsocks, Socks, Http }

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public ProfileProtocol Protocol { get; set; } = ProfileProtocol.Vless;
    public string Address { get; set; } = "";
    public int Port { get; set; } = 443;

    // VLESS / VMess
    public string Uuid { get; set; } = "";
    public int AlterId { get; set; }               // VMess only
    public string Encryption { get; set; } = "none"; // VMess: "auto"; VLESS: "none"
    public string Flow { get; set; } = "";          // e.g. xtls-rprx-vision

    // Trojan / Shadowsocks / SOCKS / HTTP
    public string Password { get; set; } = "";
    public string SsMethod { get; set; } = "aes-256-gcm";

    // Transport
    public string Network { get; set; } = "tcp";    // tcp | ws | grpc | h2
    public string WsPath { get; set; } = "/";
    public string WsHost { get; set; } = "";        // Host header for ws/h2
    public string GrpcServiceName { get; set; } = "";

    // Security
    public string Security { get; set; } = "tls";   // none | tls | reality
    public string Sni { get; set; } = "";
    public string Fingerprint { get; set; } = "chrome";
    public string RealityPublicKey { get; set; } = "";
    public string RealityShortId { get; set; } = "";
    public bool AllowInsecure { get; set; }

    public string Summary { get; set; } = "";

    public void RefreshSummary() =>
        Summary = $"{Name}  [{Protocol}]  {Address}:{Port}  {Security}/{Network}";
}
