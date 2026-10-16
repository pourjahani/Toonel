using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Toonel.Core;
using Toonel.Core.Models;
using MessageBox = System.Windows.MessageBox;
using FlowDirection = System.Windows.FlowDirection;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Toonel.App;

/// <summary>
/// One-line always-on-top status bar: dot, status, connect/disconnect, expand.
/// Closing it returns to the full window (it never quits the app by itself).
/// </summary>
public partial class CompactBar : Window
{
    private readonly DispatcherTimer _timer;

    private static Brush ThemeBrush(string key) =>
        (Brush)System.Windows.Application.Current.TryFindResource(key)!
        ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#346538"));

    private static Brush DotGreen => ThemeBrush("DotGreen");
    private static Brush DotAmber => ThemeBrush("DotAmber");
    private static Brush DotRed => ThemeBrush("DotRed");

    private MainWindow _owner;

    public CompactBar(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(icoPath))
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(icoPath));
        }
        catch { /* default icon is fine */ }

        // Default position: bottom-right of the primary screen work area.
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 24;
        Top = wa.Bottom - Height - 24;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        FlowDirection = Loc.Flow;
        Refresh();
    }

    private void Refresh()
    {
        StatusText.Text = ViewModelState() switch
        {
            EngineState.Connected => Loc.T("CONNECTED"),
            EngineState.Starting => Loc.T("WORKING"),
            EngineState.Error => Loc.T("ERROR"),
            _ => Loc.T("DISCONNECTED"),
        };

        Dot.Fill = ViewModelState() switch
        {
            EngineState.Connected => DotGreen,
            EngineState.Starting => DotAmber,
            EngineState.Error => DotRed,
            _ => (Brush)FindResource("IdleDot"),
        };

        var engines = App.Engines;
        bool showEgress = ViewModelState() == EngineState.Connected && engines.PsiphonRunning;
        UpdateFlag(showEgress);
        var egressText = showEgress
            ? " via " + PsiphonCountries.DisplayName(App.Settings.Psiphon.EgressRegion)
            : "";
        DetailText.Text = ViewModelState() == EngineState.Connected
            ? $"{(engines.XrayRunning ? "xray" : "")}{(engines.PsiphonRunning ? " + psiphon" : "")}{(engines.TunActive ? " + tun" : "")}{egressText}"
            : "";

        // While connecting, the toggle becomes an enabled Disconnect/Cancel so the user
        // can abort a stuck connect attempt right from the bar.
        BtnToggle.Content = ViewModelState() is EngineState.Connected or EngineState.Starting
            ? Loc.T("Disconnect")
            : Loc.T("Connect");
        BtnToggle.IsEnabled = true;

        // Localization switch mid-session.
        FlowDirection = Loc.Flow;
    }

    private void UpdateFlag(bool visible)
    {
        if (!visible) { PsiFlagBar.Visibility = Visibility.Collapsed; return; }
        var emoji = PsiphonCountries.Emoji(App.Settings.Psiphon.EgressRegion);
        if (string.IsNullOrEmpty(emoji)) { PsiFlagBar.Visibility = Visibility.Collapsed; return; }
        PsiFlagBar.Text = emoji;
        PsiFlagBar.Visibility = Visibility.Visible;
    }

    private static EngineState ViewModelState() => App.Engines.State;

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (App.Engines.State == EngineState.Connected)
        {
            try { await App.Engines.StopAsync(); } catch { /* logged upstream */ }
        }
        else if (App.Engines.State == EngineState.Starting)
        {
            // Mid-connect: cancel the attempt through the view model (same path as the
            // dashboard Disconnect button).
            _owner.ViewModel.DisconnectCommand.Execute(null);
        }
        else if (App.Engines.State == EngineState.Stopped || App.Engines.State == EngineState.Error)
        {
            var vm = _owner.ViewModel;
            var mode = App.Settings.Mode;
            if (mode != ConnectionMode.PsiphonOnly && App.Settings.SelectedProfile is null)
            {
                MessageBox.Show(Loc.T("This mode needs a VLESS/VMess/Trojan profile. Add one in Profiles."),
                    "Toonel", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { await App.Engines.StartAsync(App.Settings, mode); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Loc.T("Connect failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        Refresh();
    }

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExpandToDashboard();

    private void ExpandToDashboard()
    {
        var w = App.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        if (w is null) { _timer.Stop(); Close(); return; }
        w.Show();
        w.WindowState = WindowState.Normal;
        w.Activate();
        _timer.Stop();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // If the user closes the bar via Alt+F4, restore the dashboard instead of quitting.
        // The restore runs on the dispatcher AFTER this close finishes — calling Show/Close
        // synchronously from inside OnClosing throws "Window is closing" re-entrancy errors.
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(() =>
            {
                _timer.Stop();
                var w = App.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (w is null) { Close(); return; }
                w.Show();
                w.WindowState = WindowState.Normal;
                w.Activate();
                Close();
            });
            return;
        }
        base.OnClosing(e);
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            try { DragMove(); } catch { /* not pressed anymore */ }
    }
}
