namespace DevToolbox.Core.Abstractions.Plugins;

public enum ToolModuleCategory
{
    Overlay = 0,
    Network = 1,
    DeviceState = 2,
    Diagnostics = 3,
    Inspection = 4
}

[Flags]
public enum SupportedPlatformFlags
{
    None = 0,
    Android = 1 << 0,
    iOS = 1 << 1,
    Windows = 1 << 2,
    All = Android | iOS | Windows
}

public sealed record OverlayQuickAction(
    string ActionId,
    string LabelResourceKey,
    string IconGlyph,
    Func<CancellationToken, Task> ExecuteAsync);

/// <summary>
/// Contract implemented by every plug-and-play developer utility module in DevToolbox.
/// </summary>
public interface IDevToolModule
{
    string Id { get; }

    string TitleResourceKey { get; }

    string DescriptionResourceKey { get; }

    string IconGlyph { get; }

    string Route { get; }

    ToolModuleCategory Category { get; }

    int Order { get; }

    SupportedPlatformFlags SupportedPlatforms { get; }

    bool IsRunning { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<OverlayQuickAction> GetQuickActions();
}
