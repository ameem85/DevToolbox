using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.ViewModels;

public sealed partial class DeviceInspectorViewModel : ObservableObject
{
    private readonly IDeviceInspectorService _inspectorService;
    private readonly List<DevicePropertyItem> _allProperties = [];
    private string _latestMarkdownReport = string.Empty;

    public DeviceInspectorViewModel(IDeviceInspectorService inspectorService)
    {
        _inspectorService = inspectorService;
        _ = RefreshTelemetryAsync();
    }

    public ObservableCollection<DevicePropertyItem> FilteredProperties { get; } = [];

    [ObservableProperty]
    public partial string SearchFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = "Capturing system and device telemetry...";

    partial void OnSearchFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshTelemetryAsync()
    {
        var report = await _inspectorService.CaptureReportAsync();
        _latestMarkdownReport = report.FormattedSummary;

        _allProperties.Clear();
        _allProperties.AddRange(report.Items);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyFilter();
            StatusSummary = $"Captured {_allProperties.Count} diagnostic properties at {report.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
        });
    }

    [RelayCommand]
    private async Task CopyReportAsync()
    {
        if (string.IsNullOrWhiteSpace(_latestMarkdownReport))
        {
            await RefreshTelemetryAsync();
        }

        await Clipboard.Default.SetTextAsync(_latestMarkdownReport);
        StatusSummary = "Copied Markdown diagnostic report to system clipboard.";
    }

    private void ApplyFilter()
    {
        FilteredProperties.Clear();
        var query = SearchFilter?.Trim();
        var matches = string.IsNullOrWhiteSpace(query)
            ? _allProperties
            : _allProperties.Where(p =>
                p.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Value.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matches)
        {
            FilteredProperties.Add(item);
        }
    }
}
