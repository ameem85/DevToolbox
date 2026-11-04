using DevToolbox.App.Resources.Strings;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.Views;

/// <summary>
/// Multi-state Always-On-Top Floating HUD Window page supporting:
/// 1. <see cref="OverlayExpansionState.CollapsedGear"/>: Compact settings gear launcher.
/// 2. <see cref="OverlayExpansionState.ExpandedPanel"/>: Categorized quick actions &amp; telemetry panel.
/// 3. <see cref="OverlayExpansionState.FullForegroundApp"/>: Foreground application expansion.
/// </summary>
public sealed class FloatingHudPage : ContentPage
{
    private readonly IReadOnlyList<OverlayQuickAction> _quickActions;
    private readonly Func<OverlayExpansionState, Task> _onStateChangeRequested;
    private readonly Func<string?, Task> _onExpandFullRequested;
    private readonly Func<Task> _onCloseRequested;

    public FloatingHudPage(
        IReadOnlyList<OverlayQuickAction> quickActions,
        OverlayExpansionState initialState,
        Func<OverlayExpansionState, Task> onStateChangeRequested,
        Func<string?, Task> onExpandFullRequested,
        Func<Task> onCloseRequested)
    {
        _quickActions = quickActions;
        _onStateChangeRequested = onStateChangeRequested;
        _onExpandFullRequested = onExpandFullRequested;
        _onCloseRequested = onCloseRequested;

        Title = AppStrings.Hud_FloatingTitle;
        BackgroundColor = Color.FromArgb("#0B1120");
        ApplyExpansionState(initialState);
    }

    public void ApplyExpansionState(OverlayExpansionState state)
    {
        if (state == OverlayExpansionState.CollapsedGear)
        {
            Padding = new Thickness(10, 8);
            var gearButton = new Button
            {
                AutomationId = "FloatingHud_GearExpandButton",
                Text = "⚙ ● DevToolbox HUD (Click to Expand)",
                BackgroundColor = Color.FromArgb("#0F172A"),
                BorderColor = Color.FromArgb("#38BDF8"),
                BorderWidth = 1.5,
                TextColor = Color.FromArgb("#38BDF8"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 12,
                CornerRadius = 18,
                Padding = new Thickness(14, 8)
            };
            gearButton.Clicked += async (_, _) => await _onStateChangeRequested(OverlayExpansionState.ExpandedPanel);
            Content = gearButton;
            return;
        }

        Padding = new Thickness(14, 10);

        var collapseGearBtn = new Button
        {
            AutomationId = "FloatingHud_CollapseGearButton",
            Text = "⚙",
            BackgroundColor = Color.FromArgb("#1E293B"),
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            Padding = new Thickness(10, 4),
            CornerRadius = 8
        };
        collapseGearBtn.Clicked += async (_, _) => await _onStateChangeRequested(OverlayExpansionState.CollapsedGear);

        var headerLabel = new Label
        {
            AutomationId = "FloatingHud_TitleLabel",
            Text = "DEVTOOLBOX HUD",
            TextColor = Color.FromArgb("#F8FAFC"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(8, 0)
        };

        var expandFullButton = new Button
        {
            AutomationId = "FloatingHud_ExpandFullButton",
            Text = "⤢ Full View",
            BackgroundColor = Color.FromArgb("#0284C7"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 11,
            Padding = new Thickness(10, 5),
            CornerRadius = 8
        };
        expandFullButton.Clicked += async (_, _) => await _onExpandFullRequested("DashboardPage");

        var closeButton = new Button
        {
            AutomationId = "FloatingHud_CloseButton",
            Text = "✕",
            BackgroundColor = Color.FromArgb("#7F1D1D"),
            TextColor = Color.FromArgb("#FCA5A5"),
            FontSize = 11,
            Padding = new Thickness(9, 5),
            CornerRadius = 8
        };
        closeButton.Clicked += async (_, _) => await _onCloseRequested();

        var actionsWrap = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center
        };

        foreach (var action in _quickActions)
        {
            var btn = new Button
            {
                AutomationId = $"FloatingHud_Action_{action.ActionId}",
                Text = $"{action.IconGlyph} {AppStrings.GetString(action.LabelResourceKey)}",
                BackgroundColor = Color.FromArgb("#1E293B"),
                BorderColor = Color.FromArgb("#334155"),
                BorderWidth = 1,
                TextColor = Color.FromArgb("#E2E8F0"),
                FontSize = 11,
                Padding = new Thickness(10, 6),
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = 8
            };
            btn.Clicked += async (_, _) => await action.ExecuteAsync(CancellationToken.None);
            actionsWrap.Children.Add(btn);
        }

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 6,
            Children =
            {
                collapseGearBtn,
                headerLabel,
                expandFullButton,
                closeButton
            }
        };
        Grid.SetColumn(collapseGearBtn, 0);
        Grid.SetColumn(headerLabel, 1);
        Grid.SetColumn(expandFullButton, 2);
        Grid.SetColumn(closeButton, 3);

        Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                headerGrid,
                new ScrollView
                {
                    AutomationId = "FloatingHud_ActionsScrollView",
                    Orientation = ScrollOrientation.Vertical,
                    Content = actionsWrap
                }
            }
        };
    }
}
