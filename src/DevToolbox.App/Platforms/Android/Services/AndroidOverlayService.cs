using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using DevToolbox.App.Resources.Strings;
using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;
using AButton = Android.Widget.Button;
using AColor = Android.Graphics.Color;
using AUri = Android.Net.Uri;
using AView = Android.Views.View;

namespace DevToolbox.App.Platforms.Android.Services;

/// <summary>
/// Android system-level floating overlay engine using <see cref="IWindowManager"/> and
/// <see cref="WindowManagerTypes.ApplicationOverlay"/> (<c>SYSTEM_ALERT_WINDOW</c>).
/// Provisioning via ADB: <c>adb shell appops set com.devtoolbox.companion SYSTEM_ALERT_WINDOW allow</c>.
/// </summary>
public sealed class AndroidOverlayService : Java.Lang.Object, IOverlayService, AView.IOnTouchListener
{
    private IWindowManager? _windowManager;
    private LinearLayout? _floatingRootView;
    private WindowManagerLayoutParams? _layoutParams;
    private IReadOnlyList<OverlayQuickAction> _quickActions = [];
    private bool _isVisible;
    private int _initialX;
    private int _initialY;
    private float _touchStartX;
    private float _touchStartY;

    public event EventHandler<OverlayStatusSnapshot>? StatusChanged;

    public OverlayStatusSnapshot CurrentStatus => new(
        _isVisible,
        CanDrawSystemOverlays() ? OverlayPermissionState.Granted : OverlayPermissionState.Denied,
        OverlayImplementationKind.AndroidSystemWindowManager,
        _isVisible
            ? "Android WindowManager overlay active (TYPE_APPLICATION_OVERLAY)"
            : CanDrawSystemOverlays()
                ? "SYSTEM_ALERT_WINDOW granted; ready to attach overlay"
                : "SYSTEM_ALERT_WINDOW denied; grant via Settings or 'adb shell appops set com.devtoolbox.companion SYSTEM_ALERT_WINDOW allow'");

    public Task<OverlayPermissionState> CheckPermissionAsync(CancellationToken cancellationToken = default)
    {
        var state = CanDrawSystemOverlays()
            ? OverlayPermissionState.Granted
            : OverlayPermissionState.Denied;

        RaiseStatusChanged();
        return Task.FromResult(state);
    }

    public Task<OverlayPermissionState> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        var context = global::Android.App.Application.Context;
        if (CanDrawSystemOverlays())
        {
            RaiseStatusChanged();
            return Task.FromResult(OverlayPermissionState.Granted);
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var intent = new Intent(
                Settings.ActionManageOverlayPermission,
                AUri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }

        RaiseStatusChanged();
        return Task.FromResult(CanDrawSystemOverlays()
            ? OverlayPermissionState.Granted
            : OverlayPermissionState.Denied);
    }

    public async Task<OverlayStatusSnapshot> ShowOverlayAsync(
        OverlayConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!CanDrawSystemOverlays())
        {
            await RequestPermissionAsync(cancellationToken).ConfigureAwait(false);
            return CurrentStatus;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_isVisible && _floatingRootView is not null)
            {
                return;
            }

            var context = global::Android.App.Application.Context;
            _windowManager = context.GetSystemService(Context.WindowService)?.JavaCast<IWindowManager>();
            if (_windowManager is null)
            {
                return;
            }

            var overlayType = Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone;

            _layoutParams = new WindowManagerLayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent,
                overlayType,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutInScreen,
                Format.Translucent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Start,
                X = configuration.InitialX,
                Y = configuration.InitialY,
                Alpha = (float)Math.Clamp(configuration.Opacity, 0.35, 1.0)
            };

            _floatingRootView = BuildNativeOverlayToolbar(context);
            _floatingRootView.SetOnTouchListener(this);

            _windowManager.AddView(_floatingRootView, _layoutParams);
            _isVisible = true;
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public async Task<OverlayStatusSnapshot> HideOverlayAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_windowManager is not null && _floatingRootView is not null && _isVisible)
            {
                try
                {
                    _windowManager.RemoveView(_floatingRootView);
                }
                catch
                {
                    // Ignore if view was already detached
                }
            }

            _floatingRootView = null;
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

    public bool OnTouch(AView? v, MotionEvent? e)
    {
        if (e is null || _layoutParams is null || _windowManager is null || _floatingRootView is null)
        {
            return false;
        }

        switch (e.Action)
        {
            case MotionEventActions.Down:
                _initialX = _layoutParams.X;
                _initialY = _layoutParams.Y;
                _touchStartX = e.RawX;
                _touchStartY = e.RawY;
                return true;

            case MotionEventActions.Move:
                _layoutParams.X = _initialX + (int)(e.RawX - _touchStartX);
                _layoutParams.Y = _initialY + (int)(e.RawY - _touchStartY);
                _windowManager.UpdateViewLayout(_floatingRootView, _layoutParams);
                return true;

            default:
                return false;
        }
    }

    private LinearLayout BuildNativeOverlayToolbar(Context context)
    {
        var container = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal
        };
        container.SetBackgroundColor(AColor.ParseColor("#E61E293B"));
        container.SetPadding(24, 16, 24, 16);

        var badge = new TextView(context)
        {
            Text = $"🛠 {AppStrings.Hud_FloatingTitle} "
        };
        badge.SetTextColor(AColor.ParseColor("#38BDF8"));
        badge.TextSize = 13f;
        container.AddView(badge);

        foreach (var action in _quickActions)
        {
            var actionButton = new AButton(context)
            {
                Text = action.IconGlyph
            };
            actionButton.Click += async (_, _) =>
            {
                await action.ExecuteAsync(CancellationToken.None);
            };
            container.AddView(actionButton);
        }

        var closeBtn = new AButton(context)
        {
            Text = "✕"
        };
        closeBtn.Click += async (_, _) =>
        {
            await HideOverlayAsync();
        };
        container.AddView(closeBtn);

        return container;
    }

    private static bool CanDrawSystemOverlays()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
        {
            return true;
        }

        return Settings.CanDrawOverlays(global::Android.App.Application.Context);
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, CurrentStatus);
}
