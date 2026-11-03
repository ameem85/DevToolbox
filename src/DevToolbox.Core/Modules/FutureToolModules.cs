using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Modules;

public sealed record LogcatEntryRecord(
    DateTimeOffset Timestamp,
    string Level,
    string Tag,
    int ProcessId,
    string Message)
{
    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

    public string HeaderSummary => $"[{Level}] {Tag} (PID {ProcessId})";
}

/// <summary>
/// Plug-and-play module for real-time Android ADB <c>logcat</c> and unified diagnostic streaming.
/// </summary>
public sealed class LogcatReaderToolModule(ILogcatStreamService logcatStreamService) : IDevToolModule
{
    public const string ModuleId = "tool.logcat_reader";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_LogcatReader_Title";

    public string DescriptionResourceKey => "Module_LogcatReader_Description";

    public string IconGlyph => "📜";

    public string Route => "LogcatToolPage";

    public ToolModuleCategory Category => ToolModuleCategory.Diagnostics;

    public int Order => 40;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.All;

    public bool IsRunning => logcatStreamService.IsStreaming;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        logcatStreamService.StartStreamingAsync(null, null, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        logcatStreamService.StopStreamingAsync(cancellationToken);

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() =>
    [
        new OverlayQuickAction(
            "logcat.marker",
            "QuickAction_EmitLogMarker",
            "📌",
            ct =>
            {
                logcatStreamService.EmitDiagnosticEntry(
                    "I",
                    "QA.Marker",
                    $"Manual QA inspection checkpoint @ {DateTimeOffset.Now:HH:mm:ss}");
                return Task.CompletedTask;
            })
    ];
}

/// <summary>
/// Plug-and-play module for inspecting runtime hardware, ABI, display density, network interfaces, and Android system properties.
/// </summary>
public sealed class DeviceInfoInspectorToolModule(IDeviceInspectorService inspectorService) : IDevToolModule
{
    private bool _hasCaptured;

    public const string ModuleId = "tool.device_inspector";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_DeviceInspector_Title";

    public string DescriptionResourceKey => "Module_DeviceInspector_Description";

    public string IconGlyph => "🔍";

    public string Route => "DeviceInspectorPage";

    public ToolModuleCategory Category => ToolModuleCategory.Inspection;

    public int Order => 50;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.All;

    public bool IsRunning => _hasCaptured;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await inspectorService.CaptureReportAsync(null, cancellationToken).ConfigureAwait(false);
        _hasCaptured = true;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _hasCaptured = false;
        return Task.CompletedTask;
    }

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() => [];
}

/// <summary>
/// Plug-and-play module for testing custom URI schemes and App Links / Universal Links locally or via ADB.
/// </summary>
public sealed class DeepLinkTesterToolModule(IAdbBridgeService adbBridge) : IDevToolModule
{
    public const string ModuleId = "tool.deeplink_tester";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_DeepLinkTester_Title";

    public string DescriptionResourceKey => "Module_DeepLinkTester_Description";

    public string IconGlyph => "🔗";

    public string Route => "DeepLinkTesterPage";

    public ToolModuleCategory Category => ToolModuleCategory.DeviceState;

    public int Order => 60;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.All;

    public bool IsRunning => false;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await adbBridge.LaunchDeepLinkAsync("devtoolbox://inspect/session", null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() => [];
}
