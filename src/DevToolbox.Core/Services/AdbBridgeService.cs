using System.Diagnostics;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Services;

/// <summary>
/// Executes ADB commands from Desktop to provision permissions (appops SYSTEM_ALERT_WINDOW),
/// force screen orientation changes, and configure reverse proxy port forwarding on connected Android devices.
/// </summary>
public sealed class AdbBridgeService : IAdbBridgeService
{
    public async Task<IReadOnlyList<AdbDeviceInfo>> GetConnectedDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAdbCommandAsync("devices -l", cancellationToken).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return [];
        }

        var devices = new List<AdbDeviceInfo>();
        var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('*'))
            {
                continue;
            }

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                continue;
            }

            var serial = tokens[0];
            var state = tokens[1];
            var model = ExtractTokenValue(tokens, "model:") ?? "Android Device";
            var product = ExtractTokenValue(tokens, "product:") ?? "unknown";
            devices.Add(new AdbDeviceInfo(serial, state, model, product));
        }

        return devices;
    }

    public Task<AdbCommandResult> GrantOverlayPermissionAsync(
        string packageName,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        // Uses adb appops to grant SYSTEM_ALERT_WINDOW permission prior to or during deployment/testing
        var prefix = BuildDevicePrefix(deviceSerial);
        return RunAdbCommandAsync(
            $"{prefix}shell appops set {packageName} SYSTEM_ALERT_WINDOW allow",
            cancellationToken);
    }

    public async Task<AdbCommandResult> SetDeviceOrientationAsync(
        DeviceOrientationMode orientation,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = BuildDevicePrefix(deviceSerial);

        if (orientation is DeviceOrientationMode.Sensor or DeviceOrientationMode.Unlocked)
        {
            return await RunAdbCommandAsync(
                $"{prefix}shell settings put system accelerometer_rotation 1",
                cancellationToken).ConfigureAwait(false);
        }

        var rotationValue = orientation switch
        {
            DeviceOrientationMode.Portrait => 0,
            DeviceOrientationMode.Landscape => 1,
            DeviceOrientationMode.ReversePortrait => 2,
            DeviceOrientationMode.ReverseLandscape => 3,
            _ => 0
        };

        var disableAutoRotate = await RunAdbCommandAsync(
            $"{prefix}shell settings put system accelerometer_rotation 0",
            cancellationToken).ConfigureAwait(false);

        if (!disableAutoRotate.Success)
        {
            return disableAutoRotate;
        }

        return await RunAdbCommandAsync(
            $"{prefix}shell settings put system user_rotation {rotationValue}",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<AdbCommandResult> ConfigureReverseProxyAsync(
        int devicePort,
        int hostPort,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = BuildDevicePrefix(deviceSerial);
        return RunAdbCommandAsync(
            $"{prefix}reverse tcp:{devicePort} tcp:{hostPort}",
            cancellationToken);
    }

    public Task<AdbCommandResult> RemoveReverseProxyAsync(
        int devicePort,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = BuildDevicePrefix(deviceSerial);
        return RunAdbCommandAsync(
            $"{prefix}reverse --remove tcp:{devicePort}",
            cancellationToken);
    }

    public async Task<AdbCommandResult> SetDisplayOverrideAsync(
        int widthPx,
        int heightPx,
        int densityDpi,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = BuildDevicePrefix(deviceSerial);
        var sizeResult = await RunAdbCommandAsync(
            $"{prefix}shell wm size {widthPx}x{heightPx}",
            cancellationToken).ConfigureAwait(false);

        if (!sizeResult.Success)
        {
            return sizeResult;
        }

        return await RunAdbCommandAsync(
            $"{prefix}shell wm density {densityDpi}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdbCommandResult> ResetDisplayOverrideAsync(
        string? deviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = BuildDevicePrefix(deviceSerial);
        await RunAdbCommandAsync($"{prefix}shell wm size reset", cancellationToken).ConfigureAwait(false);
        return await RunAdbCommandAsync($"{prefix}shell wm density reset", cancellationToken).ConfigureAwait(false);
    }

    public Task<AdbCommandResult> LaunchDeepLinkAsync(
        string deepLinkUri,
        string? packageName = null,
        string? deviceSerial = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deepLinkUri);

        var prefix = BuildDevicePrefix(deviceSerial);
        var packageArg = string.IsNullOrWhiteSpace(packageName) ? string.Empty : $" -p {packageName}";
        return RunAdbCommandAsync(
            $"{prefix}shell am start -W -a android.intent.action.VIEW -d \"{deepLinkUri}\"{packageArg}",
            cancellationToken);
    }

    private static string BuildDevicePrefix(string? serial) =>
        string.IsNullOrWhiteSpace(serial) ? string.Empty : $"-s {serial} ";

    private static string? ExtractTokenValue(string[] tokens, string prefix)
    {
        var match = tokens.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?[prefix.Length..];
    }

    private static string ResolveAdbExecutablePath()
    {
        var envSdk = Environment.GetEnvironmentVariable("ANDROID_HOME")
                     ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (!string.IsNullOrWhiteSpace(envSdk))
        {
            var candidate = Path.Combine(envSdk, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var defaultWindowsCandidate = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe");
            if (File.Exists(defaultWindowsCandidate))
            {
                return defaultWindowsCandidate;
            }
        }

        return OperatingSystem.IsWindows() ? "adb.exe" : "adb";
    }

    private static async Task<AdbCommandResult> RunAdbCommandAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveAdbExecutablePath(),
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new AdbCommandResult(false, -1, string.Empty, "Failed to start adb process.");
            }

            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdOut = await stdOutTask.ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

            return new AdbCommandResult(process.ExitCode == 0, process.ExitCode, stdOut.Trim(), stdErr.Trim());
        }
        catch (Exception ex)
        {
            return new AdbCommandResult(false, -1, string.Empty, ex.Message);
        }
    }
}
