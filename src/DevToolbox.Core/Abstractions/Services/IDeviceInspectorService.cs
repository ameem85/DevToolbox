namespace DevToolbox.Core.Abstractions.Services;

public sealed record DevicePropertyItem(
    string Category,
    string Key,
    string Value);

public sealed record DeviceInspectionReport(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<DevicePropertyItem> Items,
    string FormattedSummary);

/// <summary>
/// Collects runtime, hardware, OS, network interface, memory, and connected Android ADB device properties.
/// </summary>
public interface IDeviceInspectorService
{
    Task<DeviceInspectionReport> CaptureReportAsync(
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default);
}
