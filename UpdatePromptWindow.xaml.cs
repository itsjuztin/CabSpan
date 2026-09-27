using System.Windows;
using System.Windows.Threading;
using CabSpan.Models;
using CabSpan.Services;

namespace CabSpan;

/// <summary>
/// Lightweight dark-themed notification dialog displayed when an update is ready during Steam --auto-launch.
/// </summary>
public partial class UpdatePromptWindow : Window
{
    private const int AUTO_UPDATE_COUNTDOWN_SECONDS = 5;

    private readonly UpdateReleaseInfo _release;
    private readonly CabSpanProfile _profile;
    private readonly ProfileStore _profileStore;
    private readonly bool _isAutoLaunchContext;
    private readonly SelfUpdateInstaller _installer = new();
    private DispatcherTimer? _autoCountdownTimer;
    private int _secondsRemaining = AUTO_UPDATE_COUNTDOWN_SECONDS;
    private bool _isDownloading;

    public UpdatePromptWindow(
        UpdateReleaseInfo release,
        CabSpanProfile profile,
        ProfileStore profileStore,
        bool isAutoLaunchContext = true)
    {
        InitializeComponent();
        _release = release;
        _profile = profile;
        _profileStore = profileStore;
        _isAutoLaunchContext = isAutoLaunchContext;

        TxtVersionBadge.Text = $"{UpdateCheckerService.GetCurrentVersionTag()} → {release.TagName}";
        TxtReleaseTitle.Text = string.IsNullOrWhiteSpace(release.ReleaseTitle)
            ? $"New Update Available ({release.TagName})"
            : release.ReleaseTitle;
        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(release.ReleaseNotes)
            ? "Includes the latest multi-monitor fixes and stability improvements."
            : release.ReleaseNotes.Trim();

        BtnUpdateNow.Content = isAutoLaunchContext ? "⚡ Update & Launch Game" : "⚡ Update & Restart Now";

        if (_profile.AutoUpdateEnabled)
        {
            StartAutoInstallCountdown();
        }
    }

    private void StartAutoInstallCountdown()
    {
        _secondsRemaining = AUTO_UPDATE_COUNTDOWN_SECONDS;
        TxtProgressStatus.Text = $"🔄 Auto-Update is ON — installing {_release.TagName} in {_secondsRemaining}s... (Click [Silence for 1 Week] to skip)";
        _autoCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoCountdownTimer.Tick += async (_, _) =>
        {
            _secondsRemaining--;
            if (_secondsRemaining > 0)
            {
                TxtProgressStatus.Text = $"🔄 Auto-Update is ON — installing {_release.TagName} in {_secondsRemaining}s... (Click [Silence for 1 Week] to skip)";
                return;
            }

            _autoCountdownTimer.Stop();
            await PerformDownloadAndSwapAsync();
        };
        _autoCountdownTimer.Start();
    }

    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        await PerformDownloadAndSwapAsync();
    }

    private async void OnAlwaysUpdateClick(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        _profile.AutoUpdateEnabled = true;
        _profile.UpdateSnoozedUntilUtc = null;
        _profileStore.Save(_profile);
        await PerformDownloadAndSwapAsync();
    }

    private void OnSilenceWeekClick(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        _profile.SnoozeUpdateForOneWeek(DateTime.UtcNow);
        _profileStore.Save(_profile);
        DialogResult = false;
        Close();
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        DialogResult = false;
        Close();
    }

    private async Task PerformDownloadAndSwapAsync()
    {
        if (_isDownloading) return;
        _isDownloading = true;
        SetButtonsEnabled(false);

        try
        {
            var progress = new Progress<int>(pct =>
            {
                TxtProgressStatus.Text = $"⬇️ Downloading & extracting {_release.TagName}... {pct}%";
            });

            string stagedPath = await _installer.DownloadAndStageBinaryAsync(_release, progress);
            TxtProgressStatus.Text = "✅ Applying update in-place...";

            if (_isAutoLaunchContext)
            {
                string currentExe = SelfUpdateInstaller.ResolveCurrentExecutablePath();
                SelfUpdateInstaller.PerformAtomicSwap(currentExe, stagedPath);
                DialogResult = true;
                Close();
                return;
            }

            _installer.ApplyStagedUpdateAndRestart(stagedPath, _release.TagName, restartGui: true);
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, "UpdatePromptWindow.PerformDownloadAndSwapAsync", isFatal: false, _profile);
            TxtProgressStatus.Text = $"⚠️ Update failed: {ex.Message}";
            SetButtonsEnabled(true);
            _isDownloading = false;
        }
    }

    private void StopCountdown() => _autoCountdownTimer?.Stop();

    private void SetButtonsEnabled(bool enabled)
    {
        BtnUpdateNow.IsEnabled = enabled;
        BtnAlwaysUpdate.IsEnabled = enabled;
        BtnSilenceWeek.IsEnabled = enabled;
    }
}
