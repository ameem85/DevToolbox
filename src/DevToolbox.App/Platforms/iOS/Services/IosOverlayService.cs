using CoreGraphics;
using DevToolbox.App.Resources.Strings;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;
using UIKit;

namespace DevToolbox.App.Platforms.iOS.Services;

/// <summary>
/// iOS 3-state in-app floating overlay engine using a dedicated high-z-order <see cref="UIWindow"/>
/// (<c>UIWindowLevel.Alert + 1</c>) to simulate system overlays within iOS sandbox constraints.
/// </summary>
public sealed class IosOverlayService : IOverlayService
{
    private UIWindow? _overlayWindow;
    private IReadOnlyList<OverlayQuickAction> _quickActions = [];
    private OverlayConfiguration _lastConfig = new();
    private OverlayExpansionState _expansionState = OverlayExpansionState.Hidden;
    private bool _isVisible;

    public event EventHandler<OverlayStatusSnapshot>? StatusChanged;

    public OverlayStatusSnapshot CurrentStatus => new(
        _isVisible,
        OverlayPermissionState.SimulatedInApp,
        OverlayImplementationKind.IosInAppWindowLayer,
        _isVisible
            ? $"iOS UIWindow (Alert + 1) floating overlay active ({_expansionState})"
            : "iOS Sandbox: In-app floating UIWindow overlay ready",
        _isVisible ? _expansionState : OverlayExpansionState.Hidden);

    public Task<OverlayPermissionState> CheckPermissionAsync(CancellationToken cancellationToken = default)
    {
        RaiseStatusChanged();
        return Task.FromResult(OverlayPermissionState.SimulatedInApp);
    }

    public Task<OverlayPermissionState> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        RaiseStatusChanged();
        return Task.FromResult(OverlayPermissionState.SimulatedInApp);
    }

    public async Task<OverlayStatusSnapshot> ShowOverlayAsync(
        OverlayConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        _lastConfig = configuration;
        var targetState = configuration.InitialExpansionState == OverlayExpansionState.Hidden
            ? OverlayExpansionState.CollapsedGear
            : configuration.InitialExpansionState;

        if (targetState == OverlayExpansionState.FullForegroundApp)
        {
            return await ExpandToFullApplicationAsync(null, cancellationToken).ConfigureAwait(false);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _expansionState = targetState;
            RebuildOverlayWindow(configuration);
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public async Task<OverlayStatusSnapshot> HideOverlayAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_overlayWindow is not null)
            {
                _overlayWindow.Hidden = true;
                _overlayWindow.Dispose();
                _overlayWindow = null;
            }

            _isVisible = false;
            _expansionState = OverlayExpansionState.Hidden;
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public Task<OverlayStatusSnapshot> ToggleOverlayAsync(
        OverlayConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        _isVisible
            ? HideOverlayAsync(cancellationToken)
            : ShowOverlayAsync(configuration, cancellationToken);

    public async Task<OverlayStatusSnapshot> SetExpansionStateAsync(
        OverlayExpansionState expansionState,
        CancellationToken cancellationToken = default)
    {
        if (expansionState == OverlayExpansionState.Hidden)
        {
            return await HideOverlayAsync(cancellationToken).ConfigureAwait(false);
        }

        if (expansionState == OverlayExpansionState.FullForegroundApp)
        {
            return await ExpandToFullApplicationAsync(null, cancellationToken).ConfigureAwait(false);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _expansionState = expansionState;
            RebuildOverlayWindow(_lastConfig);
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public async Task<OverlayStatusSnapshot> ExpandToFullApplicationAsync(
        string? targetRoute = null,
        CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_isVisible)
            {
                _expansionState = OverlayExpansionState.CollapsedGear;
                RebuildOverlayWindow(_lastConfig);
            }

            if (!string.IsNullOrWhiteSpace(targetRoute) && Shell.Current is not null)
            {
                await Shell.Current.GoToAsync($"//{targetRoute}");
            }
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public Task RefreshQuickActionsAsync(
        IReadOnlyList<OverlayQuickAction> quickActions,
        CancellationToken cancellationToken = default)
    {
        _quickActions = quickActions ?? [];
        return Task.CompletedTask;
    }

    private void RebuildOverlayWindow(OverlayConfiguration configuration)
    {
        if (_overlayWindow is not null)
        {
            _overlayWindow.Hidden = true;
            _overlayWindow.Dispose();
            _overlayWindow = null;
        }

        var windowScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();

        if (windowScene is null)
        {
            return;
        }

        var frame = _expansionState == OverlayExpansionState.CollapsedGear
            ? new CGRect(configuration.InitialX, configuration.InitialY, 64, 48)
            : new CGRect(configuration.InitialX, configuration.InitialY, 320, 180);

        _overlayWindow = new UIWindow(windowScene)
        {
            Frame = frame,
            WindowLevel = UIWindowLevel.Alert + 1,
            BackgroundColor = UIColor.FromRGBA(11, 17, 32, 242),
            Alpha = (nfloat)Math.Clamp(configuration.Opacity, 0.4, 1.0)
        };
        _overlayWindow.Layer.CornerRadius = _expansionState == OverlayExpansionState.CollapsedGear ? 24 : 16;
        _overlayWindow.Layer.BorderWidth = 1.5f;
        _overlayWindow.Layer.BorderColor = UIColor.FromRGB(56, 189, 248).CGColor;
        _overlayWindow.ClipsToBounds = true;

        var rootController = new UIViewController();
        if (_expansionState == OverlayExpansionState.CollapsedGear)
        {
            var gearButton = new UIButton(UIButtonType.System)
            {
                Frame = new CGRect(0, 0, 64, 48)
            };
            gearButton.SetTitle("⚙ ●", UIControlState.Normal);
            gearButton.SetTitleColor(UIColor.FromRGB(56, 189, 248), UIControlState.Normal);
            gearButton.TouchUpInside += async (_, _) => await SetExpansionStateAsync(OverlayExpansionState.ExpandedPanel);
            rootController.View?.AddSubview(gearButton);
        }
        else
        {
            var label = new UILabel(new CGRect(12, 10, 180, 24))
            {
                Text = $"⚙ {AppStrings.Hud_FloatingTitle}",
                TextColor = UIColor.FromRGB(56, 189, 248)
            };
            var fullBtn = new UIButton(UIButtonType.System)
            {
                Frame = new CGRect(200, 10, 104, 26)
            };
            fullBtn.SetTitle("⤢ Full View", UIControlState.Normal);
            fullBtn.SetTitleColor(UIColor.White, UIControlState.Normal);
            fullBtn.BackgroundColor = UIColor.FromRGB(2, 132, 199);
            fullBtn.Layer.CornerRadius = 6;
            fullBtn.TouchUpInside += async (_, _) => await ExpandToFullApplicationAsync("DashboardPage");

            rootController.View?.AddSubview(label);
            rootController.View?.AddSubview(fullBtn);
        }

        _overlayWindow.RootViewController = rootController;
        _overlayWindow.Hidden = false;
        _isVisible = true;
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, CurrentStatus);
}
