using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Modules;

public sealed class NetworkProxyToolModule(INetworkProxyService proxyService) : IDevToolModule
{
    public const string ModuleId = "tool.network_proxy";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_NetworkProxy_Title";

    public string DescriptionResourceKey => "Module_NetworkProxy_Description";

    public string IconGlyph => "🌐";

    public string Route => "NetworkProxyPage";

    public ToolModuleCategory Category => ToolModuleCategory.Network;

    public int Order => 20;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.All;

    public bool IsRunning => proxyService.CurrentStatus.IsRunning;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await proxyService.StartAsync(proxyService.CurrentStatus.ListenPort, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await proxyService.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() =>
    [
        new OverlayQuickAction(
            "proxy.toggle",
            "QuickAction_ToggleProxy",
            "⇄",
            async ct =>
            {
                if (proxyService.CurrentStatus.IsRunning)
                {
                    await proxyService.StopAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    await proxyService.StartAsync(proxyService.CurrentStatus.ListenPort, ct).ConfigureAwait(false);
                }
            })
    ];
}
