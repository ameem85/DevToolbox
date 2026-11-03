namespace DevToolbox.Core.Abstractions.Services;

public sealed record AdbDeviceInfo(
    string Serial,
    string State,
    string Model,
    string Product);

public sealed record AdbCommandResult(
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError);

/// <summary>
/// Desktop-to-Mobile ADB companion bridge for controlling connected Android devices from Windows Desktop.
/// Supports permission provisioning (appops SYSTEM_ALERT_WINDOW), orientation overrides, reverse proxy tunnels, and deep-links.
/// </summary>
public interface IAdbBridgeService
{
    Task<IReadOnlyList<AdbDeviceInfo>> GetConnectedDevicesAsync(CancellationToken cancellationToken = default);

    Task<AdbCommandResult> GrantOverlayPermissionAsync(
        string packageName,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> SetDeviceOrientationAsync(
        DeviceOrientationMode orientation,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> ConfigureReverseProxyAsync(
        int devicePort,
        int hostPort,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> RemoveReverseProxyAsync(
        int devicePort,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> SetDisplayOverrideAsync(
        int widthPx,
        int heightPx,
        int densityDpi,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> ResetDisplayOverrideAsync(
        string? deviceSerial = null,
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> LaunchDeepLinkAsync(
        string deepLinkUri,
        string? packageName = null,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default);
}
