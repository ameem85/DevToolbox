using DevToolbox.App.Resources.Strings;
using DevToolbox.Core.Abstractions.Plugins;

namespace DevToolbox.App.Views;

/// <summary>
/// Compact Always-On-Top Floating HUD Window page for executing registered <see cref="OverlayQuickAction"/> items.
/// </summary>
public sealed class FloatingHudPage : ContentPage
{
    public FloatingHudPage(
        IReadOnlyList<OverlayQuickAction> quickActions,
        Func<Task> onCloseRequested)
    {
        Title = AppStrings.Hud_FloatingTitle;
        BackgroundColor = Color.FromArgb("#1E293B");
        Padding = new Thickness(12, 8);

        var headerLabel = new Label
        {
            AutomationId = "FloatingHud_TitleLabel",
            Text = $"🛠 {AppStrings.Hud_FloatingTitle}",
            TextColor = Color.FromArgb("#38BDF8"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center
        };

        var actionsRow = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center
        };

        foreach (var action in quickActions)
        {
            var btn = new Button
            {
                AutomationId = $"FloatingHud_Action_{action.ActionId}",
                Text = $"{action.IconGlyph} {AppStrings.GetString(action.LabelResourceKey)}",
                BackgroundColor = Color.FromArgb("#334155"),
                TextColor = Colors.White,
                FontSize = 12,
                Padding = new Thickness(10, 6),
                CornerRadius = 8
            };
            btn.Clicked += async (_, _) => await action.ExecuteAsync(CancellationToken.None);
            actionsRow.Children.Add(btn);
        }

        var closeButton = new Button
        {
            AutomationId = "FloatingHud_CloseButton",
            Text = "✕",
            BackgroundColor = Color.FromArgb("#DC2626"),
            TextColor = Colors.White,
            FontSize = 12,
            Padding = new Thickness(10, 6),
            CornerRadius = 8,
            HorizontalOptions = LayoutOptions.End
        };
        closeButton.Clicked += async (_, _) => await onCloseRequested();

        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions =
                    [
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    ],
                    Children =
                    {
                        headerLabel,
                        closeButton
                    }
                },
                new ScrollView
                {
                    AutomationId = "FloatingHud_ActionsScrollView",
                    Orientation = ScrollOrientation.Horizontal,
                    Content = actionsRow
                }
            }
        };
        Grid.SetColumn(closeButton, 1);
    }
}
