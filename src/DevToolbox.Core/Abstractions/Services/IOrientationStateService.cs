namespace DevToolbox.Core.Abstractions.Services;

public enum DeviceOrientationMode
{
    Portrait = 0,
    Landscape = 1,
    ReversePortrait = 2,
    ReverseLandscape = 3,
    Sensor = 4,
    Unlocked = 5
}

public enum TargetExecutionMode
{
    LocalDevice = 0,
    RemoteAdbDevice = 1
}

public sealed record OrientationStateSnapshot(
    DeviceOrientationMode CurrentOrientation,
    TargetExecutionMode ExecutionMode,
    string? ConnectedDeviceSerial,
    string StatusDetails);

/// <summary>
/// Cross-platform contract for forcing or overriding screen orientation and display metrics
/// on the local device or a connected mobile companion over ADB.
/// </summary>
public interface IOrientationStateService
{
    event EventHandler<OrientationStateSnapshot>? StateChanged;

    OrientationStateSnapshot CurrentState { get; }

    Task<OrientationStateSnapshot> ApplyOrientationAsync(
        DeviceOrientationMode mode,
        TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<OrientationStateSnapshot> CycleOrientationAsync(
        TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<OrientationStateSnapshot> ApplyDisplayOverrideAsync(
        int widthPx,
        int heightPx,
        int densityDpi,
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<OrientationStateSnapshot> ResetDisplayOverrideAsync(
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default);
}
