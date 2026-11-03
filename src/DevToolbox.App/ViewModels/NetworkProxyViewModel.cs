using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.App.ViewModels;

public sealed partial class MockRuleItemViewModel(
    MockRule rule,
    Action<MockRuleItemViewModel> onToggle,
    Action<MockRuleItemViewModel> onDelete) : ObservableObject
{
    public MockRule Rule { get; } = rule;

    public Guid Id => Rule.Id;

    public string Name => Rule.Name;

    public string Summary => $"{Rule.RuleType} • [{Rule.HttpMethodFilter}] {Rule.UrlPattern} → HTTP {Rule.ResponseStatusCode} (Delay: {Rule.InjectedDelayMs}ms)";

    public string PayloadPreview => Rule.LocalPayloadOrFilePath;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = rule.IsEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        Rule.IsEnabled = value;
        onToggle(this);
    }

    [RelayCommand]
    private void Delete() => onDelete(this);
}

public sealed partial class NetworkProxyViewModel : ObservableObject, IDisposable
{
    private readonly INetworkProxyService _proxyService;
    private readonly IAdbBridgeService _adbBridgeService;
    private readonly IDisposable _trafficSubscription;

    public NetworkProxyViewModel(
        INetworkProxyService proxyService,
        IAdbBridgeService adbBridgeService,
        IHighFrequencyUpdateSink<CapturedTrafficEntry> trafficSink)
    {
        _proxyService = proxyService;
        _adbBridgeService = adbBridgeService;

        // Subscribe directly to the IoC high-frequency sink for real-time traffic capture updates
        _trafficSubscription = trafficSink.Subscribe(
            OnTrafficBatchReceived,
            () => MainThread.BeginInvokeOnMainThread(() => CapturedTraffic.Clear()));

        _proxyService.StatusChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshStatus);

        ReloadRules();
        foreach (var existing in _proxyService.GetRecentCaptures())
        {
            CapturedTraffic.Add(existing);
        }

        RefreshStatus();
    }

    public ObservableCollection<MockRuleItemViewModel> Rules { get; } = [];

    public ObservableCollection<CapturedTrafficEntry> CapturedTraffic { get; } = [];

    public IReadOnlyList<ProxyRuleType> AvailableRuleTypes { get; } = Enum.GetValues<ProxyRuleType>();

    [ObservableProperty]
    public partial string ListenPortText { get; set; } = "8888";

    [ObservableProperty]
    public partial string StatusBanner { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewRuleName { get; set; } = "Mock Orders 200";

    [ObservableProperty]
    public partial string NewRuleUrlPattern { get; set; } = "*/api/v1/orders*";

    [ObservableProperty]
    public partial ProxyRuleType SelectedRuleType { get; set; } = ProxyRuleType.MapLocal;

    [ObservableProperty]
    public partial string NewRuleStatusCodeText { get; set; } = "200";

    [ObservableProperty]
    public partial string NewRuleDelayMsText { get; set; } = "0";

    [ObservableProperty]
    public partial string NewRulePayload { get; set; } = "{\"orders\":[{\"id\":\"ORD-9001\",\"status\":\"MockedReady\"}]}";

    [RelayCommand]
    private async Task StartProxyAsync()
    {
        if (!int.TryParse(ListenPortText, out var port))
        {
            port = 8888;
        }

        await _proxyService.StartAsync(port);
        RefreshStatus();
    }

    [RelayCommand]
    private async Task StopProxyAsync()
    {
        await _proxyService.StopAsync();
        RefreshStatus();
    }

    [RelayCommand]
    private async Task BindAdbReverseTunnelAsync()
    {
        if (!int.TryParse(ListenPortText, out var port))
        {
            port = 8888;
        }

        var result = await _adbBridgeService.ConfigureReverseProxyAsync(port, port);
        StatusBanner = result.Success
            ? $"ADB reverse tunnel bound: device tcp:{port} → host tcp:{port}"
            : $"ADB reverse failed: {result.StandardError}";
    }

    [RelayCommand]
    private void AddRule()
    {
        if (string.IsNullOrWhiteSpace(NewRuleName) || string.IsNullOrWhiteSpace(NewRuleUrlPattern))
        {
            return;
        }

        _ = int.TryParse(NewRuleStatusCodeText, out var statusCode);
        _ = int.TryParse(NewRuleDelayMsText, out var delayMs);

        var rule = new MockRule
        {
            Name = NewRuleName.Trim(),
            UrlPattern = NewRuleUrlPattern.Trim(),
            RuleType = SelectedRuleType,
            ResponseStatusCode = statusCode > 0 ? statusCode : 200,
            InjectedDelayMs = Math.Max(0, delayMs),
            LocalPayloadOrFilePath = NewRulePayload
        };

        _proxyService.AddOrUpdateRule(rule);
        ReloadRules();
        RefreshStatus();
    }

    [RelayCommand]
    private Task SimulateProfileRequestAsync() =>
        _proxyService.EvaluateOrForwardRequestAsync("GET", "https://api.sandbox.dev/api/v1/user/profile");

    [RelayCommand]
    private Task SimulateCheckoutFaultAsync() =>
        _proxyService.EvaluateOrForwardRequestAsync("POST", "https://api.sandbox.dev/api/v1/checkout");

    [RelayCommand]
    private Task SimulateCatalogRewriteAsync() =>
        _proxyService.EvaluateOrForwardRequestAsync("GET", "https://api.sandbox.dev/api/v1/catalog");

    [RelayCommand]
    private void ClearTraffic()
    {
        _proxyService.ClearCaptures();
    }

    public void Dispose()
    {
        _trafficSubscription.Dispose();
    }

    private void OnTrafficBatchReceived(IReadOnlyList<CapturedTrafficEntry> batch)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var item in batch)
            {
                CapturedTraffic.Insert(0, item);
                if (CapturedTraffic.Count > 100)
                {
                    CapturedTraffic.RemoveAt(CapturedTraffic.Count - 1);
                }
            }

            RefreshStatus();
        });
    }

    private void ReloadRules()
    {
        Rules.Clear();
        foreach (var rule in _proxyService.GetRules())
        {
            Rules.Add(new MockRuleItemViewModel(
                rule,
                item => _proxyService.ToggleRule(item.Id, item.IsEnabled),
                item =>
                {
                    _proxyService.RemoveRule(item.Id);
                    ReloadRules();
                }));
        }
    }

    private void RefreshStatus()
    {
        var status = _proxyService.CurrentStatus;
        StatusBanner = status.IsRunning
            ? $"RUNNING on {status.ListenAddress} • {status.ActiveRulesCount} Active Rules • {status.TotalInterceptedCount} Captured"
            : $"STOPPED (Port {status.ListenPort}) • {status.ActiveRulesCount} Active Rules • {status.TotalInterceptedCount} Captured";
    }
}
