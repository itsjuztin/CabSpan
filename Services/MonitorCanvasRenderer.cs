using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Renders connected physical monitors proportionally onto the interactive CabSpan visual canvas.
/// </summary>
public static class MonitorCanvasRenderer
{
    private const double CANVAS_PADDING = 24.0;
    private const double MIN_CARD_WIDTH = 135.0;
    private const double MIN_CARD_HEIGHT = 96.0;
    private const double CORNER_RADIUS = 2.0;
    private const double ACTIVE_BORDER_THICKNESS = 1.5;
    private const double INACTIVE_BORDER_THICKNESS = 1.0;
    private const double GLOW_BLUR_RADIUS = 0.0;
    private const double GLOW_OPACITY = 0.0;

    // Centralized ATS / ETS2 palette tokens from ThemePalette
    private static readonly SolidColorBrush ActiveCardBg       = ThemePalette.ActiveCardBgBrush;
    private static readonly SolidColorBrush InactiveCardBg     = ThemePalette.InactiveCardBgBrush;
    private static readonly SolidColorBrush GoldAccentBrush    = ThemePalette.GoldAccentBrush;
    private static readonly SolidColorBrush GoldLightBrush     = ThemePalette.GoldLightBrush;
    private static readonly SolidColorBrush GoldDimBrush       = ThemePalette.GoldDimBrush;
    private static readonly SolidColorBrush SlateBorderBrush   = ThemePalette.SlateBorderBrush;
    private static readonly SolidColorBrush PrimaryTextBrush   = ThemePalette.TextPrimaryBrush;
    private static readonly SolidColorBrush MutedTextBrush     = ThemePalette.TextSecondaryBrush;
    private static readonly SolidColorBrush DarkBadgeTextBrush = ThemePalette.TextOnGoldBrush;
    private static readonly SolidColorBrush GoldBadgeBg        = ThemePalette.GoldPillBgBrush;

    public static void RenderMonitors(
        Canvas canvas,
        IReadOnlyList<MonitorInfo> monitors,
        Action<MonitorInfo> onToggleMonitor,
        Action<MonitorInfo> onSetMainWheel)
    {
        canvas.Children.Clear();
        if (monitors.Count == 0)
        {
            return;
        }

        double canvasWidth = Math.Max(360.0, canvas.ActualWidth);
        double canvasHeight = Math.Max(190.0, canvas.ActualHeight);

        int minX = monitors.Min(m => m.X);
        int minY = monitors.Min(m => m.Y);
        int maxRight = monitors.Max(m => m.Right);
        int maxBottom = monitors.Max(m => m.Bottom);

        double totalVirtualWidth = Math.Max(1.0, maxRight - minX);
        double totalVirtualHeight = Math.Max(1.0, maxBottom - minY);

        double usableWidth = Math.Max(100.0, canvasWidth - (CANVAS_PADDING * 2.0));
        double usableHeight = Math.Max(80.0, canvasHeight - (CANVAS_PADDING * 2.0));
        double scale = Math.Min(usableWidth / totalVirtualWidth, usableHeight / totalVirtualHeight);

        double scaledTotalW = totalVirtualWidth * scale;
        double scaledTotalH = totalVirtualHeight * scale;
        double offsetX = ((canvasWidth - scaledTotalW) / 2.0) - (minX * scale);
        double offsetY = ((canvasHeight - scaledTotalH) / 2.0) - (minY * scale);

        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorInfo monitor = monitors[i];
            string posLabel = DescribeRelativePosition(monitor, monitors);
            Border card = CreateMonitorCard(monitor, i + 1, posLabel, scale, onToggleMonitor, onSetMainWheel);
            Canvas.SetLeft(card, offsetX + (monitor.X * scale));
            Canvas.SetTop(card, offsetY + (monitor.Y * scale));
            canvas.Children.Add(card);
        }
    }

    private static string DescribeRelativePosition(MonitorInfo monitor, IReadOnlyList<MonitorInfo> allMonitors)
    {
        if (allMonitors.Count <= 1) return "Main Display";
        MonitorInfo? centerRef = allMonitors.FirstOrDefault(m => m.IsMainWheelScreen)
            ?? allMonitors.FirstOrDefault(m => m.IsPrimary)
            ?? allMonitors[allMonitors.Count / 2];

        if (ReferenceEquals(monitor, centerRef)) return "Center / Main Screen";
        if (monitor.CenterX < centerRef.X) return "Left Screen";
        if (monitor.CenterX > centerRef.Right) return "Right Screen";
        return monitor.Y < centerRef.Y ? "Top Screen" : "Auxiliary Screen";
    }

    private static Border CreateMonitorCard(
        MonitorInfo monitor,
        int displayIndex,
        string posLabel,
        double scale,
        Action<MonitorInfo> onToggleMonitor,
        Action<MonitorInfo> onSetMainWheel)
    {
        double width = Math.Max(MIN_CARD_WIDTH, (monitor.Width * scale) - 6.0);
        double height = Math.Max(MIN_CARD_HEIGHT, (monitor.Height * scale) - 6.0);

        SolidColorBrush borderBrush = ResolveBorderBrush(monitor);
        var card = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(CORNER_RADIUS),
            Background = monitor.IsSelectedForCab ? ActiveCardBg : InactiveCardBg,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(monitor.IsSelectedForCab ? ACTIVE_BORDER_THICKNESS : INACTIVE_BORDER_THICKNESS),
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(8, 6, 8, 6),
            ToolTip = "Click anywhere on this screen box to turn it ON or OFF for driving.",
            Child = BuildCardContent(monitor, displayIndex, posLabel, onSetMainWheel)
        };

        if (monitor.IsSelectedForCab)
        {
            card.Effect = new DropShadowEffect
            {
                Color = borderBrush.Color,
                BlurRadius = GLOW_BLUR_RADIUS,
                ShadowDepth = 0,
                Opacity = GLOW_OPACITY
            };
        }

        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onToggleMonitor(monitor);
        };

        return card;
    }

    private static UIElement BuildCardContent(
        MonitorInfo monitor,
        int displayIndex,
        string posLabel,
        Action<MonitorInfo> onSetMainWheel)
    {
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        stack.Children.Add(new TextBlock
        {
            Text = $"Screen #{displayIndex} • {monitor.FriendlyName}",
            Foreground = PrimaryTextBrush,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"{posLabel} ({monitor.Width}×{monitor.Height})",
            Foreground = MutedTextBrush,
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 4),
            TextAlignment = TextAlignment.Center
        });

        stack.Children.Add(CreateRoleStatusBadge(monitor));
        stack.Children.Add(CreateMainWheelRow(monitor, onSetMainWheel));

        return stack;
    }

    private static Border CreateRoleStatusBadge(MonitorInfo monitor)
    {
        string roleText = monitor.IsSelectedForCab
            ? "USED FOR DRIVING  (click to turn off)"
            : "KEPT FREE FOR DESKTOP  (click to use)";

        SolidColorBrush badgeBg = monitor.IsSelectedForCab ? GoldBadgeBg : InactiveCardBg;
        SolidColorBrush badgeFg = monitor.IsSelectedForCab ? GoldLightBrush : MutedTextBrush;
        SolidColorBrush badgeBorder = monitor.IsSelectedForCab ? GoldDimBrush : SlateBorderBrush;

        return new Border
        {
            Background = badgeBg,
            BorderBrush = badgeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 0, 5),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = roleText,
                Foreground = badgeFg,
                FontWeight = FontWeights.Bold,
                FontSize = 9.0
            }
        };
    }

    private static UIElement CreateMainWheelRow(MonitorInfo monitor, Action<MonitorInfo> onSetMainWheel)
    {
        if (monitor.IsSelectedForCab && monitor.IsMainWheelScreen)
        {
            return new Border
            {
                Background = GoldAccentBrush,
                CornerRadius = new CornerRadius(1),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                ToolTip = "Your steering wheel, dashboard, and game menus are centered on this screen.",
                Child = new TextBlock
                {
                    Text = "★ STEERING WHEEL & MENUS HERE",
                    Foreground = DarkBadgeTextBrush,
                    FontWeight = FontWeights.Bold,
                    FontSize = 9.0
                }
            };
        }

        var btn = new System.Windows.Controls.Button
        {
            Content = "★ Put Steering Wheel Here",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(8, 3, 8, 3),
            Background = InactiveCardBg,
            Foreground = MutedTextBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = SlateBorderBrush,
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            ToolTip = "Click here if this is the screen directly in front of your steering wheel."
        };

        btn.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        btn.PreviewMouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onSetMainWheel(monitor);
        };

        return btn;
    }

    private static SolidColorBrush ResolveBorderBrush(MonitorInfo monitor)
    {
        if (!monitor.IsSelectedForCab) return SlateBorderBrush;
        return monitor.IsMainWheelScreen ? GoldAccentBrush : GoldDimBrush;
    }

    public static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
