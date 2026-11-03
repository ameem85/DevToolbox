using DevToolbox.Core.Abstractions.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace DevToolbox.App.Platforms.Windows.Services;

/// <summary>
/// Windows implementation of <see cref="IOrientationStateService"/> supporting both
/// remote Android device orientation overrides via <see cref="IAdbBridgeService"/> and
/// local desktop window viewport rotation for responsive layout testing.
/// </summary>
public sealed class WindowsOrientationStateService(IAdbBridgeService adbBridge) : IOrientationStateService
{
    private OrientationStateSnapshot _currentState = new(
        DeviceOrientationMode.Landscape,
        TargetExecutionMode.LocalDevice,
        null,
        "Windows Desktop responsive viewport ready");

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
                TargetExecutionMode.RemoteAdbDevice,
                remoteDeviceSerial,
                adbResult.Success
                    ? $"Forced {mode} on Android device ({remoteDeviceSerial ?? "default"}) via ADB"
                    : $"ADB rotation command failed: {adbResult.StandardError}");

            StateChanged?.Invoke(this, _currentState);
            return _currentState;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var mainWindow = Application.Current?.Windows.FirstOrDefault();
            if (mainWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow is null)
            {
                return;
            }

            var targetSize = mode switch
            {
                DeviceOrientationMode.Portrait or DeviceOrientationMode.ReversePortrait => new SizeInt32(520, 920),
                DeviceOrientationMode.Landscape or DeviceOrientationMode.ReverseLandscape => new SizeInt32(1180, 760),
                _ => new SizeInt32(1100, 760)
            };

            appWindow.Resize(targetSize);
        }).ConfigureAwait(false);

        _currentState = new OrientationStateSnapshot(
            mode,
            TargetExecutionMode.LocalDevice,
            null,
            $"Desktop responsive viewport switched to {mode}");

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
        if (_currentState.ExecutionMode == TargetExecutionMode.RemoteAdbDevice)
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
                    ? $"ADB wm size {widthPx}x{heightPx} @ {densityDpi}dpi applied"
                    : $"ADB wm override failed: {result.StandardError}"
            };
        }
        else
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var mainWindow = Application.Current?.Windows.FirstOrDefault();
                if (mainWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                {
                    var hwnd = WindowNative.GetWindowHandle(nativeWindow);
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);
                    var scaledW = Math.Clamp(widthPx / 2, 420, 1600);
                    var scaledH = Math.Clamp(heightPx / 2, 420, 1020);
                    appWindow?.Resize(new SizeInt32(scaledW, scaledH));
                }
            }).ConfigureAwait(false);

            _currentState = _currentState with
            {
                StatusDetails = $"Simulated {widthPx}x{heightPx} ({densityDpi}dpi) viewport on Desktop"
            };
        }

        StateChanged?.Invoke(this, _currentState);
        return _currentState;
    }

    public async Task<OrientationStateSnapshot> ResetDisplayOverrideAsync(
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentState.ExecutionMode == TargetExecutionMode.RemoteAdbDevice)
        {
            await adbBridge.ResetDisplayOverrideAsync(remoteDeviceSerial, cancellationToken).ConfigureAwait(false);
        }

        await ApplyOrientationAsync(
            DeviceOrientationMode.Landscape,
            _currentState.ExecutionMode,
            remoteDeviceSerial,
            cancellationToken).ConfigureAwait(false);

        _currentState = _currentState with
        {
            StatusDetails = "Display size and density overrides reset"
        };

        StateChanged?.Invoke(this, _currentState);
        return _currentState;
    }
}
