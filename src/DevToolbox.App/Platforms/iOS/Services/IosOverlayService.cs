using CoreGraphics;
using DevToolbox.App.Resources.Strings;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;
using UIKit;

namespace DevToolbox.App.Platforms.iOS.Services;

/// <summary>
/// iOS in-app floating overlay engine using a dedicated high-z-order <see cref="UIWindow"/>
/// (<c>UIWindowLevel.Alert + 1</c>) to simulate system overlays within iOS sandbox constraints.
/// </summary>
public sealed class IosOverlayService : IOverlayService
{
    private UIWindow? _overlayWindow;
    private IReadOnlyList<OverlayQuickAction> _quickActions = [];
    private bool _isVisible;

    public event EventHandler<OverlayStatusSnapshot>? StatusChanged;

    public OverlayStatusSnapshot CurrentStatus => new(
        _isVisible,
        OverlayPermissionState.SimulatedInApp,
        OverlayImplementationKind.IosInAppWindowLayer,
        _isVisible
            ? "iOS UIWindow (UIWindowLevel.Alert + 1) floating overlay active"
            : "iOS Sandbox: In-app floating UIWindow overlay ready");

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
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_isVisible && _overlayWindow is not null)
            {
                return;
            }

            var windowScene = UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .FirstOrDefault();

            if (windowScene is null)
            {
                return;
            }

            var frame = new CGRect(configuration.InitialX, configuration.InitialY, 320, 56);
            _overlayWindow = new UIWindow(windowScene)
            {
                Frame = frame,
                WindowLevel = UIWindowLevel.Alert + 1,
                BackgroundColor = UIColor.FromRGBA(30, 41, 59, 235),
                Alpha = (nfloat)Math.Clamp(configuration.Opacity, 0.4, 1.0)
            };
            _overlayWindow.Layer.CornerRadius = 14;
            _overlayWindow.ClipsToBounds = true;

            var rootController = new UIViewController();
            var label = new UILabel(new CGRect(14, 14, 240, 28))
            {
                Text = $"🛠 {AppStrings.Hud_FloatingTitle}",
                TextColor = UIColor.FromRGB(56, 189, 248)
            };
            rootController.View?.AddSubview(label);

            _overlayWindow.RootViewController = rootController;
            _overlayWindow.Hidden = false;
            _isVisible = true;
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

    public Task RefreshQuickActionsAsync(
        IReadOnlyList<OverlayQuickAction> quickActions,
        CancellationToken cancellationToken = default)
    {
        _quickActions = quickActions ?? [];
        return Task.CompletedTask;
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, CurrentStatus);
}
