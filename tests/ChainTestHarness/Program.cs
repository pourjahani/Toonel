using Toonel.Core;
using Toonel.Core.Models;

// Generates the exact configs the app produces on Connect, for both chain modes,
// into a scratch runtime dir — so we can end-to-end test PsiphonAfter/PsiphonBefore
// by running xray + psiphon-core manually (no WPF app, no TUN, no settings changes).

var scratch = Path.Combine(Path.GetTempPath(), "toonel-chain-test");
if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
Directory.CreateDirectory(Path.Combine(scratch, "runtime"));

var settings = new AppSettings
{
    TunEnabled = false,
    SystemProxyEnabled = false,
    // Socks-profile stand-in for a V2Ray server (a second xray listens on 11999).
    Profiles =
    {
        new Profile
        {
            Name = "standin-v2ray",
            Protocol = ProfileProtocol.Socks,
            Address = "127.0.0.1",
            Port = 11999,
            Security = "none",
            Network = "tcp",
        },
    },
};
settings.SelectedProfileId = settings.Profiles[0].Id;

var engine = new EngineManager(
    new LogBuffer(),
    Path.Combine(scratch, "runtime"),
    Path.Combine(AppContext.BaseDirectory, "cores"));

foreach (var mode in new[] { ConnectionMode.PsiphonAfter, ConnectionMode.PsiphonBefore })
{
    var dir = Path.Combine(scratch, mode.ToString());
    Directory.CreateDirectory(Path.Combine(dir, "runtime"));
    var eng = new EngineManager(new LogBuffer(), Path.Combine(dir, "runtime"), Path.Combine(AppContext.BaseDirectory, "cores"));
    eng.WriteConfigs(settings, mode);
    Console.WriteLine($"[{mode}] configs written to {dir}\\runtime");
}

// Also emit the stand-in "V2Ray server" xray config (plain socks inbound -> freedom).
var serverCfg = new System.Text.Json.Nodes.JsonObject
{
    ["log"] = new System.Text.Json.Nodes.JsonObject { ["loglevel"] = "warning" },
    ["inbounds"] = new System.Text.Json.Nodes.JsonArray(
        new System.Text.Json.Nodes.JsonObject
        {
            ["tag"] = "in",
            ["listen"] = "127.0.0.1",
            ["port"] = 11999,
            ["protocol"] = "socks",
            ["settings"] = new System.Text.Json.Nodes.JsonObject { ["udp"] = false, ["auth"] = "noauth" },
        }),
    ["outbounds"] = new System.Text.Json.Nodes.JsonArray(
        new System.Text.Json.Nodes.JsonObject
        {
            ["tag"] = "out",
            ["protocol"] = "freedom",
            ["settings"] = new System.Text.Json.Nodes.JsonObject(),
        }),
};
File.WriteAllText(Path.Combine(scratch, "server.config.json"), serverCfg.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"[server] stand-in V2Ray server config: {scratch}\\server.config.json");
Console.WriteLine($"SCRATCH={scratch}");
