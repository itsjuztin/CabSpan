using System.Drawing;
using System.Management;
using System.Text;
using System.Windows.Forms;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Enumerates connected Windows displays using WinForms Screen.AllScreens and WMI WmiMonitorID.
/// </summary>
public sealed class MonitorScanner
{
    private const string WMI_MONITOR_NAMESPACE = @"\\.\root\wmi";
    private const string WMI_MONITOR_QUERY = "SELECT Active, InstanceName, ManufacturerName, ProductCodeID, UserFriendlyName FROM WmiMonitorID WHERE Active = True";
    private const string DEFAULT_MONITOR_NAME = "Generic PnP Monitor";

    /// <summary>
    /// Scans all currently connected displays (1 to 6+ monitors) with unscaled native pixel bounds and exact EDID friendly names.
    /// </summary>
    public List<MonitorInfo> ScanConnectedMonitors()
    {
        Dictionary<string, string> ccdNamesByGdi = DisplayHardwareInterop.QueryFriendlyNamesByGdiDevice();
        List<string> wmiFriendlyNames = QueryWmiFriendlyNames();

        var resolvedScreens = Screen.AllScreens
            .Select(s => (
                Screen: s,
                NativeBounds: DisplayHardwareInterop.TryGetNativeDisplayBounds(s.DeviceName, out Rectangle nb) ? nb : s.Bounds
            ))
            .OrderBy(item => item.NativeBounds.X)
            .ThenBy(item => item.NativeBounds.Y)
            .ToArray();

        var results = new List<MonitorInfo>(resolvedScreens.Length);
        for (int i = 0; i < resolvedScreens.Length; i++)
        {
            (Screen screen, Rectangle bounds) = resolvedScreens[i];
            string friendlyName = ResolveFriendlyName(screen.DeviceName, i, ccdNamesByGdi, wmiFriendlyNames);

            var info = new MonitorInfo(
                deviceName: screen.DeviceName,
                friendlyName: friendlyName,
                bounds: bounds,
                isSelectedForCab: screen.Primary,
                isMainWheelScreen: screen.Primary,
                isAuxDashOnly: false,
                isPrimary: screen.Primary);

            results.Add(info);
        }

        return results;
    }

    private static string ResolveFriendlyName(
        string gdiDeviceName,
        int displayIndex,
        IReadOnlyDictionary<string, string> ccdNamesByGdi,
        IReadOnlyList<string> wmiFallbackNames)
    {
        if (ccdNamesByGdi.TryGetValue(gdiDeviceName, out string? ccdName) && !string.IsNullOrWhiteSpace(ccdName))
        {
            return ccdName;
        }

        if (displayIndex < wmiFallbackNames.Count && !string.IsNullOrWhiteSpace(wmiFallbackNames[displayIndex]))
        {
            return wmiFallbackNames[displayIndex];
        }

        return $"{DEFAULT_MONITOR_NAME} #{displayIndex + 1}";
    }

    private static List<string> QueryWmiFriendlyNames()
    {
        var names = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(WMI_MONITOR_NAMESPACE, WMI_MONITOR_QUERY);
            using ManagementObjectCollection collection = searcher.Get();

            foreach (ManagementBaseObject obj in collection)
            {
                string decodedName = ExtractFriendlyNameFromWmiObject(obj);
                if (!string.IsNullOrWhiteSpace(decodedName))
                {
                    names.Add(decodedName);
                }
            }
        }
        catch
        {
            // Fallback gracefully if WMI is restricted or running on a headless/virtual adapter.
        }

        return names;
    }

    private static string ExtractFriendlyNameFromWmiObject(ManagementBaseObject wmiObj)
    {
        string userFriendly = DecodeWmiCharArray(wmiObj["UserFriendlyName"] as ushort[]);
        if (!string.IsNullOrWhiteSpace(userFriendly))
        {
            return userFriendly;
        }

        string manufacturer = DecodeWmiCharArray(wmiObj["ManufacturerName"] as ushort[]);
        string productCode = DecodeWmiCharArray(wmiObj["ProductCodeID"] as ushort[]);
        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            return string.IsNullOrWhiteSpace(productCode)
                ? manufacturer
                : $"{manufacturer} {productCode}";
        }

        return DEFAULT_MONITOR_NAME;
    }

    private static string DecodeWmiCharArray(ushort[]? rawChars)
    {
        if (rawChars is null || rawChars.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(rawChars.Length);
        foreach (ushort codePoint in rawChars)
        {
            if (codePoint == 0)
            {
                break;
            }

            builder.Append((char)codePoint);
        }

        return builder.ToString().Trim();
    }
}
