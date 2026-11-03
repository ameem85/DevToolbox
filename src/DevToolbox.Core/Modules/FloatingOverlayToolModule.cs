using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Modules;

public sealed class FloatingOverlayToolModule(IOverlayService overlayService) : IDevToolModule
{
    public const string ModuleId = "tool.floating_overlay";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_FloatingOverlay_Title";

    public string DescriptionResourceKey => "Module_FloatingOverlay_Description";

    public string IconGlyph => "🪟";

    public string Route => "OverlayToolPage";

    public ToolModuleCategory Category => ToolModuleCategory.Overlay;

    public int Order => 10;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.All;

    public bool IsRunning => overlayService.CurrentStatus.IsVisible;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await overlayService.CheckPermissionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await overlayService.ShowOverlayAsync(new OverlayConfiguration(), cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await overlayService.HideOverlayAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() =>
    [
        new OverlayQuickAction(
            "overlay.hide",
            "QuickAction_HideOverlay",
            "✕",
            async ct => await overlayService.HideOverlayAsync(ct).ConfigureAwait(false))
    ];
}
