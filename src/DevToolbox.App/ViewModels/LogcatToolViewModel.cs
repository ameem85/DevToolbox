using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevToolbox.Core.Abstractions.Services;
using DevToolbox.Core.Modules;

namespace DevToolbox.App.ViewModels;

public sealed partial class LogcatToolViewModel : ObservableObject, IDisposable
{
    private readonly ILogcatStreamService _logcatService;
    private readonly IDisposable _logSinkSubscription;
    private readonly List<LogcatEntryRecord> _allEntries = [];

    public LogcatToolViewModel(
        ILogcatStreamService logcatService,
        IHighFrequencyUpdateSink<LogcatEntryRecord> logSink)
    {
        _logcatService = logcatService;

        // Subscribe directly to IoC high-frequency sink for log stream updates
        _logSinkSubscription = logSink.Subscribe(
            OnLogBatchReceived,
            () => MainThread.BeginInvokeOnMainThread(() =>
            {
                _allEntries.Clear();
                FilteredEntries.Clear();
                RefreshStatus();
            }));

        _logcatService.StreamingStateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshStatus);

        foreach (var item in _logcatService.GetRecentEntries())
        {
            _allEntries.Add(item);
        }

        ApplyFilters();
        RefreshStatus();
    }

    public ObservableCollection<LogcatEntryRecord> FilteredEntries { get; } = [];

    public IReadOnlyList<string> AvailableLevels { get; } = ["ALL", "V", "D", "I", "W", "E", "F"];

    [ObservableProperty]
    public partial string SelectedLevel { get; set; } = "ALL";

    [ObservableProperty]
    public partial string TagFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StreamStatusBadge { get; set; } = string.Empty;

    partial void OnSelectedLevelChanged(string value) => ApplyFilters();

    partial void OnTagFilterChanged(string value) => ApplyFilters();

    partial void OnSearchQueryChanged(string value) => ApplyFilters();

    [RelayCommand]
    private async Task StartStreamAsync()
    {
        await _logcatService.StartStreamingAsync(null, TagFilter);
        RefreshStatus();
    }

    [RelayCommand]
    private async Task StopStreamAsync()
    {
        await _logcatService.StopStreamingAsync();
        RefreshStatus();
    }

    [RelayCommand]
    private void EmitMarker()
    {
        _logcatService.EmitDiagnosticEntry(
            "I",
            "QA.Checkpoint",
            $"Manual QA verification marker emitted at {DateTimeOffset.Now:HH:mm:ss.fff}");
    }

    [RelayCommand]
    private async Task ClearBufferAsync()
    {
        await _logcatService.ClearBufferAsync();
        RefreshStatus();
    }

    public void Dispose()
    {
        _logSinkSubscription.Dispose();
    }

    private void OnLogBatchReceived(IReadOnlyList<LogcatEntryRecord> batch)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var entry in batch)
            {
                _allEntries.Insert(0, entry);
                if (_allEntries.Count > 400)
                {
                    _allEntries.RemoveAt(_allEntries.Count - 1);
                }

                if (MatchesFilter(entry))
                {
                    FilteredEntries.Insert(0, entry);
                    if (FilteredEntries.Count > 250)
                    {
                        FilteredEntries.RemoveAt(FilteredEntries.Count - 1);
                    }
                }
            }

            RefreshStatus();
        });
    }

    private void ApplyFilters()
    {
        FilteredEntries.Clear();
        foreach (var entry in _allEntries.Where(MatchesFilter).Take(250))
        {
            FilteredEntries.Add(entry);
        }
    }

    private bool MatchesFilter(LogcatEntryRecord entry)
    {
        if (!string.Equals(SelectedLevel, "ALL", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry.Level, SelectedLevel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(TagFilter) &&
            !entry.Tag.Contains(TagFilter.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery) &&
            !entry.Message.Contains(SearchQuery.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !entry.Tag.Contains(SearchQuery.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void RefreshStatus()
    {
        StreamStatusBadge = _logcatService.IsStreaming
            ? $"STREAMING • {FilteredEntries.Count} Shown ({_allEntries.Count} Buffered)"
            : $"IDLE • {FilteredEntries.Count} Shown ({_allEntries.Count} Buffered)";
    }
}
