using System.Windows.Media;

namespace CabSpan.Models;

/// <summary>
/// Centralized CSS-like color and brush palette for CabSpan.
/// Change hex values here and in Styles/ThemePalette.xaml to re-theme the entire application at once.
/// </summary>
public static class ThemePalette
{
    // ── Background Surfaces ─────────────────────────────────────────────
    public const string HEX_BG_DEEP        = "#0E0E10";
    public const string HEX_PANEL_SURFACE  = "#141316";
    public const string HEX_ROW_SURFACE    = "#1A1820";
    public const string HEX_ROW_ALT        = "#131115";
    public const string HEX_INPUT_SURFACE  = "#0A0A0C";
    public const string HEX_CARD_ACTIVE    = "#1C1A22";
    public const string HEX_CARD_INACTIVE  = "#111015";
    public const string HEX_GOLD_BANNER_BG = "#1A1600";
    public const string HEX_GOLD_PILL_BG   = "#1C1800";

    // ── Borders ─────────────────────────────────────────────────────────
    public const string HEX_BORDER_SUBTLE  = "#252228";
    public const string HEX_BORDER_NORMAL  = "#302C38";
    public const string HEX_BORDER_SLATE   = "#2A2630";

    // ── Gold & Accent Tokens ────────────────────────────────────────────
    public const string HEX_GOLD_ACCENT    = "#C8A017";
    public const string HEX_GOLD_LIGHT     = "#E0BC3A";
    public const string HEX_GOLD_DIM       = "#7A6010";
    public const string HEX_STATUS_SUCCESS = "#5CE07A";
    public const string HEX_STATUS_WARN    = "#E0BC3A";

    // ── Typography Tokens ───────────────────────────────────────────────
    public const string HEX_TEXT_PRIMARY   = "#E8E4D8";
    public const string HEX_TEXT_SECONDARY = "#8A847C";
    public const string HEX_TEXT_MUTED     = "#3E3B44";
    public const string HEX_TEXT_ON_GOLD   = "#0A0900";

    // ── Frozen Reusable Brushes for Code-Behind & Canvas Rendering ──────
    public static readonly SolidColorBrush BgDeepBrush        = CreateFrozenBrush(HEX_BG_DEEP);
    public static readonly SolidColorBrush PanelSurfaceBrush  = CreateFrozenBrush(HEX_PANEL_SURFACE);
    public static readonly SolidColorBrush RowSurfaceBrush    = CreateFrozenBrush(HEX_ROW_SURFACE);
    public static readonly SolidColorBrush InputSurfaceBrush  = CreateFrozenBrush(HEX_INPUT_SURFACE);
    public static readonly SolidColorBrush ActiveCardBgBrush  = CreateFrozenBrush(HEX_CARD_ACTIVE);
    public static readonly SolidColorBrush InactiveCardBgBrush = CreateFrozenBrush(HEX_CARD_INACTIVE);
    public static readonly SolidColorBrush GoldBannerBgBrush  = CreateFrozenBrush(HEX_GOLD_BANNER_BG);
    public static readonly SolidColorBrush GoldPillBgBrush    = CreateFrozenBrush(HEX_GOLD_PILL_BG);

    public static readonly SolidColorBrush BorderSubtleBrush  = CreateFrozenBrush(HEX_BORDER_SUBTLE);
    public static readonly SolidColorBrush BorderNormalBrush  = CreateFrozenBrush(HEX_BORDER_NORMAL);
    public static readonly SolidColorBrush SlateBorderBrush   = CreateFrozenBrush(HEX_BORDER_SLATE);

    public static readonly SolidColorBrush GoldAccentBrush    = CreateFrozenBrush(HEX_GOLD_ACCENT);
    public static readonly SolidColorBrush GoldLightBrush     = CreateFrozenBrush(HEX_GOLD_LIGHT);
    public static readonly SolidColorBrush GoldDimBrush       = CreateFrozenBrush(HEX_GOLD_DIM);
    public static readonly SolidColorBrush StatusSuccessBrush = CreateFrozenBrush(HEX_STATUS_SUCCESS);

    public static readonly SolidColorBrush TextPrimaryBrush   = CreateFrozenBrush(HEX_TEXT_PRIMARY);
    public static readonly SolidColorBrush TextSecondaryBrush = CreateFrozenBrush(HEX_TEXT_SECONDARY);
    public static readonly SolidColorBrush TextOnGoldBrush    = CreateFrozenBrush(HEX_TEXT_ON_GOLD);

    public static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
