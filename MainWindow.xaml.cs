using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CabSpan.Models;
using CabSpan.Services;

namespace CabSpan;

/// <summary>
/// Phase 3 Desktop GUI for CabSpan multi-monitor rig configuration and mode switching.
/// </summary>
public partial class MainWindow : Window
{
    private const int MAX_SUPPORTED_CAB_MONITORS = 4;
    private static readonly SolidColorBrush Mode4PillBrush    = ThemePalette.GoldPillBgBrush;
    private static readonly SolidColorBrush Mode0PillBrush    = ThemePalette.InactiveCardBgBrush;
    private static readonly SolidColorBrush DarkPillTextBrush  = ThemePalette.GoldLightBrush;
    private static readonly SolidColorBrush AmberPillTextBrush = ThemePalette.TextSecondaryBrush;

    private readonly ProfileStore _profileStore = new();
    private readonly MonitorScanner _monitorScanner = new();
    private readonly SiiGenerator _siiGenerator = new();
    private readonly UpdateCoordinator _updateCoordinator;

    private CabSpanProfile _profile = new();
    private List<MonitorInfo> _monitors = new();
    private bool _isInitializing = true;
    private System.Windows.Threading.DispatcherTimer? _liveGameSpanTimer;

    public MainWindow()
    {
        InitializeComponent();
        _updateCoordinator = new UpdateCoordinator(
            _profileStore,
            () => _profile,
            UpdateNotificationBanner,
            TxtUpdateBannerTitle,
            TxtUpdateBannerNotes,
            BtnUpdateNowBanner,
            BtnAlwaysUpdateBanner,
            BtnSilenceUpdateWeek,
            ChkAutoUpdate,
            TxtStatusBanner);
        LoadInitialState();
        Loaded += OnMainWindowLoadedAsync;
        Closed += (_, _) => _liveGameSpanTimer?.Stop();
    }

    private async void OnMainWindowLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (!_profile.HasCompletedGuidedSetup)
        {
            RunGuidedSetupFlow();
        }

        StartLiveGameSpanWatcher();
        await _updateCoordinator.InitializeAndCheckAsync();
    }

    private void StartLiveGameSpanWatcher()
    {
        _liveGameSpanTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.0)
        };
        _liveGameSpanTimer.Tick += (_, _) => TryAutoSpanRunningGameIfNeeded();
        _liveGameSpanTimer.Start();
    }

    private void TryAutoSpanRunningGameIfNeeded()
    {
        try
        {
            bool multiModeActive =
                (_profile.ApplyToAts && ConfigManager.IsMultiMonitorModeActive(SimulatorGame.AmericanTruckSimulator)) ||
                (_profile.ApplyToEts2 && ConfigManager.IsMultiMonitorModeActive(SimulatorGame.EuroTruckSimulator2));
            if (!multiModeActive)
            {
                return;
            }

            var spanner = new BorderlessSpanner();
            IntPtr hWnd = spanner.FindSimulatorWindowHandle();
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            List<MonitorInfo> liveMonitors = _monitorScanner.ScanConnectedMonitors();
            System.Drawing.Rectangle targetBox = spanner.ResolveProfileBoundingBox(_profile, liveMonitors);
            if (!spanner.IsWindowSpannedToBox(hWnd, targetBox))
            {
                _ = spanner.ApplyBorderlessSpanToWindow(hWnd, targetBox.X, targetBox.Y, targetBox.Width, targetBox.Height);
            }
        }
        catch
        {
            // Never interrupt UI loop on background window check.
        }
    }

    private void RunGuidedSetupFlow()
    {
        var wizard = new GuidedSetupWindow(_profile, _monitors, _monitorScanner, () => _profileStore.Save(_profile))
        {
            Owner = this
        };
        wizard.ShowDialog();
        HydrateControlsFromProfile();
        if (wizard.AppliedMultiMonitorOnFinish)
        {
            OnApplyMultiMonitorClick(this, new RoutedEventArgs());
        }
    }

    private void LoadInitialState()
    {
        _profile = _profileStore.Load();
        ScanAndHydrateMonitors();
        HydrateControlsFromProfile();
    }

    private void HydrateControlsFromProfile()
    {
        _isInitializing = true;
        TxtHeaderVersion.Text = UpdateCheckerService.GetCurrentVersionTag();
        ChkAts.IsChecked = _profile.ApplyToAts;
        ChkEts2.IsChecked = _profile.ApplyToEts2;
        ChkBoth.IsChecked = _profile.ApplyToAts && _profile.ApplyToEts2;
        ChkAutoUpdate.IsChecked = _profile.AutoUpdateEnabled;
        ChkCrashReporting.IsChecked = _profile.EnableCrashReporting;
        SliderBezelGap.Value = Math.Clamp(_profile.BezelGapDegrees, 0.0, 6.0);
        SliderSideAngle.Value = Math.Clamp(_profile.SideMonitorAngleDegrees, 0.0, 45.0);
        UpdateSliderLabels();
        RedrawMonitorMap();
        RefreshLiveGameStatusPills();
        _isInitializing = false;
    }

    private void ScanAndHydrateMonitors()
    {
        _monitors = _monitorScanner.ScanConnectedMonitors();
        var selectedNames = new HashSet<string>(_profile.SelectedMonitorDeviceNames, StringComparer.OrdinalIgnoreCase);
        if (selectedNames.Count > 0)
        {
            foreach (MonitorInfo m in _monitors)
            {
                m.IsSelectedForCab = selectedNames.Contains(m.DeviceName);
                m.IsMainWheelScreen = string.Equals(m.DeviceName, _profile.MainWheelMonitorId, StringComparison.OrdinalIgnoreCase);
            }
        }

        NormalizeMainWheelSelection();
        RedrawMonitorMap();
    }

    private void NormalizeMainWheelSelection()
    {
        List<MonitorInfo> activeCab = _monitors.Where(m => m.IsSelectedForCab).ToList();
        if (activeCab.Count == 0 && _monitors.Count > 0)
        {
            MonitorInfo fallback = _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];
            fallback.IsSelectedForCab = true;
            fallback.IsMainWheelScreen = true;
            activeCab.Add(fallback);
        }

        MonitorInfo? currentMain = activeCab.FirstOrDefault(m => m.IsMainWheelScreen)
            ?? activeCab.FirstOrDefault(m => m.IsPrimary)
            ?? activeCab.FirstOrDefault();

        foreach (MonitorInfo m in _monitors) m.IsMainWheelScreen = ReferenceEquals(m, currentMain);
    }

    private void RedrawMonitorMap()
    {
        if (MonitorCanvas is null) return;
        MonitorCanvasRenderer.RenderMonitors(MonitorCanvas, _monitors, OnToggleMonitorRole, OnSetMainWheelMonitor);
        int activeCount = _monitors.Count(m => m.IsSelectedForCab);
        string mainLabel = _monitors.FirstOrDefault(m => m.IsMainWheelScreen)?.FriendlyName ?? "None";
        TxtMonitorSummary.Text = $"Your Current Selection: {activeCount} of {_monitors.Count} screen(s) turned ON for driving  •  Steering Wheel & Menus on: {mainLabel}";
    }

    private void OnToggleMonitorRole(MonitorInfo clickedMonitor)
    {
        int currentSelected = _monitors.Count(m => m.IsSelectedForCab);
        if (!clickedMonitor.IsSelectedForCab)
        {
            if (currentSelected >= MAX_SUPPORTED_CAB_MONITORS)
            {
                TxtStatusBanner.Text = $"ℹ️ The game supports up to {MAX_SUPPORTED_CAB_MONITORS} driving screens at once. Click another blue screen to turn it off first.";
                return;
            }

            clickedMonitor.IsSelectedForCab = true;
        }
        else
        {
            if (currentSelected <= 1)
            {
                TxtStatusBanner.Text = "ℹ️ At least 1 screen must stay ON for driving so the game has somewhere to show your truck.";
                return;
            }

            clickedMonitor.IsSelectedForCab = false;
            clickedMonitor.IsMainWheelScreen = false;
        }

        NormalizeMainWheelSelection();
        PersistProfileChanges();
        RedrawMonitorMap();
    }

    private void OnSetMainWheelMonitor(MonitorInfo targetMonitor)
    {
        targetMonitor.IsSelectedForCab = true;
        foreach (MonitorInfo m in _monitors)
        {
            m.IsMainWheelScreen = ReferenceEquals(m, targetMonitor);
        }

        PersistProfileChanges();
        RedrawMonitorMap();
        TxtStatusBanner.Text = $"🛞 Steering Wheel & Game Menus centered on {targetMonitor.FriendlyName} ({targetMonitor.Width}×{targetMonitor.Height}). Remember to click [⚡ Apply Multi-Screen Setup] in Step 4!";
    }

    private void OnGameTargetChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _profile.ApplyToAts = ChkAts.IsChecked == true;
        _profile.ApplyToEts2 = ChkEts2.IsChecked == true;
        ChkBoth.IsChecked = _profile.ApplyToAts && _profile.ApplyToEts2;
        PersistProfileChanges();
        RefreshLiveGameStatusPills();
    }

    private void OnBothCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _isInitializing = true;
        bool enableBoth = ChkBoth.IsChecked == true;
        ChkAts.IsChecked = enableBoth;
        ChkEts2.IsChecked = enableBoth;
        _profile.ApplyToAts = enableBoth;
        _profile.ApplyToEts2 = enableBoth;
        _isInitializing = false;
        PersistProfileChanges();
        RefreshLiveGameStatusPills();
    }

    private void OnGeometrySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        _profile.BezelGapDegrees = Math.Round(SliderBezelGap.Value, 1);
        _profile.SideMonitorAngleDegrees = Math.Round(SliderSideAngle.Value, 1);
        UpdateSliderLabels();
        PersistProfileChanges();
    }

    private void UpdateSliderLabels()
    {
        if (TxtBezelGapValue is not null)
        {
            string gapDesc = SliderBezelGap.Value <= 0.1 ? "No Border" : SliderBezelGap.Value <= 3.2 ? "Standard Frame" : "Thick Frame";
            TxtBezelGapValue.Text = $"{SliderBezelGap.Value.ToString("F1", CultureInfo.InvariantCulture)}° ({gapDesc})";
        }

        if (TxtSideAngleValue is not null)
        {
            string desc = SliderSideAngle.Value <= 0.1 ? "Flat Line" : "Angled Toward Seat";
            TxtSideAngleValue.Text = $"{SliderSideAngle.Value.ToString("F1", CultureInfo.InvariantCulture)}° ({desc})";
        }
    }

    private void OnApplyMultiMonitorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistProfileChanges();
            int screenCount = _monitors.Count(m => m.IsSelectedForCab);
            SiiGenerationResult sii = _siiGenerator.Generate(
                _monitors,
                _profile.BezelGapDegrees,
                _profile.SideMonitorAngleDegrees,
                _profile.PitchOffsetDegrees);
            var configManager = new ConfigManager(EnsureTargetGameDirectories());
            configManager.WriteMultimonSii(sii.SiiContent);
            configManager.ApplyMultiMonitorMode(sii.TotalCabWidth, sii.TotalCabHeight, borderlessWindowed: screenCount > 1);

            var spanner = new BorderlessSpanner();
            bool gameRunning = spanner.FindSimulatorWindowHandle() != IntPtr.Zero;
            if (gameRunning)
            {
                _ = spanner.TrySpanSimulatorWindow(_profile, _monitors);
            }

            RefreshLiveGameStatusPills();
            string runningNote = gameRunning
                ? " (Note: Restart your game so the new camera angles load!)"
                : " You can close CabSpan and start your game — you only need to reopen CabSpan when you want to switch back to 1 screen!";
            TxtStatusBanner.Text = $"✅ Saved permanently! Your game is set to stretch across {screenCount} screen(s) ({sii.TotalCabWidth}×{sii.TotalCabHeight}).{runningNote}";
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, nameof(OnApplyMultiMonitorClick), isFatal: false, _profile);
            TxtStatusBanner.Text = $"⚠️ Could not save Multi-Screen setup: {ex.Message}";
        }
    }

    private void OnSwitchSingleMonitorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistProfileChanges();
            var configManager = new ConfigManager(EnsureTargetGameDirectories());
            configManager.ApplySingleMonitorFallback(_profile.SingleMonitorWidth);

            var spanner = new BorderlessSpanner();
            IntPtr hWnd = spanner.FindSimulatorWindowHandle();
            bool gameRunning = hWnd != IntPtr.Zero;
            if (gameRunning)
            {
                MonitorInfo? mainMon = _monitors.FirstOrDefault(m => m.IsMainWheelScreen) ?? _monitors.FirstOrDefault(m => m.IsPrimary);
                if (mainMon is not null)
                {
                    _ = spanner.ApplyBorderlessSpanToWindow(hWnd, mainMon.X, mainMon.Y, mainMon.Width, mainMon.Height);
                }
            }

            RefreshLiveGameStatusPills();
            string runningNote = gameRunning
                ? " (Note: Restart your game so normal 1-screen camera mode reloads!)"
                : " You can close CabSpan and start your game — your other screens will stay free for your desktop!";
            TxtStatusBanner.Text = $"🖥️ Saved permanently! Your game is back in normal 1-Screen mode ({_profile.SingleMonitorWidth}px).{runningNote}";
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, nameof(OnSwitchSingleMonitorClick), isFatal: false, _profile);
            TxtStatusBanner.Text = $"⚠️ Could not switch to 1-Screen mode: {ex.Message}";
        }
    }

    private void OnCopySteamLaunchOptionClick(object sender, RoutedEventArgs e)
    {
        string launchOption = QuickStartWindow.BuildSteamLaunchCommand();
        System.Windows.Clipboard.SetText(launchOption);
        TxtStatusBanner.Text = "📋 Copied Steam command to your clipboard! Follow the 3 easy steps in the popup guide to paste it into Steam.";
        new QuickStartWindow(focusSteamSection: true) { Owner = this }.ShowDialog();
    }

    private void OnRedoGuidedSetupClick(object sender, RoutedEventArgs e) => RunGuidedSetupFlow();

    private void OnHowItWorksClick(object sender, RoutedEventArgs e) =>
        new QuickStartWindow(focusSteamSection: false) { Owner = this }.ShowDialog();

    private void OnRestoreBackupClick(object sender, RoutedEventArgs e)
    {
        var configManager = new ConfigManager(EnsureTargetGameDirectories());
        int count = configManager.RestoreOriginalBackup();
        RefreshLiveGameStatusPills();
        TxtStatusBanner.Text = count > 0
            ? $"↺ Restored your original game settings across {count} game folder(s)."
            : "ℹ️ Your game settings are already at their original defaults.";
    }

    private void OnRescanMonitorsClick(object sender, RoutedEventArgs e)
    {
        ScanAndHydrateMonitors();
        TxtStatusBanner.Text = $"🔄 Refreshed screens: found {_monitors.Count} connected monitor(s).";
    }

    private void OnMonitorCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RedrawMonitorMap();

    private async void OnUpdateNowBannerClick(object sender, RoutedEventArgs e) =>
        await _updateCoordinator.InstallUpdateNowAsync(enableAlwaysAutoUpdate: false);

    private async void OnAlwaysUpdateBannerClick(object sender, RoutedEventArgs e) =>
        await _updateCoordinator.InstallUpdateNowAsync(enableAlwaysAutoUpdate: true);

    private void OnSilenceUpdateWeekClick(object sender, RoutedEventArgs e) =>
        _updateCoordinator.SilenceForOneWeek();

    private void OnDismissUpdateBannerClick(object sender, RoutedEventArgs e) =>
        _updateCoordinator.DismissForSession();

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e) =>
        await _updateCoordinator.CheckManuallyAsync();

    private void OnAutoUpdateToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _profile.AutoUpdateEnabled = ChkAutoUpdate.IsChecked == true;
        if (_profile.AutoUpdateEnabled) _profile.UpdateSnoozedUntilUtc = null;
        PersistProfileChanges();
        TxtStatusBanner.Text = _profile.AutoUpdateEnabled
            ? "🔄 Automatic updates enabled."
            : "ℹ️ Automatic updates disabled — CabSpan will notify you when a new version is available.";
    }

    private void OnCrashReportingToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _profile.EnableCrashReporting = ChkCrashReporting.IsChecked == true;
        PersistProfileChanges();
    }

    private void OnSuggestFeatureClick(object sender, RoutedEventArgs e) =>
        new BugReportWindow(_profile, _monitors, startInSuggestionMode: true) { Owner = this }.ShowDialog();

    private void OnReportBugClick(object sender, RoutedEventArgs e) =>
        new BugReportWindow(_profile, _monitors, startInSuggestionMode: false) { Owner = this }.ShowDialog();

    private void OnOpenErrorLogClick(object sender, RoutedEventArgs e)
    {
        string logDir = ErrorLogger.GetDefaultLogDirectory();
        Directory.CreateDirectory(logDir);
        string logFile = ErrorLogger.GetDefaultLogFilePath();
        Process.Start(new ProcessStartInfo(File.Exists(logFile) ? logFile : logDir) { UseShellExecute = true });
    }

    private void OnExternalLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void RefreshLiveGameStatusPills()
    {
        bool atsMulti = ConfigManager.IsMultiMonitorModeActive(SimulatorGame.AmericanTruckSimulator);
        bool ets2Multi = ConfigManager.IsMultiMonitorModeActive(SimulatorGame.EuroTruckSimulator2);

        UpdateSingleGameStatusPill(SimulatorGame.AmericanTruckSimulator, AtsStatusPill, TxtAtsStatus);
        UpdateSingleGameStatusPill(SimulatorGame.EuroTruckSimulator2, Ets2StatusPill, TxtEts2Status);

        bool isMultiActive =
            (_profile.ApplyToAts && atsMulti) ||
            (_profile.ApplyToEts2 && ets2Multi) ||
            (!_profile.ApplyToAts && !_profile.ApplyToEts2 && (atsMulti || ets2Multi));

        if (BtnApplyMultiMonitor is not null && BtnSwitchSingleMonitor is not null)
        {
            var activeGoldStyle = (Style)FindResource("PrimaryCyanButtonStyle");
            var inactiveDarkStyle = (Style)FindResource("AmberActionButtonStyle");

            BtnApplyMultiMonitor.Style = isMultiActive ? activeGoldStyle : inactiveDarkStyle;
            BtnSwitchSingleMonitor.Style = isMultiActive ? inactiveDarkStyle : activeGoldStyle;

            if (TxtBtnMultiTitle is not null)
            {
                TxtBtnMultiTitle.Text = isMultiActive
                    ? "⚡ Multi-Screen Setup (✓ Active)"
                    : "⚡ Apply Multi-Screen Setup";
            }

            if (TxtBtnSingleTitle is not null)
            {
                TxtBtnSingleTitle.Text = isMultiActive
                    ? "🖥️ Switch Back to 1 Screen"
                    : "🖥️ 1-Screen Mode (✓ Active)";
            }
        }
    }

    private static void UpdateSingleGameStatusPill(SimulatorGame game, Border pill, TextBlock label)
    {
        bool isMode4 = ConfigManager.IsMultiMonitorModeActive(game);
        label.Text = ConfigManager.GetGameModeStatusText(game);
        pill.Background = isMode4 ? Mode4PillBrush : Mode0PillBrush;
        label.Foreground = isMode4 ? DarkPillTextBrush : AmberPillTextBrush;
    }

    private IReadOnlyList<string> EnsureTargetGameDirectories()
    {
        IReadOnlyList<string> requestedDirs = _profile.GetEnabledGameDirectories();
        List<string> existingDirs = requestedDirs.Where(Directory.Exists).ToList();
        if (existingDirs.Count > 0)
        {
            return existingDirs;
        }

        string fallbackDir = requestedDirs.FirstOrDefault()
            ?? ConfigManager.GetGameDirectory(SimulatorGame.AmericanTruckSimulator);
        Directory.CreateDirectory(fallbackDir);
        return new[] { fallbackDir };
    }

    private void PersistProfileChanges()
    {
        ProfileStore.SyncMonitorsIntoProfile(_profile, _monitors);
        _profileStore.Save(_profile);
    }
}
