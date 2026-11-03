using DevToolbox.Core.Modules;

namespace DevToolbox.Core.Abstractions.Services;

/// <summary>
/// Contract for real-time Android ADB <c>logcat</c> and unified application diagnostic streaming.
/// Streams entries via <see cref="IHighFrequencyUpdateSink{LogcatEntryRecord}"/>.
/// </summary>
public interface ILogcatStreamService
{
    event EventHandler<bool>? StreamingStateChanged;

    bool IsStreaming { get; }

    Task StartStreamingAsync(
        string? deviceSerial = null,
        string? tagFilter = null,
        CancellationToken cancellationToken = default);

    Task StopStreamingAsync(CancellationToken cancellationToken = default);

    Task ClearBufferAsync(string? deviceSerial = null, CancellationToken cancellationToken = default);

    IReadOnlyList<LogcatEntryRecord> GetRecentEntries();

    void EmitDiagnosticEntry(string level, string tag, string message);
}
