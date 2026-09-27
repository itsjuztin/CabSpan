using System.Text.Json.Serialization;
using CabSpan.Services;

namespace CabSpan.Models;

/// <summary>
/// Defines how CabSpan spans the simulator across multiple monitors.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpanningMode
{
    DriverEyefinitySurround,
    ZeroDriverBorderless
}

/// <summary>
/// Persisted user configuration saved to %AppData%\CabSpan\profile.json.
/// </summary>
public sealed class CabSpanProfile
{
    public const double DEFAULT_BEZEL_GAP_DEGREES = 2.5;
    public const double DEFAULT_SIDE_ANGLE_DEGREES = 0.0;
    public const int DEFAULT_SINGLE_WIDTH = 2560;
    public const int DEFAULT_SPANNED_WIDTH = 5120;
    public const int DEFAULT_SPANNED_HEIGHT = 1440;
    public const int DEFAULT_UPDATE_SNOOZE_DAYS = 7;

    public bool ApplyToAts { get; set; } = true;

    public bool ApplyToEts2 { get; set; } = true;

    public List<string> SelectedMonitorDeviceNames { get; set; } = new();

    public List<string> AuxDashMonitorDeviceNames { get; set; } = new();

    public string MainWheelMonitorId { get; set; } = string.Empty;

    public double BezelGapDegrees { get; set; } = DEFAULT_BEZEL_GAP_DEGREES;

    public double SideMonitorAngleDegrees { get; set; } = DEFAULT_SIDE_ANGLE_DEGREES;

    public double PitchOffsetDegrees { get; set; } = 0.0;

    public int SingleMonitorWidth { get; set; } = DEFAULT_SINGLE_WIDTH;

    public int SpannedCabWidth { get; set; } = DEFAULT_SPANNED_WIDTH;

    public int SpannedCabHeight { get; set; } = DEFAULT_SPANNED_HEIGHT;

    public int CabLeft { get; set; }

    public int CabTop { get; set; }

    public SpanningMode SpanningMode { get; set; } = SpanningMode.DriverEyefinitySurround;

    public bool EnableCrashReporting { get; set; } = true;

    public string DiagnosticWebhookUrl { get; set; } = string.Empty;

    public bool AutoUpdateEnabled { get; set; } = false;

    public DateTime? UpdateSnoozedUntilUtc { get; set; }

    public string LastNotifiedInstalledVersion { get; set; } = string.Empty;

    public bool HasCompletedGuidedSetup { get; set; } = false;

    /// <summary>
    /// Returns true if the user has silenced update notifications and the 7-day snooze window is still active.
    /// </summary>
    public bool IsUpdateNotificationSnoozed(DateTime utcNow)
    {
        return UpdateSnoozedUntilUtc.HasValue && utcNow < UpdateSnoozedUntilUtc.Value;
    }

    /// <summary>
    /// Silences update notifications and auto-update prompts for 7 days from the specified UTC timestamp.
    /// </summary>
    public void SnoozeUpdateForOneWeek(DateTime utcNow)
    {
        UpdateSnoozedUntilUtc = utcNow.AddDays(DEFAULT_UPDATE_SNOOZE_DAYS);
    }

    /// <summary>
    /// Resolves the target ATS/ETS2 document directories enabled in this profile.
    /// </summary>
    public IReadOnlyList<string> GetEnabledGameDirectories(string? documentsRoot = null)
    {
        var directories = new List<string>(2);

        if (ApplyToAts)
        {
            directories.Add(ConfigManager.GetGameDirectory(SimulatorGame.AmericanTruckSimulator, documentsRoot));
        }

        if (ApplyToEts2)
        {
            directories.Add(ConfigManager.GetGameDirectory(SimulatorGame.EuroTruckSimulator2, documentsRoot));
        }

        return directories;
    }
}
