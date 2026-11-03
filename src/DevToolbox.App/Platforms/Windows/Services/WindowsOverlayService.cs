using System.Runtime.InteropServices;
using DevToolbox.App.Views;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace DevToolbox.App.Platforms.Windows.Services;

/// <summary>
/// Windows system-level floating overlay engine using WinUI 3 <see cref="AppWindow"/> + <see cref="OverlappedPresenter"/>
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
    private static readonly nint HwndTopMost = new(-1);

    private Window? _floatingWindow;
    private IReadOnlyList<OverlayQuickAction> _quickActions = [];
    private bool _isVisible;

    public event EventHandler<OverlayStatusSnapshot>? StatusChanged;

    public OverlayStatusSnapshot CurrentStatus => new(
        _isVisible,
        OverlayPermissionState.Granted,
        OverlayImplementationKind.WindowsWin32TopMostWindow,
        _isVisible
            ? "Win32 HWND_TOPMOST + WS_EX_LAYERED floating overlay window active"
            : "Win32 TopMost overlay permission granted natively on Windows Desktop");

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
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_isVisible && _floatingWindow is not null)
            {
                ApplyNativeWin32TopMostAndOpacity(_floatingWindow, configuration);
                return;
            }

            var hudPage = new FloatingHudPage(_quickActions, async () => await HideOverlayAsync());
            _floatingWindow = new Window(hudPage)
            {
                Title = "DevToolbox Floating HUD",
                Width = 420,
                Height = 148,
                X = configuration.InitialX,
                Y = configuration.InitialY
            };

            _floatingWindow.Destroying += (_, _) =>
            {
                _isVisible = false;
                _floatingWindow = null;
                RaiseStatusChanged();
            };

            Application.Current?.OpenWindow(_floatingWindow);
            ApplyNativeWin32TopMostAndOpacity(_floatingWindow, configuration);
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
            _isVisible = false;
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

    public Task RefreshQuickActionsAsync(
        IReadOnlyList<OverlayQuickAction> quickActions,
        CancellationToken cancellationToken = default)
    {
        _quickActions = quickActions ?? [];
        return Task.CompletedTask;
    }

    private static void ApplyNativeWin32TopMostAndOpacity(Window mauiWindow, OverlayConfiguration configuration)
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

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow is not null)
        {
            appWindow.MoveAndResize(new RectInt32(configuration.InitialX, configuration.InitialY, 440, 156));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = configuration.IsPinnedTopMost;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }

        // Apply Win32 HWND_TOPMOST & WS_EX_LAYERED alpha transparency
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
