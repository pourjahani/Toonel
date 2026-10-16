using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toonel.Core;
using Toonel.Core.Models;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace Toonel.App;

public partial class MainViewModel : ObservableObject
{
    private readonly EngineManager _engines;
    private readonly LogBuffer _log;
    /// <summary>Cancels the in-flight connect attempt (binary download / engine launch).</summary>
    private CancellationTokenSource? _connectCts;

    public ObservableCollection<Profile> Profiles => App.Settings.Profiles;

    [ObservableProperty] private EngineState _state = EngineState.Stopped;
    [ObservableProperty] private ConnectionMode _mode = ConnectionMode.V2RayOnly;
    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private string _statusText = "Disconnected";
    [ObservableProperty] private string _psiphonStatus = "off";
    [ObservableProperty] private string _tunStatus = "off";
    [ObservableProperty] private string _selectedLogSource = "app";
    [ObservableProperty] private string _logText = "";

    // Pass-through dashboard toggles (persisted into App.Settings).
    public bool TunEnabled
    {
        get => App.Settings.TunEnabled;
        set { App.Settings.TunEnabled = value; App.SaveSettings(); OnPropertyChanged(); }
    }
    public bool SystemProxyEnabled
    {
        get => App.Settings.SystemProxyEnabled;
        set { App.Settings.SystemProxyEnabled = value; App.SaveSettings(); OnPropertyChanged(); }
    }
    public bool BlockQuic
    {
        get => App.Settings.BlockQuic;
        set { App.Settings.BlockQuic = value; App.SaveSettings(); OnPropertyChanged(); }
    }

    public bool IsConnected => State == EngineState.Connected;
    public bool IsBusy => State == EngineState.Starting;

    public string[] ModeNames { get; } =
    {
        "V2Ray only",
        "Psiphon alone",
        "Psiphon after V2Ray  (psiphon over vless)",
        "Psiphon before V2Ray (vless over psiphon)",
    };

    public string[] LogSources { get; } = { EngineManager.SourceApp, EngineManager.SourceXray, EngineManager.SourcePsiphon, EngineManager.SourceSingBox };

    public MainViewModel()
    {
        _engines = App.Engines;
        _log = App.Log;
        Mode = App.Settings.Mode;
        SelectedProfile = App.Settings.SelectedProfile;
        _engines.StateChanged += s => App.Current.Dispatcher.Invoke(() =>
        {
            State = s;
            StatusText = s switch
            {
                EngineState.Connected => ModeLabel(Mode),
                EngineState.Starting => "Working…",
                EngineState.Error => "Error — see logs",
                _ => "Disconnected",
            };
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
        });
        _engines.PsiphonTunnelUp += up => App.Current.Dispatcher.Invoke(() =>
            PsiphonStatus = up ? "tunnel up ✓" : "…");
        _log.LineAdded += (source, line) => App.Current.Dispatcher.Invoke(() =>
        {
            if (source != SelectedLogSource) return;
            LogText += line + Environment.NewLine;
        });

        ReloadLog();
    }

    public static string ModeLabel(ConnectionMode m) => m switch
    {
        ConnectionMode.V2RayOnly => "Connected · V2Ray",
        ConnectionMode.PsiphonOnly => "Connected · Psiphon",
        ConnectionMode.PsiphonAfter => "Connected · Psiphon after V2Ray",
        ConnectionMode.PsiphonBefore => "Connected · Psiphon before V2Ray",
        _ => "Connected",
    };

    partial void OnModeChanged(ConnectionMode value)
    {
        App.Settings.Mode = value;
        App.SaveSettings();
        if (IsConnected)
            _log.Add(EngineManager.SourceApp, "Mode changed — reconnect to apply.");
    }

    partial void OnSelectedProfileChanged(Profile? value)
    {
        App.Settings.SelectedProfileId = value?.Id ?? "";
        App.SaveSettings();
    }

    partial void OnSelectedLogSourceChanged(string value) => ReloadLog();

    private void ReloadLog()
    {
        var lines = _log.Snapshot(SelectedLogSource);
        LogText = string.Join(Environment.NewLine, lines) + (lines.Count > 0 ? Environment.NewLine : "");
    }

    private bool CanConnect() => !IsBusy && State != EngineState.Connected;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var cts = new CancellationTokenSource();
        _connectCts = cts;
        try
        {
            await ConnectCoreAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // User pressed Disconnect mid-connect; EngineManager already tore everything
            // down and set the state to Stopped — no error dialog for a deliberate cancel.
            _log.Add(EngineManager.SourceApp, "Connect cancelled by user.");
            PsiphonStatus = "off";
        }
        finally
        {
            if (ReferenceEquals(_connectCts, cts)) _connectCts = null;
            cts.Dispose();
        }
    }

    private async Task ConnectCoreAsync(CancellationToken ct)
    {
        var mode = Mode;
        if (mode != ConnectionMode.PsiphonOnly && SelectedProfile is null)
        {
            MessageBox.Show(Loc.T("This mode needs a VLESS/VMess/Trojan profile. Add one in Profiles."), "Toonel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PsiphonStatus = mode == ConnectionMode.V2RayOnly ? "off" : "connecting…";
        TunStatus = App.Settings.TunEnabled ? "on" : "off";

        // Ensure engine binaries exist; offer to fetch the pinned ones first.
        // Psiphon-only doesn't use xray at all, so it isn't required there.
        var dl = App.Downloader;
        var psiphonOnly = mode == ConnectionMode.PsiphonOnly;
        var needsXray = !psiphonOnly && !dl.XrayPresent;
        var needsPsiphon = mode != ConnectionMode.V2RayOnly && !dl.PsiphonPresent;
        var needsSingBox = App.Settings.TunEnabled && (!dl.SingBoxPresent || !dl.WintunPresent);
        if (needsXray || needsPsiphon || needsSingBox)
        {
            var answer = MessageBox.Show(
                Loc.T("Some engine binaries are missing:") + "\n" + dl.MissingSummary() +
                Loc.T("\n\nDownload them now? (pinned official releases)"),
                "Toonel", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                PsiphonStatus = "off";
                return;
            }
            try
            {
                await dl.DownloadMissingAsync(new Progress<string>(l => _log.Add(EngineManager.SourceApp, l)), ct);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.F("Core download failed: {0}", ex.Message), "Toonel", MessageBoxButton.OK, MessageBoxImage.Error);
                PsiphonStatus = "off";
                return;
            }
        }

        _engines.WriteConfigs(App.Settings, mode); // always inspectable on disk
        try
        {
            await _engines.StartAsync(App.Settings, mode, ct);
            if (mode == ConnectionMode.V2RayOnly) PsiphonStatus = "off";
        }
        catch (OperationCanceledException)
        {
            throw; // handled by ConnectAsync (no error dialog)
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("Connect failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanDisconnect() => State != EngineState.Stopped;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        // Enabled while connected AND while a connect attempt is still in progress:
        // first cancel the attempt, wait for it to unwind (it stops the engines it
        // already started), then tear down everything that may be left over.
        try { _connectCts?.Cancel(); }
        catch (ObjectDisposedException) { /* connect just finished on its own */ }

        if (ConnectCommand.IsRunning)
        {
            try { await ConnectCommand.ExecutionTask!; }
            catch { /* connect path reports its own errors */ }
        }

        try { await _engines.StopAsync(); }
        catch (Exception ex) { _log.Add(EngineManager.SourceApp, $"stop error: {ex.Message}"); }
        PsiphonStatus = "off";
    }

    // ---- Profile management -------------------------------------------------

    [RelayCommand]
    private void ImportFromClipboard()
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(Loc.T("Clipboard is empty."), Loc.T("Import"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ImportText(text);
    }

    [RelayCommand]
    private void ImportFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Text|*.txt;*.conf;*.*" };
        if (dlg.ShowDialog() != true) return;
        ImportText(File.ReadAllText(dlg.FileName));
    }

    private void ImportText(string text)
    {
        var parsed = ProfileUrlParser.ParseMany(text);
        int added = 0, skipped = 0;
        foreach (var p in parsed)
        {
            if (Profiles.Any(x => x.Address == p.Address && x.Port == p.Port && x.Uuid == p.Uuid && x.Password == p.Password))
            { skipped++; continue; }
            Profiles.Add(p);
            added++;
        }
        App.SaveSettings();
        _log.Add(EngineManager.SourceApp, $"Imported {added} profile(s){(skipped > 0 ? $", skipped {skipped} duplicate(s)" : "")}.");
        MessageBox.Show(Loc.F("Imported {0} profile(s).{1}", added, skipped > 0 ? Loc.F(" Skipped {0} duplicate(s).", skipped) : ""),
            Loc.T("Import"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void DeleteProfile(Profile? profile)
    {
        if (profile is null) return;
        Profiles.Remove(profile);
        if (App.Settings.SelectedProfileId == profile.Id)
            SelectedProfile = Profiles.FirstOrDefault();
        App.SaveSettings();
    }

    [RelayCommand]
    private async Task TestProfileDelayAsync(Profile? profile)
    {
        if (profile is null) return;
        profile.Summary = $"{profile.Name}  — testing…";
        var ms = await LatencyTester.TcpPingAsync(profile.Address, profile.Port);
        profile.Summary = $"{profile.Name}  [{profile.Protocol}]  {profile.Address}:{profile.Port}  " +
                          (ms < 0 ? "✗ timeout" : $"{ms} ms");
    }
}
