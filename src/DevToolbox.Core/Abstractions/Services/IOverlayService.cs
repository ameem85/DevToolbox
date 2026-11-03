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

public sealed record OverlayConfiguration(
    bool IsPinnedTopMost = true,
    double Opacity = 0.94,
    int InitialX = 48,
    int InitialY = 140,
    bool ShowQuickActions = true);

public sealed record OverlayStatusSnapshot(
    bool IsVisible,
    OverlayPermissionState PermissionState,
    OverlayImplementationKind ImplementationKind,
    string StatusDetails);

/// <summary>
/// Cross-platform contract for managing system-level floating overlays (Android WindowManager, Windows Win32 TopMost)
/// and in-app floating HUDs (iOS UIWindow overlay layer).
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

    Task RefreshQuickActionsAsync(
        IReadOnlyList<OverlayQuickAction> quickActions,
        CancellationToken cancellationToken = default);
}
