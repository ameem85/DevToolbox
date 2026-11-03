namespace DevToolbox.Core.Abstractions.Plugins;

/// <summary>
/// Central container and lifecycle coordinator for plug-and-play developer tool modules.
/// </summary>
public interface IDevToolRegistry
{
    IReadOnlyList<IDevToolModule> GetAllModules();

    IReadOnlyList<IDevToolModule> GetModulesByCategory(ToolModuleCategory category);

    IDevToolModule? GetModule(string id);

    IReadOnlyList<OverlayQuickAction> GetAllOverlayQuickActions();

    Task InitializeAllAsync(CancellationToken cancellationToken = default);

    Task StopAllAsync(CancellationToken cancellationToken = default);
}
