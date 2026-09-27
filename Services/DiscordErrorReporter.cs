using System.Net.Http;
using System.Text;
using System.Text.Json;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Formats and dispatches rich Discord Webhook embeds for CabSpan crash and error alerts.
/// </summary>
public static class DiscordErrorReporter
{
    public const string ENV_WEBHOOK_KEY = "CABSPAN_WEBHOOK_URL";
    public const string ENV_SUGGESTIONS_WEBHOOK_KEY = "CABSPAN_SUGGESTIONS_WEBHOOK_URL";
    public const string LOCAL_WEBHOOK_CONFIG_FILE = "webhooks.local.json";
    public const string DEFAULT_PUBLIC_RELAY_URL = "https://cabspan-relay.imthatkindofperson.workers.dev/report";
    public const string DEFAULT_SUGGESTIONS_RELAY_URL = "https://cabspan-relay.imthatkindofperson.workers.dev/suggestions";

    private const int COLOR_FATAL_RED = 0xFF4B4B;
    private const int COLOR_ERROR_AMBER = 0xFF8C00;
    private const int COLOR_TEST_CYAN = 0x00D2FF;
    private const int MAX_STACK_TRACE_CHARS = 920;
    private const int HTTP_TIMEOUT_MS = 3000;

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS)
    };

    /// <summary>
    /// Resolves the active Discord Webhook or Relay URL from environment, profile, local dev config, or public Cloudflare Relay.
    /// </summary>
    public static string ResolveWebhookEndpoint(CabSpanProfile? profile = null)
    {
        string? envUrl = Environment.GetEnvironmentVariable(ENV_WEBHOOK_KEY);
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl.Trim();
        }

        if (profile is not null && !string.IsNullOrWhiteSpace(profile.DiagnosticWebhookUrl))
        {
            return profile.DiagnosticWebhookUrl.Trim();
        }

        string? localUrl = TryReadLocalWebhookConfig("DiagnosticWebhookUrl");
        return !string.IsNullOrWhiteSpace(localUrl) ? localUrl : DEFAULT_PUBLIC_RELAY_URL;
    }

    /// <summary>
    /// Resolves the Discord Suggestions Channel Webhook or Relay URL.
    /// </summary>
    public static string ResolveSuggestionsWebhookEndpoint()
    {
        string? envUrl = Environment.GetEnvironmentVariable(ENV_SUGGESTIONS_WEBHOOK_KEY);
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl.Trim();
        }

        string? localUrl = TryReadLocalWebhookConfig("SuggestionsWebhookUrl");
        return !string.IsNullOrWhiteSpace(localUrl) ? localUrl : DEFAULT_SUGGESTIONS_RELAY_URL;
    }

    private static string? TryReadLocalWebhookConfig(string propertyName)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configPath = System.IO.Path.Combine(appData, ProfileStore.APP_DATA_FOLDER_NAME, LOCAL_WEBHOOK_CONFIG_FILE);
            if (!System.IO.File.Exists(configPath))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(configPath, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString()?.Trim();
            }
        }
        catch
        {
            // Ignore malformed local developer config and fall back to public relay.
        }

        return null;
    }

    /// <summary>
    /// Serializes an ErrorReportPayload into a Discord Webhook JSON string with rich embeds.
    /// </summary>
    public static string BuildDiscordPayloadJson(ErrorReportPayload report, bool isTestAlert = false)
    {
        ArgumentNullException.ThrowIfNull(report);

        int embedColor = GetEmbedColor(report.IsFatal, isTestAlert);
        string titlePrefix = isTestAlert
            ? "✅ CabSpan Diagnostic Alert (Live Test)"
            : report.IsFatal ? "🚨 CabSpan Fatal Crash Report" : "⚠️ CabSpan Operational Error";

        string truncatedTrace = TruncateForDiscord(report.SanitizedStackTrace, MAX_STACK_TRACE_CHARS);
        string formattedTrace = string.IsNullOrWhiteSpace(truncatedTrace)
            ? "`(No stack trace frames)`"
            : $"```text\n{truncatedTrace}\n```";

        var payload = new
        {
            username = "CabSpan Diagnostics",
            embeds = new[]
            {
                new
                {
                    title = $"{titlePrefix} — {report.ExceptionType}",
                    color = embedColor,
                    timestamp = report.TimestampUtc.ToString("O"),
                    fields = new object[]
                    {
                        new { name = "🧭 Context & Mode", value = $"`{report.ExecutionMode}` • `{report.ContextOperation}`", inline = true },
                        new { name = "💻 OS & App Version", value = $"`v{report.AppVersion}` • `{report.OsDescription}`", inline = true },
                        new { name = "🖥️ Sim Rig Topology", value = $"`{report.RigSummary}`", inline = false },
                        new { name = "❗ Error Message", value = $"`{TruncateForDiscord(report.SanitizedMessage, 400)}`", inline = false },
                        new { name = "📜 Sanitized Stack Trace", value = formattedTrace, inline = false }
                    },
                    footer = new
                    {
                        text = $"CabSpan Telemetry • .NET {report.DotNetRuntime}"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Sends the error report embed to Discord asynchronously. Never throws exceptions back to caller.
    /// </summary>
    public static async Task<bool> SendErrorReportAsync(
        ErrorReportPayload report,
        CabSpanProfile? profile = null,
        bool isTestAlert = false)
    {
        try
        {
            if (!isTestAlert && profile is not null && !profile.EnableCrashReporting)
            {
                return false;
            }

            string endpoint = ResolveWebhookEndpoint(profile);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return false;
            }

            string json = BuildDiscordPayloadJson(report, isTestAlert);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await SharedHttpClient.PostAsync(endpoint, content).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static int GetEmbedColor(bool isFatal, bool isTestAlert)
    {
        if (isTestAlert)
        {
            return COLOR_TEST_CYAN;
        }

        return isFatal ? COLOR_FATAL_RED : COLOR_ERROR_AMBER;
    }

    private static string TruncateForDiscord(string input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "None";
        }

        if (input.Length <= maxLength)
        {
            return input;
        }

        return input[..maxLength] + "\n... [truncated]";
    }
}
