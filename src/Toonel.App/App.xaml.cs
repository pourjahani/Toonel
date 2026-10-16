using System.IO;
using System.Windows;
using Microsoft.Win32;
using Toonel.Core;
using Toonel.Core.Models;
using Application = System.Windows.Application;

namespace Toonel.App;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static LogBuffer Log { get; } = new();
    public static EngineManager Engines { get; private set; } = null!;
    public static SubscriptionClient Subscriptions { get; } = new();
    public static CoreDownloader Downloader { get; private set; } = null!;

    private NotifyIconController? _tray;
    private SingleInstanceGuard? _singleInstance;
    public static bool IsShuttingDown { get; private set; }

    /// <summary>File the app writes fatal errors to (readable even if the UI never appears).</summary>
    public static string CrashLogPath { get; } = PortablePaths.CrashLogFile;

    /// <summary>Swaps the merged palette dictionary (Theme.Light / Theme.Dark) at runtime.</summary>
    public static void ApplyTheme(string theme)
    {
        var dicts = Current.Resources.MergedDictionaries;
        var palette = dicts.FirstOrDefault(d =>
            d.Source?.OriginalString is { } s &&
            (s.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
             s.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase)));
        var dict = new ResourceDictionary { Source = new Uri($"Theme.{theme}.xaml", UriKind.Relative) };
        var index = dicts.IndexOf(palette);
        if (index >= 0) dicts[index] = dict;
        else dicts.Insert(0, dict);
    }

    private static void WriteCrashLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* nothing more we can do */ }
    }

    /// <summary>Runs OnStartup body inside try/catch: any startup failure is logged to
    /// crash.log, shown in a message box, and shuts the app down instead of leaving an
    /// invisible zombie process behind (ShutdownMode is OnExplicitShutdown).</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            StartupCore();
        }
        catch (Exception ex)
        {
            WriteCrashLog($"startup failed: {ex}");
            try
            {
                System.Windows.MessageBox.Show($"Toonel failed to start:\n\n{ex.Message}\n\nDetails: {CrashLogPath}",
                    "Toonel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* headless */ }
            Environment.ExitCode = 1;
            Shutdown();
        }
    }

    private void StartupCore()
    {
        // Single instance: a second launch pings this one (bring window to front)
        // and exits before touching engines, proxy or settings.
        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.TryAcquireFirst(ActivateMainWindow))
        {
            ToastWindow.Show(Loc.T("Toonel is already running."));
            // Keep the dispatcher pumping so the toast can actually be seen,
            // then exit without touching engines, proxy or settings.
            var exitTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            exitTimer.Tick += (_, _) => { exitTimer.Stop(); Environment.Exit(0); };
            exitTimer.Start();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog($"dispatcher unhandled: {args.Exception}");
            Log.Add("app", $"Unhandled: {args.Exception.Message}");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrashLog($"appdomain unhandled: {ex}");
        };

        // Portable data: pull legacy %APPDATA% settings into the side-by-side data folder
        // on the first run, then always read/write inside the app directory.
        PortablePaths.MigrateLegacyData();

        Settings = SettingsStore.Load();
        ApplyTheme(Settings.Theme == "dark" ? "Dark" : "Light");
        Engines = new EngineManager(Log);
        Downloader = new CoreDownloader();
        Loc.Set(Settings.Language == "fa" ? AppLanguage.Fa : AppLanguage.En);
        Log.Add("app", EngineManager.IsElevated
            ? "Toonel starting (elevated)…"
            : "Toonel starting…");

        // Recover from a previous crashed/killed run before doing anything else.
        EngineManager.KillStaleEngines(Engines.CoresDir);
        SystemProxyManager.DisableIfOurs(Settings.Ports.MainHttpPort);

        // Last-resort cleanup for paths where OnExit never runs.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Engines.EmergencyStop();
        SystemEvents.SessionEnding += (_, _) => Engines.EmergencyStop();

        var window = new MainWindow();
        MainWindow = window;

        _tray = new NotifyIconController(window);
        _ = _tray; // keep alive

        if (Settings.CompactOnStart)
        {
            var bar = new CompactBar(window);
            bar.Show(); // full window stays hidden until the bar is expanded
        }
        else
        {
            window.Show();
        }

        if (Settings.AutoConnectOnStart)
            window.ViewModel.ConnectCommand.Execute(null);
    }

    /// <summary>Called when a second launch pings us: surface the existing UI.</summary>
    private void ActivateMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            var window = MainWindow as MainWindow;
            if (window is null) return;
            window.Show();
            window.WindowState = System.Windows.WindowState.Normal;
            window.Activate();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        try
        {
            Engines.StopAsync().Wait(TimeSpan.FromSeconds(8));
        }
        catch
        {
            // fall through to the synchronous teardown below
        }
        Engines.EmergencyStop(); // idempotent; guarantees proxy/TUN/engines are reset
        try { SettingsStore.Save(Settings); } catch { /* best effort on shutdown */ }
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public static void SaveSettings() => SettingsStore.Save(Settings);
}
