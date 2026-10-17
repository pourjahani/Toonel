using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Toonel.Core;
using Toonel.Core.Models;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using RadioButton = System.Windows.Controls.RadioButton;
using FlowDirection = System.Windows.FlowDirection;

namespace Toonel.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private bool _loadingUi;
    private readonly DispatcherTimer _uiTimer;

    public bool ForceClose { get; set; }

    private static Brush ThemeBrush(string key) =>
        (Brush)System.Windows.Application.Current.TryFindResource(key)!
        ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#346538"));

    private static Brush DotGreen => ThemeBrush("DotGreen");
    private static Brush DotAmber => ThemeBrush("DotAmber");
    private static Brush DotRed => ThemeBrush("DotRed");

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        FlowDirection = Loc.Flow;
        TryLoadAppIcon();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _uiTimer.Tick += (_, _) => RefreshTiles();
        _uiTimer.Start();

        Loaded += (_, _) => { ApplyLocalization(); LoadSettingsToUi(); ApplyNav("Dash"); RefreshTiles(); RefreshCoreStatus(); };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && App.Settings.MinimizeToTray)
                Hide();
        };
        Closing += (_, e) =>
        {
            if (!ForceClose && App.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            // Closing the window quits the app; OnExit + EmergencyStop reset
            // the system proxy, TUN adapter and all engine processes.
            if (!ForceClose)
            {
                ForceClose = true;
                System.Windows.Application.Current.Shutdown();
            }
        };
    }

    /// <summary>Loads app.ico from the exe folder as the window/taskbar icon (best effort).</summary>
    private void TryLoadAppIcon()
    {
        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(icoPath))
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(icoPath));
        }
        catch { /* default icon is fine */ }
    }

    // ---------------- navigation ----------------

    private void ApplyNav(string page)
    {
        PageDash.Visibility = page == "Dash" ? Visibility.Visible : Visibility.Collapsed;
        PageProfiles.Visibility = page == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        PageSplit.Visibility = page == "Split" ? Visibility.Visible : Visibility.Collapsed;
        PagePsiphon.Visibility = page == "Psiphon" ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = page == "Logs" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Logs") ScrollLogToEnd();
    }

    private void NavClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            ApplyNav(tag);
    }

    private void OnActivatePsiphonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Mode == ConnectionMode.V2RayOnly)
            ViewModel.Mode = ViewModel.SelectedProfile is null
                ? ConnectionMode.PsiphonOnly
                : ConnectionMode.PsiphonAfter;
        ViewModel.ConnectCommand.Execute(null);
    }

    // ---------------- mode cards ----------------

    private void OnModeCardChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || ViewModel is null) return;
        ViewModel.Mode =
            ModePsiphon.IsChecked == true ? ConnectionMode.PsiphonOnly :
            ModeAfter.IsChecked == true ? ConnectionMode.PsiphonAfter :
            ModeBefore.IsChecked == true ? ConnectionMode.PsiphonBefore :
            ModeWarp.IsChecked == true ? ConnectionMode.WarpBefore :
            ConnectionMode.V2RayOnly;
        ModeHint.Text = ModeHintFor(ViewModel.Mode);
    }

    private void SyncModeCards()
    {
        _loadingUi = true;
        switch (ViewModel.Mode)
        {
            case ConnectionMode.PsiphonOnly: ModePsiphon.IsChecked = true; break;
            case ConnectionMode.PsiphonAfter: ModeAfter.IsChecked = true; break;
            case ConnectionMode.PsiphonBefore: ModeBefore.IsChecked = true; break;
            case ConnectionMode.WarpBefore: ModeWarp.IsChecked = true; break;
            default: ModeV2ray.IsChecked = true; break;
        }
        _loadingUi = false;
        ModeHint.Text = ModeHintFor(ViewModel.Mode);
    }

    private static string ModeHintFor(ConnectionMode m) => m switch
    {
        ConnectionMode.V2RayOnly => Loc.T("Traffic goes straight through your profile."),
        ConnectionMode.PsiphonOnly => Loc.T("Everything goes through the Psiphon network."),
        ConnectionMode.PsiphonAfter => Loc.T("Psiphon dials out through your VLESS line."),
        ConnectionMode.PsiphonBefore => Loc.T("Your VLESS line dials through Psiphon first."),
        ConnectionMode.WarpBefore => Loc.T("Your VLESS line dials out over the Cloudflare WARP tunnel."),
        _ => "",
    };

    private void OnTunChanged(object sender, RoutedEventArgs e) => SaveDashboardToggles();
    private void OnSysProxyChanged(object sender, RoutedEventArgs e) => SaveDashboardToggles();
    private void OnBlockQuicChanged(object sender, RoutedEventArgs e) => SaveDashboardToggles();

    private void SaveDashboardToggles()
    {
        if (_loadingUi) return;
        // Bound TwoWay properties already persist; this keeps refresh in sync.
        RefreshTiles();
    }

    // ---------------- status / tiles ----------------

    private void RefreshTiles()
    {
        bool xrayUp = App.Engines.XrayRunning;
        bool psiphonWanted = ViewModel.Mode != ConnectionMode.V2RayOnly;
        bool psiphonUp = ViewModel.PsiphonStatus.Contains("up") || App.Engines.PsiphonRunning;
        bool tunUp = ViewModel.TunStatus == "on";

        // Status dot + pill + headline
        StatusDot.Fill = ViewModel.State switch
        {
            EngineState.Connected => DotGreen,
            EngineState.Starting => DotAmber,
            EngineState.Error => DotRed,            _ => (Brush)FindResource("IdleDot"),
        };
        StatusPill.Text = ViewModel.State switch
        {
            EngineState.Connected => Loc.T("CONNECTED"),
            EngineState.Starting => Loc.T("WORKING"),
            EngineState.Error => Loc.T("ERROR"),
            _ => Loc.T("DISCONNECTED"),
        };
        HeadlineText.Text = ViewModel.State switch
        {
            EngineState.Connected => Loc.T(MainViewModel.ModeLabel(ViewModel.Mode)),
            EngineState.Starting => Loc.T("Working…"),
            EngineState.Error => Loc.T("Something failed — see Logs"),
            _ => Loc.T("Not connected"),
        };
        ModeSubText.Text = ViewModel.State == EngineState.Connected
            ? ModeHintFor(ViewModel.Mode)
            : Loc.T("Pick how traffic should travel, then press Connect.");

        // Engine rows
        TileXray.Text = xrayUp ? Loc.T("running") : Loc.T("stopped");
        TileXray.Foreground = xrayUp ? DotGreen : (Brush)FindResource("Muted");
        TileXraySub.Text = $"socks {App.Settings.Ports.MainSocksPort} · http {App.Settings.Ports.MainHttpPort}";

        TilePsiphon.Text = !psiphonWanted ? Loc.T("off") : psiphonUp ? Loc.T("tunnel up") : Loc.T("connecting…");
        TilePsiphon.Foreground = psiphonUp ? DotGreen : (Brush)FindResource("Muted");
        TilePsiphonSub.Text = $"socks {App.Settings.Ports.PsiphonSocksPort} · http {App.Settings.Ports.PsiphonHttpPort}";
        UpdatePsiphonFlag(psiphonWanted && (psiphonUp || ViewModel.State == EngineState.Starting));
        if (psiphonWanted && (psiphonUp || ViewModel.State == EngineState.Starting))
        {
            var egress = PsiphonCountries.DisplayName(App.Settings.Psiphon.EgressRegion);
            TilePsiphonSub.Text += " · " + Loc.T("exit") + " " + egress;
        }

        TileTun.Text = tunUp ? Loc.T("active") : App.Settings.TunEnabled ? Loc.T("enabled") : Loc.T("off");
        TileTun.Foreground = tunUp ? DotGreen : (Brush)FindResource("Muted");
        TileTunSub.Text = App.Settings.SplitTunnel.Mode switch
        {
            SplitTunnelMode.Include => Loc.F("include · {0} apps", App.Settings.SplitTunnel.Processes.Count),
            SplitTunnelMode.Exclude => Loc.F("exclude · {0} apps", App.Settings.SplitTunnel.Processes.Count),
            _ => App.Settings.SystemProxyEnabled ? Loc.T("system proxy") : Loc.T("split tunnel: off"),
        };

        IpCheckHint.Text = ViewModel.IpCheckText;

        // Sidebar dots
        SideDotXray.Fill = xrayUp ? DotGreen : (Brush)FindResource("IdleDot");
        SideXray.Text = xrayUp ? $"{Loc.T("Xray")} · {Loc.T("running")}" : Loc.T("Xray");
        SideDotPsiphon.Fill = psiphonUp ? DotGreen : (Brush)FindResource("IdleDot");
        SidePsiphon.Text = psiphonUp ? $"{Loc.T("Psiphon")} · {Loc.T("tunnel up")}" : Loc.T("Psiphon");
        SideDotTun.Fill = tunUp ? DotGreen : (Brush)FindResource("IdleDot");
        SideTun.Text = tunUp ? $"{Loc.T("TUN adapter")} · {Loc.T("active")}" : Loc.T("TUN adapter");
    }

    private void UpdatePsiphonFlag(bool visible)
    {
        if (!visible)
        {
            PsiFlagDash.Visibility = Visibility.Collapsed;
            return;
        }
        var emoji = PsiphonCountries.Emoji(App.Settings.Psiphon.EgressRegion);
        if (string.IsNullOrEmpty(emoji))
        {
            PsiFlagDash.Visibility = Visibility.Collapsed;
            return;
        }
        PsiFlagDash.Text = emoji;
        PsiFlagDash.Visibility = Visibility.Visible;
        EngPsiphon.Margin = new Thickness(6, 0, 0, 0);
    }

    // ---------------- settings pages ----------------

    private void LoadSettingsToUi()
    {
        _loadingUi = true;
        var s = App.Settings;

        StOff.IsChecked = s.SplitTunnel.Mode == SplitTunnelMode.Off;
        StInclude.IsChecked = s.SplitTunnel.Mode == SplitTunnelMode.Include;
        StExclude.IsChecked = s.SplitTunnel.Mode == SplitTunnelMode.Exclude;
        StProcesses.Text = string.Join(Environment.NewLine, s.SplitTunnel.Processes);
        StDirect.Text = string.Join(Environment.NewLine, s.SplitTunnel.DirectDomains
            .Concat(s.SplitTunnel.DirectIps).Concat(s.SplitTunnel.DirectPorts));
        StProxy.Text = string.Join(Environment.NewLine, s.SplitTunnel.ProxyDomains
            .Concat(s.SplitTunnel.ProxyIps).Concat(s.SplitTunnel.ProxyPorts));
        StBypassPrivate.IsChecked = s.SplitTunnel.BypassPrivateIps;

        LoadEgressCountries();
        PsiEgress.Text = string.IsNullOrEmpty(s.Psiphon.EgressRegion)
            ? PsiphonCountries.DisplayName("")
            : PsiphonCountries.DisplayName(s.Psiphon.EgressRegion);
        PsiSponsor.Text = s.Psiphon.SponsorId;
        PsiProp.Text = s.Psiphon.PropagationChannelId;
        PsiRsl.Text = s.Psiphon.RemoteServerListUrl;
        PsiCustom.Text = s.Psiphon.CustomConfigJson;

        PortMainSocks.Text = s.Ports.MainSocksPort.ToString();
        PortMainHttp.Text = s.Ports.MainHttpPort.ToString();
        PortVlessOnly.Text = s.Ports.VlessOnlySocksPort.ToString();
        PortPsiSocks.Text = s.Ports.PsiphonSocksPort.ToString();
        PortPsiHttp.Text = s.Ports.PsiphonHttpPort.ToString();

        DnsBox.SelectedIndex = s.DnsChoice switch
        {
            "1.1.1.1" => 1,
            "doh:1.1.1.1" => 2,
            "8.8.8.8" => 3,
            "doh:8.8.8.8" => 4,
            "9.9.9.9" => 5,
            "doh:9.9.9.9" => 6,
            _ => 0,
        };
        WarpKey.Text = s.Warp.PrivateKey;
        WarpAddr.Text = s.Warp.Address;
        WarpRes.Text = s.Warp.Reserved;
        SubHoursBox.SelectedIndex = s.SubscriptionUpdateHours switch { 6 => 1, 12 => 2, 24 => 3, 0 => 4, _ => 2 };
        RefreshSubscriptions();
        ChkAutoConnect.IsChecked = s.AutoConnectOnStart;
        ChkMinToTray.IsChecked = s.MinimizeToTray;
        ChkCompactStart.IsChecked = s.CompactOnStart;

        LangBox.SelectedIndex = s.Language == "fa" ? 1 : 0;

        SyncThemeSwitch();

        SyncModeCards();
        _loadingUi = false;
    }

    private void SaveUiToSettings()
    {
        if (_loadingUi) return;
        var s = App.Settings;

        s.SplitTunnel.Mode =
            StInclude.IsChecked == true ? SplitTunnelMode.Include :
            StExclude.IsChecked == true ? SplitTunnelMode.Exclude :
            SplitTunnelMode.Off;
        s.SplitTunnel.Processes = Lines(StProcesses.Text);
        s.SplitTunnel.BypassPrivateIps = StBypassPrivate.IsChecked == true;

        var direct = Classify(StDirect.Text);
        s.SplitTunnel.DirectDomains = direct.domains;
        s.SplitTunnel.DirectIps = direct.ips;
        s.SplitTunnel.DirectPorts = direct.ports;
        var proxy = Classify(StProxy.Text);
        s.SplitTunnel.ProxyDomains = proxy.domains;
        s.SplitTunnel.ProxyIps = proxy.ips;
        s.SplitTunnel.ProxyPorts = proxy.ports;

        App.SaveSettings();
    }

    private static List<string> Lines(string text) =>
        text.Split('\n', '\r').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private static (List<string> domains, List<string> ips, List<string> ports) Classify(string text)
    {
        var domains = new List<string>();
        var ips = new List<string>();
        var ports = new List<string>();
        foreach (var line in Lines(text))
        {
            if (line.All(char.IsDigit) || (line.Contains('-') && line.Replace("-", "").All(char.IsDigit)))
                ports.Add(line);
            else if (line.Contains('/') || line.Split('.').All(p => p.Length > 0 && p.All(char.IsDigit)))
                ips.Add(line);
            else
                domains.Add(line);
        }
        return (domains, ips, ports);
    }

    private void OnSplitTunnelChanged(object sender, RoutedEventArgs e) => SaveUiToSettings();
    private void OnStModeChanged(object sender, RoutedEventArgs e) => SaveUiToSettings();

    private bool _egressLoaded;

    private void LoadEgressCountries()
    {
        if (_egressLoaded) return;
        _egressLoaded = true;
        foreach (var c in PsiphonCountries.List)
            PsiEgress.Items.Add(new ComboBoxItem { Content = PsiphonCountries.ListLabel(c.Code), Tag = c.Code });
    }

    private void OnEgressCountryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || PsiEgress?.SelectedItem is not ComboBoxItem item) return;
        // Picking from the list writes the canonical code straight into settings.
        App.Settings.Psiphon.EgressRegion = (item.Tag as string) ?? "";
        App.SaveSettings();
    }

    private void OnEgressDropDownClosed(object? sender, EventArgs e) => OnPsiphonChanged(sender, null!);

    private void OnPsiphonChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        var s = App.Settings;
        s.Psiphon.EgressRegion = PsiphonCountries.Normalize(PsiEgress.Text);
        s.Psiphon.SponsorId = PsiSponsor.Text.Trim();
        s.Psiphon.PropagationChannelId = PsiProp.Text.Trim();
        s.Psiphon.RemoteServerListUrl = PsiRsl.Text.Trim();
        s.Psiphon.CustomConfigJson = PsiCustom.Text;
        App.SaveSettings();
    }

    private void OnDnsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || DnsBox?.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is string tag) App.Settings.DnsChoice = tag;
        App.SaveSettings();
    }

    private void OnWarpChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        App.Settings.Warp.PrivateKey = WarpKey.Text.Trim();
        App.Settings.Warp.Address = WarpAddr.Text.Trim();
        App.Settings.Warp.Reserved = WarpRes.Text.Trim();
        App.SaveSettings();
    }

    private void OnWarpImportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "WireGuard conf|*.conf;*.ini;*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var text = File.ReadAllText(dlg.FileName);
            if (ProfileUrlParser.TryParseWireGuardConf(text, out var p))
            {
                WarpKey.Text = p.WgPrivateKey;
                WarpAddr.Text = p.WgAddress;
                WarpRes.Text = p.Reserved;
                ViewModel.AddWarpProfile(p);
                ToastWindow.Show(Loc.T("WARP conf imported."));
            }
            else
            {
                MessageBox.Show(Loc.T("Could not read the WireGuard config (missing PrivateKey/PublicKey/Endpoint)."), "Toonel");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Toonel");
        }
    }

    private void OnSubscriptionIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || SubHoursBox?.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Tag as string, out var h)) App.Settings.SubscriptionUpdateHours = h;
        App.SaveSettings();
    }

    private void OnSubUpdateClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.UpdateSubscriptionsCommand.ExecuteAsync(null);
    }

    private void RefreshSubscriptions()
    {
        var subs = App.Settings.Subscriptions;
        SubList.Text = subs.Count == 0
            ? Loc.T("No subscriptions yet.")
            : string.Join("\n", subs.Select(x => SubLine(x)));
    }

    private static string SubLine(SubscriptionInfo x)
    {
        var traffic = x.TotalBytes == -1 && x.UploadBytes == -1
            ? " · "
            : " · " + SubscriptionClient.FormatTraffic(x.DownloadBytes, x.UploadBytes, x.TotalBytes, x.ExpiresUnix);
        return $"{x.Name}{traffic}  ({x.ProfileCount} servers, updated {x.LastUpdated})";
    }

    private void OnPortChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        var s = App.Settings;
        if (int.TryParse(PortMainSocks.Text, out var p1)) s.Ports.MainSocksPort = p1;
        if (int.TryParse(PortMainHttp.Text, out var p2)) s.Ports.MainHttpPort = p2;
        if (int.TryParse(PortVlessOnly.Text, out var p3)) s.Ports.VlessOnlySocksPort = p3;
        if (int.TryParse(PortPsiSocks.Text, out var p4)) s.Ports.PsiphonSocksPort = p4;
        if (int.TryParse(PortPsiHttp.Text, out var p5)) s.Ports.PsiphonHttpPort = p5;
        App.SaveSettings();
    }

    private void OnBehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        App.Settings.AutoConnectOnStart = ChkAutoConnect.IsChecked == true;
        App.Settings.MinimizeToTray = ChkMinToTray.IsChecked == true;
        App.Settings.CompactOnStart = ChkCompactStart.IsChecked == true;
        App.SaveSettings();
    }

    private void OnThemeToggleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_loadingUi) return;
        var theme = App.Settings.Theme == "dark" ? "light" : "dark";
        App.Settings.Theme = theme;
        App.SaveSettings();
        App.ApplyTheme(theme == "dark" ? "Dark" : "Light");
        SyncThemeSwitch();
    }

    /// <summary>Highlights the active half of the Light/Dark theme switch.</summary>
    private void SyncThemeSwitch()
    {
        bool dark = App.Settings.Theme == "dark";
        var activeBg = (System.Windows.Media.Brush)FindResource("Ink");
        var activeFg = (System.Windows.Media.Brush)FindResource("OnInk");
        var idleFg = (System.Windows.Media.Brush)FindResource("Muted");

        ThemeLightPart.Background = dark ? null : activeBg;
        ThemeDarkPart.Background = dark ? activeBg : null;
        ThemeLightText.Foreground = dark ? idleFg : activeFg;
        ThemeDarkText.Foreground = dark ? activeFg : idleFg;
    }

    // ---------------- localization ----------------

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || LangBox?.SelectedItem is not ComboBoxItem item) return;
        var lang = item.Tag as string == "fa" ? AppLanguage.Fa : AppLanguage.En;
        if ((lang == AppLanguage.Fa) == Loc.IsFa) return;
        App.Settings.Language = lang == AppLanguage.Fa ? "fa" : "en";
        App.SaveSettings();
        Loc.Set(lang);
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        FlowDirection = Loc.Flow;

        BrandName.Text = "Toonel";
        BrandTag.Text = Loc.T("XRAY + PSIPHON CLIENT");
        NavDash.Content = Loc.T("Dashboard");
        NavProfiles.Content = Loc.T("Profiles");
        NavSplit.Content = Loc.T("Split Tunnel");
        NavPsiphon.Content = Loc.T("Psiphon");
        NavLogs.Content = Loc.T("Logs");
        NavSettings.Content = Loc.T("Settings");
        SideEnginesLabel.Text = Loc.T("ENGINES");
        SideTun.Text = Loc.T("TUN adapter");

        BtnDisconnect.Content = Loc.T("Disconnect");
        BtnConnect.Content = Loc.T("Connect");
        BtnGoCompact.ToolTip = Loc.T("Compact bar");
        RouteLabel.Text = Loc.T("ROUTE");

        ModeV2rayTitle.Text = Loc.T("V2Ray");
        ModeV2raySub.Text = Loc.T("Your profile only. Fast, simple.");
        ModePsiphonTitle.Text = Loc.T("Psiphon alone");
        ModePsiphonSub.Text = Loc.T("No profile. Everything through Psiphon.");
        ModeAfterTitle.Text = Loc.T("Psiphon after V2Ray");
        ModeAfterSub.Text = Loc.T("Psiphon rides on your VLESS line.");
        ModeBeforeTitle.Text = Loc.T("Psiphon before V2Ray");
        ModeBeforeSub.Text = Loc.T("VLESS rides through Psiphon first.");
        ModeHint.Text = ModeHintFor(ViewModel.Mode);

        DashEnginesLabel.Text = Loc.T("ENGINES");
        EngXray.Text = Loc.T("Xray");
        EngPsiphon.Text = Loc.T("Psiphon");
        EngTun.Text = Loc.T("TUN / proxy");

        ChkTun.Content = Loc.T("Whole system (TUN)");
        ChkSysProxy.Content = Loc.T("System proxy");
        ChkQuic.Content = Loc.T("Block QUIC");

        BtnImportClip.Content = Loc.T("Import from clipboard");
        BtnImportFile.Content = Loc.T("From file…");
        BtnPing.Content = Loc.T("Ping");
        BtnDelete.Content = Loc.T("Delete");
        ProfilesTag.Text = Loc.T("Profiles").ToUpperInvariant();
        ProfilesHint.Text = Loc.T("Paste vless:// · vmess:// · trojan:// · ss:// links or a subscription URL via Import from clipboard. Ctrl+C copies selected profiles (one link per line). Ctrl+V pastes and skips duplicates.");

        SplitLabel.Text = Loc.T("PER-APP ROUTING");
        SplitQuestion.Text = Loc.T("Which apps go through the tunnel?");
        StOff.Content = Loc.T("All apps tunnel");
        StInclude.Content = Loc.T("Only these apps");
        StExclude.Content = Loc.T("Everyone except these");
        AppsLabel.Text = Loc.T("APPS");
        AppsHint.Text = Loc.T("One executable per line, e.g. chrome.exe");
        ExcLabel.Text = Loc.T("EXCEPTIONS");
        ExcHint.Text = Loc.T("Domains, IPs/CIDRs, ports — e.g. bank.ir · 10.0.0.0/8 · 80");
        NeverTunnelLabel.Text = Loc.T("Never tunnel");
        AlwaysTunnelLabel.Text = Loc.T("Always tunnel");
        StBypassPrivate.Content = Loc.T("Bypass LAN and private IPs");

        PsiLabel.Text = Loc.T("PSIPHON");
        PsiQuestion.Text = Loc.T("Network options");
        PsiEgressLabel.Text = Loc.T("Egress country");
        PsiSponsorLabel.Text = Loc.T("Sponsor ID");
        PsiPropLabel.Text = Loc.T("Propagation channel ID");
        PsiRslLabel.Text = Loc.T("Remote server list URL");
        AdvLabel.Text = Loc.T("ADVANCED");
        PsiAdvTitle.Text = Loc.T("Custom config");
        PsiAdvHint.Text = Loc.T("JSON merged into client.config, or a psiphon:// link (region, protocol, sponsor, upstream). Example: psiphon://?region=DE&protocol=QUIC-OSSH. UpstreamProxyURL is managed by chain modes.");

        MenuCopyProfiles.Header = Loc.T("Copy (Ctrl+C)");
        MenuPasteProfiles.Header = Loc.T("Paste (Ctrl+V)");

        AutoScrollBox.Content = Loc.T("Follow");
        BtnClearLog.Content = Loc.T("Clear");

        SetLabel.Text = Loc.T("SETTINGS");
        SetBehaviorTitle.Text = Loc.T("Behavior");
        ChkAutoConnect.Content = Loc.T("Connect automatically at launch");
        ChkMinToTray.Content = Loc.T("Minimize to tray instead of quitting");
        QuitHint.Text = Loc.T("When off, closing the window quits and resets the proxy and TUN.");
        ChkCompactStart.Content = Loc.T("Start with the compact bar");
        BtnSwitchCompact.Content = Loc.T("Switch to compact bar now");
        ThemeLabel.Text = Loc.IsFa ? "پوسته" : "THEME";
        ThemeLightText.Text = Loc.IsFa ? "روشن" : "Light";
        ThemeDarkText.Text = Loc.IsFa ? "تاریک" : "Dark";

        LanguageLabel.Text = Loc.IsFa ? "زبان" : "LANGUAGE";
        DnsLabel.Text = Loc.T("DNS");
        BtnCheckIp.Content = Loc.T("Check IP");
        IpCheckHint.Text = ViewModel.IpCheckText;
        WarpLabel.Text = Loc.T("WARP CHAIN HOP");
        WarpKeyLabel.Text = Loc.T("Private key (base64)");
        WarpAddrLabel.Text = Loc.T("Interface address");
        WarpResLabel.Text = Loc.T("Reserved bytes");
        BtnWarpImport.Content = Loc.T("Import from WireGuard conf…");
        WarpHint.Text = Loc.T("Generate with wgcf (register + generate). Used by the V2Ray-over-WARP mode.");
        SubLabel.Text = Loc.T("SUBSCRIPTIONS");
        BtnSubUpdate.Content = Loc.T("Update all");
        SubHint.Text = Loc.T("Paste a subscription URL via Import from clipboard. Auto-refresh happens on start and every N hours (Settings).");

        PortsLabel.Text = Loc.T("PORTS");
        PortLabel1.Text = Loc.T("Main SOCKS");
        PortLabel2.Text = Loc.T("Main HTTP");
        PortLabel3.Text = Loc.T("Chain hop (anti-loop)");
        PortLabel4.Text = Loc.T("Psiphon SOCKS");
        PortLabel5.Text = Loc.T("Psiphon HTTP");
        EnginesLabel.Text = Loc.T("ENGINES");
        EnginesTitle.Text = Loc.T("Binaries");
        BtnDownloadCores.Content = Loc.T("Download missing");
        BtnRefreshCores.Content = Loc.T("Refresh");
        MaintenanceLabel.Text = Loc.T("MAINTENANCE");
        FlushHint.Text = Loc.T("Stops all engines, resets the system proxy and terminal variables, flushes DNS.");
        BtnFlushNetwork.Content = Loc.T("Flush network");

        TerminalLabel.Text = Loc.T("TERMINAL");
        BtnTerminal.Content = Loc.T("Apply proxy to future terminals");
        BtnRuntime.Content = Loc.T("Open runtime folder");

        Title = "Toonel";
        RefreshTiles();
    }

    // ---------------- compact bar ----------------

    private void OnGoCompactClick(object sender, RoutedEventArgs e)
    {
        var bar = new CompactBar(this);
        bar.Show();
        Hide();
    }

    // ---------------- engine binaries ----------------

    private void RefreshCoreStatus()
    {
        CoreStatusText.Text = App.Downloader.MissingSummary();
    }

    private async void OnDownloadCoresClick(object sender, RoutedEventArgs e)
    {
        BtnDownloadCores.IsEnabled = false;
        CoreStatusText.Text = "downloading…";
        var progress = new Progress<string>(line => CoreStatusText.Text = line);
        try
        {
            await App.Downloader.DownloadMissingAsync(progress);
            App.Log.Add(EngineManager.SourceApp, "Core binaries ready.");
        }
        catch (Exception ex)
        {
            App.Log.Add(EngineManager.SourceApp, $"Core download failed: {ex.Message}");
            MessageBox.Show($"Download failed: {ex.Message}\n\nYou can also place binaries manually into the cores folder (see path below).",
                "Toonel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnDownloadCores.IsEnabled = true;
            RefreshCoreStatus();
        }
    }

    private void OnRefreshCoresClick(object sender, RoutedEventArgs e) => RefreshCoreStatus();

    // ---------------- profiles page ----------------

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
        => ViewModel.DeleteProfileCommand.Execute(ViewModel.SelectedProfile);

    private void OnPingProfileClick(object sender, RoutedEventArgs e)
        => ViewModel.TestProfileDelayCommand.Execute(ViewModel.SelectedProfile);

    private void OnQrProfileClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProfile is not Profile profile)
        {
            ToastWindow.Show(Loc.T("No profile selected."));
            return;
        }
        var qr = new QrWindow(profile) { Owner = this };
        qr.ShowDialog();
    }

    // ---------------- copy / paste profiles ----------------

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        // Window-level shortcut so Ctrl+C / Ctrl+V work no matter which control
        // inside the Profiles page is focused (text boxes, grid, etc.).
        if (PageProfiles.Visibility == Visibility.Visible)
        {
            if (e.Key == System.Windows.Input.Key.C
                && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                CopySelectedProfiles();
                e.Handled = true;
                return;
            }
            if (e.Key == System.Windows.Input.Key.V
                && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                PasteProfiles();
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    private void OnCopyProfilesClick(object sender, RoutedEventArgs e) => CopySelectedProfiles();
    private void OnPasteProfilesClick(object sender, RoutedEventArgs e) => PasteProfiles();

    private void CopySelectedProfiles()
    {
        var selected = ProfileList.SelectedItems.Cast<Profile>().ToList();
        if (selected.Count == 0)
        {
            if (ViewModel.SelectedProfile is Profile single) selected.Add(single);
        }
        if (selected.Count == 0)
        {
            ToastWindow.Show(Loc.T("No profile selected."));
            return;
        }

        var text = ProfileShareLink.BuildMany(selected);
        if (text.Length == 0)
        {
            ToastWindow.Show(Loc.T("Selected profiles cannot be copied."));
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(text);
            ToastWindow.Show(Loc.F("Copied {0} profile(s) to clipboard.", selected.Count));
        }
        catch (Exception ex)
        {
            ToastWindow.Show(Loc.F("Copy failed: {0}", ex.Message));
        }
    }

    private void PasteProfiles()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                ToastWindow.Show(Loc.T("Clipboard is empty."));
                return;
            }
            var text = System.Windows.Clipboard.GetText();
            var parsed = ProfileUrlParser.ParseMany(text);
            if (parsed.Count == 0)
            {
                ToastWindow.Show(Loc.T("No valid profiles found in clipboard."));
                return;
            }

            int added = 0, skipped = 0;
            foreach (var p in parsed)
            {
                if (ViewModel.Profiles.Any(x => x.Address == p.Address && x.Port == p.Port
                        && x.Uuid == p.Uuid && x.Password == p.Password))
                { skipped++; continue; }
                ViewModel.Profiles.Add(p);
                added++;
            }
            App.SaveSettings();
            App.Log.Add(EngineManager.SourceApp,
                $"Pasted {added} profile(s){(skipped > 0 ? $", skipped {skipped} duplicate(s)" : "")}.");
            ToastWindow.Show(Loc.F("Pasted {0} profile(s).{1}", added,
                skipped > 0 ? Loc.F(" Skipped {0} duplicate(s).", skipped) : ""));
        }
        catch (Exception ex)
        {
            ToastWindow.Show(Loc.F("Paste failed: {0}", ex.Message));
        }
    }

    // ---------------- logs page ----------------

    private void OnLogSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogBox is null) return;
        ScrollLogToEnd();
    }

    private void OnClearLogClick(object sender, RoutedEventArgs e)
    {
        App.Log.Clear(ViewModel.SelectedLogSource);
        ViewModel.LogText = "";
    }

    private void ScrollLogToEnd()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (AutoScrollBox.IsChecked == true && LogBox.Text.Length > 0)
            {
                LogBox.CaretIndex = LogBox.Text.Length;
                LogBox.ScrollToEnd();
            }
        }, DispatcherPriority.Background);
    }

    // ---------------- settings actions ----------------

    private void OnOpenRuntimeClick(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start("explorer.exe", PortablePaths.RuntimeDir);
    }

    private void OnApplyTerminalClick(object sender, RoutedEventArgs e)
    {
        var http = App.Settings.Ports.MainHttpPort;
        var socks = App.Settings.Ports.MainSocksPort;
        RunCmd($"setx HTTP_PROXY http://127.0.0.1:{http}");
        RunCmd($"setx HTTPS_PROXY http://127.0.0.1:{http}");
        RunCmd($"setx ALL_PROXY socks5://127.0.0.1:{socks}");
        MessageBox.Show("Set for future terminals (setx). Terminals already open need the paste-in snippet from the runtime folder.", "Toonel");
    }

    private static void RunCmd(string args)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {args}")
            { CreateNoWindow = true });
        }
        catch { /* ignore */ }
    }

    // ---------------- network flush (maintenance) ----------------

    private async void OnFlushNetworkClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            Loc.T("This stops all engines, resets the system proxy, removes the terminal proxy variables and flushes DNS. Continue?"),
            Loc.T("Flush network"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        BtnFlushNetwork.IsEnabled = false;
        try
        {
            // Disconnect first (graceful stop: proxy restore + TUN down + engines exit).
            if (App.Engines.State is EngineState.Connected or EngineState.Starting or EngineState.Error)
            {
                try { await App.Engines.StopAsync(); } catch { /* flush handles the rest */ }
            }

            var report = await System.Threading.Tasks.Task.Run(() =>
                NetworkFlusher.Flush(App.Log, App.Settings.Ports.MainHttpPort));

            ViewModel.PsiphonStatus = "off";
            ViewModel.TunStatus = "off";
            RefreshTiles();

            var summary = report.AllGood
                ? Loc.T("Network flushed successfully.")
                : Loc.F("Network flushed with warnings:\n{0}", string.Join("\n", report.Notes));
            MessageBox.Show(
                summary + "\n\n" + Loc.F("{0} leftover engine process(es) killed.", report.StaleEnginesKilled),
                Loc.T("Flush network"), MessageBoxButton.OK,
                report.AllGood ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            BtnFlushNetwork.IsEnabled = true;
        }
    }
}
