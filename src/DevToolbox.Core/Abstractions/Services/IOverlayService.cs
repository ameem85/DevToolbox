using DevToolbox.Core.Abstractions.Plugins;

namespace DevToolbox.Core.Abstractions.Services;

public enum OverlayPermissionState
{
    Unknown = 0,
    Granted = 1,
    Denied = 2,
    SimulatedInApp = 3
}

public enum OverlayImplementationKind
{
    AndroidSystemWindowManager = 0,
    WindowsWin32TopMostWindow = 1,
    IosInAppWindowLayer = 2
}

public enum OverlayExpansionState
{
    Hidden = 0,
    CollapsedGear = 1,
    ExpandedPanel = 2,
    FullForegroundApp = 3
}

public sealed record OverlayConfiguration(
    bool IsPinnedTopMost = true,
    double Opacity = 0.96,
    int InitialX = 36,
    int InitialY = 140,
    bool ShowQuickActions = true,
    OverlayExpansionState InitialExpansionState = OverlayExpansionState.CollapsedGear);

public sealed record OverlayStatusSnapshot(
    bool IsVisible,
    OverlayPermissionState PermissionState,
    OverlayImplementationKind ImplementationKind,
    string StatusDetails,
    OverlayExpansionState ExpansionState = OverlayExpansionState.Hidden);

/// <summary>
/// Cross-platform contract for managing system-level floating overlays (Android WindowManager, Windows Win32 TopMost)
/// and in-app floating HUDs (iOS UIWindow overlay layer) with 3-state expansion:
/// Collapsed Settings Gear -> Expanded Quick Panel -> Full Foreground Application.
/// </summary>
public interface IOverlayService
{
    event EventHandler<OverlayStatusSnapshot>? StatusChanged;

    OverlayStatusSnapshot CurrentStatus { get; }

    Task<OverlayPermissionState> CheckPermissionAsync(CancellationToken cancellationToken = default);

    Task<OverlayPermissionState> RequestPermissionAsync(CancellationToken cancellationToken = default);

    Task<OverlayStatusSnapshot> ShowOverlayAsync(
        OverlayConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<OverlayStatusSnapshot> HideOverlayAsync(CancellationToken cancellationToken = default);

    Task<OverlayStatusSnapshot> ToggleOverlayAsync(
        OverlayConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<OverlayStatusSnapshot> SetExpansionStateAsync(
        OverlayExpansionState expansionState,
        CancellationToken cancellationToken = default);

    Task<OverlayStatusSnapshot> ExpandToFullApplicationAsync(
        string? targetRoute = null,
        CancellationToken cancellationToken = default);

    Task RefreshQuickActionsAsync(
        IReadOnlyList<OverlayQuickAction> quickActions,
        CancellationToken cancellationToken = default);
}
