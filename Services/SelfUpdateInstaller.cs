using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Downloads GitHub Release assets (.exe or .zip), auto-extracts archives in-memory/temp without user intervention,
/// validates PE headers, and performs an atomic in-place binary replacement of CabSpan.exe.
/// </summary>
public sealed class SelfUpdateInstaller
{
    public const string JUST_UPDATED_FLAG = "--just-updated";
    public const string OLD_BACKUP_EXTENSION = ".old";
    public const string STAGED_NEW_EXTENSION = ".new";
    private const int MIN_VALID_EXE_BYTES = 16384;
    private const int DOWNLOAD_TIMEOUT_SECONDS = 90;

    private static readonly HttpClient DownloadClient = CreateDownloadClient();

    /// <summary>
    /// Cleans up any leftover `CabSpan.exe.old` created during a previous in-place update swap.
    /// </summary>
    public static void CleanupPreviousBackup(string? currentExePath = null)
    {
        try
        {
            string exePath = ResolveCurrentExecutablePath(currentExePath);
            string backupPath = exePath + OLD_BACKUP_EXTENSION;
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch
        {
            // If the old process handle is still closing for a few milliseconds, ignore; it will be cleaned next run.
        }
    }

    public static string ResolveCurrentExecutablePath(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CabSpan.exe");
    }

    /// <summary>
    /// Downloads the release asset (.exe or .zip), extracts CabSpan.exe automatically if zipped,
    /// validates the PE header, and stages it at `CabSpan.exe.new`.
    /// </summary>
    public async Task<string> DownloadAndStageBinaryAsync(
        UpdateReleaseInfo release,
        IProgress<int>? progress = null,
        string? targetExecutablePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        string targetExe = ResolveCurrentExecutablePath(targetExecutablePath);
        string stagedNewPath = targetExe + STAGED_NEW_EXTENSION;
        string tempDownloadPath = Path.Combine(
            Path.GetTempPath(),
            $"CabSpan_Update_{release.ParsedVersion}_{Guid.NewGuid():N}.tmp");

        try
        {
            await DownloadAssetToFileAsync(release.DownloadUrl, tempDownloadPath, progress, cancellationToken)
                .ConfigureAwait(false);

            if (release.IsZipArchive || HasZipHeader(tempDownloadPath))
            {
                ExtractExecutableFromZip(tempDownloadPath, stagedNewPath);
            }
            else
            {
                File.Copy(tempDownloadPath, stagedNewPath, overwrite: true);
            }

            if (!ValidateExecutableFile(stagedNewPath))
            {
                File.Delete(stagedNewPath);
                throw new InvalidDataException("Downloaded update file did not pass Windows PE ('MZ') verification.");
            }

            if (!VerifyOptionalSha256Hash(stagedNewPath, release.ReleaseNotes))
            {
                File.Delete(stagedNewPath);
                throw new InvalidDataException("Downloaded update file failed SHA-256 cryptographic checksum verification.");
            }

            progress?.Report(100);
            return stagedNewPath;
        }
        finally
        {
            TryDeleteFileQuietly(tempDownloadPath);
        }
    }

    /// <summary>
    /// Extracts the primary `.exe` entry from a `.zip` archive directly to destinationExePath.
    /// </summary>
    public static void ExtractExecutableFromZip(string zipFilePath, string destinationExePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipFilePath);
        ZipArchiveEntry? exeEntry = archive.Entries
            .FirstOrDefault(e => string.Equals(e.Name, "CabSpan.exe", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (exeEntry is null)
        {
            throw new FileNotFoundException("No CabSpan.exe executable was found inside the update ZIP archive.");
        }

        exeEntry.ExtractToFile(destinationExePath, overwrite: true);
    }

    /// <summary>
    /// Verifies that a file exists, exceeds minimum size, and begins with the Windows PE 'MZ' magic bytes.
    /// </summary>
    public static bool ValidateExecutableFile(string filePath, int minSizeBytes = MIN_VALID_EXE_BYTES)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var info = new FileInfo(filePath);
        if (info.Length < minSizeBytes)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[2];
        using FileStream fs = File.OpenRead(filePath);
        int read = fs.Read(header);
        return read == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
    }

    /// <summary>
    /// Verifies the staged executable's SHA-256 digest against a `SHA256: <64-hex>` entry in the release notes, if present.
    /// </summary>
    public static bool VerifyOptionalSha256Hash(string filePath, string? releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            return true;
        }

        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            releaseNotes,
            @"SHA256:\s*([A-Fa-f0-9]{64})");
        if (!match.Success)
        {
            return true;
        }

        string expectedHex = match.Groups[1].Value;
        using FileStream fs = File.OpenRead(filePath);
        byte[] actualBytes = System.Security.Cryptography.SHA256.HashData(fs);
        string actualHex = Convert.ToHexString(actualBytes);
        return string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Atomically replaces the running executable on disk using Windows' in-place rename mechanism:
    /// `CabSpan.exe` -> `CabSpan.exe.old`, then `CabSpan.exe.new` -> `CabSpan.exe`.
    /// </summary>
    public static bool PerformAtomicSwap(string targetExePath, string stagedNewExePath)
    {
        if (!File.Exists(stagedNewExePath))
        {
            return false;
        }

        string backupPath = targetExePath + OLD_BACKUP_EXTENSION;
        try
        {
            TryDeleteFileQuietly(backupPath);
            if (File.Exists(targetExePath))
            {
                File.Move(targetExePath, backupPath, overwrite: true);
            }

            File.Move(stagedNewExePath, targetExePath, overwrite: true);
            return true;
        }
        catch
        {
            // Restore original if move failed midway
            if (!File.Exists(targetExePath) && File.Exists(backupPath))
            {
                try { File.Move(backupPath, targetExePath, overwrite: true); } catch { }
            }

            return false;
        }
    }

    /// <summary>
    /// Applies the staged update in-place and optionally restarts CabSpan into the newly installed version.
    /// </summary>
    public void ApplyStagedUpdateAndRestart(string stagedNewExePath, string versionTag, bool restartGui = true)
    {
        string targetExe = ResolveCurrentExecutablePath();
        bool swapped = PerformAtomicSwap(targetExe, stagedNewExePath);

        if (swapped)
        {
            if (restartGui)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = targetExe,
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add(JUST_UPDATED_FLAG);
                startInfo.ArgumentList.Add(versionTag);
                Process.Start(startInfo);
            }

            System.Windows.Application.Current?.Shutdown(0);
            return;
        }

        LaunchFallbackCmdSwapper(targetExe, stagedNewExePath, versionTag, restartGui);
        System.Windows.Application.Current?.Shutdown(0);
    }

    private static async Task DownloadAssetToFileAsync(
        string downloadUrl,
        string destinationFile,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(downloadUrl))
        {
            File.Copy(downloadUrl, destinationFile, overwrite: true);
            progress?.Report(90);
            return;
        }

        using HttpResponseMessage response = await DownloadClient
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream fileStream = new(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;
            if (totalBytes is > 0)
            {
                int pct = (int)Math.Clamp((totalRead * 95L) / totalBytes.Value, 1, 95);
                progress?.Report(pct);
            }
        }
    }

    private static bool HasZipHeader(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        Span<byte> header = stackalloc byte[2];
        using FileStream fs = File.OpenRead(filePath);
        return fs.Read(header) == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
    }

    private static void LaunchFallbackCmdSwapper(string targetExe, string stagedNewExe, string versionTag, bool restartGui)
    {
        string safeTag = new string((versionTag ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or 'v' or 'V').ToArray());
        string restartCmd = restartGui
            ? $"& start \"\" \"{targetExe}\" {JUST_UPDATED_FLAG} \"{safeTag}\""
            : string.Empty;
        string cmdArgs = $"/C timeout /t 1 /nobreak >nul & move /y \"{stagedNewExe}\" \"{targetExe}\" >nul {restartCmd}";
        Process.Start(new ProcessStartInfo("cmd.exe", cmdArgs)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private static void TryDeleteFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(DOWNLOAD_TIMEOUT_SECONDS) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UpdateCheckerService.USER_AGENT_HEADER);
        return client;
    }
}
