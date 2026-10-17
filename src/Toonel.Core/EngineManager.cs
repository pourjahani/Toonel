using System.Diagnostics;
using System.Security.Principal;
using Toonel.Core.Models;

namespace Toonel.Core;

public enum EngineState { Stopped, Starting, Connected, Error }

/// <summary>
/// Orchestrates the engine chain for the selected connection mode:
///   xray (always) -> psiphon tunnel-core (modes B/C/D) -> sing-box TUN (optional).
/// Start order matters: xray first so psiphon's UpstreamProxyURL target exists ("after" mode);
/// sing-box last so its rules can already route into a live xray.
/// </summary>
public sealed class EngineManager
{
    public const string SourceXray = "xray";
    public const string SourcePsiphon = "psiphon";
    public const string SourceSingBox = "sing-box";
    public const string SourceApp = "app";

    private readonly LogBuffer _log;
    private readonly string _runtimeDir;
    private readonly string _coresDir;

    private ProcessRunner? _xray;
    private ProcessRunner? _psiphon;
    private ProcessRunner? _singBox;
    private Process? _elevatedSingBox;
    private ConnectionMode _mode;
    private bool _tunActive;
    private bool _systemProxyActive;
    private int _activeHttpPort;

    public EngineState State { get; private set; } = EngineState.Stopped;
    public ConnectionMode Mode => _mode;
    public bool TunActive => _tunActive;
    public bool PsiphonRunning => _psiphon?.IsRunning == true;
    public bool XrayRunning => _xray?.IsRunning == true;

    public event Action<EngineState>? StateChanged;
    public event Action<bool>? PsiphonTunnelUp; // true when count>=1 noticed

    /// <summary>Set while a psiphon mode is starting; flips state to Connected on first tunnel.</summary>
    private bool _awaitingPsiphonTunnel;

    public EngineManager(LogBuffer log, string? runtimeDir = null, string? coresDir = null)
    {
        _log = log;
        _runtimeDir = runtimeDir ?? PortablePaths.RuntimeDir;
        _coresDir = coresDir ?? PortablePaths.CoresDir;
        Directory.CreateDirectory(_runtimeDir);
    }

    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private void OnPsiphonLine(string line)
    {
        _log.Add(SourcePsiphon, line);
        if (line.Contains("\"Tunnels\"") && line.Contains("\"count\":1"))
        {
            PsiphonTunnelUp?.Invoke(true);
            if (_awaitingPsiphonTunnel && State == EngineState.Starting)
            {
                _awaitingPsiphonTunnel = false;
                SetState(EngineState.Connected);
                _log.Add(SourceApp, "Connected (Psiphon tunnel up).");
            }
        }
    }

    public string XrayPath => Path.Combine(_coresDir, "xray.exe");
    public string SingBoxPath => Path.Combine(_coresDir, "sing-box.exe");
    public string PsiphonPath => Path.Combine(_coresDir, PsiphonConfigBuilder.PsiphonBinaryName);
    public string CoresDir => _coresDir;

    public string WriteConfigs(AppSettings s, ConnectionMode mode)
    {
        var xrayCfg = Path.Combine(_runtimeDir, "config.xray.json");
        var psiphonCfg = Path.Combine(_runtimeDir, "client.config");
        var singboxCfg = Path.Combine(_runtimeDir, "config.singbox.json");
        File.WriteAllText(xrayCfg, XrayConfigBuilder.Build(s, mode));
        File.WriteAllText(psiphonCfg, PsiphonConfigBuilder.Build(s, mode));
File.WriteAllText(singboxCfg, SingBoxConfigBuilder.Build(s, mode, Path.Combine(_runtimeDir, "singbox.log"), s.TunEnabled));
        return singboxCfg;
    }

    public string GeneratePsiphonConfig(AppSettings s) =>
        PsiphonConfigBuilder.Build(s, ConnectionMode.PsiphonAfter);

    public async Task StartAsync(AppSettings s, ConnectionMode mode, CancellationToken ct = default)
    {
        if (State is EngineState.Starting or EngineState.Connected)
            await StopAsync();

        _mode = mode;
        SetState(EngineState.Starting);
        var native = SingBoxConfigBuilder.IsNativeProxy(s.SelectedProfile, mode);
        var psiphonOnly = mode == ConnectionMode.PsiphonOnly;
        var wantsPsiphon = psiphonOnly || mode is ConnectionMode.PsiphonAfter or ConnectionMode.PsiphonBefore;

        try
        {
            if (psiphonOnly)
            {
                // Psiphon alone: tunnel-core is the whole VPN — no profile, no xray.
                var psiphonCfgOnly = Path.Combine(_runtimeDir, "client.config");
                File.WriteAllText(psiphonCfgOnly, PsiphonConfigBuilder.Build(s, mode));
                _psiphon = new ProcessRunner(SourcePsiphon, PsiphonPath);
                _psiphon.OutputLine += OnPsiphonLine;
                _psiphon.Start($"-config \"{psiphonCfgOnly}\"");
                await Task.Delay(700, ct);
                if (!_psiphon.IsRunning)
                    throw new InvalidOperationException($"psiphon exited early (code {_psiphon.ExitCode}). See psiphon log.");
                _awaitingPsiphonTunnel = true;
                _log.Add(SourceApp, $"psiphon started (socks :{s.Ports.PsiphonSocksPort}, http :{s.Ports.PsiphonHttpPort}) — waiting for tunnel…");
            }
            else if (native)
            {
                // Hysteria2/TUIC cannot run under Xray: sing-box hosts the local
                // SOCKS/HTTP inbounds itself (same ports) and proxies natively.
                _log.Add(SourceApp, $"native {s.SelectedProfile!.Protocol} mode — sing-box hosts the proxy (no xray).");
            }
            else
            {
                // 1. xray (front router) — required in every other mode.
                var xrayCfg = Path.Combine(_runtimeDir, "config.xray.json");
                File.WriteAllText(xrayCfg, XrayConfigBuilder.Build(s, mode));
                _xray = new ProcessRunner(SourceXray, XrayPath);
                _xray.OutputLine += line => _log.Add(SourceXray, line);
                _xray.Start($"run -c \"{xrayCfg}\"");
                await Task.Delay(700, ct);
                if (!_xray.IsRunning)
                    throw new InvalidOperationException($"xray exited early (code {_xray.ExitCode}). See xray log.");
                _log.Add(SourceApp, $"xray started (socks :{s.Ports.MainSocksPort}, http :{s.Ports.MainHttpPort}, vless-only :{s.Ports.VlessOnlySocksPort})");

                // 2. psiphon tunnel-core (chain modes).
                if (wantsPsiphon)
                {
                    var psiphonCfg = Path.Combine(_runtimeDir, "client.config");
                    File.WriteAllText(psiphonCfg, PsiphonConfigBuilder.Build(s, mode));
                    _psiphon = new ProcessRunner(SourcePsiphon, PsiphonPath);
                    _psiphon.OutputLine += OnPsiphonLine;
                    _psiphon.Start($"-config \"{psiphonCfg}\"");
                    _awaitingPsiphonTunnel = true;
                    _log.Add(SourceApp, "psiphon tunnel-core starting (waiting for tunnel)…");
                }
            }

            // 3. sing-box TUN (optional, needs admin for auto_route).
            // Native hy2/tuic mode also requires sing-box as the proxy host (no TUN).
            if (s.TunEnabled || native)
            {
                try
                {
                    StartSingBox(s, withTun: s.TunEnabled);
                    _tunActive = true;
                    _log.Add(SourceApp, "TUN mode active — whole-system traffic captured (terminals included).");
                }
                catch (Exception ex)
                {
                    _log.Add(SourceApp, $"TUN failed ({ex.Message}). Falling back to system proxy.");
                }
            }
            if (!_tunActive && (s.SystemProxyEnabled || native))
            {
                // Psiphon-only has no xray: point the system proxy at psiphon's own local proxy.
                _activeHttpPort = psiphonOnly ? s.Ports.PsiphonHttpPort : s.Ports.MainHttpPort;
                SystemProxyManager.Enable(_activeHttpPort);
                _systemProxyActive = true;
                _log.Add(SourceApp, $"Windows system proxy set to 127.0.0.1:{_activeHttpPort}.");
            }

            if (!wantsPsiphon)
            {
                SetState(EngineState.Connected);
                _log.Add(SourceApp, "Connected (V2Ray).");
            }
            // Psiphon modes flip to Connected in OnPsiphonLine when the tunnel is up.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User aborted the connect attempt (Disconnect while connecting): tear down
            // whatever was already launched and land on Stopped, not Error.
            _log.Add(SourceApp, "Connect cancelled.");
            await StopAsync();
            throw;
        }
        catch (Exception ex)
        {
            _log.Add(SourceApp, $"ERROR: {ex.Message}");
            await StopAsync();
            SetState(EngineState.Error);
            throw;
        }
    }

    private void StartSingBox(AppSettings s, bool withTun = true)
    {
        var cfgPath = Path.Combine(_runtimeDir, "config.singbox.json");
        File.WriteAllText(cfgPath, SingBoxConfigBuilder.Build(s, _mode, Path.Combine(_runtimeDir, "singbox.log"), withTun));

        _singBox = new ProcessRunner(SourceSingBox, SingBoxPath);
        _singBox.OutputLine += line => _log.Add(SourceSingBox, line);

        if (IsElevated)
        {
            _singBox.Start($"run -c \"{cfgPath}\"");
        }
        else
        {
            // Launch elevated via UAC prompt; stream output through a detached watcher
            // (elevated children cannot pipe to us directly, so log to file instead).
            var elevatedCfg = Path.Combine(_runtimeDir, "config.singbox.json");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = SingBoxPath,
                Arguments = $"run -c \"{elevatedCfg}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            _elevatedSingBox = Process.Start(psi);
            var p = _elevatedSingBox;
            if (p is null) throw new InvalidOperationException("UAC prompt was cancelled.");
            _log.Add(SourceSingBox, "started elevated (logs in runtime\\singbox.log)");
        }
        // give the TUN adapter a moment to come up
        Thread.Sleep(800);
    }

    public async Task StopAsync()
    {
        SetState(EngineState.Starting); // transient "stopping"
        if (_systemProxyActive)
        {
            SystemProxyManager.Disable();
            _systemProxyActive = false;
            _log.Add(SourceApp, "Windows system proxy restored.");
        }
        if (_elevatedSingBox is not null)
        {
            try { if (!_elevatedSingBox.HasExited) _elevatedSingBox.Kill(entireProcessTree: true); } catch { }
            _elevatedSingBox.Dispose();
            _elevatedSingBox = null;
        }
        if (_singBox is not null) { await _singBox.StopAsync(); _singBox = null; }
        _tunActive = false;
        if (_psiphon is not null) { await _psiphon.StopAsync(); _psiphon = null; }
        if (_xray is not null) { await _xray.StopAsync(); _xray = null; }
        _awaitingPsiphonTunnel = false;
        SetState(EngineState.Stopped);
        _log.Add(SourceApp, "Stopped.");
    }

    /// <summary>
    /// Synchronous best-effort teardown for exit paths where async StopAsync cannot run
    /// (ProcessExit, session end, fatal exceptions). Resets proxy, TUN and engines. Idempotent.
    /// </summary>
    public void EmergencyStop()
    {
        try
        {
            if (_activeHttpPort > 0) SystemProxyManager.DisableIfOurs(_activeHttpPort);
        }
        catch { /* best effort */ }

        try { if (_elevatedSingBox is { HasExited: false }) _elevatedSingBox.Kill(entireProcessTree: true); } catch { }
        _elevatedSingBox = null;

        foreach (var runner in new[] { _singBox, _psiphon, _xray })
        {
            if (runner is null) continue;
            try { runner.StopAsync(1500).Wait(TimeSpan.FromSeconds(3)); } catch { /* best effort */ }
        }
        _singBox = null;
        _psiphon = null;
        _xray = null;
        _tunActive = false;
        _systemProxyActive = false;
        _awaitingPsiphonTunnel = false;
        if (State != EngineState.Stopped) SetState(EngineState.Stopped);
    }

    /// <summary>Kills leftover engine processes from a crashed previous run (only ones under coresDir).</summary>
    public static void KillStaleEngines(string coresDir)
    {
        foreach (var name in new[] { "xray", "sing-box", "psiphon-core" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (path is not null && path.StartsWith(coresDir, StringComparison.OrdinalIgnoreCase))
                        proc.Kill(entireProcessTree: true);
                }
                catch { /* not ours or access denied */ }
                finally
                {
                    proc.Dispose();
                }
            }
        }
    }

    private void SetState(EngineState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }
}
