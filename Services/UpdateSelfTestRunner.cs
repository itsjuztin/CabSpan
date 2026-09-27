using System.IO;
using System.IO.Compression;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Deterministic automated verification suite for CabSpan's GitHub Releases update checker,
/// 7-day notification snooze logic, in-memory ZIP extraction, and atomic single-file binary swap.
/// </summary>
public static class UpdateSelfTestRunner
{
    public static void RunAllUpdateTests()
    {
        Console.WriteLine("\n-----------------------------------------------------------------");
        Console.WriteLine(" TEST 7: Hands-Free Auto-Update, 1-Week Snooze & Atomic Swap");
        Console.WriteLine("-----------------------------------------------------------------");

        VerifySemanticVersionAndReleaseParsing();
        VerifyDefaultOffAndOneWeekSnoozeWindow();
        VerifyZipExtractionAndAtomicBinarySwap();

        Console.WriteLine("[PASS] UpdateCheckerService, 7-day snooze, ZIP auto-extraction & atomic swap verified.");
    }

    private static void VerifySemanticVersionAndReleaseParsing()
    {
        const string sampleReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "name": "CabSpan v1.2.0 — Bezel & Auto-Update Release",
          "draft": false,
          "published_at": "2026-09-26T15:00:00Z",
          "body": "### Highlights\n- Added hands-free auto-updater with 1-week snooze\n- Fixed triple-monitor bezel offset rounding",
          "assets": [
            {
              "name": "CabSpan-v1.2.0.zip",
              "browser_download_url": "https://github.com/itsjuztin/CabSpan/releases/download/v1.2.0/CabSpan-v1.2.0.zip"
            },
            {
              "name": "CabSpan.exe",
              "browser_download_url": "https://github.com/itsjuztin/CabSpan/releases/download/v1.2.0/CabSpan.exe"
            }
          ]
        }
        """;

        UpdateReleaseInfo? newer = UpdateCheckerService.ParseReleaseJson(sampleReleaseJson, new Version(1, 0, 0));
        if (newer is null)
        {
            throw new InvalidOperationException("Expected ParseReleaseJson to detect v1.2.0 > 1.0.0.");
        }

        if (newer.ParsedVersion != new Version(1, 2, 0) || newer.IsZipArchive || newer.AssetFileName != "CabSpan.exe")
        {
            throw new InvalidOperationException($"Unexpected parsed release metadata: Version={newer.ParsedVersion}, Asset={newer.AssetFileName}");
        }

        UpdateReleaseInfo? upToDate = UpdateCheckerService.ParseReleaseJson(sampleReleaseJson, new Version(1, 2, 0));
        if (upToDate is not null)
        {
            throw new InvalidOperationException("Expected ParseReleaseJson to return null when currentVersion == 1.2.0.");
        }
    }

    private static void VerifyDefaultOffAndOneWeekSnoozeWindow()
    {
        var profile = new CabSpanProfile();
        if (profile.AutoUpdateEnabled)
        {
            throw new InvalidOperationException("Expected AutoUpdateEnabled to default to false.");
        }

        DateTime baseTime = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        if (profile.IsUpdateNotificationSnoozed(baseTime))
        {
            throw new InvalidOperationException("Expected fresh profile not to be snoozed.");
        }

        profile.SnoozeUpdateForOneWeek(baseTime);

        if (!profile.IsUpdateNotificationSnoozed(baseTime.AddDays(6.9)))
        {
            throw new InvalidOperationException("Expected notification to remain snoozed at +6.9 days.");
        }

        if (profile.IsUpdateNotificationSnoozed(baseTime.AddDays(7.1)))
        {
            throw new InvalidOperationException("Expected 1-week snooze to expire after 7 days.");
        }
    }

    private static void VerifyZipExtractionAndAtomicBinarySwap()
    {
        string sandboxDir = Path.Combine(Path.GetTempPath(), $"CabSpan_UpdateTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDir);

        try
        {
            string currentExePath = Path.Combine(sandboxDir, "CabSpan.exe");
            byte[] oldBinary = CreateMockPeBinary(20000, markerByte: 0x11);
            byte[] newBinary = CreateMockPeBinary(24000, markerByte: 0x22);
            File.WriteAllBytes(currentExePath, oldBinary);

            string mockZipPath = Path.Combine(sandboxDir, "CabSpan-v1.2.0.zip");
            using (ZipArchive zip = ZipFile.Open(mockZipPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = zip.CreateEntry("CabSpan.exe");
                using Stream entryStream = entry.Open();
                entryStream.Write(newBinary, 0, newBinary.Length);
            }

            string stagedPath = currentExePath + SelfUpdateInstaller.STAGED_NEW_EXTENSION;
            SelfUpdateInstaller.ExtractExecutableFromZip(mockZipPath, stagedPath);

            if (!SelfUpdateInstaller.ValidateExecutableFile(stagedPath))
            {
                throw new InvalidOperationException("Extracted mock executable failed PE ('MZ') validation.");
            }

            string actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(newBinary));
            if (!SelfUpdateInstaller.VerifyOptionalSha256Hash(stagedPath, $"Release Notes\nSHA256: {actualHash}"))
            {
                throw new InvalidOperationException("Expected VerifyOptionalSha256Hash to succeed with matching SHA-256 digest.");
            }

            if (SelfUpdateInstaller.VerifyOptionalSha256Hash(stagedPath, "Release Notes\nSHA256: 0000000000000000000000000000000000000000000000000000000000000000"))
            {
                throw new InvalidOperationException("Expected VerifyOptionalSha256Hash to reject mismatched SHA-256 digest.");
            }

            bool swapped = SelfUpdateInstaller.PerformAtomicSwap(currentExePath, stagedPath);
            if (!swapped)
            {
                throw new InvalidOperationException("PerformAtomicSwap returned false in sandbox.");
            }

            byte[] installedBytes = File.ReadAllBytes(currentExePath);
            if (installedBytes.Length != newBinary.Length || installedBytes[2] != 0x22)
            {
                throw new InvalidOperationException("Target CabSpan.exe was not replaced with new binary content.");
            }

            if (!File.Exists(currentExePath + SelfUpdateInstaller.OLD_BACKUP_EXTENSION))
            {
                throw new InvalidOperationException("Expected CabSpan.exe.old backup to exist immediately after atomic swap.");
            }

            SelfUpdateInstaller.CleanupPreviousBackup(currentExePath);
            if (File.Exists(currentExePath + SelfUpdateInstaller.OLD_BACKUP_EXTENSION))
            {
                throw new InvalidOperationException("Expected CleanupPreviousBackup to remove CabSpan.exe.old.");
            }
        }
        finally
        {
            try { Directory.Delete(sandboxDir, recursive: true); } catch { }
        }
    }

    private static byte[] CreateMockPeBinary(int sizeBytes, byte markerByte)
    {
        byte[] data = new byte[sizeBytes];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        data[2] = markerByte;
        return data;
    }
}
