using DevToolbox.Core.Abstractions.Services;
using UIKit;

namespace DevToolbox.App.Platforms.iOS.Services;

/// <summary>
/// iOS implementation of <see cref="IOrientationStateService"/> using
/// <see cref="UIWindowScene.RequestGeometryUpdate"/> on iOS 16+.
/// </summary>
public sealed class IosOrientationStateService : IOrientationStateService
{
    private OrientationStateSnapshot _currentState = new(
        DeviceOrientationMode.Portrait,
        TargetExecutionMode.LocalDevice,
        null,
        "iOS UIWindowScene geometry orientation controller ready");

    public event EventHandler<OrientationStateSnapshot>? StateChanged;

    public OrientationStateSnapshot CurrentState => _currentState;

    public async Task<OrientationStateSnapshot> ApplyOrientationAsync(
        DeviceOrientationMode mode,
        TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!OperatingSystem.IsIOSVersionAtLeast(16))
            {
                return;
            }

            var scene = UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .FirstOrDefault();

            if (scene is null)
            {
                return;
            }

            var mask = mode switch
            {
                DeviceOrientationMode.Portrait => UIInterfaceOrientationMask.Portrait,
                DeviceOrientationMode.Landscape => UIInterfaceOrientationMask.LandscapeRight,
                DeviceOrientationMode.ReversePortrait => UIInterfaceOrientationMask.PortraitUpsideDown,
                DeviceOrientationMode.ReverseLandscape => UIInterfaceOrientationMask.LandscapeLeft,
                _ => UIInterfaceOrientationMask.All
            };

            var preferences = new UIWindowSceneGeometryPreferencesIOS(mask);
            scene.RequestGeometryUpdate(preferences, _ => { });
        }).ConfigureAwait(false);

        _currentState = new OrientationStateSnapshot(
            mode,
            TargetExecutionMode.LocalDevice,
            null,
            $"iOS UIWindowScene geometry requested: {mode}");

        StateChanged?.Invoke(this, _currentState);
        return _currentState;
    }

    public Task<OrientationStateSnapshot> CycleOrientationAsync(
        TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var next = _currentState.CurrentOrientation == DeviceOrientationMode.Portrait
            ? DeviceOrientationMode.Landscape
            : DeviceOrientationMode.Portrait;

        return ApplyOrientationAsync(next, executionMode, remoteDeviceSerial, cancellationToken);
    }

    public Task<OrientationStateSnapshot> ApplyDisplayOverrideAsync(
        int widthPx,
        int heightPx,
        int densityDpi,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        _currentState = _currentState with
        {
            StatusDetails = $"iOS Sandbox: display size override ({widthPx}x{heightPx}) simulated via layout container"
        };

        StateChanged?.Invoke(this, _currentState);
        return Task.FromResult(_currentState);
    }

    public Task<OrientationStateSnapshot> ResetDisplayOverrideAsync(
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        _currentState = _currentState with
        {
            StatusDetails = "iOS display layout container reset"
        };

        StateChanged?.Invoke(this, _currentState);
        return Task.FromResult(_currentState);
    }
}
