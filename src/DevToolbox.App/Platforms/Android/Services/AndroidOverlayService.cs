using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Util;
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
/// Android system-level 3-state floating overlay engine using <see cref="IWindowManager"/> and
/// <see cref="WindowManagerTypes.ApplicationOverlay"/> (<c>SYSTEM_ALERT_WINDOW</c>):
/// 1. <see cref="OverlayExpansionState.CollapsedGear"/>: Draggable floating settings gear bubble (<c>⚙</c>).
/// 2. <see cref="OverlayExpansionState.ExpandedPanel"/>: Compact interactive developer control center with live telemetry and quick actions.
/// 3. <see cref="OverlayExpansionState.FullForegroundApp"/>: Expands into full view by bringing the main application to the foreground.
/// </summary>
public sealed class AndroidOverlayService : Java.Lang.Object, IOverlayService
{
    private readonly INetworkProxyService _proxyService;
    private readonly IOrientationStateService _orientationService;
    private readonly ILogcatStreamService _logcatService;
    private readonly IDeviceInspectorService _inspectorService;
    private readonly IAdbBridgeService _adbBridgeService;

    private IWindowManager? _windowManager;
    private AView? _floatingRootView;
    private WindowManagerLayoutParams? _layoutParams;
    private IReadOnlyList<OverlayQuickAction> _quickActions = [];
    private OverlayConfiguration _lastConfiguration = new();
    private OverlayExpansionState _expansionState = OverlayExpansionState.Hidden;
    private bool _isVisible;
    private int _currentX = 36;
    private int _currentY = 160;
    private string _lastActionFeedback = "Ready • Tap any tool or expand to Full View";

    public AndroidOverlayService(
        INetworkProxyService proxyService,
        IOrientationStateService orientationService,
        ILogcatStreamService logcatService,
        IDeviceInspectorService inspectorService,
        IAdbBridgeService adbBridgeService)
    {
        _proxyService = proxyService;
        _orientationService = orientationService;
        _logcatService = logcatService;
        _inspectorService = inspectorService;
        _adbBridgeService = adbBridgeService;

        _proxyService.StatusChanged += (_, _) => RefreshOverlayUiIfVisible();
        _orientationService.StateChanged += (_, _) => RefreshOverlayUiIfVisible();
    }

    public event EventHandler<OverlayStatusSnapshot>? StatusChanged;

    public OverlayStatusSnapshot CurrentStatus => new(
        _isVisible,
        CanDrawSystemOverlays() ? OverlayPermissionState.Granted : OverlayPermissionState.Denied,
        OverlayImplementationKind.AndroidSystemWindowManager,
        _isVisible
            ? _expansionState == OverlayExpansionState.CollapsedGear
                ? "Floating Gear active (tap ⚙ to expand options)"
                : "Expanded Quick HUD active (tap ⤢ for Full Foreground App)"
            : CanDrawSystemOverlays()
                ? "SYSTEM_ALERT_WINDOW granted • Ready to launch Floating Gear"
                : "SYSTEM_ALERT_WINDOW denied • Grant via Settings or ADB appops",
        _isVisible ? _expansionState : OverlayExpansionState.Hidden);

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

        _lastConfiguration = configuration;
        var targetState = configuration.InitialExpansionState == OverlayExpansionState.Hidden
            ? OverlayExpansionState.CollapsedGear
            : configuration.InitialExpansionState;

        if (targetState == OverlayExpansionState.FullForegroundApp)
        {
            return await ExpandToFullApplicationAsync(null, cancellationToken).ConfigureAwait(false);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var context = global::Android.App.Application.Context;
            _windowManager ??= context.GetSystemService(Context.WindowService)?.JavaCast<IWindowManager>();
            if (_windowManager is null)
            {
                return;
            }

            if (!_isVisible)
            {
                _currentX = configuration.InitialX;
                _currentY = configuration.InitialY;
            }

            _expansionState = targetState;
            RebuildAndAttachOverlayView(context);
        }).ConfigureAwait(false);

        RaiseStatusChanged();
        return CurrentStatus;
    }

    public async Task<OverlayStatusSnapshot> HideOverlayAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            DetachCurrentOverlayView();
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
            return await ShowOverlayAsync(
                _lastConfiguration with { InitialExpansionState = expansionState },
                cancellationToken).ConfigureAwait(false);
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var context = global::Android.App.Application.Context;
            _expansionState = expansionState;
            RebuildAndAttachOverlayView(context);
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
            var context = global::Android.App.Application.Context;

            // Keep the floating gear docked unobtrusively when returning to full view
            if (_isVisible && _expansionState != OverlayExpansionState.CollapsedGear)
            {
                _expansionState = OverlayExpansionState.CollapsedGear;
                RebuildAndAttachOverlayView(context);
            }

            // Bring MainActivity to the foreground as the active full-screen application
            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!)
                               ?? new Intent(context, typeof(MainActivity));
            launchIntent.AddFlags(
                ActivityFlags.NewTask
                | ActivityFlags.SingleTop
                | ActivityFlags.ClearTop
                | ActivityFlags.ReorderToFront);
            context.StartActivity(launchIntent);

            if (!string.IsNullOrWhiteSpace(targetRoute) && Shell.Current is not null)
            {
                try
                {
                    await Shell.Current.GoToAsync($"//{targetRoute}");
                }
                catch
                {
                    // Ignore route navigation errors if Shell is still initializing
                }
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
        RefreshOverlayUiIfVisible();
        return Task.CompletedTask;
    }

    private void RefreshOverlayUiIfVisible()
    {
        if (!_isVisible || _expansionState != OverlayExpansionState.ExpandedPanel)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isVisible && _expansionState == OverlayExpansionState.ExpandedPanel)
            {
                RebuildAndAttachOverlayView(global::Android.App.Application.Context);
            }
        });
    }

    private void RebuildAndAttachOverlayView(Context context)
    {
        if (_windowManager is null)
        {
            return;
        }

        if (_layoutParams is not null && _isVisible)
        {
            _currentX = _layoutParams.X;
            _currentY = _layoutParams.Y;
        }

        DetachCurrentOverlayView();

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
            X = _currentX,
            Y = _currentY,
            Alpha = (float)Math.Clamp(_lastConfiguration.Opacity, 0.45, 1.0)
        };

        _floatingRootView = _expansionState == OverlayExpansionState.CollapsedGear
            ? BuildCollapsedGearBubble(context)
            : BuildExpandedQuickPanel(context);

        _windowManager.AddView(_floatingRootView, _layoutParams);
        _isVisible = true;
    }

    private void DetachCurrentOverlayView()
    {
        if (_windowManager is not null && _floatingRootView is not null)
        {
            try
            {
                _windowManager.RemoveView(_floatingRootView);
            }
            catch
            {
                // Ignore if already detached
            }
        }

        _floatingRootView = null;
    }

    /// <summary>
    /// Stage 1: Compact draggable Settings Gear bubble (<c>⚙</c>) with active telemetry indicator dot.
    /// Clicking it expands into Stage 2 (<see cref="BuildExpandedQuickPanel"/>).
    /// </summary>
    private AView BuildCollapsedGearBubble(Context context)
    {
        var isProxyOrLogActive = _proxyService.CurrentStatus.IsRunning || _logcatService.IsStreaming;
        var accentHex = isProxyOrLogActive ? "#10B981" : "#38BDF8";

        var bubble = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal
        };
        bubble.SetGravity(GravityFlags.Center);
        bubble.SetPadding(Dp(context, 14), Dp(context, 10), Dp(context, 14), Dp(context, 10));
        bubble.Background = CreateRoundedSurface(context, "#F2090D16", accentHex, cornerDp: 28, strokeDp: 2);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
        {
            bubble.Elevation = Dp(context, 14);
        }

        var gearLabel = new TextView(context)
        {
            Text = "⚙",
            TextSize = 20f
        };
        gearLabel.SetTextColor(AColor.ParseColor("#F8FAFC"));
        gearLabel.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        bubble.AddView(gearLabel);

        var statusDot = new TextView(context)
        {
            Text = " ●",
            TextSize = 10f
        };
        statusDot.SetTextColor(AColor.ParseColor(accentHex));
        bubble.AddView(statusDot);

        var dragTouchListener = new DragOrTapTouchListener(
            onMoveDelta: (dx, dy) => UpdateOverlayPosition(dx, dy),
            onDragEnd: CommitOverlayPosition,
            onTap: async () => await SetExpansionStateAsync(OverlayExpansionState.ExpandedPanel));

        bubble.SetOnTouchListener(dragTouchListener);
        return bubble;
    }

    /// <summary>
    /// Stage 2: Expanded Quick Control Center with live status pills, 9 instant developer tools,
    /// and Stage 3 (<c>⤢ FULL VIEW</c>) foreground launcher.
    /// </summary>
    private AView BuildExpandedQuickPanel(Context context)
    {
        var panel = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical
        };
        panel.SetPadding(Dp(context, 14), Dp(context, 12), Dp(context, 14), Dp(context, 12));
        panel.Background = CreateRoundedSurface(context, "#F70B1120", "#38BDF8", cornerDp: 16, strokeDp: 2);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
        {
            panel.Elevation = Dp(context, 18);
        }

        // 1. Header Bar (Draggable handle + Gear collapse + Full View expand + Close)
        var headerRow = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal
        };
        headerRow.SetGravity(GravityFlags.CenterVertical);

        var gearCollapseBtn = CreateCompactPillButton(
            context,
            "⚙",
            "#1E293B",
            "#38BDF8",
            async () => await SetExpansionStateAsync(OverlayExpansionState.CollapsedGear),
            horizontalPaddingDp: 10);
        headerRow.AddView(gearCollapseBtn);

        var titleColumn = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical
        };
        titleColumn.SetPadding(Dp(context, 8), 0, Dp(context, 10), 0);

        var titleText = new TextView(context)
        {
            Text = "DEVTOOLBOX HUD",
            TextSize = 12f
        };
        titleText.SetTextColor(AColor.ParseColor("#F8FAFC"));
        titleText.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        titleColumn.AddView(titleText);

        var subtitleText = new TextView(context)
        {
            Text = "Drag header • Tap ⚙ to collapse",
            TextSize = 9.5f
        };
        subtitleText.SetTextColor(AColor.ParseColor("#94A3B8"));
        titleColumn.AddView(subtitleText);

        var headerDragListener = new DragOrTapTouchListener(
            onMoveDelta: (dx, dy) => UpdateOverlayPosition(dx, dy),
            onDragEnd: CommitOverlayPosition,
            onTap: null);
        titleColumn.SetOnTouchListener(headerDragListener);
        headerRow.AddView(titleColumn, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        // Expand to Full Foreground App button
        var fullViewBtn = CreateCompactPillButton(
            context,
            "⤢ FULL VIEW",
            "#0284C7",
            "#FFFFFF",
            async () => await ExpandToFullApplicationAsync("DashboardPage"),
            horizontalPaddingDp: 11);
        headerRow.AddView(fullViewBtn);

        var closeBtn = CreateCompactPillButton(
            context,
            "✕",
            "#7F1D1D",
            "#FCA5A5",
            async () => await HideOverlayAsync(),
            horizontalPaddingDp: 9);
        var closeParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = Dp(context, 6)
        };
        headerRow.AddView(closeBtn, closeParams);

        panel.AddView(headerRow);

        // 2. Live Status Strip
        var proxyStatus = _proxyService.CurrentStatus;
        var orientationState = _orientationService.CurrentState;
        var statusStrip = new TextView(context)
        {
            Text = $"● PROXY: {(proxyStatus.IsRunning ? $"ON :{proxyStatus.ListenPort} ({proxyStatus.TotalInterceptedCount})" : "STOPPED")}   •   ROT: {orientationState.CurrentOrientation.ToString().ToUpperInvariant()}   •   LOGS: {(_logcatService.IsStreaming ? "LIVE" : "IDLE")}",
            TextSize = 10f
        };
        statusStrip.SetTextColor(AColor.ParseColor("#38BDF8"));
        statusStrip.SetTypeface(Typeface.Monospace, TypefaceStyle.Bold);
        statusStrip.SetPadding(Dp(context, 10), Dp(context, 6), Dp(context, 10), Dp(context, 6));
        statusStrip.Background = CreateRoundedSurface(context, "#FF070B14", "#1E293B", cornerDp: 8, strokeDp: 1);

        var stripParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(context, 8),
            BottomMargin = Dp(context, 8)
        };
        panel.AddView(statusStrip, stripParams);

        // 3. Quick Action Grid (3 rows x 3 columns)
        // Row 1: Orientation Controls
        panel.AddView(BuildActionRow(
            context,
            ("📱 Portrait", "#1E293B", "#E2E8F0", async () =>
            {
                await _orientationService.ApplyOrientationAsync(
                    DeviceOrientationMode.Portrait,
                    _orientationService.CurrentState.ExecutionMode,
                    _orientationService.CurrentState.ConnectedDeviceSerial);
                UpdateFeedback("Forced Portrait (0°)");
            }),
            ("📺 Landscape", "#1E293B", "#E2E8F0", async () =>
            {
                await _orientationService.ApplyOrientationAsync(
                    DeviceOrientationMode.Landscape,
                    _orientationService.CurrentState.ExecutionMode,
                    _orientationService.CurrentState.ConnectedDeviceSerial);
                UpdateFeedback("Forced Landscape (90°)");
            }),
            ("🔄 Rotate 90°", "#1E293B", "#FBBF24", async () =>
            {
                var next = await _orientationService.CycleOrientationAsync(
                    _orientationService.CurrentState.ExecutionMode,
                    _orientationService.CurrentState.ConnectedDeviceSerial);
                UpdateFeedback($"Rotated to {next.CurrentOrientation}");
            })));

        // Row 2: Network Proxy & Mocking Controls
        var proxyBtnColor = proxyStatus.IsRunning ? "#065F46" : "#1E293B";
        var proxyBtnText = proxyStatus.IsRunning ? "⇄ Stop Proxy" : "⇄ Start Proxy";
        panel.AddView(BuildActionRow(
            context,
            (proxyBtnText, proxyBtnColor, "#34D399", async () =>
            {
                if (_proxyService.CurrentStatus.IsRunning)
                {
                    await _proxyService.StopAsync();
                    UpdateFeedback("MITM Proxy stopped");
                }
                else
                {
                    await _proxyService.StartAsync(_proxyService.CurrentStatus.ListenPort);
                    UpdateFeedback($"MITM Proxy listening on :{_proxyService.CurrentStatus.ListenPort}");
                }
            }),
            ("⚡ 503 Fault", "#3B1219", "#FCA5A5", async () =>
            {
                var entry = await _proxyService.EvaluateOrForwardRequestAsync(
                    "POST",
                    "https://api.sandbox.dev/api/v1/checkout",
                    new Dictionary<string, string> { ["X-HUD"] = "FaultTest" },
                    "{\"test\":true}");
                UpdateFeedback($"Injected {entry.StatusCode} Fault on /checkout");
            }),
            ("🧹 Clear Net", "#1E293B", "#94A3B8", () =>
            {
                _proxyService.ClearCaptures();
                UpdateFeedback("Cleared captured proxy traffic");
                return Task.CompletedTask;
            })));

        // Row 3: Diagnostics, Logcat & Deep-Link Controls
        panel.AddView(BuildActionRow(
            context,
            ("📌 Log Mark", "#1E293B", "#38BDF8", () =>
            {
                var stamp = DateTimeOffset.Now.ToString("HH:mm:ss");
                _logcatService.EmitDiagnosticEntry("I", "QA.HUD", $"HUD QA checkpoint @ {stamp}");
                UpdateFeedback($"Emitted QA Marker @ {stamp}");
                return Task.CompletedTask;
            }),
            ("📋 Inspect", "#1E293B", "#C084FC", async () =>
            {
                var report = await _inspectorService.CaptureReportAsync();
                UpdateFeedback($"Captured {report.Items.Count} device properties");
            }),
            ("🔗 Deep-Link", "#1E293B", "#60A5FA", async () =>
            {
                await _adbBridgeService.LaunchDeepLinkAsync("devtoolbox://inspect/session", null, null);
                UpdateFeedback("Dispatched devtoolbox://inspect/session");
            })));

        // 4. Direct Workspace Jump Row (Expands to Full Foreground App on selected tab)
        var jumpRow = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal
        };
        var jumpParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(context, 6)
        };

        AddJumpButton(context, jumpRow, "⤢ Proxy", "NetworkProxyPage");
        AddJumpButton(context, jumpRow, "⤢ Orientation", "OrientationToolPage");
        AddJumpButton(context, jumpRow, "⤢ Logs", "LogcatToolPage");
        AddJumpButton(context, jumpRow, "⤢ Inspector", "DeviceInspectorPage");
        panel.AddView(jumpRow, jumpParams);

        // 5. Feedback Footer
        var feedbackLabel = new TextView(context)
        {
            Text = $"✓ {_lastActionFeedback}",
            TextSize = 10f
        };
        feedbackLabel.SetTextColor(AColor.ParseColor("#94A3B8"));
        feedbackLabel.SetPadding(Dp(context, 4), Dp(context, 6), Dp(context, 4), 0);
        panel.AddView(feedbackLabel);

        return panel;
    }

    private LinearLayout BuildActionRow(
        Context context,
        params (string Label, string BgHex, string TextHex, Func<Task> Action)[] items)
    {
        var row = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal
        };
        var rowParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(context, 6)
        };
        row.LayoutParameters = rowParams;

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var btn = CreateCompactPillButton(context, item.Label, item.BgHex, item.TextHex, item.Action, horizontalPaddingDp: 8);
            var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            if (i > 0)
            {
                lp.LeftMargin = Dp(context, 6);
            }

            row.AddView(btn, lp);
        }

        return row;
    }

    private void AddJumpButton(Context context, LinearLayout row, string label, string route)
    {
        var btn = CreateCompactPillButton(
            context,
            label,
            "#0F172A",
            "#38BDF8",
            async () => await ExpandToFullApplicationAsync(route),
            horizontalPaddingDp: 6);
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        if (row.ChildCount > 0)
        {
            lp.LeftMargin = Dp(context, 4);
        }

        row.AddView(btn, lp);
    }

    private static TextView CreateCompactPillButton(
        Context context,
        string text,
        string bgHex,
        string textHex,
        Func<Task> onClick,
        int horizontalPaddingDp = 10)
    {
        var buttonView = new TextView(context)
        {
            Text = text,
            TextSize = 10.5f,
            Gravity = GravityFlags.Center
        };
        buttonView.SetTextColor(AColor.ParseColor(textHex));
        buttonView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        buttonView.SetPadding(
            Dp(context, horizontalPaddingDp),
            Dp(context, 7),
            Dp(context, horizontalPaddingDp),
            Dp(context, 7));
        buttonView.Background = CreateRoundedSurface(context, bgHex, "#334155", cornerDp: 8, strokeDp: 1);
        buttonView.Click += async (_, _) => await onClick();
        return buttonView;
    }

    private void UpdateFeedback(string message)
    {
        _lastActionFeedback = message;
        RefreshOverlayUiIfVisible();
    }

    private void UpdateOverlayPosition(int deltaX, int deltaY)
    {
        if (_layoutParams is null || _windowManager is null || _floatingRootView is null)
        {
            return;
        }

        _layoutParams.X = Math.Max(0, _currentX + deltaX);
        _layoutParams.Y = Math.Max(0, _currentY + deltaY);
        _windowManager.UpdateViewLayout(_floatingRootView, _layoutParams);
    }

    private void CommitOverlayPosition()
    {
        if (_layoutParams is not null)
        {
            _currentX = _layoutParams.X;
            _currentY = _layoutParams.Y;
        }
    }

    private static GradientDrawable CreateRoundedSurface(
        Context context,
        string fillHex,
        string strokeHex,
        int cornerDp,
        int strokeDp)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(AColor.ParseColor(fillHex));
        drawable.SetCornerRadius(Dp(context, cornerDp));
        drawable.SetStroke(Dp(context, strokeDp), AColor.ParseColor(strokeHex));
        return drawable;
    }

    private static int Dp(Context context, float dp) =>
        (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, dp, context.Resources?.DisplayMetrics);

    private static bool CanDrawSystemOverlays()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
        {
            return true;
        }

        return Settings.CanDrawOverlays(global::Android.App.Application.Context);
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, CurrentStatus);

    private sealed class DragOrTapTouchListener(
        Action<int, int> onMoveDelta,
        Action onDragEnd,
        Func<Task>? onTap) : Java.Lang.Object, AView.IOnTouchListener
    {
        private float _downRawX;
        private float _downRawY;
        private bool _hasMovedBeyondSlop;

        public bool OnTouch(AView? v, MotionEvent? e)
        {
            if (e is null)
            {
                return false;
            }

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _downRawX = e.RawX;
                    _downRawY = e.RawY;
                    _hasMovedBeyondSlop = false;
                    return true;

                case MotionEventActions.Move:
                    var dx = (int)(e.RawX - _downRawX);
                    var dy = (int)(e.RawY - _downRawY);
                    if (Math.Abs(dx) > 12 || Math.Abs(dy) > 12)
                    {
                        _hasMovedBeyondSlop = true;
                    }

                    if (_hasMovedBeyondSlop)
                    {
                        onMoveDelta(dx, dy);
                    }

                    return true;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (_hasMovedBeyondSlop)
                    {
                        onDragEnd();
                    }
                    else if (e.ActionMasked == MotionEventActions.Up && onTap is not null)
                    {
                        _ = onTap();
                    }

                    return true;

                default:
                    return false;
            }
        }
    }
}
