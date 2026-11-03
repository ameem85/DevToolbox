using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Services;

/// <summary>
/// Gathers comprehensive host, runtime, network interface, memory, and connected Android device diagnostics.
/// </summary>
public sealed class DeviceInspectorService(IAdbBridgeService adbBridge) : IDeviceInspectorService
{
    public async Task<DeviceInspectionReport> CaptureReportAsync(
        string? remoteDeviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<DevicePropertyItem>
        {
            new("Host & OS", "OS Description", RuntimeInformation.OSDescription),
            new("Host & OS", "OS Architecture", RuntimeInformation.OSArchitecture.ToString()),
            new("Host & OS", "Process Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
            new("Host & OS", ".NET Runtime", RuntimeInformation.FrameworkDescription),
            new("Host & OS", "Machine Name", Environment.MachineName),
            new("Host & OS", "Logical Processors", Environment.ProcessorCount.ToString()),
            new("Memory & Process", "Process ID (PID)", Environment.ProcessId.ToString()),
            new("Memory & Process", "Working Set (MB)", (Environment.WorkingSet / (1024 * 1024)).ToString("N1")),
            new("Memory & Process", "GC Managed Heap (MB)", (GC.GetTotalMemory(false) / (1024.0 * 1024.0)).ToString("N2"))
        };

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                     n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var ipv4 = nic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();

                if (!string.IsNullOrWhiteSpace(ipv4))
                {
                    items.Add(new DevicePropertyItem("Network Interfaces", nic.Name, $"{ipv4} ({nic.NetworkInterfaceType})"));
                }
            }
        }
        catch
        {
            // Ignore network enumeration issues on restricted sandboxes
        }

        var connectedDevices = await adbBridge.GetConnectedDevicesAsync(cancellationToken).ConfigureAwait(false);
        if (connectedDevices.Count > 0)
        {
            foreach (var device in connectedDevices)
            {
                items.Add(new DevicePropertyItem(
                    "Connected Android Device",
                    $"ADB Device ({device.Serial})",
                    $"Model: {device.Model} • Product: {device.Product} • State: {device.State}"));
            }

            var targetSerial = remoteDeviceSerial ?? connectedDevices[0].Serial;
            var androidProps = await adbBridge.GetAndroidDevicePropertiesAsync(targetSerial, cancellationToken)
                .ConfigureAwait(false);

            foreach (var kvp in androidProps)
            {
                items.Add(new DevicePropertyItem("Android System Properties", kvp.Key, kvp.Value));
            }
        }
        else
        {
            items.Add(new DevicePropertyItem(
                "Connected Android Device",
                "ADB Status",
                "No USB/Wi-Fi Android device attached"));
        }

        var summaryBuilder = new StringBuilder();
        summaryBuilder.AppendLine($"# DevToolbox Diagnostic Snapshot ({DateTimeOffset.UtcNow:O})");
        foreach (var group in items.GroupBy(i => i.Category))
        {
            summaryBuilder.AppendLine($"## {group.Key}");
            foreach (var prop in group)
            {
                summaryBuilder.AppendLine($"- **{prop.Key}**: `{prop.Value}`");
            }
        }

        return new DeviceInspectionReport(
            DateTimeOffset.UtcNow,
            items,
            summaryBuilder.ToString());
    }
}
