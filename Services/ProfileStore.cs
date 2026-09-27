using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Persists and loads CabSpanProfile settings at %AppData%\CabSpan\profile.json.
/// </summary>
public sealed class ProfileStore
{
    public const string APP_DATA_FOLDER_NAME = "CabSpan";
    public const string PROFILE_FILE_NAME = "profile.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ProfileFilePath { get; }

    public ProfileStore(string? customProfilePath = null)
    {
        ProfileFilePath = string.IsNullOrWhiteSpace(customProfilePath)
            ? GetDefaultProfileFilePath()
            : customProfilePath;
    }

    public static string GetDefaultProfileFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, APP_DATA_FOLDER_NAME, PROFILE_FILE_NAME);
    }

    /// <summary>
    /// Loads the user profile from disk, or initializes a default profile from connected displays if none exists.
    /// </summary>
    public CabSpanProfile Load()
    {
        if (File.Exists(ProfileFilePath))
        {
            try
            {
                string json = File.ReadAllText(ProfileFilePath, Encoding.UTF8);
                CabSpanProfile? loaded = JsonSerializer.Deserialize<CabSpanProfile>(json, SerializerOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
            catch
            {
                // Fall back to a fresh hardware-derived profile if the JSON file is corrupted.
            }
        }

        CabSpanProfile defaultProfile = CreateDefaultProfileFromHardware();
        Save(defaultProfile);
        return defaultProfile;
    }

    /// <summary>
    /// Saves the specified profile to %AppData%\CabSpan\profile.json.
    /// </summary>
    public void Save(CabSpanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string? directory = Path.GetDirectoryName(ProfileFilePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(profile, SerializerOptions);
        File.WriteAllText(ProfileFilePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Synchronizes monitor selection and bounding-box dimensions into a CabSpanProfile instance.
    /// </summary>
    public static void SyncMonitorsIntoProfile(CabSpanProfile profile, IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(monitors);

        List<MonitorInfo> all = monitors.ToList();
        List<MonitorInfo> selectedCab = all
            .Where(m => m.IsSelectedForCab && !m.IsAuxDashOnly)
            .ToList();

        profile.SelectedMonitorDeviceNames = selectedCab.Select(m => m.DeviceName).ToList();
        profile.AuxDashMonitorDeviceNames = all
            .Where(m => m.IsAuxDashOnly)
            .Select(m => m.DeviceName)
            .ToList();

        MonitorInfo? mainMonitor = selectedCab.FirstOrDefault(m => m.IsMainWheelScreen)
            ?? all.FirstOrDefault(m => m.IsPrimary)
            ?? all.FirstOrDefault();

        if (mainMonitor is not null)
        {
            profile.MainWheelMonitorId = mainMonitor.DeviceName;
            profile.SingleMonitorWidth = mainMonitor.Width;
        }

        if (selectedCab.Count > 0)
        {
            int left = selectedCab.Min(m => m.X);
            int right = selectedCab.Max(m => m.Right);
            int top = selectedCab.Min(m => m.Y);
            int bottom = selectedCab.Max(m => m.Bottom);

            profile.CabLeft = left;
            profile.CabTop = top;
            profile.SpannedCabWidth = Math.Max(1, right - left);
            profile.SpannedCabHeight = Math.Max(1, bottom - top);
            profile.SpanningMode = selectedCab.Count > 1
                ? SpanningMode.ZeroDriverBorderless
                : SpanningMode.DriverEyefinitySurround;
        }
    }

    private static CabSpanProfile CreateDefaultProfileFromHardware()
    {
        var profile = new CabSpanProfile();
        try
        {
            string atsDir = ConfigManager.GetGameDirectory(SimulatorGame.AmericanTruckSimulator);
            string ets2Dir = ConfigManager.GetGameDirectory(SimulatorGame.EuroTruckSimulator2);
            bool atsExists = Directory.Exists(atsDir);
            bool ets2Exists = Directory.Exists(ets2Dir);
            if (atsExists || ets2Exists)
            {
                profile.ApplyToAts = atsExists;
                profile.ApplyToEts2 = ets2Exists;
            }

            var scanner = new MonitorScanner();
            List<MonitorInfo> connected = scanner.ScanConnectedMonitors();
            SyncMonitorsIntoProfile(profile, connected);
        }
        catch
        {
            // Keep baseline defaults if scanning is unavailable.
        }

        return profile;
    }
}
