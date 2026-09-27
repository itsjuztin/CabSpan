using System.IO;
using System.Text;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Centralized error logging service that writes PII-scrubbed rolling logs to %AppData%\CabSpan\logs
/// and dispatches automated Discord Webhook alerts when enabled.
/// </summary>
public static class ErrorLogger
{
    public const string LOGS_SUBFOLDER_NAME = "logs";
    public const string LOG_FILE_NAME = "cabspan-errors.log";
    public const string ARCHIVE_LOG_FILE_NAME = "cabspan-errors.old.log";
    public const long MAX_LOG_FILE_BYTES = 1_048_576; // 1 MB
    public const int MAX_ALERTS_PER_SESSION = 5;
    private const int FATAL_WEBHOOK_WAIT_MS = 2500;

    private static readonly object FileSyncRoot = new();
    private static readonly HashSet<string> SentSessionFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private static int _sessionAlertsSent;

    public static string CurrentExecutionMode { get; set; } = "DesktopGUI";

    public static string GetDefaultLogDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, ProfileStore.APP_DATA_FOLDER_NAME, LOGS_SUBFOLDER_NAME);
    }

    public static string GetDefaultLogFilePath()
    {
        return Path.Combine(GetDefaultLogDirectory(), LOG_FILE_NAME);
    }

    /// <summary>
    /// Logs an exception to the local rolling file and dispatches a Discord alert if enabled.
    /// </summary>
    public static ErrorReportPayload LogException(
        Exception exception,
        string contextOperation,
        bool isFatal = false,
        CabSpanProfile? profile = null,
        string? customLogFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        CabSpanProfile activeProfile = ResolveActiveProfile(profile);
        ErrorReportPayload report = CreateReportPayload(exception, contextOperation, isFatal, activeProfile);

        WriteToRollingLogFile(report.ToLogFileEntry(), customLogFilePath ?? GetDefaultLogFilePath());
        DispatchDiscordAlertIfEligible(report, activeProfile, isFatal);

        return report;
    }

    /// <summary>
    /// Sends a live test diagnostic alert and sample user bug report to Discord.
    /// </summary>
    public static bool SendTestDiagnosticAlert(CabSpanProfile? profile = null)
    {
        CabSpanProfile activeProfile = ResolveActiveProfile(profile);
        var testEx = new InvalidOperationException(
            @"Simulated test error at C:\Users\" + Environment.UserName + @"\Documents\American Truck Simulator\config.cfg");

        ErrorReportPayload report = CreateReportPayload(testEx, "DiagnosticSelfTest", isFatal: false, activeProfile);
        WriteToRollingLogFile(report.ToLogFileEntry(), GetDefaultLogFilePath());

        bool crashSent = DiscordErrorReporter.SendErrorReportAsync(report, activeProfile, isTestAlert: true)
            .GetAwaiter()
            .GetResult();

        List<MonitorInfo> monitors = new MonitorScanner().ScanConnectedMonitors();
        var (bugSent, _) = BugReportService.SubmitBugReportAsync(
            category: BugReportService.Categories[0],
            description: "Sample manual bug report test: left monitor horizon pitch shifts slightly when Side Angle is set above 25 degrees.",
            optionalContact: "@itsjuztin (Test)",
            profile: activeProfile,
            monitors: monitors,
            bypassRateLimitForTest: true).GetAwaiter().GetResult();

        return crashSent && bugSent;
    }

    /// <summary>
    /// Replaces Windows user profile paths and usernames with %USERPROFILE% so PII never leaks.
    /// </summary>
    public static string ScrubPersonalPaths(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        string scrubbed = rawText;
        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfilePath))
        {
            scrubbed = scrubbed.Replace(userProfilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            string forwardSlashProfile = userProfilePath.Replace('\\', '/');
            scrubbed = scrubbed.Replace(forwardSlashProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        string userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName) && userName.Length > 2)
        {
            scrubbed = scrubbed.Replace($@"Users\{userName}", @"Users\%USERNAME%", StringComparison.OrdinalIgnoreCase);
            scrubbed = scrubbed.Replace($@"Users/{userName}", @"Users/%USERNAME%", StringComparison.OrdinalIgnoreCase);
        }

        return scrubbed;
    }

    private static ErrorReportPayload CreateReportPayload(
        Exception exception,
        string contextOperation,
        bool isFatal,
        CabSpanProfile profile)
    {
        int monitorCount = Math.Max(1, profile.SelectedMonitorDeviceNames.Count);
        string rigSummary = $"Cab Monitors: {monitorCount} | Spanned: {profile.SpannedCabWidth}x{profile.SpannedCabHeight} | " +
                            $"Wheel: {profile.SingleMonitorWidth}px | Mode: {profile.SpanningMode}";

        return new ErrorReportPayload
        {
            TimestampUtc = DateTime.UtcNow,
            ExecutionMode = CurrentExecutionMode,
            ContextOperation = string.IsNullOrWhiteSpace(contextOperation) ? "General" : contextOperation,
            ExceptionType = exception.GetType().Name,
            SanitizedMessage = ScrubPersonalPaths(exception.Message),
            SanitizedStackTrace = ScrubPersonalPaths(exception.ToString()),
            IsFatal = isFatal,
            RigSummary = rigSummary
        };
    }

    private static void WriteToRollingLogFile(string logEntry, string logFilePath)
    {
        try
        {
            lock (FileSyncRoot)
            {
                string? directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateLogFileIfNeeded(logFilePath);
                File.AppendAllText(logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Never throw if disk is full or locked.
        }
    }

    private static void RotateLogFileIfNeeded(string logFilePath)
    {
        if (!File.Exists(logFilePath))
        {
            return;
        }

        var fileInfo = new FileInfo(logFilePath);
        if (fileInfo.Length < MAX_LOG_FILE_BYTES)
        {
            return;
        }

        string directory = Path.GetDirectoryName(logFilePath) ?? GetDefaultLogDirectory();
        string archivePath = Path.Combine(directory, ARCHIVE_LOG_FILE_NAME);
        File.Copy(logFilePath, archivePath, overwrite: true);
        File.WriteAllText(logFilePath, string.Empty, Encoding.UTF8);
    }

    private static void DispatchDiscordAlertIfEligible(
        ErrorReportPayload report,
        CabSpanProfile profile,
        bool waitSynchronously)
    {
        if (!profile.EnableCrashReporting)
        {
            return;
        }

        string fingerprint = $"{report.ExceptionType}|{report.ContextOperation}|{report.SanitizedMessage}";
        lock (FileSyncRoot)
        {
            if (_sessionAlertsSent >= MAX_ALERTS_PER_SESSION || !SentSessionFingerprints.Add(fingerprint))
            {
                return;
            }

            _sessionAlertsSent++;
        }

        Task<bool> sendTask = DiscordErrorReporter.SendErrorReportAsync(report, profile);
        if (waitSynchronously)
        {
            try
            {
                sendTask.Wait(FATAL_WEBHOOK_WAIT_MS);
            }
            catch
            {
                // Ignore timeout on fatal exit.
            }
        }
    }

    private static CabSpanProfile ResolveActiveProfile(CabSpanProfile? providedProfile)
    {
        if (providedProfile is not null)
        {
            return providedProfile;
        }

        try
        {
            return new ProfileStore().Load();
        }
        catch
        {
            return new CabSpanProfile();
        }
    }
}
