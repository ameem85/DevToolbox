using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Modules;

public sealed record LogcatEntryRecord(
    DateTimeOffset Timestamp,
    string Level,
    string Tag,
    int ProcessId,
    string Message);

/// <summary>
/// Extensibility module template demonstrating how a real-time Logcat / Unified Console reader
/// plugs into <see cref="IDevToolRegistry"/> and streams via <see cref="IHighFrequencyUpdateSink{LogcatEntryRecord}"/>.
/// </summary>
public sealed class LogcatReaderToolModule(IHighFrequencyUpdateSink<LogcatEntryRecord> logSink) : IDevToolModule
{
    private bool _isStreaming;

    public const string ModuleId = "tool.logcat_reader";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_LogcatReader_Title";

    public string DescriptionResourceKey => "Module_LogcatReader_Description";

    public string IconGlyph => "📜";

    public string Route => "DashboardPage";

    public ToolModuleCategory Category => ToolModuleCategory.Diagnostics;

    public int Order => 40;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.Android | SupportedPlatformFlags.Windows;

    public bool IsRunning => _isStreaming;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isStreaming = true;
        logSink.Publish(new LogcatEntryRecord(
            DateTimeOffset.UtcNow,
            "I",
            "DevToolbox.Logcat",
            Environment.ProcessId,
            "Logcat stream listener attached."));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isStreaming = false;
        return Task.CompletedTask;
    }

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() => [];
}

/// <summary>
/// Extensibility module template for inspecting runtime hardware, ABI, display density, and environment flags.
/// </summary>
public sealed class DeviceInfoInspectorToolModule : IDevToolModule
{
    public const string ModuleId = "tool.device_inspector";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_DeviceInspector_Title";

    public string DescriptionResourceKey => "Module_DeviceInspector_Description";

    public string IconGlyph => "🔍";

    public string Route => "DashboardPage";

    public ToolModuleCategory Category => ToolModuleCategory.Inspection;

    public int Order => 50;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.All;

    public bool IsRunning => false;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() => [];
}

/// <summary>
/// Extensibility module template for testing custom URI schemes and App Links / Universal Links locally or via ADB.
/// </summary>
public sealed class DeepLinkTesterToolModule(IAdbBridgeService adbBridge) : IDevToolModule
{
    public const string ModuleId = "tool.deeplink_tester";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_DeepLinkTester_Title";

    public string DescriptionResourceKey => "Module_DeepLinkTester_Description";

    public string IconGlyph => "🔗";

    public string Route => "DashboardPage";

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
