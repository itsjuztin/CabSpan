using System.Drawing;

namespace CabSpan.Models;

/// <summary>
/// Represents a physical display connected to Windows and its role in the CabSpan rig configuration.
/// </summary>
public sealed class MonitorInfo
{
    public string DeviceName { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = "Generic PnP Monitor";

    public Rectangle Bounds { get; set; }

    public int X => Bounds.X;

    public int Y => Bounds.Y;

    public int Width => Bounds.Width;

    public int Height => Bounds.Height;

    public int Right => Bounds.Right;

    public int Bottom => Bounds.Bottom;

    public double CenterX => Bounds.X + (Bounds.Width / 2.0);

    public bool IsPrimary { get; set; }

    public bool IsSelectedForCab { get; set; }

    public bool IsMainWheelScreen { get; set; }

    public bool IsAuxDashOnly { get; set; }

    public MonitorInfo()
    {
    }

    public MonitorInfo(
        string deviceName,
        string friendlyName,
        Rectangle bounds,
        bool isSelectedForCab = false,
        bool isMainWheelScreen = false,
        bool isAuxDashOnly = false,
        bool isPrimary = false)
    {
        DeviceName = deviceName;
        FriendlyName = friendlyName;
        Bounds = bounds;
        IsSelectedForCab = isSelectedForCab;
        IsMainWheelScreen = isMainWheelScreen;
        IsAuxDashOnly = isAuxDashOnly;
        IsPrimary = isPrimary;
    }

    public override string ToString()
    {
        string role = IsMainWheelScreen
            ? "MAIN WHEEL"
            : (IsSelectedForCab ? "CAB SIDE" : (IsAuxDashOnly ? "AUX DASH" : "IGNORED"));
        return $"{FriendlyName} ({DeviceName}) [{Width}x{Height} at ({X},{Y})] - {role}";
    }
}
