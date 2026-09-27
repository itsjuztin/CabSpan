using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CabSpan.Models;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace CabSpan.Services;

/// <summary>
/// Coordinates the MainWindow in-app update notification banner, 5-second auto-update countdown,
/// 1-click installation, and 7-day notification snooze ("Silence for 1 Week").
/// </summary>
public sealed class UpdateCoordinator
{
    private const int AUTO_INSTALL_GRACE_SECONDS = 5;

    private readonly ProfileStore _profileStore;
    private readonly Func<CabSpanProfile> _getProfile;
    private readonly Border _bannerBorder;
    private readonly TextBlock _txtTitle;
    private readonly TextBlock _txtNotes;
    private readonly Button _btnUpdateNow;
    private readonly Button _btnAlwaysUpdate;
    private readonly Button _btnSilenceWeek;
    private readonly CheckBox _chkAutoUpdate;
    private readonly TextBlock _txtStatusBanner;

    private readonly UpdateCheckerService _checker = new();
    private readonly SelfUpdateInstaller _installer = new();
    private UpdateReleaseInfo? _pendingRelease;
    private DispatcherTimer? _countdownTimer;
    private int _countdownSeconds;
    private bool _isInstalling;

    public UpdateCoordinator(
        ProfileStore profileStore,
        Func<CabSpanProfile> getProfile,
        Border bannerBorder,
        TextBlock txtTitle,
        TextBlock txtNotes,
        Button btnUpdateNow,
        Button btnAlwaysUpdate,
        Button btnSilenceWeek,
        CheckBox chkAutoUpdate,
        TextBlock txtStatusBanner)
    {
        _profileStore = profileStore;
        _getProfile = getProfile;
        _bannerBorder = bannerBorder;
        _txtTitle = txtTitle;
        _txtNotes = txtNotes;
        _btnUpdateNow = btnUpdateNow;
        _btnAlwaysUpdate = btnAlwaysUpdate;
        _btnSilenceWeek = btnSilenceWeek;
        _chkAutoUpdate = chkAutoUpdate;
        _txtStatusBanner = txtStatusBanner;
    }

    /// <summary>
    /// Runs on MainWindow startup: cleans up old update backups, greets after a restart update, and checks GitHub Releases.
    /// </summary>
    public async Task InitializeAndCheckAsync()
    {
        SelfUpdateInstaller.CleanupPreviousBackup();
        ShowPostUpdateGreetingIfApplicable();

        UpdateReleaseInfo? release = await _checker.CheckForUpdateAsync();
        if (release is null)
        {
            return;
        }

        CabSpanProfile profile = _getProfile();
        if (profile.IsUpdateNotificationSnoozed(DateTime.UtcNow))
        {
            return;
        }

        ShowUpdateBanner(release, profile);
    }

    /// <summary>
    /// Manually checks for updates (ignoring any active 7-day snooze window when explicitly clicked by the user).
    /// </summary>
    public async Task CheckManuallyAsync()
    {
        _txtStatusBanner.Text = "🔄 Checking GitHub Releases for CabSpan updates...";
        UpdateReleaseInfo? release = await _checker.CheckForUpdateAsync();

        if (release is null)
        {
            _txtStatusBanner.Text = $"✅ CabSpan {UpdateCheckerService.GetCurrentVersionTag()} is up to date!";
            return;
        }

        CabSpanProfile profile = _getProfile();
        profile.UpdateSnoozedUntilUtc = null;
        _profileStore.Save(profile);
        ShowUpdateBanner(release, profile);
    }

    public async Task InstallUpdateNowAsync(bool enableAlwaysAutoUpdate)
    {
        StopCountdown();
        if (_pendingRelease is null || _isInstalling)
        {
            return;
        }

        CabSpanProfile profile = _getProfile();
        if (enableAlwaysAutoUpdate)
        {
            profile.AutoUpdateEnabled = true;
            profile.UpdateSnoozedUntilUtc = null;
            _chkAutoUpdate.IsChecked = true;
            _profileStore.Save(profile);
        }

        _isInstalling = true;
        SetBannerButtonsEnabled(false);

        try
        {
            var progress = new Progress<int>(pct =>
            {
                _txtTitle.Text = $"⬇️ Downloading {_pendingRelease.TagName}... ({pct}%)";
                _txtStatusBanner.Text = $"⬇️ Downloading & extracting CabSpan {_pendingRelease.TagName} ({pct}%)...";
            });

            string stagedPath = await _installer.DownloadAndStageBinaryAsync(_pendingRelease, progress);
            _txtTitle.Text = $"✅ Installing {_pendingRelease.TagName} & Restarting...";
            _installer.ApplyStagedUpdateAndRestart(stagedPath, _pendingRelease.TagName, restartGui: true);
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, "UpdateCoordinator.InstallUpdateNowAsync", isFatal: false, profile);
            _txtTitle.Text = $"⚠️ Update to {_pendingRelease.TagName} failed";
            _txtNotes.Text = ex.Message;
            _txtStatusBanner.Text = $"⚠️ Could not install update: {ex.Message}";
            SetBannerButtonsEnabled(true);
            _isInstalling = false;
        }
    }

    public void SilenceForOneWeek()
    {
        StopCountdown();
        CabSpanProfile profile = _getProfile();
        profile.SnoozeUpdateForOneWeek(DateTime.UtcNow);
        _profileStore.Save(profile);

        _bannerBorder.Visibility = Visibility.Collapsed;
        string untilDate = profile.UpdateSnoozedUntilUtc?.ToLocalTime().ToString("MMM d") ?? "7 days";
        _txtStatusBanner.Text = $"🔕 Update notifications silenced for 1 week (until {untilDate}).";
    }

    public void DismissForSession()
    {
        StopCountdown();
        _bannerBorder.Visibility = Visibility.Collapsed;
    }

    private void ShowUpdateBanner(UpdateReleaseInfo release, CabSpanProfile profile)
    {
        _pendingRelease = release;
        _txtTitle.Text = $"🚀 New Update Ready: CabSpan {release.TagName} (Current: {UpdateCheckerService.GetCurrentVersionTag()})";
        _txtNotes.Text = release.GetCompactNotesSummary();
        _btnAlwaysUpdate.Visibility = profile.AutoUpdateEnabled ? Visibility.Collapsed : Visibility.Visible;
        _bannerBorder.Visibility = Visibility.Visible;

        if (profile.AutoUpdateEnabled)
        {
            StartAutoUpdateCountdown(release);
        }
        else
        {
            _txtStatusBanner.Text = $"🚀 Update {release.TagName} is available! Click [Update & Restart Now], [Always Update], or [Silence for 1 Week] above.";
        }
    }

    private void StartAutoUpdateCountdown(UpdateReleaseInfo release)
    {
        StopCountdown();
        _countdownSeconds = AUTO_INSTALL_GRACE_SECONDS;
        _txtTitle.Text = $"🔄 Auto-Updating to {release.TagName} in {_countdownSeconds}s... (Click [Silence for 1 Week] to cancel)";

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += async (_, _) =>
        {
            _countdownSeconds--;
            if (_countdownSeconds > 0)
            {
                _txtTitle.Text = $"🔄 Auto-Updating to {release.TagName} in {_countdownSeconds}s... (Click [Silence for 1 Week] to cancel)";
                return;
            }

            StopCountdown();
            await InstallUpdateNowAsync(enableAlwaysAutoUpdate: false);
        };
        _countdownTimer.Start();
    }

    private void ShowPostUpdateGreetingIfApplicable()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], SelfUpdateInstaller.JUST_UPDATED_FLAG, StringComparison.OrdinalIgnoreCase))
            {
                _txtStatusBanner.Text = $"🎉 Successfully updated to CabSpan {args[i + 1]}! No manual extraction required.";
                return;
            }
        }
    }

    private void StopCountdown() => _countdownTimer?.Stop();

    private void SetBannerButtonsEnabled(bool enabled)
    {
        _btnUpdateNow.IsEnabled = enabled;
        _btnAlwaysUpdate.IsEnabled = enabled;
        _btnSilenceWeek.IsEnabled = enabled;
    }
}
