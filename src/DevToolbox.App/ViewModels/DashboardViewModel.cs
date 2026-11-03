using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevToolbox.App.Resources.Strings;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.ViewModels;

public sealed partial class ToolModuleCardItem(
    IDevToolModule module,
    Func<ToolModuleCardItem, Task> openAction,
    Func<ToolModuleCardItem, Task> toggleAction) : ObservableObject
{
    public IDevToolModule Module { get; } = module;

    public string Id => Module.Id;

    public string IconGlyph => Module.IconGlyph;

    public string Title => AppStrings.GetString(Module.TitleResourceKey);

    public string Description => AppStrings.GetString(Module.DescriptionResourceKey);

    public string CategoryBadge => $"{Module.Category} • {Module.SupportedPlatforms}";

    public string Route => Module.Route;

    [ObservableProperty]
    public partial string StatusText { get; set; } = module.IsRunning ? "ACTIVE" : "READY";

    [ RelayCommand ]
    private Task OpenAsync() => openAction(this);

    [ RelayCommand ]
    private Task ToggleAsync() => toggleAction(this);

    public void RefreshState()
    {
        StatusText = Module.IsRunning ? "ACTIVE" : "READY";
    }
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDevToolRegistry _toolRegistry;
    private readonly IOverlayService _overlayService;
    private readonly INetworkProxyService _proxyService;
    private readonly IOrientationStateService _orientationService;

    public DashboardViewModel(
        IDevToolRegistry toolRegistry,
        IOverlayService overlayService,
        INetworkProxyService proxyService,
        IOrientationStateService orientationService)
    {
        _toolRegistry = toolRegistry;
        _overlayService = overlayService;
        _proxyService = proxyService;
        _orientationService = orientationService;

        _overlayService.StatusChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshSummaryState);
        _proxyService.StatusChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshSummaryState);
        _orientationService.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshSummaryState);

        LoadModules();
        RefreshSummaryState();
    }

    public ObservableCollection<ToolModuleCardItem> Modules { get; } = [];

    [ObservableProperty]
    public partial string OverlaySummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProxySummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OrientationSummary { get; set; } = string.Empty;

    [RelayCommand]
    private async Task QuickToggleOverlayAsync()
    {
        var quickActions = _toolRegistry.GetAllOverlayQuickActions();
        await _overlayService.RefreshQuickActionsAsync(quickActions);
        await _overlayService.ToggleOverlayAsync(new OverlayConfiguration());
        RefreshSummaryState();
    }

    [RelayCommand]
    private async Task QuickToggleProxyAsync()
    {
        if (_proxyService.CurrentStatus.IsRunning)
        {
            await _proxyService.StopAsync();
        }
        else
        {
            await _proxyService.StartAsync(_proxyService.CurrentStatus.ListenPort);
        }

        RefreshSummaryState();
    }

    [RelayCommand]
    private async Task QuickCycleOrientationAsync()
    {
        await _orientationService.CycleOrientationAsync(
            _orientationService.CurrentState.ExecutionMode,
            _orientationService.CurrentState.ConnectedDeviceSerial);
        RefreshSummaryState();
    }

    private void LoadModules()
    {
        Modules.Clear();
        foreach (var module in _toolRegistry.GetAllModules())
        {
            Modules.Add(new ToolModuleCardItem(
                module,
                OpenModuleCardAsync,
                ToggleModuleCardAsync));
        }
    }

    private async Task OpenModuleCardAsync(ToolModuleCardItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Route) && Shell.Current is not null)
        {
            await Shell.Current.GoToAsync($"//{item.Route}");
        }
    }

    private async Task ToggleModuleCardAsync(ToolModuleCardItem item)
    {
        var quickActions = _toolRegistry.GetAllOverlayQuickActions();
        await _overlayService.RefreshQuickActionsAsync(quickActions);

        if (item.Module.IsRunning)
        {
            await item.Module.StopAsync();
        }
        else
        {
            await item.Module.StartAsync();
        }

        RefreshSummaryState();
    }

    private void RefreshSummaryState()
    {
        var overlay = _overlayService.CurrentStatus;
        OverlaySummary = $"Overlay: {(overlay.IsVisible ? "Visible" : "Hidden")} ({overlay.PermissionState})";

        var proxy = _proxyService.CurrentStatus;
        ProxySummary = $"Proxy: {(proxy.IsRunning ? $"Listening on {proxy.ListenAddress}" : "Stopped")} • {proxy.ActiveRulesCount} Active Rules • {proxy.TotalInterceptedCount} Requests";

        var orientation = _orientationService.CurrentState;
        OrientationSummary = $"Orientation: {orientation.CurrentOrientation} ({orientation.ExecutionMode})";

        foreach (var card in Modules)
        {
            card.RefreshState();
        }
    }
}
