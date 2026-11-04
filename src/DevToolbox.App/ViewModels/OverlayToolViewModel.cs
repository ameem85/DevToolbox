using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevToolbox.App.Resources.Strings;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.ViewModels;

public sealed partial class QuickActionItemViewModel(OverlayQuickAction action) : ObservableObject
{
    public OverlayQuickAction Action { get; } = action;

    public string ActionId => Action.ActionId;

    public string IconGlyph => Action.IconGlyph;

    public string Label => AppStrings.GetString(Action.LabelResourceKey);

    [RelayCommand]
    private Task InvokeAsync() => Action.ExecuteAsync(CancellationToken.None);
}

public sealed partial class OverlayToolViewModel : ObservableObject
{
    private readonly IOverlayService _overlayService;
    private readonly IDevToolRegistry _toolRegistry;
    private readonly IAdbBridgeService _adbBridgeService;

    public OverlayToolViewModel(
        IOverlayService overlayService,
        IDevToolRegistry toolRegistry,
        IAdbBridgeService adbBridgeService)
    {
        _overlayService = overlayService;
        _toolRegistry = toolRegistry;
        _adbBridgeService = adbBridgeService;

        _overlayService.StatusChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshStatus);
        ReloadQuickActions();
        RefreshStatus();
    }

    public ObservableCollection<QuickActionItemViewModel> QuickActions { get; } = [];

    [ObservableProperty]
    public partial bool IsPinnedTopMost { get; set; } = true;

    [ObservableProperty]
    public partial double OverlayOpacity { get; set; } = 0.96;

    [ObservableProperty]
    public partial string PermissionBadge { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImplementationBadge { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExpansionStateBadge { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusDetails { get; set; } = string.Empty;

    [RelayCommand]
    private async Task CheckPermissionAsync()
    {
        await _overlayService.CheckPermissionAsync();
        RefreshStatus();
    }

    [RelayCommand]
    private async Task RequestPermissionAsync()
    {
        await _overlayService.RequestPermissionAsync();
        RefreshStatus();
    }

    [RelayCommand]
    private async Task GrantViaAdbAppOpsAsync()
    {
        var result = await _adbBridgeService.GrantOverlayPermissionAsync("com.devtoolbox.companion");
        StatusDetails = result.Success
            ? "Granted SYSTEM_ALERT_WINDOW via 'adb shell appops set com.devtoolbox.companion SYSTEM_ALERT_WINDOW allow'"
            : $"ADB appops command result: {result.StandardError}";
    }

    [RelayCommand]
    private async Task ShowOverlayAsync()
    {
        await ShowWithStateAsync(OverlayExpansionState.CollapsedGear);
    }

    [RelayCommand]
    private async Task ShowCollapsedGearAsync()
    {
        await ShowWithStateAsync(OverlayExpansionState.CollapsedGear);
    }

    [RelayCommand]
    private async Task ShowExpandedPanelAsync()
    {
        await ShowWithStateAsync(OverlayExpansionState.ExpandedPanel);
    }

    [RelayCommand]
    private async Task ExpandToFullAppAsync()
    {
        await _overlayService.ExpandToFullApplicationAsync("DashboardPage");
        RefreshStatus();
    }

    [RelayCommand]
    private async Task FloatOverOsAsGearAsync()
    {
        await ShowWithStateAsync(OverlayExpansionState.CollapsedGear);
#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
    }

    [RelayCommand]
    private async Task HideOverlayAsync()
    {
        await _overlayService.HideOverlayAsync();
        RefreshStatus();
    }

    private async Task ShowWithStateAsync(OverlayExpansionState state)
    {
        await _overlayService.RefreshQuickActionsAsync(_toolRegistry.GetAllOverlayQuickActions());
        var config = new OverlayConfiguration(
            IsPinnedTopMost,
            OverlayOpacity,
            36,
            140,
            true,
            state);
        await _overlayService.ShowOverlayAsync(config);
        RefreshStatus();
    }

    private void ReloadQuickActions()
    {
        QuickActions.Clear();
        foreach (var action in _toolRegistry.GetAllOverlayQuickActions())
        {
            QuickActions.Add(new QuickActionItemViewModel(action));
        }
    }

    private void RefreshStatus()
    {
        var snapshot = _overlayService.CurrentStatus;
        PermissionBadge = $"PERMISSION: {snapshot.PermissionState.ToString().ToUpperInvariant()}";
        ImplementationBadge = $"ENGINE: {snapshot.ImplementationKind}";
        ExpansionStateBadge = $"MODE: {snapshot.ExpansionState.ToString().ToUpperInvariant()}";
        StatusDetails = snapshot.StatusDetails;
    }
}
