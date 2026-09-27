using System.Drawing;
using System.Runtime.InteropServices;

namespace CabSpan.Services;

/// <summary>
/// Provides Win32 CCD (QueryDisplayConfig) and DEVMODE (EnumDisplaySettingsEx) hardware queries
/// to obtain exact native unscaled pixel bounds and 1:1 GDI-to-EDID monitor model names.
/// </summary>
internal static class DisplayHardwareInterop
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int ERROR_SUCCESS = 0;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    /// <summary>
    /// Queries exact physical pixel bounds (unscaled by Windows 125%/150% DPI) for a GDI display (`\\.\DISPLAY1`).
    /// </summary>
    public static bool TryGetNativeDisplayBounds(string gdiDeviceName, out Rectangle nativeBounds)
    {
        nativeBounds = Rectangle.Empty;
        if (string.IsNullOrWhiteSpace(gdiDeviceName))
        {
            return false;
        }

        try
        {
            var devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
            if (EnumDisplaySettingsEx(gdiDeviceName, ENUM_CURRENT_SETTINGS, ref devMode, 0) &&
                devMode.dmPelsWidth > 0 &&
                devMode.dmPelsHeight > 0)
            {
                nativeBounds = new Rectangle(
                    devMode.dmPositionX,
                    devMode.dmPositionY,
                    devMode.dmPelsWidth,
                    devMode.dmPelsHeight);
                return true;
            }
        }
        catch
        {
            // Fall back to Screen.Bounds if P/Invoke fails.
        }

        return false;
    }

    /// <summary>
    /// Maps each active GDI device name (`\\.\DISPLAY1`) directly to its EDID monitor model name via Win32 CCD.
    /// </summary>
    public static Dictionary<string, string> QueryFriendlyNamesByGdiDevice()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != ERROR_SUCCESS ||
                pathCount == 0)
            {
                return map;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != ERROR_SUCCESS)
            {
                return map;
            }

            for (int i = 0; i < pathCount; i++)
            {
                string gdiName = GetSourceGdiName(paths[i].sourceInfo);
                string edidName = GetTargetEdidFriendlyName(paths[i].targetInfo);
                if (!string.IsNullOrWhiteSpace(gdiName) && !string.IsNullOrWhiteSpace(edidName))
                {
                    map[gdiName] = edidName;
                }
            }
        }
        catch
        {
            // Gracefully fall back if CCD is unsupported on virtual/remote display drivers.
        }

        return map;
    }

    private static string GetSourceGdiName(DISPLAYCONFIG_PATH_SOURCE_INFO source)
    {
        var packet = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
        packet.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
        packet.header.adapterId = source.adapterId;
        packet.header.id = source.id;
        packet.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;

        return DisplayConfigGetDeviceInfo(ref packet) == ERROR_SUCCESS
            ? (packet.viewGdiDeviceName ?? string.Empty).Trim()
            : string.Empty;
    }

    private static string GetTargetEdidFriendlyName(DISPLAYCONFIG_PATH_TARGET_INFO target)
    {
        var packet = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
        packet.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
        packet.header.adapterId = target.adapterId;
        packet.header.id = target.id;
        packet.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;

        return DisplayConfigGetDeviceInfo(ref packet) == ERROR_SUCCESS
            ? (packet.monitorFriendlyDeviceName ?? string.Empty).Trim()
            : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public ulong refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }
}
