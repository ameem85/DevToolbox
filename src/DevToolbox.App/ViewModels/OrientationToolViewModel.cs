using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.ViewModels;

public sealed partial class OrientationToolViewModel : ObservableObject
{
    private readonly IOrientationStateService _orientationService;
    private readonly IAdbBridgeService _adbBridgeService;

    public OrientationToolViewModel(
        IOrientationStateService orientationService,
        IAdbBridgeService adbBridgeService)
    {
        _orientationService = orientationService;
        _adbBridgeService = adbBridgeService;

        _orientationService.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshState);
        RefreshState();
    }

    public ObservableCollection<AdbDeviceInfo> ConnectedDevices { get; } = [];

    [ObservableProperty]
    public partial bool UseRemoteAdbDevice { get; set; }

    [ObservableProperty]
    public partial AdbDeviceInfo? SelectedAdbDevice { get; set; }

    [ObservableProperty]
    public partial string CurrentOrientationBadge { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [RelayCommand]
    private async Task RefreshAdbDevicesAsync()
    {
        var devices = await _adbBridgeService.GetConnectedDevicesAsync();
        ConnectedDevices.Clear();
        foreach (var device in devices)
        {
            ConnectedDevices.Add(device);
        }

        SelectedAdbDevice = ConnectedDevices.FirstOrDefault();
        StatusMessage = ConnectedDevices.Count > 0
            ? $"Found {ConnectedDevices.Count} ADB device(s): {string.Join(", ", ConnectedDevices.Select(d => $"{d.Model} ({d.Serial})"))}"
            : "No connected ADB devices detected. Connect a device or start an Android emulator.";
    }

    [RelayCommand]
    private Task SetPortraitAsync() => ApplyModeAsync(DeviceOrientationMode.Portrait);

    [RelayCommand]
    private Task SetLandscapeAsync() => ApplyModeAsync(DeviceOrientationMode.Landscape);

    [RelayCommand]
    private Task SetReversePortraitAsync() => ApplyModeAsync(DeviceOrientationMode.ReversePortrait);

    [RelayCommand]
    private Task SetReverseLandscapeAsync() => ApplyModeAsync(DeviceOrientationMode.ReverseLandscape);

    [RelayCommand]
    private Task SetSensorAsync() => ApplyModeAsync(DeviceOrientationMode.Sensor);

    [RelayCommand]
    private Task ResetOrientationAsync() => ApplyModeAsync(DeviceOrientationMode.Unlocked);

    [RelayCommand]
    private Task ApplyPhonePresetAsync() =>
        _orientationService.ApplyDisplayOverrideAsync(1080, 2400, 420, SelectedAdbDevice?.Serial);

    [RelayCommand]
    private Task ApplyRuggedHandheldPresetAsync() =>
        _orientationService.ApplyDisplayOverrideAsync(1080, 2160, 480, SelectedAdbDevice?.Serial);

    [RelayCommand]
    private Task ApplyTabletPresetAsync() =>
        _orientationService.ApplyDisplayOverrideAsync(2560, 1600, 320, SelectedAdbDevice?.Serial);

    [RelayCommand]
    private Task ResetDisplayPresetAsync() =>
        _orientationService.ResetDisplayOverrideAsync(SelectedAdbDevice?.Serial);

    private Task ApplyModeAsync(DeviceOrientationMode mode)
    {
        var executionMode = UseRemoteAdbDevice
            ? TargetExecutionMode.RemoteAdbDevice
            : TargetExecutionMode.LocalDevice;

        return _orientationService.ApplyOrientationAsync(
            mode,
            executionMode,
            SelectedAdbDevice?.Serial);
    }

    private void RefreshState()
    {
        var snapshot = _orientationService.CurrentState;
        CurrentOrientationBadge = $"Active Orientation: {snapshot.CurrentOrientation} ({snapshot.ExecutionMode})";
        StatusMessage = snapshot.StatusDetails;
    }
}
