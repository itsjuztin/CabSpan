using System.IO;
using System.Windows;
using System.Windows.Media;
using CabSpan.Services;

namespace CabSpan;

/// <summary>
/// Plain-English step-by-step guide dialog explaining CabSpan setup and optional Steam Launch Options.
/// </summary>
public partial class QuickStartWindow : Window
{
    public QuickStartWindow(bool focusSteamSection = false)
    {
        InitializeComponent();
        if (focusSteamSection)
        {
            Loaded += (_, _) => HighlightAndScrollToSteamSection();
        }
    }

    public static string BuildSteamLaunchCommand()
    {
        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CabSpan.exe");
        return $"\"{exePath}\" {AutoLaunchHandler.AUTO_LAUNCH_FLAG} %command%";
    }

    private void HighlightAndScrollToSteamSection()
    {
        SteamGuideCard.BorderBrush = Models.ThemePalette.GoldAccentBrush;
        SteamGuideCard.BorderThickness = new Thickness(2);
        TxtSteamCardHeading.Text = "✅ Command Copied! Here Is Where to Paste It in Steam:";
        TxtSteamCopyStatus.Text = "✓ Already copied to your clipboard — ready to paste (Ctrl+V)!";
        SteamGuideCard.BringIntoView();
    }

    private void OnCopySteamInGuideClick(object sender, RoutedEventArgs e)
    {
        string cmd = BuildSteamLaunchCommand();
        System.Windows.Clipboard.SetText(cmd);
        TxtSteamCopyStatus.Text = "✓ Copied! Now right-click your game in Steam -> Properties -> Paste in Launch Options.";
    }

    private void OnCloseGuideClick(object sender, RoutedEventArgs e) => Close();
}
