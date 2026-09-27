using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Stretches a borderless ATS/ETS2 window (amtrucks / eurotrucks2) across the exact
/// (CabLeft, CabTop, TotalCabWidth, TotalCabHeight) bounding box using Win32 user32.dll
/// without requiring AMD Eyefinity or NVIDIA Surround.
/// </summary>
public sealed class BorderlessSpanner
{
    public const string ATS_PROCESS_NAME = "amtrucks";
    public const string ETS2_PROCESS_NAME = "eurotrucks2";
    public const string PRISM3D_WINDOW_CLASS = "prism3d";
    public const string ATS_WINDOW_TITLE = "American Truck Simulator";
    public const string ETS2_WINDOW_TITLE = "Euro Truck Simulator 2";

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZE = 0x20000000;
    private const int WS_MAXIMIZE = 0x01000000;
    private const int WS_SYSMENU = 0x00080000;

    private const int WS_EX_DLGMODALFRAME = 0x00000001;
    private const int WS_EX_CLIENTEDGE = 0x00000200;
    private const int WS_EX_STATICEDGE = 0x00020000;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Returns true if the target window's current dimensions already match or exceed 90% of the target cab bounding box.
    /// </summary>
    public bool IsWindowSpannedToBox(IntPtr hWnd, Rectangle targetBox)
    {
        if (hWnd == IntPtr.Zero || !GetWindowRect(hWnd, out RECT rect))
        {
            return false;
        }

        int currentWidth = rect.Right - rect.Left;
        int currentHeight = rect.Bottom - rect.Top;
        bool widthMatches = currentWidth >= (int)(targetBox.Width * 0.9);
        bool heightMatches = currentHeight >= (int)(targetBox.Height * 0.9);
        bool xMatches = Math.Abs(rect.Left - targetBox.X) <= 32;
        return widthMatches && heightMatches && xMatches;
    }

    /// <summary>
    /// Computes the exact (CabLeft, CabTop, TotalCabWidth, TotalCabHeight) bounding box for selected cab monitors.
    /// </summary>
    public Rectangle CalculateCabBoundingBox(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        List<MonitorInfo> selected = monitors
            .Where(m => m.IsSelectedForCab && !m.IsAuxDashOnly)
            .ToList();

        if (selected.Count == 0)
        {
            throw new InvalidOperationException("At least one cab monitor must be selected to compute a borderless bounding box.");
        }

        int cabLeft = selected.Min(m => m.X);
        int cabTop = selected.Min(m => m.Y);
        int cabRight = selected.Max(m => m.Right);
        int cabBottom = selected.Max(m => m.Bottom);

        int totalCabWidth = Math.Max(1, cabRight - cabLeft);
        int totalCabHeight = Math.Max(1, cabBottom - cabTop);

        return new Rectangle(cabLeft, cabTop, totalCabWidth, totalCabHeight);
    }

    /// <summary>
    /// Resolves the bounding box from connected monitors matching the profile, handling Per-Game Eyefinity (0,0) origin transitions.
    /// </summary>
    public Rectangle ResolveProfileBoundingBox(CabSpanProfile profile, IEnumerable<MonitorInfo>? connectedMonitors = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        int fallbackWidth = Math.Max(1, profile.SpannedCabWidth);
        int fallbackHeight = Math.Max(1, profile.SpannedCabHeight);

        if (connectedMonitors is not null)
        {
            List<MonitorInfo> allConnected = connectedMonitors.ToList();

            // If AMD Eyefinity / NVIDIA Surround is currently active on a single merged display,
            // its desktop origin is (0,0) rather than the multi-monitor negative X offset (e.g. -2560).
            MonitorInfo? activeEyefinityDisplay = allConnected
                .FirstOrDefault(m => m.Width >= profile.SpannedCabWidth * AutoLaunchHandler.SURROUND_THRESHOLD_RATIO);
            if (activeEyefinityDisplay is not null)
            {
                return new Rectangle(
                    activeEyefinityDisplay.X,
                    activeEyefinityDisplay.Y,
                    activeEyefinityDisplay.Width,
                    activeEyefinityDisplay.Height);
            }

            if (profile.SelectedMonitorDeviceNames.Count > 0)
            {
                var selectedSet = new HashSet<string>(profile.SelectedMonitorDeviceNames, StringComparer.OrdinalIgnoreCase);
                var auxSet = new HashSet<string>(profile.AuxDashMonitorDeviceNames, StringComparer.OrdinalIgnoreCase);

                List<MonitorInfo> matched = allConnected
                    .Where(m => selectedSet.Contains(m.DeviceName) && !auxSet.Contains(m.DeviceName))
                    .ToList();

                if (matched.Count > 0)
                {
                    int left = matched.Min(m => m.X);
                    int top = matched.Min(m => m.Y);
                    int right = matched.Max(m => m.Right);
                    int bottom = matched.Max(m => m.Bottom);
                    return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
                }
            }
        }

        return new Rectangle(profile.CabLeft, profile.CabTop, fallbackWidth, fallbackHeight);
    }

    /// <summary>
    /// Locates a running ATS (amtrucks) or ETS2 (eurotrucks2) top-level window handle via FindWindow and Process lookup.
    /// </summary>
    public IntPtr FindSimulatorWindowHandle()
    {
        IntPtr byClass = FindWindow(PRISM3D_WINDOW_CLASS, null);
        if (byClass != IntPtr.Zero)
        {
            return byClass;
        }

        IntPtr byAtsTitle = FindWindow(null, ATS_WINDOW_TITLE);
        if (byAtsTitle != IntPtr.Zero)
        {
            return byAtsTitle;
        }

        IntPtr byEts2Title = FindWindow(null, ETS2_WINDOW_TITLE);
        if (byEts2Title != IntPtr.Zero)
        {
            return byEts2Title;
        }

        string[] processNames = { ATS_PROCESS_NAME, ETS2_PROCESS_NAME };
        foreach (string procName in processNames)
        {
            Process[] processes = Process.GetProcessesByName(procName);
            try
            {
                foreach (Process process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainWindowHandle;
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Strips window borders and stretches a running ATS/ETS2 window across the profile's cab bounding box.
    /// </summary>
    public bool TrySpanSimulatorWindow(CabSpanProfile profile, IEnumerable<MonitorInfo>? connectedMonitors = null)
    {
        Rectangle bounds = ResolveProfileBoundingBox(profile, connectedMonitors);
        return TrySpanSimulatorWindow(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>
    /// Strips window borders and stretches a running ATS/ETS2 window across (cabLeft, cabTop, totalCabWidth, totalCabHeight).
    /// </summary>
    public bool TrySpanSimulatorWindow(int cabLeft, int cabTop, int totalCabWidth, int totalCabHeight)
    {
        IntPtr hWnd = FindSimulatorWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return ApplyBorderlessSpanToWindow(hWnd, cabLeft, cabTop, totalCabWidth, totalCabHeight);
    }

    /// <summary>
    /// Applies Win32 SetWindowLong (borderless style) and SetWindowPos (exact multi-monitor bounding box) to hWnd.
    /// </summary>
    public bool ApplyBorderlessSpanToWindow(
        IntPtr hWnd,
        int cabLeft,
        int cabTop,
        int totalCabWidth,
        int totalCabHeight)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (totalCabWidth <= 0 || totalCabHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCabWidth), "Cab bounding box dimensions must be positive.");
        }

        int currentStyle = GetWindowLong(hWnd, GWL_STYLE);
        int borderlessStyle = currentStyle & ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZE | WS_MAXIMIZE | WS_SYSMENU);
        _ = SetWindowLong(hWnd, GWL_STYLE, borderlessStyle);

        int currentExStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        int borderlessExStyle = currentExStyle & ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
        _ = SetWindowLong(hWnd, GWL_EXSTYLE, borderlessExStyle);

        uint flags = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW;
        return SetWindowPos(hWnd, IntPtr.Zero, cabLeft, cabTop, totalCabWidth, totalCabHeight, flags);
    }
}
