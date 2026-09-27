using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CabSpan.Models;
using CabSpan.Services;

namespace CabSpan;

/// <summary>
/// First-launch step-by-step guided setup wizard that walks users through Steps 1–4 and saves to CabSpanProfile.
/// </summary>
public partial class GuidedSetupWindow : Window
{
    private const int MAX_SUPPORTED_CAB_MONITORS = 4;
    private readonly CabSpanProfile _profile;
    private readonly List<MonitorInfo> _monitors;
    private readonly MonitorScanner _scanner;
    private readonly Action _onSetupApplied;
    private int _currentStep = 1;
    private bool _isInitializing = true;

    public bool AppliedMultiMonitorOnFinish { get; private set; }

    public GuidedSetupWindow(
        CabSpanProfile profile,
        List<MonitorInfo> monitors,
        MonitorScanner scanner,
        Action onSetupApplied)
    {
        InitializeComponent();
        _profile = profile;
        _monitors = monitors;
        _scanner = scanner;
        _onSetupApplied = onSetupApplied;

        WizChkAts.IsChecked = _profile.ApplyToAts;
        WizChkEts2.IsChecked = _profile.ApplyToEts2;
        WizSliderBezel.Value = Math.Clamp(_profile.BezelGapDegrees, 0.0, 6.0);
        WizSliderAngle.Value = Math.Clamp(_profile.SideMonitorAngleDegrees, 0.0, 45.0);
        UpdateSliderLabels();
        _isInitializing = false;
        RenderStep();
    }

    private void RenderStep()
    {
        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        BtnBack.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Hidden;
        TxtStepCounter.Text = $"  •  STEP {_currentStep} OF 4";

        switch (_currentStep)
        {
            case 1:
                TxtStepTitle.Text = "Step 1: Which Truck Simulator do you want to set up?";
                BtnNextOrFinish.Content = "Next: Pick Your Screens ➔";
                break;
            case 2:
                TxtStepTitle.Text = "Step 2: Click the screens you want to drive on";
                BtnNextOrFinish.Content = "Next: Screen Borders & Angle ➔";
                RedrawWizardCanvas();
                break;
            case 3:
                TxtStepTitle.Text = "Step 3: Match your monitor borders and desk angle";
                BtnNextOrFinish.Content = "Next: Review & Save ➔";
                break;
            case 4:
                TxtStepTitle.Text = "Step 4: Ready to Save & Apply to Your Game!";
                BtnNextOrFinish.Content = "⚡ Apply Multi-Screen Setup & Finish";
                PopulateSummary();
                break;
        }
    }

    private void RedrawWizardCanvas()
    {
        if (WizMonitorCanvas is null) return;
        MonitorCanvasRenderer.RenderMonitors(WizMonitorCanvas, _monitors, OnToggleMonitor, OnSetMainWheel);
    }

    private void OnToggleMonitor(MonitorInfo clicked)
    {
        int currentSelected = _monitors.Count(m => m.IsSelectedForCab);
        if (!clicked.IsSelectedForCab)
        {
            if (currentSelected >= MAX_SUPPORTED_CAB_MONITORS) return;
            clicked.IsSelectedForCab = true;
        }
        else
        {
            if (currentSelected <= 1) return;
            clicked.IsSelectedForCab = false;
            clicked.IsMainWheelScreen = false;
        }

        EnsureValidWheelMonitor();
        RedrawWizardCanvas();
    }

    private void OnSetMainWheel(MonitorInfo target)
    {
        target.IsSelectedForCab = true;
        foreach (MonitorInfo m in _monitors) m.IsMainWheelScreen = ReferenceEquals(m, target);
        RedrawWizardCanvas();
    }

    private void EnsureValidWheelMonitor()
    {
        List<MonitorInfo> active = _monitors.Where(m => m.IsSelectedForCab).ToList();
        MonitorInfo? main = active.FirstOrDefault(m => m.IsMainWheelScreen) ?? active.FirstOrDefault();
        foreach (MonitorInfo m in _monitors) m.IsMainWheelScreen = ReferenceEquals(m, main);
    }

    private void OnWizRescanClick(object sender, RoutedEventArgs e)
    {
        List<MonitorInfo> fresh = _scanner.ScanConnectedMonitors();
        _monitors.Clear();
        _monitors.AddRange(fresh);
        EnsureValidWheelMonitor();
        RedrawWizardCanvas();
    }

    private void OnWizCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RedrawWizardCanvas();

    private void OnBezelPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double v))
            WizSliderBezel.Value = v;
    }

    private void OnAnglePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, CultureInfo.InvariantCulture, out double v))
            WizSliderAngle.Value = v;
    }

    private void OnWizSlidersChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        if (WizTxtBezelVal is not null)
        {
            string gapDesc = WizSliderBezel.Value <= 0.1 ? "No Gap" : WizSliderBezel.Value <= 3.2 ? "Standard Frame" : "Thick Frame";
            WizTxtBezelVal.Text = $"{WizSliderBezel.Value.ToString("F1", CultureInfo.InvariantCulture)}° ({gapDesc})";
        }

        if (WizTxtAngleVal is not null)
            WizTxtAngleVal.Text = $"{WizSliderAngle.Value.ToString("F1", CultureInfo.InvariantCulture)}° ({(WizSliderAngle.Value <= 0.1 ? "Flat Line" : "Angled")})";
    }

    private void PopulateSummary()
    {
        SyncStateIntoProfile();
        var games = new List<string>();
        if (_profile.ApplyToAts) games.Add("American Truck Simulator");
        if (_profile.ApplyToEts2) games.Add("Euro Truck Simulator 2");
        string gamesStr = games.Count > 0 ? string.Join(" & ", games) : "American Truck Simulator";

        int activeCount = _monitors.Count(m => m.IsSelectedForCab);
        string wheelName = _monitors.FirstOrDefault(m => m.IsMainWheelScreen)?.FriendlyName ?? "Main Screen";

        WizTxtSummary.Text =
            $"• Games to Set Up:  {gamesStr}\n" +
            $"• Active Driving Screens:  {activeCount} of {_monitors.Count} connected monitor(s)\n" +
            $"• Steering Wheel & Menus Centered On:  {wheelName}\n" +
            $"• Plastic Border Gap:  {_profile.BezelGapDegrees:F1}°\n" +
            $"• Side Monitor Angle:  {_profile.SideMonitorAngleDegrees:F1}°";
    }

    private void SyncStateIntoProfile()
    {
        _profile.ApplyToAts = WizChkAts.IsChecked == true;
        _profile.ApplyToEts2 = WizChkEts2.IsChecked == true;
        if (!_profile.ApplyToAts && !_profile.ApplyToEts2) _profile.ApplyToAts = true;
        _profile.BezelGapDegrees = Math.Round(WizSliderBezel.Value, 1);
        _profile.SideMonitorAngleDegrees = Math.Round(WizSliderAngle.Value, 1);
        ProfileStore.SyncMonitorsIntoProfile(_profile, _monitors);
    }

    private void OnCopySteamInWizardClick(object sender, RoutedEventArgs e)
    {
        string cmd = QuickStartWindow.BuildSteamLaunchCommand();
        System.Windows.Clipboard.SetText(cmd);
        WizTxtSteamStatus.Text = "✓ Copied! Paste into Steam -> Right-click Game -> Properties -> Launch Options.";
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            RenderStep();
        }
    }

    private void OnNextOrFinishClick(object sender, RoutedEventArgs e)
    {
        SyncStateIntoProfile();
        if (_currentStep < 4)
        {
            _currentStep++;
            RenderStep();
            return;
        }

        _profile.HasCompletedGuidedSetup = true;
        AppliedMultiMonitorOnFinish = true;
        _onSetupApplied();
        Close();
    }

    private void OnSkipWizardClick(object sender, RoutedEventArgs e)
    {
        SyncStateIntoProfile();
        _profile.HasCompletedGuidedSetup = true;
        AppliedMultiMonitorOnFinish = false;
        _onSetupApplied();
        Close();
    }
}
