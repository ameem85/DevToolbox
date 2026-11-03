using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.ViewModels;

public sealed partial class DeepLinkPresetItem(
    string title,
    string uri,
    string? packageName,
    Func<DeepLinkPresetItem, Task> onLaunch) : ObservableObject
{
    public string Title { get; } = title;

    public string Uri { get; } = uri;

    public string? PackageName { get; } = packageName;

    public string Subtitle => string.IsNullOrWhiteSpace(PackageName)
        ? Uri
        : $"{Uri} ({PackageName})";

    [RelayCommand]
    private Task LaunchPresetAsync() => onLaunch(this);
}

public sealed record DeepLinkHistoryEntry(
    string TimestampText,
    string Uri,
    string TargetScope,
    string ResultSummary);

public sealed partial class DeepLinkTesterViewModel : ObservableObject
{
    private readonly IAdbBridgeService _adbBridgeService;

    public DeepLinkTesterViewModel(IAdbBridgeService adbBridgeService)
    {
        _adbBridgeService = adbBridgeService;
        SeedDefaultPresets();
    }

    public ObservableCollection<DeepLinkPresetItem> Presets { get; } = [];

    public ObservableCollection<DeepLinkHistoryEntry> DispatchHistory { get; } = [];

    [ObservableProperty]
    public partial string DeepLinkUri { get; set; } = "devtoolbox://inspect/session?env=staging&mock=true";

    [ObservableProperty]
    public partial string TargetPackageName { get; set; } = "com.devtoolbox.companion";

    [ObservableProperty]
    public partial bool DispatchViaAdb { get; set; } = true;

    [ObservableProperty]
    public partial string StatusBanner { get; set; } = "Ready to dispatch custom URI schemes or Universal/App Links.";

    [RelayCommand]
    private async Task LaunchDeepLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(DeepLinkUri))
        {
            return;
        }

        await ExecuteDispatchAsync(DeepLinkUri.Trim(), TargetPackageName?.Trim());
    }

    [RelayCommand]
    private void SavePreset()
    {
        if (string.IsNullOrWhiteSpace(DeepLinkUri))
        {
            return;
        }

        var trimmedUri = DeepLinkUri.Trim();
        var title = $"Custom: {(trimmedUri.Length > 32 ? trimmedUri[..32] + "..." : trimmedUri)}";
        Presets.Insert(0, new DeepLinkPresetItem(
            title,
            trimmedUri,
            TargetPackageName?.Trim(),
            preset =>
            {
                DeepLinkUri = preset.Uri;
                TargetPackageName = preset.PackageName ?? string.Empty;
                return ExecuteDispatchAsync(preset.Uri, preset.PackageName);
            }));
    }

    private async Task ExecuteDispatchAsync(string uriText, string? packageName)
    {
        if (DispatchViaAdb)
        {
            var result = await _adbBridgeService.LaunchDeepLinkAsync(uriText, packageName);
            var summary = result.Success
                ? $"ADB VIEW Intent launched ({(string.IsNullOrWhiteSpace(result.StandardOutput) ? "OK" : result.StandardOutput.Replace('\r', ' ').Replace('\n', ' '))})"
                : $"ADB error: {result.StandardError}";

            StatusBanner = summary;
            DispatchHistory.Insert(0, new DeepLinkHistoryEntry(
                DateTimeOffset.Now.ToString("HH:mm:ss"),
                uriText,
                "ADB am start",
                summary));
            return;
        }

        try
        {
            if (Uri.TryCreate(uriText, UriKind.Absolute, out var parsedUri))
            {
                var opened = await Launcher.Default.TryOpenAsync(parsedUri);
                var summary = opened
                    ? "Opened URI via local OS Launcher"
                    : "OS Launcher reported no registered handler for URI scheme";

                StatusBanner = summary;
                DispatchHistory.Insert(0, new DeepLinkHistoryEntry(
                    DateTimeOffset.Now.ToString("HH:mm:ss"),
                    uriText,
                    "Local OS Launcher",
                    summary));
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Launcher error: {ex.Message}";
            DispatchHistory.Insert(0, new DeepLinkHistoryEntry(
                DateTimeOffset.Now.ToString("HH:mm:ss"),
                uriText,
                "Local OS Launcher",
                StatusBanner));
        }
    }

    private void SeedDefaultPresets()
    {
        Presets.Add(new DeepLinkPresetItem(
            "Inspect Active Session (Custom Scheme)",
            "devtoolbox://inspect/session?env=staging&mock=true",
            "com.devtoolbox.companion",
            p => ExecuteDispatchAsync(p.Uri, p.PackageName)));

        Presets.Add(new DeepLinkPresetItem(
            "Checkout Cart Deep-Link (App Link)",
            "https://api.sandbox.dev/checkout?cartId=QA-9001&promo=DEV100",
            null,
            p => ExecuteDispatchAsync(p.Uri, p.PackageName)));

        Presets.Add(new DeepLinkPresetItem(
            "Product Detail SKU Override",
            "myapp://catalog/product/SKU-40219?branch=104",
            null,
            p => ExecuteDispatchAsync(p.Uri, p.PackageName)));
    }
}
