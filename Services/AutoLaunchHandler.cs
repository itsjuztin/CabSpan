using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Pre-launch CLI watchdog invoked via `CabSpan.exe --auto-launch` in Steam Launch Options.
/// Detects whether AMD Eyefinity / NVIDIA Surround is active and updates config.cfg before launching the simulator.
/// </summary>
public sealed class AutoLaunchHandler
{
    public const string AUTO_LAUNCH_FLAG = "--auto-launch";
    public const double SURROUND_THRESHOLD_RATIO = 0.9;

    private readonly ProfileStore _profileStore;

    public AutoLaunchHandler(ProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new ProfileStore();
    }

    /// <summary>
    /// Executes the pre-launch watchdog check using the current primary screen width and launches any forwarded game command.
    /// </summary>
    public int Execute(IReadOnlyList<string> cliArgs, string? documentsRoot = null)
    {
        ArgumentNullException.ThrowIfNull(cliArgs);

        CabSpanProfile profile = _profileStore.Load();
        if (string.IsNullOrWhiteSpace(documentsRoot))
        {
            TryPromptUpdateBeforeGameLaunch(profile);
        }

        IReadOnlyList<string> gameDirectories = profile.GetEnabledGameDirectories(documentsRoot);
        var configManager = new ConfigManager(gameDirectories);

        int activeDisplayWidth = ResolveActiveCabDisplayWidth(profile, documentsRoot);
        bool multiMonitorActive = EvaluateAndApplyDisplayMode(profile, activeDisplayWidth, configManager);

        Console.WriteLine(multiMonitorActive
            ? $"[CabSpan AutoLaunch] Multi-screen topology active (Width={activeDisplayWidth} >= {profile.SpannedCabWidth * SURROUND_THRESHOLD_RATIO:F0}). Multi-monitor mode applied."
            : $"[CabSpan AutoLaunch] Single-screen topology detected (Width={activeDisplayWidth} < {profile.SpannedCabWidth * SURROUND_THRESHOLD_RATIO:F0}). Single-monitor fallback ({profile.SingleMonitorWidth}px) applied.");

        IReadOnlyList<string> forwardedArgs = ExtractForwardedArguments(cliArgs);
        if (forwardedArgs.Count > 0)
        {
            return LaunchForwardedGameProcess(forwardedArgs, profile, multiMonitorActive);
        }

        return 0;
    }

    private void TryPromptUpdateBeforeGameLaunch(CabSpanProfile profile)
    {
        try
        {
            SelfUpdateInstaller.CleanupPreviousBackup();
            if (profile.IsUpdateNotificationSnoozed(DateTime.UtcNow))
            {
                return;
            }

            var checker = new UpdateCheckerService();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2200));
            UpdateReleaseInfo? release = Task.Run(() => checker.CheckForUpdateAsync(cancellationToken: cts.Token)).GetAwaiter().GetResult();
            if (release is null)
            {
                return;
            }

            var promptWindow = new UpdatePromptWindow(release, profile, _profileStore, isAutoLaunchContext: true);
            promptWindow.ShowDialog();
        }
        catch
        {
            // Never allow update check issues to interrupt launching ATS/ETS2 from Steam.
        }
    }

    /// <summary>
    /// Evaluates active cab width against the profile's SpannedCabWidth * 0.9 threshold and updates config.cfg.
    /// Returns true if Multi-Monitor mode was applied, or false if Single-Monitor fallback was applied.
    /// </summary>
    public bool EvaluateAndApplyDisplayMode(
        CabSpanProfile profile,
        int primaryWidth,
        ConfigManager configManager)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(configManager);

        int effectivePrimaryWidth = primaryWidth > 0
            ? primaryWidth
            : Math.Max(1, profile.SingleMonitorWidth);

        double surroundThreshold = profile.SpannedCabWidth * SURROUND_THRESHOLD_RATIO;

        if (profile.SpannedCabWidth > 0 && effectivePrimaryWidth >= surroundThreshold)
        {
            bool isBorderless = profile.SpanningMode == SpanningMode.ZeroDriverBorderless || profile.SelectedMonitorDeviceNames.Count > 1;
            configManager.ApplyMultiMonitorMode(effectivePrimaryWidth, profile.SpannedCabHeight, borderlessWindowed: isBorderless);
            return true;
        }

        int fallbackWidth = profile.SingleMonitorWidth > 0
            ? profile.SingleMonitorWidth
            : effectivePrimaryWidth;

        configManager.ApplySingleMonitorFallback(fallbackWidth);
        return false;
    }

    /// <summary>
    /// Extracts any command-line tokens that appear after `--auto-launch`.
    /// </summary>
    public static IReadOnlyList<string> ExtractForwardedArguments(IReadOnlyList<string> cliArgs)
    {
        ArgumentNullException.ThrowIfNull(cliArgs);

        for (int i = 0; i < cliArgs.Count; i++)
        {
            if (string.Equals(cliArgs[i], AUTO_LAUNCH_FLAG, StringComparison.OrdinalIgnoreCase))
            {
                int remaining = cliArgs.Count - (i + 1);
                if (remaining <= 0)
                {
                    return Array.Empty<string>();
                }

                var forwarded = new List<string>(remaining);
                for (int j = i + 1; j < cliArgs.Count; j++)
                {
                    forwarded.Add(cliArgs[j]);
                }

                return forwarded;
            }
        }

        return Array.Empty<string>();
    }

    private static int ResolveActiveCabDisplayWidth(CabSpanProfile profile, string? documentsRoot)
    {
        int primaryWidth = GetPrimaryScreenWidth(profile.SingleMonitorWidth);
        if (primaryWidth >= profile.SpannedCabWidth * SURROUND_THRESHOLD_RATIO)
        {
            return primaryWidth;
        }

        bool gameCurrentlyInMode4 =
            (profile.ApplyToAts && ConfigManager.IsMultiMonitorModeActive(SimulatorGame.AmericanTruckSimulator, documentsRoot)) ||
            (profile.ApplyToEts2 && ConfigManager.IsMultiMonitorModeActive(SimulatorGame.EuroTruckSimulator2, documentsRoot));

        if (gameCurrentlyInMode4)
        {
            var scanner = new MonitorScanner();
            List<MonitorInfo> connected = scanner.ScanConnectedMonitors();
            if (connected.Count >= 2)
            {
                int totalConnectedWidth = connected.Max(m => m.Right) - connected.Min(m => m.X);
                if (totalConnectedWidth >= profile.SpannedCabWidth * SURROUND_THRESHOLD_RATIO)
                {
                    return profile.SpannedCabWidth;
                }
            }
        }

        return primaryWidth;
    }

    private static int GetPrimaryScreenWidth(int fallbackSingleWidth)
    {
        Screen? primary = Screen.PrimaryScreen;
        if (primary is not null)
        {
            if (DisplayHardwareInterop.TryGetNativeDisplayBounds(primary.DeviceName, out var nativeBounds) &&
                nativeBounds.Width > 0)
            {
                return nativeBounds.Width;
            }

            if (primary.Bounds.Width > 0)
            {
                return primary.Bounds.Width;
            }
        }

        return fallbackSingleWidth > 0 ? fallbackSingleWidth : CabSpanProfile.DEFAULT_SINGLE_WIDTH;
    }

    private static int LaunchForwardedGameProcess(
        IReadOnlyList<string> forwardedArgs,
        CabSpanProfile profile,
        bool multiMonitorActive)
    {
        string executablePath = forwardedArgs[0];
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return 0;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };

        string? workingDir = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
        {
            startInfo.WorkingDirectory = workingDir;
        }

        for (int i = 1; i < forwardedArgs.Count; i++)
        {
            startInfo.ArgumentList.Add(forwardedArgs[i]);
        }

        using Process? process = Process.Start(startInfo);
        if (process is not null && multiMonitorActive)
        {
            TrySpanLaunchedProcessWindow(process, profile);
        }

        return 0;
    }

    private static void TrySpanLaunchedProcessWindow(Process process, CabSpanProfile profile)
    {
        const int MAX_ATTEMPTS = 36; // 18 seconds total monitoring window
        const int POLL_INTERVAL_MS = 500;
        const int MIN_EYEFINITY_SETTLE_ATTEMPTS = 10; // Wait at least 5s for AMD Per-Game Eyefinity display switch
        const int REQUIRED_STABLE_TICKS = 4;

        var spanner = new BorderlessSpanner();
        var scanner = new MonitorScanner();
        int stableSpannedTicks = 0;

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            if (process.HasExited)
            {
                return;
            }

            process.Refresh();
            IntPtr hWnd = process.MainWindowHandle != IntPtr.Zero
                ? process.MainWindowHandle
                : spanner.FindSimulatorWindowHandle();

            if (hWnd != IntPtr.Zero)
            {
                List<MonitorInfo> liveMonitors = scanner.ScanConnectedMonitors();
                System.Drawing.Rectangle targetBox = spanner.ResolveProfileBoundingBox(profile, liveMonitors);

                if (!spanner.IsWindowSpannedToBox(hWnd, targetBox))
                {
                    stableSpannedTicks = 0;
                    _ = spanner.ApplyBorderlessSpanToWindow(
                        hWnd,
                        targetBox.X,
                        targetBox.Y,
                        targetBox.Width,
                        targetBox.Height);
                }
                else if (attempt >= MIN_EYEFINITY_SETTLE_ATTEMPTS)
                {
                    stableSpannedTicks++;
                    if (stableSpannedTicks >= REQUIRED_STABLE_TICKS)
                    {
                        return;
                    }
                }
            }

            Thread.Sleep(POLL_INTERVAL_MS);
        }
    }
}
