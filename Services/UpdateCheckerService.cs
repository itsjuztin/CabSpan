using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Checks GitHub Releases (itsjuztin/CabSpan) for newer versions and resolves direct .exe or .zip assets.
/// </summary>
public sealed class UpdateCheckerService
{
    public const string DEFAULT_GITHUB_LATEST_RELEASE_API = "https://api.github.com/repos/itsjuztin/CabSpan/releases/latest";
    public const string USER_AGENT_HEADER = "CabSpan-Updater/1.0 (+https://github.com/itsjuztin/CabSpan)";
    public const string MOCK_UPDATE_ENV_VAR = "CABSPAN_MOCK_UPDATE_JSON";
    private const int DEFAULT_HTTP_TIMEOUT_MS = 2800;

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly string _releaseApiUrl;

    public UpdateCheckerService(string? customReleaseApiUrl = null)
    {
        _releaseApiUrl = string.IsNullOrWhiteSpace(customReleaseApiUrl)
            ? DEFAULT_GITHUB_LATEST_RELEASE_API
            : customReleaseApiUrl;
    }

    public static Version GetCurrentVersion()
    {
        Version? asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (asmVersion is null)
        {
            return new Version(1, 0, 0);
        }

        return new Version(
            Math.Max(0, asmVersion.Major),
            Math.Max(0, asmVersion.Minor),
            Math.Max(0, asmVersion.Build));
    }

    public static string GetCurrentVersionTag()
    {
        Version v = GetCurrentVersion();
        return $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Queries GitHub Releases for a newer version than currentVersion. Returns null if up-to-date or offline.
    /// </summary>
    public async Task<UpdateReleaseInfo?> CheckForUpdateAsync(
        Version? currentVersionOverride = null,
        CancellationToken cancellationToken = default)
    {
        Version currentVersion = currentVersionOverride ?? GetCurrentVersion();

        try
        {
            string? mockJson = Environment.GetEnvironmentVariable(MOCK_UPDATE_ENV_VAR);
            if (!string.IsNullOrWhiteSpace(mockJson))
            {
                return ParseReleaseJson(mockJson, currentVersion);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, _releaseApiUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using HttpResponseMessage response = await SharedHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseReleaseJson(json, currentVersion);
        }
        catch
        {
            // Silently ignore network offline or timeout conditions so startup/game launch is never blocked.
            return null;
        }
    }

    /// <summary>
    /// Parses a GitHub Release JSON payload and returns an UpdateReleaseInfo if its version exceeds currentVersion.
    /// </summary>
    public static UpdateReleaseInfo? ParseReleaseJson(string json, Version currentVersion)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("draft", out JsonElement draftProp) && draftProp.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        string tagName = root.TryGetProperty("tag_name", out JsonElement tagProp)
            ? tagProp.GetString() ?? string.Empty
            : string.Empty;

        if (!TryParseSemanticVersion(tagName, out Version remoteVersion) || remoteVersion <= currentVersion)
        {
            return null;
        }

        (string downloadUrl, string assetName, bool isZip) = SelectBestReleaseAsset(root);
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        string title = root.TryGetProperty("name", out JsonElement nameProp)
            ? nameProp.GetString() ?? tagName
            : tagName;
        string notes = root.TryGetProperty("body", out JsonElement bodyProp)
            ? bodyProp.GetString() ?? string.Empty
            : string.Empty;

        DateTime publishedUtc = DateTime.UtcNow;
        if (root.TryGetProperty("published_at", out JsonElement pubProp) &&
            DateTime.TryParse(pubProp.GetString(), out DateTime parsedDate))
        {
            publishedUtc = parsedDate.ToUniversalTime();
        }

        return new UpdateReleaseInfo
        {
            TagName = tagName.StartsWith('v') || tagName.StartsWith('V') ? tagName : $"v{tagName}",
            ParsedVersion = remoteVersion,
            ReleaseTitle = title,
            ReleaseNotes = notes,
            DownloadUrl = downloadUrl,
            AssetFileName = assetName,
            IsZipArchive = isZip,
            PublishedAtUtc = publishedUtc
        };
    }

    public static bool TryParseSemanticVersion(string? rawVersionText, out Version parsedVersion)
    {
        parsedVersion = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(rawVersionText))
        {
            return false;
        }

        string cleaned = rawVersionText.Trim().TrimStart('v', 'V');
        int suffixIdx = cleaned.IndexOfAny(new[] { '-', '+', ' ' });
        if (suffixIdx > 0)
        {
            cleaned = cleaned[..suffixIdx];
        }

        string[] parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            cleaned += ".0";
        }

        if (Version.TryParse(cleaned, out Version? v) && v is not null)
        {
            parsedVersion = new Version(
                Math.Max(0, v.Major),
                Math.Max(0, v.Minor),
                Math.Max(0, v.Build));
            return true;
        }

        return false;
    }

    private static (string Url, string FileName, bool IsZip) SelectBestReleaseAsset(JsonElement root)
    {
        string? fallbackZipUrl = null;
        string fallbackZipName = string.Empty;

        if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
                string url = asset.TryGetProperty("browser_download_url", out JsonElement u) ? u.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return (url, name, false);
                }

                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && fallbackZipUrl is null)
                {
                    fallbackZipUrl = url;
                    fallbackZipName = name;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackZipUrl))
        {
            return (fallbackZipUrl, fallbackZipName, true);
        }

        return (string.Empty, string.Empty, false);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(DEFAULT_HTTP_TIMEOUT_MS)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", USER_AGENT_HEADER);
        return client;
    }
}
