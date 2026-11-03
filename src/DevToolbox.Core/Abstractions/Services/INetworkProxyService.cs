namespace DevToolbox.Core.Abstractions.Services;

public enum ProxyRuleType
{
    MapLocal = 0,
    Rewrite = 1,
    ErrorInjection = 2,
    LatencyInjection = 3
}

public sealed class MockRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public ProxyRuleType RuleType { get; set; } = ProxyRuleType.MapLocal;

    public string UrlPattern { get; set; } = "*";

    public string HttpMethodFilter { get; set; } = "ANY";

    public int ResponseStatusCode { get; set; } = 200;

    public string ResponseContentType { get; set; } = "application/json";

    public string LocalPayloadOrFilePath { get; set; } = "{\"mocked\":true}";

    public string? RewriteTargetUrl { get; set; }

    public string? RewriteHeaderName { get; set; }

    public string? RewriteHeaderValue { get; set; }

    public int InjectedDelayMs { get; set; }

    public long HitCount { get; set; }
}

public sealed record CapturedTrafficEntry(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Method,
    string Url,
    string Host,
    string Path,
    int StatusCode,
    long DurationMs,
    bool IsMocked,
    string? MatchedRuleName,
    string RequestHeadersSummary,
    string ResponsePreview);

public sealed record ProxyEngineStatus(
    bool IsRunning,
    int ListenPort,
    string ListenAddress,
    int ActiveRulesCount,
    long TotalInterceptedCount);

/// <summary>
/// Embedded local HTTP/HTTPS interception, rewrite, fault-injection, and mock-response proxy engine.
/// </summary>
public interface INetworkProxyService
{
    event EventHandler<ProxyEngineStatus>? StatusChanged;

    ProxyEngineStatus CurrentStatus { get; }

    Task<ProxyEngineStatus> StartAsync(int port = 8888, CancellationToken cancellationToken = default);

    Task<ProxyEngineStatus> StopAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<MockRule> GetRules();

    void AddOrUpdateRule(MockRule rule);

    bool RemoveRule(Guid ruleId);

    bool ToggleRule(Guid ruleId, bool isEnabled);

    IReadOnlyList<CapturedTrafficEntry> GetRecentCaptures();

    void ClearCaptures();

    Task<CapturedTrafficEntry> EvaluateOrForwardRequestAsync(
        string httpMethod,
        string requestUrl,
        IReadOnlyDictionary<string, string>? requestHeaders = null,
        string? requestBody = null,
        CancellationToken cancellationToken = default);
}
