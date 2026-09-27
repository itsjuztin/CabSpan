using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CabSpan.Models;
using CabSpan.Services;

namespace CabSpan;

/// <summary>
/// Modal dialog for submitting structured user bug reports with automatic monitor topology snapshots.
/// </summary>
public partial class BugReportWindow : Window
{
    private static readonly SolidColorBrush ReadyCyanBrush = ThemePalette.GoldLightBrush;
    private static readonly SolidColorBrush WarnAmberBrush = ThemePalette.GoldAccentBrush;

    private readonly CabSpanProfile _profile;
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private bool _isSuggestionMode;

    public BugReportWindow(CabSpanProfile profile, IReadOnlyList<MonitorInfo> monitors, bool startInSuggestionMode = false)
    {
        _profile = profile;
        _monitors = monitors;
        _isSuggestionMode = startInSuggestionMode;
        InitializeComponent();
        HydrateDialogState();
    }

    private void HydrateDialogState()
    {
        TxtClientHashBadge.Text = $"CLIENT {BugReportService.GetAnonymousClientHash()}";
        TxtSnapshotPreview.Text = BugReportService.BuildDetailedTopologySnapshot(_profile, _monitors);
        ApplyModeUi();

        if (!BugReportService.CanSubmitNow(out string cooldownNote))
        {
            TxtStatusMessage.Text = $"⏳ {cooldownNote}";
            TxtStatusMessage.Foreground = WarnAmberBrush;
        }
    }

    private void ApplyModeUi()
    {
        var activeGoldStyle = (Style)FindResource("PrimaryCyanButtonStyle");
        var inactiveSlateStyle = (Style)FindResource("SecondarySlateButtonStyle");

        BtnModeBug.Style = _isSuggestionMode ? inactiveSlateStyle : activeGoldStyle;
        BtnModeSuggestion.Style = _isSuggestionMode ? activeGoldStyle : inactiveSlateStyle;

        if (_isSuggestionMode)
        {
            Title = "CabSpan — Suggest a Feature or Improvement";
            TxtDialogHeaderTitle.Text = "💡 SUGGEST A FEATURE";
            TxtDialogSubtitle.Text = "Have an idea to make CabSpan even better? Send it straight to the Discord #suggestions channel!";
            TxtCategoryLabel.Text = "1. What area is your suggestion for?";
            TxtDescriptionLabel.Text = "3. Describe your idea or feature request (20–350 chars, plain text):";
            BtnSubmitDiscord.Content = "💡 Send Suggestion to Discord (#suggestions)";
            BtnOpenGitHub.Content = "🐙 Open Feature Request on GitHub";
            TxtStatusMessage.Text = "Pick a category and describe your feature idea above.";
            CmbCategory.ItemsSource = BugReportService.SuggestionCategories;
            CmbCategory.SelectedIndex = 0;
        }
        else
        {
            Title = "CabSpan — Report a Bug or Rig Alignment Issue";
            TxtDialogHeaderTitle.Text = "🐞 REPORT A BUG";
            TxtDialogSubtitle.Text = "Automatically attaches your anonymous monitor geometry & FOV settings so layout/math issues can be reproduced.";
            TxtCategoryLabel.Text = "1. Which subsystem had an issue?";
            TxtDescriptionLabel.Text = "3. What went wrong? (20–350 chars, plain text without external links)";
            BtnSubmitDiscord.Content = "📤 Send Bug Report to Dev (Discord)";
            BtnOpenGitHub.Content = "🐙 Open Bug Issue on GitHub";
            TxtStatusMessage.Text = "Select a category and describe what happened above.";
            CmbCategory.ItemsSource = BugReportService.Categories;
            CmbCategory.SelectedIndex = 0;
        }
    }

    private void OnSwitchToBugModeClick(object sender, RoutedEventArgs e)
    {
        _isSuggestionMode = false;
        ApplyModeUi();
    }

    private void OnSwitchToSuggestionModeClick(object sender, RoutedEventArgs e)
    {
        _isSuggestionMode = true;
        ApplyModeUi();
    }

    private void OnDescriptionTextChanged(object sender, TextChangedEventArgs e)
    {
        int count = TxtBugDescription.Text?.Trim().Length ?? 0;
        bool validLen = count >= BugReportService.MIN_DESCRIPTION_CHARS && count <= BugReportService.MAX_DESCRIPTION_CHARS;
        TxtCharCounter.Text = $"{count} / {BugReportService.MAX_DESCRIPTION_CHARS} chars (min {BugReportService.MIN_DESCRIPTION_CHARS})";
        TxtCharCounter.Foreground = validLen ? ReadyCyanBrush : WarnAmberBrush;
    }

    private async void OnSubmitDiscordClick(object sender, RoutedEventArgs e)
    {
        var list = _isSuggestionMode ? BugReportService.SuggestionCategories : BugReportService.Categories;
        string selectedCategory = CmbCategory.SelectedItem as string ?? list[0];
        string description = TxtBugDescription.Text ?? string.Empty;
        string contact = TxtContactHandle.Text ?? string.Empty;

        BtnSubmitDiscord.IsEnabled = false;
        TxtStatusMessage.Text = _isSuggestionMode
            ? "⏳ Sending feature suggestion to Discord..."
            : "⏳ Validating and sending bug report...";
        TxtStatusMessage.Foreground = ReadyCyanBrush;

        try
        {
            var (success, statusMessage) = await BugReportService.SubmitBugReportAsync(
                selectedCategory,
                description,
                contact,
                _profile,
                _monitors,
                isSuggestion: _isSuggestionMode);

            TxtStatusMessage.Text = statusMessage;
            TxtStatusMessage.Foreground = success ? ReadyCyanBrush : WarnAmberBrush;
            if (success)
            {
                TxtBugDescription.Clear();
            }
        }
        finally
        {
            BtnSubmitDiscord.IsEnabled = true;
        }
    }

    private void OnOpenGitHubClick(object sender, RoutedEventArgs e)
    {
        var list = _isSuggestionMode ? BugReportService.SuggestionCategories : BugReportService.Categories;
        string selectedCategory = CmbCategory.SelectedItem as string ?? list[0];
        string description = TxtBugDescription.Text ?? string.Empty;
        BugReportService.OpenPreFilledGitHubIssue(selectedCategory, description, _profile, _monitors, isSuggestion: _isSuggestionMode);
        TxtStatusMessage.Text = "🐙 Opened pre-filled GitHub Issue in your browser!";
        TxtStatusMessage.Foreground = ReadyCyanBrush;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
