using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Handles user-submitted manual bug reports with anti-spam validation, disk-persisted rate limiting,
/// hardware topology snapshotting, Discord webhook delivery, and pre-filled GitHub Issue creation.
/// </summary>
public static class BugReportService
{
    public const int MIN_DESCRIPTION_CHARS = 20;
    public const int MAX_DESCRIPTION_CHARS = 350;
    public const int MAX_CONTACT_CHARS = 40;
    public const int MIN_UNIQUE_CHARS = 8;
    public const int COOLDOWN_MINUTES = 15;
    public const int MAX_DAILY_REPORTS = 3;
    private const int COLOR_BUG_REPORT_PURPLE = 0x9B59B6;
    private const int COLOR_SUGGESTION_GOLD = 0xE0BC3A;
    private const string RATE_LIMIT_FILE_NAME = "bug-ratelimit.json";
    private const string GITHUB_NEW_ISSUE_BASE_URL = "https://github.com/itsjuztin/CabSpan/issues/new";

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(4000)
    };

    public static readonly IReadOnlyList<string> Categories = new[]
    {
        "FOV / Bezel Math Alignment",
        "Monitor Detection / Layout Map",
        "Steam --auto-launch Watchdog",
        "Zero-Driver Borderless Spanner",
        "config.cfg / Mode Switching"
    };

    public static readonly IReadOnlyList<string> SuggestionCategories = new[]
    {
        "New Multi-Monitor / Camera Feature",
        "UI / Dashboard Improvement",
        "New Game or Sim Hardware Support",
        "Quality of Life / Automation Idea",
        "Other Feature Suggestion"
    };

    /// <summary>
    /// Validates user bug description against length, keyboard-mashing, link, and ping rules.
    /// </summary>
    public static bool ValidateReportInput(string? description, out string sanitizedDescription, out string validationError)
    {
        sanitizedDescription = SanitizeUserText(description, MAX_DESCRIPTION_CHARS);
        validationError = string.Empty;

        if (sanitizedDescription.Length < MIN_DESCRIPTION_CHARS)
        {
            validationError = $"Please describe what happened in at least {MIN_DESCRIPTION_CHARS} characters (currently {sanitizedDescription.Length}).";
            return false;
        }

        if (ContainsBlockedLinks(sanitizedDescription))
        {
            validationError = "External links and invite URLs are blocked to prevent spam. Please describe the issue in plain text.";
            return false;
        }

        int uniqueChars = sanitizedDescription.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).Distinct().Count();
        int wordCount = sanitizedDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (uniqueChars < MIN_UNIQUE_CHARS || wordCount < 3)
        {
            validationError = "Please provide a clear description with at least a few words describing the bug.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether the user is currently on cooldown from submitting manual reports.
    /// </summary>
    public static bool CanSubmitNow(out string cooldownMessage, string? customRateLimitPath = null)
    {
        cooldownMessage = string.Empty;
        List<DateTime> recentTimestamps = LoadRecentSubmissions(customRateLimitPath);
        DateTime now = DateTime.UtcNow;

        DateTime? lastSubmission = recentTimestamps.Count > 0 ? recentTimestamps.Max() : null;
        if (lastSubmission.HasValue)
        {
            TimeSpan elapsed = now - lastSubmission.Value;
            if (elapsed.TotalMinutes < COOLDOWN_MINUTES)
            {
                int remainingMinutes = Math.Max(1, (int)Math.Ceiling(COOLDOWN_MINUTES - elapsed.TotalMinutes));
                cooldownMessage = $"Cooldown active: please wait {remainingMinutes} minute(s) before sending another report, or use [Open on GitHub].";
                return false;
            }
        }

        int last24HoursCount = recentTimestamps.Count(t => (now - t).TotalHours < 24.0);
        if (last24HoursCount >= MAX_DAILY_REPORTS)
        {
            cooldownMessage = $"Daily limit reached ({MAX_DAILY_REPORTS} in-app reports per 24h). Please use [Open on GitHub] for additional reports.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Formats a multi-line hardware and monitor topology summary with zero personal data.
    /// </summary>
    public static string BuildDetailedTopologySnapshot(CabSpanProfile profile, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var sb = new StringBuilder();
        sb.AppendLine($"CabSpan v{ErrorReportPayload.CURRENT_APP_VERSION} | {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Bezel Gap: {profile.BezelGapDegrees:F1}° | Side Angle: {profile.SideMonitorAngleDegrees:F1}° | Mode: {profile.SpanningMode}");
        sb.AppendLine($"Spanned Cab Box: {profile.SpannedCabWidth}x{profile.SpannedCabHeight} @ ({profile.CabLeft},{profile.CabTop}) | Single Fallback: {profile.SingleMonitorWidth}px");

        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorInfo m = monitors[i];
            string role = m.IsMainWheelScreen ? "MAIN WHEEL + CAB" : m.IsSelectedForCab ? "IN CAB" : "IGNORED";
            sb.AppendLine($"  [{i + 1}] {m.FriendlyName} ({m.Width}x{m.Height} @ {m.X},{m.Y}) -> [{role}]");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Validates, rate-limits, and sends a user bug report or feature suggestion embed to Discord.
    /// </summary>
    public static async Task<(bool Success, string StatusMessage)> SubmitBugReportAsync(
        string category,
        string description,
        string? optionalContact,
        CabSpanProfile profile,
        IReadOnlyList<MonitorInfo> monitors,
        string? customRateLimitPath = null,
        bool bypassRateLimitForTest = false,
        bool isSuggestion = false)
    {
        if (!ValidateReportInput(description, out string cleanDesc, out string validationError))
        {
            return (false, validationError);
        }

        if (!bypassRateLimitForTest && !CanSubmitNow(out string cooldownError, customRateLimitPath))
        {
            return (false, cooldownError);
        }

        IReadOnlyList<string> validList = isSuggestion ? SuggestionCategories : Categories;
        string safeCategory = validList.Contains(category) ? category : validList[0];
        string safeContact = string.IsNullOrWhiteSpace(optionalContact)
            ? "Not provided (Anonymous)"
            : SanitizeUserText(optionalContact, MAX_CONTACT_CHARS);
        string rigSnapshot = BuildDetailedTopologySnapshot(profile, monitors);
        string payloadJson = BuildBugEmbedJson(safeCategory, cleanDesc, safeContact, rigSnapshot, isSuggestion);

        try
        {
            string endpoint = isSuggestion
                ? DiscordErrorReporter.ResolveSuggestionsWebhookEndpoint()
                : DiscordErrorReporter.ResolveWebhookEndpoint(profile);
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await SharedHttpClient.PostAsync(endpoint, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Could not reach Discord server (HTTP {(int)response.StatusCode}). Try [Open on GitHub].");
            }

            if (!bypassRateLimitForTest)
            {
                RecordSubmissionTimestamp(customRateLimitPath);
            }

            return isSuggestion
                ? (true, "💡 Feature suggestion sent directly to the Discord #suggestions channel! Thank you for your idea.")
                : (true, "✅ Bug report and monitor topology snapshot sent! Thank you for helping improve CabSpan.");
        }
        catch
        {
            return (false, "Network error while sending to Discord. Please check your connection or click [Open on GitHub].");
        }
    }

    /// <summary>
    /// Opens a pre-populated GitHub New Issue tab in the user's default browser.
    /// </summary>
    public static void OpenPreFilledGitHubIssue(
        string category,
        string description,
        CabSpanProfile profile,
        IReadOnlyList<MonitorInfo> monitors,
        bool isSuggestion = false)
    {
        IReadOnlyList<string> validList = isSuggestion ? SuggestionCategories : Categories;
        string safeCategory = string.IsNullOrWhiteSpace(category) ? validList[0] : category;
        string cleanDesc = SanitizeUserText(description, MAX_DESCRIPTION_CHARS);
        string snapshot = BuildDetailedTopologySnapshot(profile, monitors);

        string tagPrefix = isSuggestion ? "[Feature Suggestion]" : "[Bug]";
        string title = $"{tagPrefix} {safeCategory}: {(cleanDesc.Length > 50 ? cleanDesc[..50] + "..." : cleanDesc)}";
        string body = $"### Category\n`{safeCategory}`\n\n### {(isSuggestion ? "Suggested Feature / Idea" : "What Happened")}\n{(string.IsNullOrWhiteSpace(cleanDesc) ? "(Describe details here)" : cleanDesc)}\n\n### Monitor Topology & Rig Snapshot\n```text\n{snapshot}\n```";

        string url = $"{GITHUB_NEW_ISSUE_BASE_URL}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    /// Computes an 8-character irreversible anonymous client identifier hash (#A1B2C3D4).
    /// </summary>
    public static string GetAnonymousClientHash()
    {
        string rawSeed = $"CabSpan-Salt|{Environment.MachineName}|{Environment.ProcessorCount}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawSeed));
        return "#" + Convert.ToHexString(hashBytes)[..8];
    }

    private static string BuildBugEmbedJson(string category, string description, string contact, string rigSnapshot, bool isSuggestion = false)
    {
        string clientHash = GetAnonymousClientHash();
        var payload = new
        {
            username = isSuggestion ? "CabSpan Feature Suggestions" : "CabSpan Bug Reporter",
            embeds = new[]
            {
                new
                {
                    title = isSuggestion
                        ? $"💡 Feature Suggestion — {category}"
                        : $"🐞 User Bug Report — {category}",
                    color = isSuggestion ? COLOR_SUGGESTION_GOLD : COLOR_BUG_REPORT_PURPLE,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    fields = new object[]
                    {
                        new { name = isSuggestion ? "📂 Suggestion Area" : "📂 Subsystem Category", value = $"`{category}`", inline = true },
                        new { name = "💬 Submitted By", value = $"`{contact}`", inline = true },
                        new { name = isSuggestion ? "💡 Feature Idea / Suggestion" : "📝 User Description", value = description, inline = false },
                        new { name = "🖥️ Attached Monitor Topology Snapshot", value = $"```text\n{rigSnapshot}\n```", inline = false }
                    },
                    footer = new
                    {
                        text = $"CabSpan {(isSuggestion ? "Suggestions Channel" : "Manual Bug Reporter")} • Client ID: {clientHash}"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string SanitizeUserText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string cleaned = ErrorLogger.ScrubPersonalPaths(text.Trim())
            .Replace("@everyone", "[everyone]", StringComparison.OrdinalIgnoreCase)
            .Replace("@here", "[here]", StringComparison.OrdinalIgnoreCase)
            .Replace("<@", "[mention]", StringComparison.OrdinalIgnoreCase)
            .Replace("`", "'", StringComparison.Ordinal);

        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }

    private static bool ContainsBlockedLinks(string text)
    {
        return text.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || text.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || text.Contains("discord.gg", StringComparison.OrdinalIgnoreCase)
            || text.Contains("www.", StringComparison.OrdinalIgnoreCase);
    }

    private static List<DateTime> LoadRecentSubmissions(string? customPath)
    {
        string path = customPath ?? Path.Combine(ErrorLogger.GetDefaultLogDirectory(), RATE_LIMIT_FILE_NAME);
        if (!File.Exists(path)) return new List<DateTime>();
        try
        {
            List<DateTime>? loaded = JsonSerializer.Deserialize<List<DateTime>>(File.ReadAllText(path));
            return loaded?.Where(t => (DateTime.UtcNow - t).TotalHours < 24.0).ToList() ?? new List<DateTime>();
        }
        catch
        {
            return new List<DateTime>();
        }
    }

    private static void RecordSubmissionTimestamp(string? customPath)
    {
        try
        {
            string path = customPath ?? Path.Combine(ErrorLogger.GetDefaultLogDirectory(), RATE_LIMIT_FILE_NAME);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ErrorLogger.GetDefaultLogDirectory());
            List<DateTime> timestamps = LoadRecentSubmissions(path);
            timestamps.Add(DateTime.UtcNow);
            File.WriteAllText(path, JsonSerializer.Serialize(timestamps));
        }
        catch
        {
            // Ignore disk write failure on rate limit file.
        }
    }
}
