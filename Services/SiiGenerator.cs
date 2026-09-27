using System.Globalization;
using System.Text;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Holds calculated normalized viewport metrics for a single monitor entry in multimon_config.sii.
/// </summary>
public sealed record SiiMonitorEntry(
    string UnitName,
    string DisplayLabel,
    MonitorInfo SourceMonitor,
    double NormalizedX,
    double NormalizedY,
    double NormalizedWidth,
    double NormalizedHeight,
    double HorizontalFovRelativeOffset,
    double HeadingOffset);

/// <summary>
/// Encapsulates the computed bounding box, normalized UI slice, monitor entries, and SII file content.
/// </summary>
public sealed record SiiGenerationResult(
    int CabLeft,
    int CabTop,
    int TotalCabWidth,
    int TotalCabHeight,
    double NormalizedUiX,
    double NormalizedUiWidth,
    IReadOnlyList<SiiMonitorEntry> Monitors,
    string SiiContent)
{
    public override string ToString() => SiiContent;
}

/// <summary>
/// Generates valid SCS Software SiiNunit multimon_config.sii files for ATS and ETS2.
/// </summary>
public sealed class SiiGenerator
{
    public const double DEFAULT_BEZEL_GAP_DEGREES = 2.5;
    public const double DEFAULT_SIDE_ANGLE_DEGREES = 0.0;
    private const int MIN_CAB_MONITORS = 1;
    private const int MAX_CAB_MONITORS = 4;
    private const double LEFT_FOV_OFFSET = 1.0;
    private const double RIGHT_FOV_OFFSET = -1.0;

    public string GenerateSiiText(
        IEnumerable<MonitorInfo> monitors,
        double bezelGapDegrees = DEFAULT_BEZEL_GAP_DEGREES,
        double sideMonitorAngleDegrees = DEFAULT_SIDE_ANGLE_DEGREES,
        double pitchOffsetDegrees = 0.0)
    {
        return Generate(monitors, bezelGapDegrees, sideMonitorAngleDegrees, pitchOffsetDegrees).SiiContent;
    }

    public SiiGenerationResult Generate(
        IEnumerable<MonitorInfo> monitors,
        double bezelGapDegrees = DEFAULT_BEZEL_GAP_DEGREES,
        double sideMonitorAngleDegrees = DEFAULT_SIDE_ANGLE_DEGREES,
        double pitchOffsetDegrees = 0.0)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        List<MonitorInfo> selected = monitors
            .Where(m => m.IsSelectedForCab && !m.IsAuxDashOnly)
            .ToList();

        ValidateSelection(selected);

        MonitorInfo mainMonitor = selected.First(m => m.IsMainWheelScreen);
        int cabLeft = selected.Min(m => m.X);
        int cabRight = selected.Max(m => m.Right);
        int totalCabWidth = cabRight - cabLeft;

        int cabTop = selected.Min(m => m.Y);
        int cabBottom = selected.Max(m => m.Bottom);
        int totalCabHeight = Math.Max(1, cabBottom - cabTop);

        double normalizedUiX = (double)(mainMonitor.X - cabLeft) / totalCabWidth;
        double normalizedUiWidth = (double)mainMonitor.Width / totalCabWidth;

        List<SiiMonitorEntry> entries = BuildMonitorEntries(
            selected,
            mainMonitor,
            cabLeft,
            cabTop,
            totalCabWidth,
            totalCabHeight,
            bezelGapDegrees,
            sideMonitorAngleDegrees);

        string siiContent = RenderSiiDocument(normalizedUiX, normalizedUiWidth, entries, pitchOffsetDegrees);

        return new SiiGenerationResult(
            CabLeft: cabLeft,
            CabTop: cabTop,
            TotalCabWidth: totalCabWidth,
            TotalCabHeight: totalCabHeight,
            NormalizedUiX: normalizedUiX,
            NormalizedUiWidth: normalizedUiWidth,
            Monitors: entries,
            SiiContent: siiContent);
    }

    private static void ValidateSelection(List<MonitorInfo> selected)
    {
        if (selected.Count < MIN_CAB_MONITORS || selected.Count > MAX_CAB_MONITORS)
        {
            throw new InvalidOperationException(
                $"CabSpan requires between {MIN_CAB_MONITORS} and {MAX_CAB_MONITORS} selected cab monitors (found {selected.Count}).");
        }

        int mainCount = selected.Count(m => m.IsMainWheelScreen);
        if (mainCount != 1)
        {
            throw new InvalidOperationException(
                $"Exactly one selected cab monitor must be marked as IsMainWheelScreen (found {mainCount}).");
        }
    }

    private static List<SiiMonitorEntry> BuildMonitorEntries(
        List<MonitorInfo> selected,
        MonitorInfo main,
        int cabLeft,
        int cabTop,
        int totalCabWidth,
        int totalCabHeight,
        double bezelGapDegrees,
        double sideMonitorAngleDegrees)
    {
        var entries = new List<SiiMonitorEntry>(selected.Count)
        {
            CreateMonitorEntry("_nameless.monitor.main", "main", main, cabLeft, cabTop, totalCabWidth, totalCabHeight, 0.0, 0.0)
        };

        double baseHeadingDelta = bezelGapDegrees + sideMonitorAngleDegrees;

        List<MonitorInfo> leftMonitors = selected
            .Where(m => !ReferenceEquals(m, main) && m.CenterX < main.CenterX)
            .OrderByDescending(m => m.CenterX)
            .ToList();

        for (int i = 0; i < leftMonitors.Count; i++)
        {
            int rank = i + 1;
            double fovOffset = LEFT_FOV_OFFSET * rank;
            double headingOffset = baseHeadingDelta * rank;
            entries.Add(CreateMonitorEntry(
                $"_nameless.monitor.left.{rank}",
                $"left_{rank}",
                leftMonitors[i],
                cabLeft,
                cabTop,
                totalCabWidth,
                totalCabHeight,
                fovOffset,
                headingOffset));
        }

        List<MonitorInfo> rightMonitors = selected
            .Where(m => !ReferenceEquals(m, main) && m.CenterX >= main.CenterX)
            .OrderBy(m => m.CenterX)
            .ToList();

        for (int i = 0; i < rightMonitors.Count; i++)
        {
            int rank = i + 1;
            double fovOffset = RIGHT_FOV_OFFSET * rank;
            double headingOffset = -(baseHeadingDelta * rank);
            entries.Add(CreateMonitorEntry(
                $"_nameless.monitor.right.{rank}",
                $"right_{rank}",
                rightMonitors[i],
                cabLeft,
                cabTop,
                totalCabWidth,
                totalCabHeight,
                fovOffset,
                headingOffset));
        }

        return entries;
    }

    private static SiiMonitorEntry CreateMonitorEntry(
        string unitName,
        string displayLabel,
        MonitorInfo monitor,
        int cabLeft,
        int cabTop,
        int totalCabWidth,
        int totalCabHeight,
        double fovRelativeOffset,
        double headingOffset)
    {
        double normX = (double)(monitor.X - cabLeft) / totalCabWidth;
        double normY = (double)(monitor.Y - cabTop) / totalCabHeight;
        double normWidth = (double)monitor.Width / totalCabWidth;
        double normHeight = (double)monitor.Height / totalCabHeight;

        return new SiiMonitorEntry(
            UnitName: unitName,
            DisplayLabel: displayLabel,
            SourceMonitor: monitor,
            NormalizedX: normX,
            NormalizedY: normY,
            NormalizedWidth: normWidth,
            NormalizedHeight: normHeight,
            HorizontalFovRelativeOffset: fovRelativeOffset,
            HeadingOffset: headingOffset);
    }

    private static string RenderSiiDocument(
        double normalizedUiX,
        double normalizedUiWidth,
        List<SiiMonitorEntry> entries,
        double pitchOffsetDegrees = 0.0)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SiiNunit");
        sb.AppendLine("{");
        sb.AppendLine("multimon_config : _nameless.cabspan.config {");
        sb.AppendLine($" normalized_ui_x: {FormatFloat(normalizedUiX)}");
        sb.AppendLine($" normalized_ui_width: {FormatFloat(normalizedUiWidth)}");
        sb.AppendLine($" monitors: {entries.Count}");
        for (int i = 0; i < entries.Count; i++)
        {
            sb.AppendLine($" monitors[{i}]: {entries[i].UnitName}");
        }

        sb.AppendLine("}");
        sb.AppendLine();

        foreach (SiiMonitorEntry entry in entries)
        {
            AppendMonitorBlock(sb, entry, pitchOffsetDegrees);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendMonitorBlock(StringBuilder sb, SiiMonitorEntry entry, double pitchOffsetDegrees)
    {
        sb.AppendLine($"monitor_config : {entry.UnitName} {{");
        sb.AppendLine($" name: \"{entry.DisplayLabel}\"");
        sb.AppendLine($" normalized_x: {FormatFloat(entry.NormalizedX)}");
        sb.AppendLine($" normalized_y: {FormatFloat(entry.NormalizedY)}");
        sb.AppendLine($" normalized_width: {FormatFloat(entry.NormalizedWidth)}");
        sb.AppendLine($" normalized_height: {FormatFloat(entry.NormalizedHeight)}");
        sb.AppendLine($" horizontal_fov_relative_offset: {FormatFloat(entry.HorizontalFovRelativeOffset)}");
        sb.AppendLine(" vertical_fov_relative_offset: 0.000000");
        sb.AppendLine($" heading_offset: {FormatFloat(entry.HeadingOffset)}");
        sb.AppendLine($" pitch_offset: {FormatFloat(pitchOffsetDegrees)}");
        sb.AppendLine(" roll_offset: 0.000000");
        sb.AppendLine(" camera_space_offset: (0.000000, 0.000000, 0.000000)");
        sb.AppendLine(" horizontal_fov_override: 0.000000");
        sb.AppendLine(" vertical_fov_override: 0.000000");
        sb.AppendLine(" frustum_subrect_x: 0.000000");
        sb.AppendLine(" frustum_subrect_y: 0.000000");
        sb.AppendLine(" frustum_subrect_width: 1.000000");
        sb.AppendLine(" frustum_subrect_height: 1.000000");
        sb.AppendLine(" render_interior: true");
        sb.AppendLine(" render_exterior: true");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string FormatFloat(double value)
    {
        return value.ToString("F6", CultureInfo.InvariantCulture);
    }
}
