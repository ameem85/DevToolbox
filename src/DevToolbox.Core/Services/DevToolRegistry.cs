using DevToolbox.Core.Abstractions.Plugins;

namespace DevToolbox.Core.Services;

/// <summary>
/// Default implementation of <see cref="IDevToolRegistry"/> that coordinates all registered <see cref="IDevToolModule"/> plugins.
/// </summary>
public sealed class DevToolRegistry(IEnumerable<IDevToolModule> modules) : IDevToolRegistry
{
    private readonly IReadOnlyList<IDevToolModule> _modules = modules
        .OrderBy(m => m.Order)
        .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<IDevToolModule> GetAllModules() => _modules;

    public IReadOnlyList<IDevToolModule> GetModulesByCategory(ToolModuleCategory category) =>
        _modules.Where(m => m.Category == category).ToList();

    public IDevToolModule? GetModule(string id) =>
        _modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<OverlayQuickAction> GetAllOverlayQuickActions() =>
        _modules.SelectMany(m => m.GetQuickActions()).ToList();

    public async Task InitializeAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var module in _modules)
        {
            await module.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var module in _modules.Where(m => m.IsRunning))
        {
            await module.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
