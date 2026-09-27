namespace CabSpan.Models;

/// <summary>
/// Represents a published GitHub Release available for in-app downloading and atomic self-updating.
/// </summary>
public sealed class UpdateReleaseInfo
{
    public string TagName { get; init; } = string.Empty;

    public Version ParsedVersion { get; init; } = new(1, 0, 0);

    public string ReleaseTitle { get; init; } = string.Empty;

    public string ReleaseNotes { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    public string AssetFileName { get; init; } = string.Empty;

    public bool IsZipArchive { get; init; }

    public DateTime PublishedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Returns a compact single-line summary of the release notes suitable for notification banners.
    /// </summary>
    public string GetCompactNotesSummary(int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(ReleaseNotes))
        {
            return string.IsNullOrWhiteSpace(ReleaseTitle)
                ? $"CabSpan {TagName} is ready for hands-free installation."
                : ReleaseTitle.Trim();
        }

        string firstMeaningfulLine = ReleaseNotes
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('#', '-', '*', ' '))
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? ReleaseTitle;

        if (firstMeaningfulLine.Length <= maxLength)
        {
            return firstMeaningfulLine;
        }

        return firstMeaningfulLine[..(maxLength - 1)].TrimEnd() + "…";
    }
}
