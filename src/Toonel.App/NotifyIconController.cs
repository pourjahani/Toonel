using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Toonel.Core;
using Toonel.Core.Models;

namespace Toonel.App;

/// <summary>
/// Tray icon with connect/disconnect/show/exit actions. The tooltip and the menu's
/// status header update live: connection state, mode, egress country and uptime.
/// </summary>
public sealed class NotifyIconController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainWindow _window;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _disconnectItem;
    private DateTime? _connectedAt;

    public NotifyIconController(MainWindow window)
    {
        _window = window;
        _statusItem = new ToolStripMenuItem("Toonel — " + Loc.T("DISCONNECTED"))
        {
            Enabled = false,
        };
        _connectItem = new ToolStripMenuItem(Loc.T("Connect"), null, (_, _) =>
            _window.Dispatcher.Invoke(() => _window.ViewModel.ConnectCommand.Execute(null)));
        _disconnectItem = new ToolStripMenuItem(Loc.T("Disconnect"), null, (_, _) =>
            _window.Dispatcher.Invoke(() => _window.ViewModel.DisconnectCommand.Execute(null)));

        _icon = new NotifyIcon
        {
            Text = "Toonel",
            Visible = true,
            Icon = LoadIcon(),
            ContextMenuStrip = BuildMenu(),
        };
        _icon.DoubleClick += (_, _) => ShowWindow();

        App.Engines.StateChanged += _ => _connectedAt =
            App.Engines.State == EngineState.Connected ? DateTime.Now : null;

        // Refresh tooltip + status header once per second while connected.
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_connectItem);
        menu.Items.Add(_disconnectItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("Dashboard"), null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => _window.Dispatcher.Invoke(() =>
        {
            _window.ForceClose = true;
            System.Windows.Application.Current.Shutdown();
        }));
        return menu;
    }

    private void RefreshStatus()
    {
        try
        {
            var state = App.Engines.State;
            var stateLabel = state switch
            {
                EngineState.Connected => Loc.T("CONNECTED"),
                EngineState.Starting => Loc.T("WORKING"),
                EngineState.Error => Loc.T("ERROR"),
                _ => Loc.T("DISCONNECTED"),
            };

            // Header line: state (+ mode when connected).
            var header = "Toonel — " + stateLabel;
            if (state == EngineState.Connected)
                header += " · " + Loc.T(MainViewModel.ModeLabel(App.Settings.Mode));

            // Egress country for the psiphon modes.
            var psiphonMode = App.Settings.Mode != ConnectionMode.V2RayOnly;
            if (state == EngineState.Connected && psiphonMode)
                header += "\n" + Loc.T("exit") + ": " + PsiphonCountries.DisplayName(App.Settings.Psiphon.EgressRegion);

            // Uptime.
            if (state == EngineState.Connected && _connectedAt is { } at)
            {
                var up = DateTime.Now - at;
                header += "\n" + Loc.T("Uptime") + ": " + FormatUptime(up);
            }

            _statusItem.Text = header.Replace("\n", "  ·  ");

            // Tray tooltip is limited to 63 chars by Windows — keep a compact single line.
            var tip = state == EngineState.Connected
                ? $"Toonel — {stateLabel}"
                  + (psiphonMode ? $" · {PsiphonCountries.DisplayName(App.Settings.Psiphon.EgressRegion)}" : "")
                  + (_connectedAt is { } t0 ? $" · {FormatUptime(DateTime.Now - t0)}" : "")
                : $"Toonel — {stateLabel}";
            _icon.Text = tip.Length > 63 ? tip[..63] : tip;

            _connectItem.Enabled = state is EngineState.Stopped or EngineState.Error;
            _disconnectItem.Enabled = state is EngineState.Connected or EngineState.Starting;
        }
        catch
        {
            // tray refresh is best effort
        }
    }

    private static string FormatUptime(TimeSpan up) =>
        up.TotalHours >= 1
            ? $"{(int)up.TotalHours}h {up.Minutes}m"
            : up.TotalMinutes >= 1
                ? $"{up.Minutes}m {up.Seconds}s"
                : $"{up.Seconds}s";

    private static Icon LoadIcon()
    {
        // 1) app.ico shipped next to the exe (works for normal publishes).
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(icoPath)) return new Icon(icoPath);
        }
        catch { /* fall through */ }

        // 2) Extract from the running exe (works in single-file: host exe has the icon).
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                using var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null) return (Icon)extracted.Clone();
            }
        }
        catch { /* fall through */ }

        // 3) Last resort.
        return SystemIcons.Shield;
    }

    private void ShowWindow()
    {
        _window.Dispatcher.Invoke(() =>
        {
            _window.Show();
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
        });
    }

    public void Dispose()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
