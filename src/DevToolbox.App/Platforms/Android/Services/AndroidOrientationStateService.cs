using Android.Content.PM;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.Platforms.Android.Services;

/// <summary>
/// Android implementation of <see cref="IOrientationStateService"/> supporting both direct
/// in-process <c>Activity.RequestedOrientation</c> overrides and remote ADB rotation overrides.
/// </summary>
public sealed class AndroidOrientationStateService(IAdbBridgeService adbBridge) : IOrientationStateService
{
    private OrientationStateSnapshot _currentState = new(
        DeviceOrientationMode.Unlocked,
        TargetExecutionMode.LocalDevice,
        null,
        "Default Android activity orientation");

    public event EventHandler<OrientationStateSnapshot>? StateChanged;

    public OrientationStateSnapshot CurrentState => _currentState;

    public async Task<OrientationStateSnapshot> ApplyOrientationAsync(
        DeviceOrientationMode mode,
        TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        if (executionMode == TargetExecutionMode.RemoteAdbDevice)
        {
            var adbResult = await adbBridge.SetDeviceOrientationAsync(mode, remoteDeviceSerial, cancellationToken)
                .ConfigureAwait(false);

            _currentState = new OrientationStateSnapshot(
                mode,
                executionMode,
                remoteDeviceSerial,
                adbResult.Success
                    ? $"Applied {mode} via ADB settings put system user_rotation"
                    : $"ADB error: {adbResult.StandardError}");

            StateChanged?.Invoke(this, _currentState);
            return _currentState;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var activity = Platform.CurrentActivity;
            if (activity is null)
            {
                return;
            }

            activity.RequestedOrientation = mode switch
            {
                DeviceOrientationMode.Portrait => ScreenOrientation.Portrait,
                DeviceOrientationMode.Landscape => ScreenOrientation.Landscape,
                DeviceOrientationMode.ReversePortrait => ScreenOrientation.ReversePortrait,
                DeviceOrientationMode.ReverseLandscape => ScreenOrientation.ReverseLandscape,
                DeviceOrientationMode.Sensor => ScreenOrientation.FullSensor,
                _ => ScreenOrientation.Unspecified
            };
        }).ConfigureAwait(false);

        _currentState = new OrientationStateSnapshot(
            mode,
            TargetExecutionMode.LocalDevice,
            null,
            $"Android Activity.RequestedOrientation forced to {mode}");

        StateChanged?.Invoke(this, _currentState);
        return _currentState;
    }

    public Task<OrientationStateSnapshot> CycleOrientationAsync(
        TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var next = _currentState.CurrentOrientation switch
        {
            DeviceOrientationMode.Portrait => DeviceOrientationMode.Landscape,
            DeviceOrientationMode.Landscape => DeviceOrientationMode.ReversePortrait,
            DeviceOrientationMode.ReversePortrait => DeviceOrientationMode.ReverseLandscape,
            DeviceOrientationMode.ReverseLandscape => DeviceOrientationMode.Sensor,
            _ => DeviceOrientationMode.Portrait
        };

        return ApplyOrientationAsync(next, executionMode, remoteDeviceSerial, cancellationToken);
    }

    public async Task<OrientationStateSnapshot> ApplyDisplayOverrideAsync(
        int widthPx,
        int heightPx,
        int densityDpi,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var result = await adbBridge.SetDisplayOverrideAsync(
            widthPx,
            heightPx,
            densityDpi,
            remoteDeviceSerial,
            cancellationToken).ConfigureAwait(false);

        _currentState = _currentState with
        {
            StatusDetails = result.Success
                ? $"Display override applied: {widthPx}x{heightPx} @ {densityDpi}dpi"
                : $"Display override requires ADB shell permissions: {result.StandardError}"
        };

        StateChanged?.Invoke(this, _currentState);
        return _currentState;
    }

    public async Task<OrientationStateSnapshot> ResetDisplayOverrideAsync(
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        await adbBridge.ResetDisplayOverrideAsync(remoteDeviceSerial, cancellationToken).ConfigureAwait(false);
        _currentState = _currentState with
        {
            StatusDetails = "Display size and density reset to hardware defaults"
        };

        StateChanged?.Invoke(this, _currentState);
        return _currentState;
    }
}
