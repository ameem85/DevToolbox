using System.Runtime.InteropServices;
using DevToolbox.App.Views;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace DevToolbox.App.Platforms.Windows.Services;

/// <summary>
/// Windows system-level 3-state floating overlay engine using WinUI 3 <see cref="AppWindow"/> + <see cref="OverlappedPresenter"/>
/// and Win32 <c>user32.dll</c> P/Invoke (<c>SetWindowPos(HWND_TOPMOST)</c>, <c>WS_EX_LAYERED</c>, <c>SetLayeredWindowAttributes</c>).
/// </summary>
public sealed partial class WindowsOverlayService : IOverlayService
{
    private const int GwlExStyle = -20;
    private const nint WsExLayered = 0x00080000;
    private const nint WsExToolWindow = 0x00000080;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwRestore = 9;
    private static readonly nint HwndTopMost = new(-1);

    private Window? _floatingWindow;
    private FloatingHudPage? _hudPage;
    private IReadOnlyList<OverlayQuickAction> _quickActions = [];
    private OverlayConfiguration _lastConfig = new();
    private OverlayExpansionState _expansionState = OverlayExpansionState.Hidden;
    private bool _isVisible;

    public event EventHandler<OverlayStatusSnapshot>? StatusChanged;

    public OverlayStatusSnapshot CurrentStatus => new(
        _isVisible,
        OverlayPermissionState.Granted,
        OverlayImplementationKind.WindowsWin32TopMostWindow,
        _isVisible
            ? $"Win32 HWND_TOPMOST floating overlay active ({_expansionState})"
            : "Win32 TopMost overlay permission granted natively on Windows Desktop",
        _isVisible ? _expansionState : OverlayExpansionState.Hidden);

    public Task<OverlayPermissionState> CheckPermissionAsync(CancellationToken cancellationToken = default)
    {
        RaiseStatusChanged();
        return Task.FromResult(OverlayPermissionState.Granted);
    }

    public Task<OverlayPermissionState> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        RaiseStatusChanged();
        return Task.FromResult(OverlayPermissionState.Granted);
    }

    public async Task<OverlayStatusSnapshot> ShowOverlayAsync(
        OverlayConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        _lastConfig = configuration;
        var targetState = configuration.InitialExpansionState == OverlayExpansionState.Hidden
            ? OverlayExpansionState.CollapsedGear
            : configuration.InitialExpansionState;

        if (targetState == OverlayExpansionState.FullForegroundApp)
        {
            return await ExpandToFullApplicationAsync(null, cancellationToken).ConfigureAwait(false);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _expansionState = targetState;
            if (_isVisible && _floatingWindow is not null && _hudPage is not null)
            {
                _hudPage.ApplyExpansionState(_expansionState);
                ApplyNativeWin32TopMostAndOpacity(_floatingWindow, configuration, _expansionState);
                return;
            }

            _hudPage = new FloatingHudPage(
                _quickActions,
                _expansionState,
                async nextState => await SetExpansionStateAsync(nextState),
                async route => await ExpandToFullApplicationAsync(route),
                async () => await HideOverlayAsync());

            _floatingWindow = new Window(_hudPage)
            {
                Title = "DevToolbox Floating HUD",
                X = configuration.InitialX,
                Y = configuration.InitialY
            };

            _floatingWindow.Destroying += (_, _) =>
            {
                _isVisible = false;
                _expansionState = OverlayExpansionState.Hidden;
                _floatingWindow = null;
                _hudPage = null;
                RaiseStatusChanged();
            };

            Application.Current?.OpenWindow(_floatingWindow);
            ApplyNativeWin32TopMostAndOpacity(_floatingWindow, configuration, _expansionState);
            _isVisible = true;
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public async Task<OverlayStatusSnapshot> HideOverlayAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_floatingWindow is not null)
            {
                try
                {
                    Application.Current?.CloseWindow(_floatingWindow);
                }
                catch
                {
                    // Ignore window close errors if already closing
                }
            }

            _floatingWindow = null;
            _hudPage = null;
            _isVisible = false;
            _expansionState = OverlayExpansionState.Hidden;
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public Task<OverlayStatusSnapshot> ToggleOverlayAsync(
        OverlayConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        _isVisible
            ? HideOverlayAsync(cancellationToken)
            : ShowOverlayAsync(configuration, cancellationToken);

    public async Task<OverlayStatusSnapshot> SetExpansionStateAsync(
        OverlayExpansionState expansionState,
        CancellationToken cancellationToken = default)
    {
        if (expansionState == OverlayExpansionState.Hidden)
        {
            return await HideOverlayAsync(cancellationToken).ConfigureAwait(false);
        }

        if (expansionState == OverlayExpansionState.FullForegroundApp)
        {
            return await ExpandToFullApplicationAsync(null, cancellationToken).ConfigureAwait(false);
        }

        if (!_isVisible)
        {
            return await ShowOverlayAsync(_lastConfig with { InitialExpansionState = expansionState }, cancellationToken)
                .ConfigureAwait(false);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _expansionState = expansionState;
            _hudPage?.ApplyExpansionState(expansionState);
            if (_floatingWindow is not null)
            {
                ApplyNativeWin32TopMostAndOpacity(_floatingWindow, _lastConfig, expansionState);
            }
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public async Task<OverlayStatusSnapshot> ExpandToFullApplicationAsync(
        string? targetRoute = null,
        CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_isVisible)
            {
                _expansionState = OverlayExpansionState.CollapsedGear;
                _hudPage?.ApplyExpansionState(_expansionState);
                if (_floatingWindow is not null)
                {
                    ApplyNativeWin32TopMostAndOpacity(_floatingWindow, _lastConfig, _expansionState);
                }
            }

            var mainWindow = Application.Current?.Windows.FirstOrDefault();
            if (mainWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeMain)
            {
                var mainHwnd = WindowNative.GetWindowHandle(nativeMain);
                if (mainHwnd != nint.Zero)
                {
                    _ = ShowWindow(mainHwnd, SwRestore);
                    _ = SetForegroundWindow(mainHwnd);
                }
            }

            if (!string.IsNullOrWhiteSpace(targetRoute) && Shell.Current is not null)
            {
                await Shell.Current.GoToAsync($"//{targetRoute}");
            }
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public Task RefreshQuickActionsAsync(
        IReadOnlyList<OverlayQuickAction> quickActions,
        CancellationToken cancellationToken = default)
    {
        _quickActions = quickActions ?? [];
        return Task.CompletedTask;
    }

    private static void ApplyNativeWin32TopMostAndOpacity(
        Window mauiWindow,
        OverlayConfiguration configuration,
        OverlayExpansionState state)
    {
        if (mauiWindow.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == nint.Zero)
        {
            return;
        }

        var width = state == OverlayExpansionState.CollapsedGear ? 280 : 480;
        var height = state == OverlayExpansionState.CollapsedGear ? 92 : 240;

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow is not null)
        {
            appWindow.MoveAndResize(new RectInt32(configuration.InitialX, configuration.InitialY, width, height));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = configuration.IsPinnedTopMost;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }

        if (configuration.IsPinnedTopMost)
        {
            _ = SetWindowPos(
                hwnd,
                HwndTopMost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        var existingExStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        _ = SetWindowLongPtr(hwnd, GwlExStyle, existingExStyle | WsExLayered | WsExToolWindow);

        var alphaByte = (byte)Math.Clamp((int)(configuration.Opacity * 255), 90, 255);
        _ = SetLayeredWindowAttributes(hwnd, 0, alphaByte, LwaAlpha);
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, CurrentStatus);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(
        nint hwnd,
        uint crKey,
        byte bAlpha,
        uint dwFlags);
}
