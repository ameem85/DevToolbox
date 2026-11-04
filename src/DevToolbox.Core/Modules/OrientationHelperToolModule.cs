using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Modules;

public sealed class OrientationHelperToolModule(IOrientationStateService orientationService) : IDevToolModule
{
    public const string ModuleId = "tool.orientation_helper";

    public string Id => ModuleId;

    public string TitleResourceKey => "Module_OrientationHelper_Title";

    public string DescriptionResourceKey => "Module_OrientationHelper_Description";

    public string IconGlyph => "🔄";

    public string Route => "OrientationToolPage";

    public ToolModuleCategory Category => ToolModuleCategory.DeviceState;

    public int Order => 30;

    public SupportedPlatformFlags SupportedPlatforms => SupportedPlatformFlags.All;

    public bool IsRunning => orientationService.CurrentState.CurrentOrientation != DeviceOrientationMode.Unlocked;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await orientationService.CycleOrientationAsync(
            orientationService.CurrentState.ExecutionMode,
            orientationService.CurrentState.ConnectedDeviceSerial,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await orientationService.ApplyOrientationAsync(
            DeviceOrientationMode.Unlocked,
            orientationService.CurrentState.ExecutionMode,
            orientationService.CurrentState.ConnectedDeviceSerial,
            cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<OverlayQuickAction> GetQuickActions() =>
    [
        new OverlayQuickAction(
            "orientation.portrait",
            "QuickAction_ForcePortrait",
            "📱",
            async ct => await orientationService.ApplyOrientationAsync(
                DeviceOrientationMode.Portrait,
                orientationService.CurrentState.ExecutionMode,
                orientationService.CurrentState.ConnectedDeviceSerial,
                ct).ConfigureAwait(false)),
        new OverlayQuickAction(
            "orientation.landscape",
            "QuickAction_ForceLandscape",
            "📺",
            async ct => await orientationService.ApplyOrientationAsync(
                DeviceOrientationMode.Landscape,
                orientationService.CurrentState.ExecutionMode,
                orientationService.CurrentState.ConnectedDeviceSerial,
                ct).ConfigureAwait(false)),
        new OverlayQuickAction(
            "orientation.cycle",
            "QuickAction_CycleOrientation",
            "🔄",
            async ct => await orientationService.CycleOrientationAsync(
                orientationService.CurrentState.ExecutionMode,
                orientationService.CurrentState.ConnectedDeviceSerial,
                ct).ConfigureAwait(false)),
        new OverlayQuickAction(
            "orientation.unlock",
            "QuickAction_UnlockOrientation",
            "🔓",
            async ct => await orientationService.ApplyOrientationAsync(
                DeviceOrientationMode.Unlocked,
                orientationService.CurrentState.ExecutionMode,
                orientationService.CurrentState.ConnectedDeviceSerial,
                ct).ConfigureAwait(false))
    ];
}
