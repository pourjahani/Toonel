using System.Diagnostics;
using System.Text.Json.Nodes;
using Toonel.Core;
using Toonel.Core.Models;

namespace Toonel.Tests;

/// <summary>
/// Self-contained check runner (no test framework dependency):
/// dotnet run --project tests/Toonel.Tests
/// </summary>
public static class Program
{
    private static int _pass, _fail;

    public static int Main()
    {
        Console.WriteLine("Toonel config-generation tests");
        Console.WriteLine("---------------------------------");

        VlessProfile_RoutesToProxyOutbound_InV2RayOnly();
        PsiphonOnly_RoutesMainInboundsToPsiphon();
        Run("PsiphonAfter_SetsUpstreamProxyUrl_AndDedicatedInbound", PsiphonAfter_SetsUpstreamProxyUrl_AndDedicatedInbound);
        PsiphonBefore_UsesDialerProxy();
        SingBox_IncludeMode_TunnelsOnlyListedApps();
        SingBox_ExcludeMode_BypassesListedApps();
        SingBox_AntiLoop_EngineBinariesAlwaysDirect();
        SingBox_BlocksQuic_InPsiphonModes();
        SingBox_Exceptions_AlwaysDirectAndAlwaysProxy();
        PsiphonConfig_MergesCustomJson();
        PsiphonLink_ImportsFields();
        PsiphonLink_RejectsBadValues();
        EngineManager_CancelDuringConnect_LandsOnStopped();
        Parser_VlessUrl();
        Parser_VmessBase64();
        Parser_TrojanAndSs();
        Parser_SubscriptionBase64Blob();
        PsiphonCountries_Normalize();
        PsiphonCountries_Emoji();
        ShareLink_VlessRoundTrip();
        ShareLink_VmessAndSs();

        Console.WriteLine($"\n{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    // ---------- helpers ----------

    private static AppSettings Settings() => new()
    {
        Ports = new PortSettings { MainSocksPort = 10808, MainHttpPort = 10810, VlessOnlySocksPort = 10809, PsiphonSocksPort = 10811, PsiphonHttpPort = 10812 },
        SplitTunnel = new SplitTunnelSettings { Mode = SplitTunnelMode.Off },
        Profiles =
        {
            new Profile
            {
                Name = "test", Protocol = ProfileProtocol.Vless, Address = "example.com", Port = 443,
                Uuid = "b831381d-6324-4d53-ad4f-8cda48b30811", Security = "tls", Network = "tcp",
            },
        },
        SelectedProfileId = "",
    };

    private static JsonObject Xray(AppSettings s, ConnectionMode m) =>
        JsonNode.Parse(XrayConfigBuilder.Build(s, m))!.AsObject();

    private static JsonObject SingBox(AppSettings s, ConnectionMode m) =>
        JsonNode.Parse(SingBoxConfigBuilder.Build(s, m))!.AsObject();

    private static JsonArray Rules(JsonObject cfg) => cfg["route"]?["rules"]?.AsArray() ?? cfg["routing"]?["rules"]?.AsArray()!;

    private static void Check(bool cond, string name)
    {
        if (cond) { _pass++; Console.WriteLine($"  ok  {name}"); }
        else { _fail++; Console.WriteLine($"FAIL  {name}"); }
    }

    private static void Run(string name, Action test)
    {
        try { test(); }
        catch (Exception ex)
        {
            _fail++;
            Console.WriteLine($"FAIL  {name} (exception: {ex.GetType().Name}: {ex.Message})");
        }
    }

    // ---------- xray ----------

    private static void VlessProfile_RoutesToProxyOutbound_InV2RayOnly()
    {
        var s = Settings();
        s.SelectedProfileId = s.Profiles[0].Id;
        var cfg = Xray(s, ConnectionMode.V2RayOnly);
        var outProxy = cfg["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == "proxy-out");
        Check(outProxy["protocol"]!.GetValue<string>() == "vless", "v2ray-only: outbound protocol vless");
        Check(outProxy["settings"]!["vnext"]![0]!["address"]!.GetValue<string>() == "example.com", "v2ray-only: server address");
        Check(outProxy["streamSettings"]!["tlsSettings"]!["serverName"]!.GetValue<string>() == "example.com", "v2ray-only: tls sni default");
        var main = Rules(cfg).First(r => r!["inboundTag"]!.AsArray().Any(t => t!.GetValue<string>() == "in-main-socks"));
        Check(main["outboundTag"]!.GetValue<string>() == "proxy-out", "v2ray-only: main socks -> proxy");
    }

    private static void PsiphonOnly_RoutesMainInboundsToPsiphon()
    {
        var cfg = Xray(Settings(), ConnectionMode.PsiphonOnly);
        var outPsiphon = cfg["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == "psiphon-out");
        Check(outPsiphon["settings"]!["servers"]![0]!["port"]!.GetValue<int>() == 10811, "psiphon-only: socks outbound targets psiphon port");
        var main = Rules(cfg).First(r => r!["inboundTag"]!.AsArray().Any(t => t!.GetValue<string>() == "in-main-socks"));
        Check(main["outboundTag"]!.GetValue<string>() == "psiphon-out", "psiphon-only: main socks -> psiphon");
    }

    private static void PsiphonAfter_SetsUpstreamProxyUrl_AndDedicatedInbound()
    {
        var s = Settings();
        var xray = Xray(s, ConnectionMode.PsiphonAfter);
        var vlessIn = Rules(xray).First(r => r!["inboundTag"]!.AsArray().Any(t => t!.GetValue<string>() == "in-vless-only"));
        Check(vlessIn["outboundTag"]!.GetValue<string>() == "proxy-out", "psiphon-after: dedicated inbound -> proxy only");

        var psiphonCfg = JsonNode.Parse(PsiphonConfigBuilder.Build(s, ConnectionMode.PsiphonAfter))!.AsObject();
        Check(psiphonCfg["UpstreamProxyURL"]?.GetValue<string>() == "socks5://127.0.0.1:10809", "psiphon-after: UpstreamProxyURL points at vless-only inbound");
        Check(psiphonCfg["LocalSocksProxyPort"]!.GetValue<int>() == 10811, "psiphon-after: local socks port");

        var psiphonAlone = JsonNode.Parse(PsiphonConfigBuilder.Build(s, ConnectionMode.PsiphonOnly))!.AsObject();
        Check(!psiphonAlone.ContainsKey("UpstreamProxyUrl"), "psiphon-alone: no upstream proxy set");
    }

    private static void PsiphonBefore_UsesDialerProxy()
    {
        var s = Settings();
        s.SelectedProfileId = s.Profiles[0].Id;
        var cfg = Xray(s, ConnectionMode.PsiphonBefore);
        var outProxy = cfg["outbounds"]!.AsArray().First(o => o!["tag"]!.GetValue<string>() == "proxy-out");
        Check(outProxy["streamSettings"]!["sockopt"]!["dialerProxy"]!.GetValue<string>() == "psiphon-out",
            "psiphon-before: proxy dials through psiphon (dialerProxy)");
    }

    // ---------- sing-box ----------

    private static JsonArray SingBoxRules(JsonObject cfg) => cfg["route"]!["rules"]!.AsArray();

    private static void SingBox_IncludeMode_TunnelsOnlyListedApps()
    {
        var s = Settings();
        s.SplitTunnel.Mode = SplitTunnelMode.Include;
        s.SplitTunnel.Processes.Add("chrome.exe");
        var cfg = SingBox(s, ConnectionMode.V2RayOnly);
        var procRule = SingBoxRules(cfg).First(r => r!["process_name"] != null && r["process_name"]!.AsArray().Any(p => p!.GetValue<string>() == "chrome.exe"));
        Check(procRule["outbound"]!.GetValue<string>() == "xray", "include: listed app -> xray");
        Check(cfg["route"]!["final"]!.GetValue<string>() == "direct", "include: catch-all -> direct");
    }

    private static void SingBox_ExcludeMode_BypassesListedApps()
    {
        var s = Settings();
        s.SplitTunnel.Mode = SplitTunnelMode.Exclude;
        s.SplitTunnel.Processes.Add("bankapp.exe");
        var cfg = SingBox(s, ConnectionMode.V2RayOnly);
        var procRule = SingBoxRules(cfg).First(r => r!["process_name"] != null && r["process_name"]!.AsArray().Any(p => p!.GetValue<string>() == "bankapp.exe"));
        Check(procRule["outbound"]!.GetValue<string>() == "direct", "exclude: listed app -> direct");
        Check(cfg["route"]!["final"]!.GetValue<string>() == "xray", "exclude: catch-all -> xray");
    }

    private static void SingBox_AntiLoop_EngineBinariesAlwaysDirect()
    {
        var cfg = SingBox(Settings(), ConnectionMode.PsiphonAfter);
        var antiLoop = SingBoxRules(cfg).First(r => r!["process_name"] != null);
        var names = antiLoop["process_name"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();
        Check(names.Contains("psiphon-core.exe") && names.Contains("xray.exe") && names.Contains("sing-box.exe"),
            "anti-loop: engines bypass TUN");
        Check(antiLoop["outbound"]!.GetValue<string>() == "direct", "anti-loop: engines -> direct");
    }

    private static void SingBox_BlocksQuic_InPsiphonModes()
    {
        var psiphon = SingBox(Settings(), ConnectionMode.PsiphonOnly);
        var quic = SingBoxRules(psiphon).FirstOrDefault(r => r!["action"]?.GetValue<string>() == "reject");
        Check(quic is not null && quic["port"]!.GetValue<int>() == 443, "psiphon modes: QUIC udp/443 rejected");

        var v2ray = SingBox(Settings(), ConnectionMode.V2RayOnly);
        Check(!SingBoxRules(v2ray).Any(r => r!["action"]?.GetValue<string>() == "reject"), "v2ray mode: QUIC allowed");
    }

    private static void SingBox_Exceptions_AlwaysDirectAndAlwaysProxy()
    {
        var s = Settings();
        s.SplitTunnel.DirectDomains.Add("bank.ir");
        s.SplitTunnel.DirectIps.Add("10.0.0.0/8");
        s.SplitTunnel.DirectPorts.Add("80");
        s.SplitTunnel.ProxyDomains.Add("openai.com");
        s.SplitTunnel.ProxyPorts.Add("8443");
        var cfg = SingBox(s, ConnectionMode.V2RayOnly);
        var rules = SingBoxRules(cfg);
        Check(rules.Any(r => r!["domain_suffix"]?.AsArray().Any(x => x!.GetValue<string>() == "bank.ir") == true
                             && r["outbound"]!.GetValue<string>() == "direct"), "exceptions: direct domain rule");
        Check(rules.Any(r => r!["ip_cidr"]?.AsArray().Any(x => x!.GetValue<string>() == "10.0.0.0/8") == true
                             && r["outbound"]!.GetValue<string>() == "direct"), "exceptions: direct CIDR rule");
        Check(rules.Any(r => r!["domain_suffix"]?.AsArray().Any(x => x!.GetValue<string>() == "openai.com") == true
                             && r["outbound"]!.GetValue<string>() == "xray"), "exceptions: proxy domain rule");
        Check(rules.Any(r => r!["port"]?.AsArray().Any(x => x!.GetValue<string>() == "8443") == true
                             && r["outbound"]!.GetValue<string>() == "xray"), "exceptions: proxy port rule");
    }

    // ---------- psiphon config ----------

    private static void PsiphonConfig_MergesCustomJson()
    {
        var s = Settings();
        s.Psiphon.CustomConfigJson = """{"LimitTunnelProtocols":["FRONTED-MEEK-OSSH"],"EgressRegion":"DE"}""";
        var cfg = JsonNode.Parse(PsiphonConfigBuilder.Build(s, ConnectionMode.PsiphonOnly))!.AsObject();
        Check(cfg["LimitTunnelProtocols"]!.AsArray().Count == 1, "custom json: LimitTunnelProtocols merged");
        Check(cfg["EgressRegion"]!.GetValue<string>() == "DE", "custom json: EgressRegion merged");
    }

    private static void PsiphonLink_ImportsFields()
    {
        var s = Settings();
        s.Psiphon.CustomConfigJson =
            "psiphon://?region=de&protocol=auto,QUIC-OSSH&sponsor=ffffffffffffffff&channel=FFFFFFFFFFFFFFFF" +
            "&upstream=socks5%3A%2F%2F127.0.0.1%3A10809&tls=1&timeout=30&fragment=%23ignored";

        Check(PsiphonLinkParser.IsLink(s.Psiphon.CustomConfigJson), "psiphon link: scheme detected");

        var cfg = JsonNode.Parse(PsiphonConfigBuilder.Build(s, ConnectionMode.PsiphonOnly))!.AsObject();
        Check(cfg["EgressRegion"]!.GetValue<string>() == "DE", "psiphon link: region normalized -> DE");
        Check(cfg["LimitTunnelProtocols"]!.AsArray().Count == 1
              && cfg["LimitTunnelProtocols"]![0]!.GetValue<string>() == "QUIC-OSSH",
              "psiphon link: protocol list merged, auto skipped");
        Check(cfg["SponsorId"]!.GetValue<string>() == "FFFFFFFFFFFFFFFF", "psiphon link: sponsor id");
        Check(cfg["PropagationChannelId"]!.GetValue<string>() == "FFFFFFFFFFFFFFFF", "psiphon link: channel id");
        Check(cfg["UpstreamProxyURL"]!.GetValue<string>() == "socks5://127.0.0.1:10809", "psiphon link: upstream decoded");
        Check(cfg["UseIndistinguishableTLS"]!.GetValue<bool>(), "psiphon link: tls=1");
        Check(cfg["EstablishTunnelTimeoutSeconds"]!.GetValue<int>() == 30, "psiphon link: timeout");

        // JSON stays authoritative: a link override applies on top of the generated config.
        var alone = JsonNode.Parse(PsiphonConfigBuilder.Build(Settings(), ConnectionMode.PsiphonOnly))!.AsObject();
        Check(!alone.ContainsKey("EgressRegion"), "psiphon link: no residue without link");

        // Named region and unknown params are tolerated.
        var ok = PsiphonLinkParser.TryParse("psiphon://tunnel?region=United%20States&lan=1&weird=", out var fields, out var err);
        Check(ok && err == null, "psiphon link: named region + unknown params accepted");
        Check(fields["EgressRegion"]!.GetValue<string>() == "US", "psiphon link: country name -> US");
        Check(PsiphonLinkParser.TryParse("psiphon://?region=auto", out _, out _)!, "psiphon link: region=auto is valid no-op");
        Check(PsiphonConfigBuilder.ValidateCustomConfig != null, "psiphon link: validator exists");
    }

    private static void PsiphonLink_RejectsBadValues()
    {
        Check(PsiphonLinkParser.TryParse("psiphon://", out _, out var err) == false
              && err!.Contains("no ?query"), "psiphon link: bare link rejected");
        Check(PsiphonLinkParser.TryParse("psiphon://?region=Not%20a%20country", out _, out err) == false
              && err!.Contains("region"), "psiphon link: bad region rejected");
        Check(PsiphonLinkParser.TryParse("psiphon://?upstream=ftp://x", out _, out err) == false
              && err!.Contains("upstream"), "psiphon link: bad upstream scheme rejected");
        Check(PsiphonLinkParser.TryParse("psiphon://?timeout=-5", out _, out err) == false
              && err!.Contains("timeout"), "psiphon link: negative timeout rejected");
        Check(PsiphonLinkParser.TryParse("psiphon://?pubkey=not-base64!!", out _, out err) == false
              && err!.Contains("base64"), "psiphon link: non-base64 pubkey rejected");

        // Invalid link in the custom field must not corrupt the generated config.
        var s = Settings();
        s.Psiphon.CustomConfigJson = "psiphon://?region=Not a country";
        var cfg = JsonNode.Parse(PsiphonConfigBuilder.Build(s, ConnectionMode.PsiphonOnly))!.AsObject();
        Check(!cfg.ContainsKey("EgressRegion"), "psiphon link: invalid link leaves config untouched");
        Check(PsiphonConfigBuilder.ValidateCustomConfig(s.Psiphon.CustomConfigJson) != null,
              "psiphon link: validator flags invalid link");
        Check(PsiphonConfigBuilder.ValidateCustomConfig("psiphon://?region=DE") == null,
              "psiphon link: validator accepts good link");
        Check(PsiphonConfigBuilder.ValidateCustomConfig("{\"EgressRegion\":\"DE\"}") == null,
              "psiphon link: validator still accepts JSON");
    }

    // ---------- parsers ----------

    private static void Parser_VlessUrl()
    {
        var ok = ProfileUrlParser.TryParse(
            "vless://b831381d-6324-4d53-ad4f-8cda48b30811@example.com:443?encryption=none&security=tls&sni=cdn.example.com&type=ws&host=cdn.example.com&path=%2Fws#MyNode",
            out var p);
        Check(ok, "vless url parsed");
        Check(p.Protocol == ProfileProtocol.Vless && p.Address == "example.com" && p.Port == 443, "vless url fields");
        Check(p.Sni == "cdn.example.com" && p.Network == "ws" && p.WsPath == "/ws", "vless url transport fields");
    }

    private static void Parser_VmessBase64()
    {
        var json = """{"ps":"test node","add":"vm.example.com","port":"8443","id":"uuid-123","aid":"0","net":"grpc","tls":"tls","sni":"sni.example.com","path":"","host":"","type":"","fp":"chrome"}""";
        var ok = ProfileUrlParser.TryParse("vmess://" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)), out var p);
        Check(ok, "vmess base64 parsed");
        Check(p.Protocol == ProfileProtocol.Vmess && p.Address == "vm.example.com" && p.Port == 8443, "vmess fields");
        Check(p.Network == "grpc" && p.Security == "tls", "vmess transport/tls");
    }

    private static void Parser_TrojanAndSs()
    {
        var trojanOk = ProfileUrlParser.TryParse("trojan://pass123@t.example.com:443?sni=t.example.com#TNode", out var t);
        Check(trojanOk && t.Protocol == ProfileProtocol.Trojan && t.Password == "pass123" && t.Security == "tls", "trojan url parsed");

        var ssBody = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aes-256-gcm:secret@ss.example.com:8388"));
        var ssOk = ProfileUrlParser.TryParse($"ss://{ssBody}#SSNode", out var ss);
        Check(ssOk && ss.Protocol == ProfileProtocol.Shadowsocks && ss.Address == "ss.example.com" && ss.Port == 8388, "ss url parsed");
        Check(ss.SsMethod == "aes-256-gcm" && ss.Password == "secret", "ss method/password");
    }

    private static void ShareLink_VlessRoundTrip()
    {
        var p = new Profile
        {
            Name = "RT Node", Protocol = ProfileProtocol.Vless, Address = "rt.example.com", Port = 443,
            Uuid = "uuid-abc", Security = "reality", Sni = "sni.example.com",
            Flow = "xtls-rprx-vision", Fingerprint = "chrome",
            RealityPublicKey = "pbk-key", RealityShortId = "sid-1", Network = "tcp",
        };
        var link = ProfileShareLink.Build(p);
        var ok = ProfileUrlParser.TryParse(link, out var back);
        Check(ok, "share link: vless parses back");
        Check(back.Address == p.Address && back.Port == p.Port && back.Uuid == p.Uuid, "share link: vless fields");
        Check(back.Security == "reality" && back.Sni == p.Sni && back.Flow == p.Flow, "share link: vless reality/sni/flow");
        Check(back.RealityPublicKey == "pbk-key" && back.RealityShortId == "sid-1", "share link: vless reality keys");
        Check(back.Name == p.Name, "share link: vless name round-trip");
    }

    private static void ShareLink_VmessAndSs()
    {
        var v = new Profile
        {
            Name = "V Node", Protocol = ProfileProtocol.Vmess, Address = "v.example.com", Port = 8443,
            Uuid = "uuid-v", Network = "ws", WsPath = "/ws", WsHost = "h.example.com", Security = "tls",
        };
        var vlink = ProfileShareLink.Build(v);
        var vok = ProfileUrlParser.TryParse(vlink, out var vback);
        Check(vok && vback.Protocol == ProfileProtocol.Vmess, "share link: vmess parses back");
        Check(vback.Address == v.Address && vback.Port == v.Port && vback.Uuid == v.Uuid, "share link: vmess fields");
        Check(vback.Network == "ws" && vback.WsHost == v.WsHost, "share link: vmess ws fields");

        var s = new Profile
        {
            Name = "S Node", Protocol = ProfileProtocol.Shadowsocks, Address = "s.example.com", Port = 8388,
            SsMethod = "aes-256-gcm", Password = "secret",
        };
        var slink = ProfileShareLink.Build(s);
        var sok = ProfileUrlParser.TryParse(slink, out var sback);
        Check(sok && sback.Protocol == ProfileProtocol.Shadowsocks, "share link: ss parses back");
        Check(sback.Address == s.Address && sback.Port == s.Port && sback.SsMethod == s.SsMethod
              && sback.Password == s.Password, "share link: ss fields");

        var multi = ProfileShareLink.BuildMany(new[] { v, s });
        Check(multi.Split('\n').Length == 2, "share link: many -> one per line");
    }

    private static void PsiphonCountries_Normalize()
    {
        Check(PsiphonCountries.Normalize("") == "", "egress: empty -> automatic");
        Check(PsiphonCountries.Normalize("  ") == "", "egress: whitespace -> automatic");
        Check(PsiphonCountries.Normalize("us") == "US", "egress: lowercase code -> US");
        Check(PsiphonCountries.Normalize("United States") == "US", "egress: name -> US");
        Check(PsiphonCountries.Normalize("UK") == "GB", "egress: UK alias -> GB");
        Check(PsiphonCountries.Normalize("UAE") == "AE", "egress: UAE alias -> AE");
        Check(PsiphonCountries.Normalize("de") == "DE", "egress: passthrough ISO code");
        Check(PsiphonCountries.Normalize("Not a country") == "", "egress: garbage -> automatic");
        Check(PsiphonCountries.List.Count > 25, "egress: country catalog populated");
        Check(PsiphonCountries.List[0].Code == "", "egress: first entry is fastest/automatic");
    }

    private static void PsiphonCountries_Emoji()
    {
        Check(PsiphonCountries.Emoji("") == "🌐", "flag emoji: automatic -> globe");
        Check(PsiphonCountries.Emoji("DE") == "\U0001F1E9\U0001F1EA", "flag emoji: DE -> regional indicators");
        Check(PsiphonCountries.Emoji("us") == "\U0001F1FA\U0001F1F8", "flag emoji: lowercase us -> US flags");
        Check(PsiphonCountries.Emoji("XX") == "\U0001F1FD\U0001F1FD", "flag emoji: unknown code still renders");
        Check(PsiphonCountries.Emoji("D") == "", "flag emoji: single letter -> empty");
        Check(PsiphonCountries.Emoji("1A") == "", "flag emoji: non-letter -> empty");
        Check(PsiphonCountries.ListLabel("DE") == "\U0001F1E9\U0001F1EA  Germany", "list label: emoji + name");
        Check(PsiphonCountries.ListLabel("") == "🌐  Fastest (automatic)", "list label: globe + automatic");
        // Dropdown-label text (with emoji prefix) must still normalize back to the code.
        Check(PsiphonCountries.Normalize(PsiphonCountries.ListLabel("DE")) == "DE", "normalize: emoji-prefixed label -> DE");
        Check(PsiphonCountries.Normalize(PsiphonCountries.ListLabel("")) == "", "normalize: automatic label -> empty");
    }

    // ---------- engine cancel ----------

    /// <summary>
    /// Disconnect pressed while a connect attempt is still starting: the attempt must
    /// unwind with OperationCanceledException, leave the state on Stopped (not Error)
    /// and tear down any engine process that was already launched.
    /// </summary>
    private static void EngineManager_CancelDuringConnect_LandsOnStopped()
    {
        var root = Path.Combine(Path.GetTempPath(), "toonel-cancel-test-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "runtime");
        var cores = Path.Combine(root, "cores");
        Directory.CreateDirectory(cores);

        // Stand-in "psiphon-core.exe": ping.exe ignores our args, but it is a real
        // child process that the cancel path must kill mid-connect.
        var fake = Path.Combine(cores, "psiphon-core.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), fake, true);

        var manager = new EngineManager(new LogBuffer(), runtime, cores);
        var s = new AppSettings { TunEnabled = false, SystemProxyEnabled = false };

        using var cts = new CancellationTokenSource();
        var startTask = manager.StartAsync(s, ConnectionMode.PsiphonOnly, cts.Token);

        Thread.Sleep(150); // mid-connect: inside StartAsync's 700 ms engine-alive delay
        cts.Cancel();

        var cancelled = false;
        try { startTask.Wait(); }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { cancelled = true; }

        Check(cancelled, "cancel: StartAsync throws OperationCanceledException");
        Check(manager.State == EngineState.Stopped, "cancel: state lands on Stopped (not Error)");
        Check(!manager.PsiphonRunning, "cancel: engine runner torn down");

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && Process.GetProcessesByName("psiphon-core").Length > 0)
            Thread.Sleep(100);
        Check(Process.GetProcessesByName("psiphon-core").Length == 0, "cancel: no psiphon-core process left behind");

        try { Directory.Delete(root, true); } catch { }
    }

    private static void Parser_SubscriptionBase64Blob()
    {
        var lines = string.Join("\n",
            "trojan://pass@t1.example.com:443#A",
            "vless://uuid@example.com:443?security=tls#B");
        var blob = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(lines));
        var profiles = ProfileUrlParser.ParseMany(blob);
        Check(profiles.Count == 2, "subscription base64 blob -> 2 profiles");
        Check(profiles[0].Name == "A" && profiles[1].Name == "B", "subscription remarks kept");
    }
}
