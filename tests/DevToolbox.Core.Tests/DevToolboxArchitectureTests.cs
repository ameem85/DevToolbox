using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;
using DevToolbox.Core.DependencyInjection;
using DevToolbox.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevToolbox.Core.Tests;

public sealed class DevToolboxArchitectureTests
{
    [Fact]
    public async Task DevToolRegistry_ResolvesOrderedModulesAndQuickActions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOverlayService, FakeOverlayService>();
        services.AddSingleton<IOrientationStateService, FakeOrientationStateService>();
        services.AddDevToolboxCore();
        services.AddDevToolModule<LogcatReaderToolModule>();
        services.AddDevToolModule<DeviceInfoInspectorToolModule>();
        services.AddDevToolModule<DeepLinkTesterToolModule>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IDevToolRegistry>();

        await registry.InitializeAllAsync();

        var modules = registry.GetAllModules();
        Assert.Equal(6, modules.Count);
        Assert.Equal(FloatingOverlayToolModule.ModuleId, modules[0].Id);
        Assert.Equal(NetworkProxyToolModule.ModuleId, modules[1].Id);
        Assert.Equal(OrientationHelperToolModule.ModuleId, modules[2].Id);

        var quickActions = registry.GetAllOverlayQuickActions();
        Assert.Contains(quickActions, a => a.ActionId == "overlay.hide");
        Assert.Contains(quickActions, a => a.ActionId == "proxy.toggle");
        Assert.Contains(quickActions, a => a.ActionId == "orientation.portrait");
        Assert.Contains(quickActions, a => a.ActionId == "orientation.landscape");
    }

    [Fact]
    public async Task LocalNetworkProxyEngine_EvaluatesMapLocalFaultAndRewriteRules_AndStreamsToDirectSink()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOverlayService, FakeOverlayService>();
        services.AddSingleton<IOrientationStateService, FakeOrientationStateService>();
        services.AddDevToolboxCore();

        await using var provider = services.BuildServiceProvider();
        var proxy = provider.GetRequiredService<INetworkProxyService>();
        var sink = provider.GetRequiredService<IHighFrequencyUpdateSink<CapturedTrafficEntry>>();

        var streamedEntries = new List<CapturedTrafficEntry>();
        using var subscription = sink.Subscribe(batch => streamedEntries.AddRange(batch));

        // 1. Map Local rule evaluation
        var profileCapture = await proxy.EvaluateOrForwardRequestAsync(
            "GET",
            "https://api.sandbox.dev/api/v1/user/profile");
        Assert.True(profileCapture.IsMocked);
        Assert.Equal(200, profileCapture.StatusCode);
        Assert.Contains("SuperAdmin", profileCapture.ResponsePreview);

        // 2. Error Injection rule evaluation (503)
        var checkoutCapture = await proxy.EvaluateOrForwardRequestAsync(
            "POST",
            "https://api.sandbox.dev/api/v1/checkout");
        Assert.True(checkoutCapture.IsMocked);
        Assert.Equal(503, checkoutCapture.StatusCode);
        Assert.Contains("SERVICE_UNAVAILABLE", checkoutCapture.ResponsePreview);

        // 3. Header/URL Rewrite + Latency rule evaluation
        var catalogCapture = await proxy.EvaluateOrForwardRequestAsync(
            "GET",
            "https://api.sandbox.dev/api/v1/catalog");
        Assert.True(catalogCapture.IsMocked);
        Assert.Equal(200, catalogCapture.StatusCode);
        Assert.Contains("X-Environment-Override: staging-canary", catalogCapture.RequestHeadersSummary);

        Assert.Equal(3, streamedEntries.Count);
        Assert.Equal(3, proxy.CurrentStatus.TotalInterceptedCount);
    }

    private sealed class FakeOverlayService : IOverlayService
    {
        public event EventHandler<OverlayStatusSnapshot>? StatusChanged;

        public OverlayStatusSnapshot CurrentStatus { get; private set; } = new(
            false,
            OverlayPermissionState.Granted,
            OverlayImplementationKind.WindowsWin32TopMostWindow,
            "Ready");

        public Task<OverlayPermissionState> CheckPermissionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OverlayPermissionState.Granted);

        public Task<OverlayPermissionState> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OverlayPermissionState.Granted);

        public Task<OverlayStatusSnapshot> ShowOverlayAsync(
            OverlayConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            CurrentStatus = CurrentStatus with { IsVisible = true };
            StatusChanged?.Invoke(this, CurrentStatus);
            return Task.FromResult(CurrentStatus);
        }

        public Task<OverlayStatusSnapshot> HideOverlayAsync(CancellationToken cancellationToken = default)
        {
            CurrentStatus = CurrentStatus with { IsVisible = false };
            StatusChanged?.Invoke(this, CurrentStatus);
            return Task.FromResult(CurrentStatus);
        }

        public Task<OverlayStatusSnapshot> ToggleOverlayAsync(
            OverlayConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            CurrentStatus.IsVisible ? HideOverlayAsync(cancellationToken) : ShowOverlayAsync(configuration, cancellationToken);

        public Task RefreshQuickActionsAsync(
            IReadOnlyList<OverlayQuickAction> quickActions,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeOrientationStateService : IOrientationStateService
    {
        public event EventHandler<OrientationStateSnapshot>? StateChanged;

        public OrientationStateSnapshot CurrentState { get; private set; } = new(
            DeviceOrientationMode.Portrait,
            TargetExecutionMode.LocalDevice,
            null,
            "Ready");

        public Task<OrientationStateSnapshot> ApplyOrientationAsync(
            DeviceOrientationMode mode,
            TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
            string? remoteDeviceSerial = null,
            CancellationToken cancellationToken = default)
        {
            CurrentState = new OrientationStateSnapshot(mode, executionMode, remoteDeviceSerial, "Updated");
            StateChanged?.Invoke(this, CurrentState);
            return Task.FromResult(CurrentState);
        }

        public Task<OrientationStateSnapshot> CycleOrientationAsync(
            TargetExecutionMode executionMode = TargetExecutionMode.LocalDevice,
            string? remoteDeviceSerial = null,
            CancellationToken cancellationToken = default) =>
            ApplyOrientationAsync(DeviceOrientationMode.Landscape, executionMode, remoteDeviceSerial, cancellationToken);

        public Task<OrientationStateSnapshot> ApplyDisplayOverrideAsync(
            int widthPx,
            int heightPx,
            int densityDpi,
            string? remoteDeviceSerial = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentState);

        public Task<OrientationStateSnapshot> ResetDisplayOverrideAsync(
            string? remoteDeviceSerial = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentState);
    }
}
