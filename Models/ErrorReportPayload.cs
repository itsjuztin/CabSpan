using System.Runtime.InteropServices;

namespace CabSpan.Models;

/// <summary>
/// Represents a structured, PII-sanitized diagnostic error report for local logging and Discord alerting.
/// </summary>
public sealed class ErrorReportPayload
{
    public static string CURRENT_APP_VERSION => Services.UpdateCheckerService.GetCurrentVersion().ToString(3);

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public string AppVersion { get; init; } = CURRENT_APP_VERSION;

    public string ExecutionMode { get; init; } = "DesktopGUI";

    public string ContextOperation { get; init; } = "General";

    public string ExceptionType { get; init; } = "Exception";

    public string SanitizedMessage { get; init; } = string.Empty;

    public string SanitizedStackTrace { get; init; } = string.Empty;

    public bool IsFatal { get; init; }

    public string RigSummary { get; init; } = "Unknown Topology";

    public string OsDescription { get; init; } = RuntimeInformation.OSDescription;

    public string DotNetRuntime { get; init; } = RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// Formats a human-readable summary block for local log files.
    /// </summary>
    public string ToLogFileEntry()
    {
        string severity = IsFatal ? "FATAL" : "ERROR";
        return $"""
            ================================================================================
            [{TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC] [{severity}] CabSpan v{AppVersion} ({ExecutionMode})
            Context   : {ContextOperation}
            Exception : {ExceptionType}: {SanitizedMessage}
            OS / .NET : {OsDescription} | {DotNetRuntime}
            Sim Rig   : {RigSummary}
            --------------------------------------------------------------------------------
            Stack Trace:
            {SanitizedStackTrace}
            ================================================================================
            """;
    }
}
