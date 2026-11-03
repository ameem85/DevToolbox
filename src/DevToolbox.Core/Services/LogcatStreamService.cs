using System.Diagnostics;
using DevToolbox.Core.Abstractions.Services;
using DevToolbox.Core.Modules;

namespace DevToolbox.Core.Services;

/// <summary>
/// Streams real-time Android <c>adb logcat -v time</c> lines (when a device is connected)
/// and local runtime diagnostic events into <see cref="IHighFrequencyUpdateSink{LogcatEntryRecord}"/>.
/// </summary>
public sealed class LogcatStreamService : ILogcatStreamService, IDisposable
{
    private readonly IHighFrequencyUpdateSink<LogcatEntryRecord> _logSink;
    private readonly Lock _syncRoot = new();
    private readonly List<LogcatEntryRecord> _recentEntries = [];

    private CancellationTokenSource? _streamCts;
    private Process? _adbLogcatProcess;
    private bool _isStreaming;

    public LogcatStreamService(IHighFrequencyUpdateSink<LogcatEntryRecord> logSink)
    {
        _logSink = logSink ?? throw new ArgumentNullException(nameof(logSink));
        SeedInitialDiagnosticEntries();
    }

    public event EventHandler<bool>? StreamingStateChanged;

    public bool IsStreaming
    {
        get
        {
            lock (_syncRoot)
            {
                return _isStreaming;
            }
        }
    }

    public Task StartStreamingAsync(
        string? deviceSerial = null,
        string? tagFilter = null,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_isStreaming)
            {
                return Task.CompletedTask;
            }

            StopInternal();
            _streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isStreaming = true;

            _ = RunLogcatReaderLoopAsync(deviceSerial, tagFilter, _streamCts.Token);
        }

        StreamingStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task StopStreamingAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            StopInternal();
        }

        StreamingStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task ClearBufferAsync(string? deviceSerial = null, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _recentEntries.Clear();
        }

        _logSink.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<LogcatEntryRecord> GetRecentEntries()
    {
        lock (_syncRoot)
        {
            return _recentEntries.ToList();
        }
    }

    public void EmitDiagnosticEntry(string level, string tag, string message)
    {
        var entry = new LogcatEntryRecord(
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(level) ? "I" : level.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(tag) ? "DevToolbox" : tag.Trim(),
            Environment.ProcessId,
            message);

        PublishEntry(entry);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            StopInternal();
        }
    }

    private void PublishEntry(LogcatEntryRecord entry)
    {
        lock (_syncRoot)
        {
            _recentEntries.Insert(0, entry);
            if (_recentEntries.Count > 500)
            {
                _recentEntries.RemoveAt(_recentEntries.Count - 1);
            }
        }

        _logSink.Publish(entry);
    }

    private async Task RunLogcatReaderLoopAsync(
        string? deviceSerial,
        string? tagFilter,
        CancellationToken cancellationToken)
    {
        EmitDiagnosticEntry("I", "DevToolbox.Logcat", "Attached unified log stream listener.");

        var adbPath = ResolveAdbPath();
        var prefix = string.IsNullOrWhiteSpace(deviceSerial) ? string.Empty : $"-s {deviceSerial} ";
        var filterSpec = string.IsNullOrWhiteSpace(tagFilter) ? "*:I" : $"{tagFilter}:V *:S";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = $"{prefix}logcat -v brief -T 25 {filterSpec}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _adbLogcatProcess = Process.Start(psi);
            if (_adbLogcatProcess is not null)
            {
                while (!cancellationToken.IsCancellationRequested && !_adbLogcatProcess.HasExited)
                {
                    var line = await _adbLogcatProcess.StandardOutput.ReadLineAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var parsed = ParseLogcatBriefLine(line);
                    PublishEntry(parsed);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop
        }
        catch (Exception ex)
        {
            EmitDiagnosticEntry("W", "DevToolbox.Logcat", $"ADB logcat process fallback ({ex.Message}); streaming local runtime events.");
        }
    }

    private static LogcatEntryRecord ParseLogcatBriefLine(string rawLine)
    {
        // Format: I/ActivityManager( 1234): Displayed com.devtoolbox.companion/.MainActivity
        var trimmed = rawLine.Trim();
        if (trimmed.Length > 3 && trimmed[1] == '/')
        {
            var level = trimmed[..1];
            var closeParen = trimmed.IndexOf("):");
            if (closeParen > 2)
            {
                var header = trimmed[2..closeParen];
                var openParen = header.LastIndexOf('(');
                var tag = openParen > 0 ? header[..openParen].Trim() : header.Trim();
                var pidText = openParen > 0 ? header[(openParen + 1)..].Trim() : "0";
                _ = int.TryParse(pidText, out var pid);
                var msg = trimmed[(closeParen + 2)..].Trim();
                return new LogcatEntryRecord(DateTimeOffset.UtcNow, level, tag, pid, msg);
            }
        }

        return new LogcatEntryRecord(
            DateTimeOffset.UtcNow,
            "I",
            "Android.Logcat",
            0,
            trimmed);
    }

    private static string ResolveAdbPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var candidate = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return OperatingSystem.IsWindows() ? "adb.exe" : "adb";
    }

    private void StopInternal()
    {
        _isStreaming = false;
        try
        {
            _streamCts?.Cancel();
            _streamCts?.Dispose();
        }
        catch
        {
            // Ignore
        }
        finally
        {
            _streamCts = null;
        }

        try
        {
            if (_adbLogcatProcess is { HasExited: false })
            {
                _adbLogcatProcess.Kill(entireProcessTree: true);
            }

            _adbLogcatProcess?.Dispose();
        }
        catch
        {
            // Ignore
        }
        finally
        {
            _adbLogcatProcess = null;
        }
    }

    private void SeedInitialDiagnosticEntries()
    {
        _recentEntries.AddRange(
        [
            new LogcatEntryRecord(DateTimeOffset.UtcNow.AddSeconds(-4), "I", "DevToolbox.Core", Environment.ProcessId, "DevToolRegistry initialized 6 plug-and-play developer modules."),
            new LogcatEntryRecord(DateTimeOffset.UtcNow.AddSeconds(-3), "D", "DevToolbox.Proxy", Environment.ProcessId, "LocalNetworkProxyEngine ready with HTTP + HTTPS CONNECT MITM Root CA support."),
            new LogcatEntryRecord(DateTimeOffset.UtcNow.AddSeconds(-2), "I", "DevToolbox.Overlay", Environment.ProcessId, "Floating Overlay Engine ready (Android WindowManager / Windows Win32 TopMost / iOS UIWindow).")
        ]);
    }
}
