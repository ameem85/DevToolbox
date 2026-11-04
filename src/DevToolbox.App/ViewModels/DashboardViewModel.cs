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

    public string CategoryBadge => $"{Module.Category.ToString().ToUpperInvariant()}  •  {Module.SupportedPlatforms}";

    public string Route => Module.Route;

    [ObservableProperty]
    public partial string StatusText { get; set; } = module.IsRunning ? "● ACTIVE" : "○ STANDBY";

    [ObservableProperty]
    public partial Color StatusColor { get; set; } = module.IsRunning
        ? Color.FromArgb("#34D399")
        : Color.FromArgb("#94A3B8");

    [RelayCommand]
    private Task OpenAsync() => openAction(this);

    [RelayCommand]
    private Task ToggleAsync() => toggleAction(this);

    public void RefreshState()
    {
        StatusText = Module.IsRunning ? "● ACTIVE" : "○ STANDBY";
        StatusColor = Module.IsRunning
            ? Color.FromArgb("#34D399")
            : Color.FromArgb("#94A3B8");
    }
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDevToolRegistry _toolRegistry;
    private readonly IOverlayService _overlayService;
    private readonly INetworkProxyService _proxyService;
    private readonly IOrientationStateService _orientationService;
    private readonly ILogcatStreamService _logcatService;

    public DashboardViewModel(
        IDevToolRegistry toolRegistry,
        IOverlayService overlayService,
        INetworkProxyService proxyService,
        IOrientationStateService orientationService,
        ILogcatStreamService logcatService)
    {
        _toolRegistry = toolRegistry;
        _overlayService = overlayService;
        _proxyService = proxyService;
        _orientationService = orientationService;
        _logcatService = logcatService;

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
    public partial string OverlayStateBadge { get; set; } = "STANDBY";

    [ObservableProperty]
    public partial string ProxySummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProxyStateBadge { get; set; } = "STOPPED";

    [ObservableProperty]
    public partial string OrientationSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OrientationStateBadge { get; set; } = "LOCAL";

    [ObservableProperty]
    public partial string LogcatSummary { get; set; } = "Unified ADB & Diagnostic Sink";

    [ObservableProperty]
    public partial string LogcatStateBadge { get; set; } = "READY";

    [RelayCommand]
    private async Task QuickToggleOverlayAsync()
    {
        var quickActions = _toolRegistry.GetAllOverlayQuickActions();
        await _overlayService.RefreshQuickActionsAsync(quickActions);
        await _overlayService.ToggleOverlayAsync(
            new OverlayConfiguration(InitialExpansionState: OverlayExpansionState.CollapsedGear));
        RefreshSummaryState();
    }

    [RelayCommand]
    private async Task LaunchGearOverlayAsync()
    {
        var quickActions = _toolRegistry.GetAllOverlayQuickActions();
        await _overlayService.RefreshQuickActionsAsync(quickActions);
        await _overlayService.ShowOverlayAsync(
            new OverlayConfiguration(InitialExpansionState: OverlayExpansionState.CollapsedGear));
        RefreshSummaryState();
    }

    [RelayCommand]
    private async Task LaunchExpandedOverlayAsync()
    {
        var quickActions = _toolRegistry.GetAllOverlayQuickActions();
        await _overlayService.RefreshQuickActionsAsync(quickActions);
        await _overlayService.ShowOverlayAsync(
            new OverlayConfiguration(InitialExpansionState: OverlayExpansionState.ExpandedPanel));
        RefreshSummaryState();
    }

    [RelayCommand]
    private async Task FloatOverOsAsync()
    {
        var quickActions = _toolRegistry.GetAllOverlayQuickActions();
        await _overlayService.RefreshQuickActionsAsync(quickActions);
        await _overlayService.ShowOverlayAsync(
            new OverlayConfiguration(InitialExpansionState: OverlayExpansionState.CollapsedGear));
        RefreshSummaryState();

#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
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

    [RelayCommand]
    private void QuickEmitMarker()
    {
        var stamp = DateTimeOffset.Now.ToString("HH:mm:ss");
        _logcatService.EmitDiagnosticEntry("I", "QA.Workbench", $"Workbench QA Marker @ {stamp}");
        LogcatSummary = $"Last marker @ {stamp}";
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
        OverlayStateBadge = overlay.IsVisible
            ? overlay.ExpansionState == OverlayExpansionState.CollapsedGear ? "⚙ GEAR HUD" : "⊞ EXPANDED"
            : overlay.PermissionState == OverlayPermissionState.Granted ? "READY" : "DENIED";
        OverlaySummary = overlay.IsVisible
            ? $"Active: {overlay.ExpansionState} ({overlay.PermissionState})"
            : $"Standby • Permission {overlay.PermissionState}";

        var proxy = _proxyService.CurrentStatus;
        ProxyStateBadge = proxy.IsRunning ? "● LISTENING" : "STOPPED";
        ProxySummary = proxy.IsRunning
            ? $":{proxy.ListenPort} • {proxy.ActiveRulesCount} Rules • {proxy.TotalInterceptedCount} Req"
            : $"Port :{proxy.ListenPort} • {proxy.ActiveRulesCount} Rules • {proxy.TotalInterceptedCount} Req";

        var orientation = _orientationService.CurrentState;
        OrientationStateBadge = orientation.ExecutionMode == TargetExecutionMode.LocalDevice ? "LOCAL" : "ADB";
        OrientationSummary = $"{orientation.CurrentOrientation} ({orientation.ExecutionMode})";

        LogcatStateBadge = _logcatService.IsStreaming ? "● STREAMING" : "READY";

        foreach (var card in Modules)
        {
            card.RefreshState();
        }
    }
}
