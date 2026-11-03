using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Services;

/// <summary>
/// Embedded local HTTP/HTTPS interception, rewrite, fault-injection, and mock-response proxy engine.
/// Uses <see cref="IHighFrequencyUpdateSink{CapturedTrafficEntry}"/> for high-frequency direct UI updates.
/// </summary>
public sealed class LocalNetworkProxyEngine : INetworkProxyService, IDisposable
{
    private readonly IHighFrequencyUpdateSink<CapturedTrafficEntry> _trafficSink;
    private readonly HttpClient _upstreamClient;
    private readonly Lock _syncRoot = new();
    private readonly List<MockRule> _rules = [];
    private readonly List<CapturedTrafficEntry> _captures = [];

    private TcpListener? _tcpListener;
    private CancellationTokenSource? _listenerCts;
    private bool _isRunning;
    private int _listenPort = 8888;
    private long _totalInterceptedCount;

    public LocalNetworkProxyEngine(
        IHighFrequencyUpdateSink<CapturedTrafficEntry> trafficSink,
        HttpClient? upstreamClient = null)
    {
        _trafficSink = trafficSink ?? throw new ArgumentNullException(nameof(trafficSink));
        _upstreamClient = upstreamClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        SeedDefaultDeveloperRules();
    }

    public event EventHandler<ProxyEngineStatus>? StatusChanged;

    public ProxyEngineStatus CurrentStatus
    {
        get
        {
            lock (_syncRoot)
            {
                return new ProxyEngineStatus(
                    _isRunning,
                    _listenPort,
                    $"127.0.0.1:{_listenPort}",
                    _rules.Count(r => r.IsEnabled),
                    Interlocked.Read(ref _totalInterceptedCount));
            }
        }
    }

    public Task<ProxyEngineStatus> StartAsync(int port = 8888, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        lock (_syncRoot)
        {
            if (_isRunning && _listenPort == port)
            {
                return Task.FromResult(CurrentStatus);
            }

            StopInternal();

            _listenPort = port;
            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _tcpListener = new TcpListener(IPAddress.Loopback, _listenPort);
            _tcpListener.Start();
            _isRunning = true;

            _ = AcceptConnectionsLoopAsync(_tcpListener, _listenerCts.Token);
        }

        var status = CurrentStatus;
        StatusChanged?.Invoke(this, status);
        return Task.FromResult(status);
    }

    public Task<ProxyEngineStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            StopInternal();
        }

        var status = CurrentStatus;
        StatusChanged?.Invoke(this, status);
        return Task.FromResult(status);
    }

    public IReadOnlyList<MockRule> GetRules()
    {
        lock (_syncRoot)
        {
            return _rules.ToList();
        }
    }

    public void AddOrUpdateRule(MockRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_syncRoot)
        {
            var existingIndex = _rules.FindIndex(r => r.Id == rule.Id);
            if (existingIndex >= 0)
            {
                _rules[existingIndex] = rule;
            }
            else
            {
                _rules.Add(rule);
            }
        }

        StatusChanged?.Invoke(this, CurrentStatus);
    }

    public bool RemoveRule(Guid ruleId)
    {
        bool removed;
        lock (_syncRoot)
        {
            removed = _rules.RemoveAll(r => r.Id == ruleId) > 0;
        }

        if (removed)
        {
            StatusChanged?.Invoke(this, CurrentStatus);
        }

        return removed;
    }

    public bool ToggleRule(Guid ruleId, bool isEnabled)
    {
        lock (_syncRoot)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is null)
            {
                return false;
            }

            rule.IsEnabled = isEnabled;
        }

        StatusChanged?.Invoke(this, CurrentStatus);
        return true;
    }

    public IReadOnlyList<CapturedTrafficEntry> GetRecentCaptures()
    {
        lock (_syncRoot)
        {
            return _captures.ToList();
        }
    }

    public void ClearCaptures()
    {
        lock (_syncRoot)
        {
            _captures.Clear();
        }

        _trafficSink.Clear();
        StatusChanged?.Invoke(this, CurrentStatus);
    }

    public async Task<CapturedTrafficEntry> EvaluateOrForwardRequestAsync(
        string httpMethod,
        string requestUrl,
        IReadOnlyDictionary<string, string>? requestHeaders = null,
        string? requestBody = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUrl);

        var stopwatch = Stopwatch.StartNew();
        var normalizedMethod = httpMethod.Trim().ToUpperInvariant();
        var matchedRule = FindMatchingRule(normalizedMethod, requestUrl);

        int statusCode;
        bool isMocked = false;
        string? matchedRuleName = null;
        string responsePreview;
        string effectiveUrl = requestUrl;

        var headerSummary = requestHeaders is { Count: > 0 }
            ? string.Join("; ", requestHeaders.Select(kvp => $"{kvp.Key}: {kvp.Value}"))
            : "Accept: application/json; User-Agent: DevToolbox-Proxy/1.0";

        if (matchedRule is not null)
        {
            matchedRule.HitCount++;
            matchedRuleName = matchedRule.Name;
            isMocked = true;

            if (matchedRule.InjectedDelayMs > 0)
            {
                await Task.Delay(matchedRule.InjectedDelayMs, cancellationToken).ConfigureAwait(false);
            }

            switch (matchedRule.RuleType)
            {
                case ProxyRuleType.MapLocal:
                    statusCode = matchedRule.ResponseStatusCode;
                    responsePreview = await ResolveMapLocalPayloadAsync(
                        matchedRule.LocalPayloadOrFilePath,
                        cancellationToken).ConfigureAwait(false);
                    break;

                case ProxyRuleType.ErrorInjection:
                    statusCode = matchedRule.ResponseStatusCode >= 400 ? matchedRule.ResponseStatusCode : 503;
                    responsePreview = string.IsNullOrWhiteSpace(matchedRule.LocalPayloadOrFilePath)
                        ? $"{{\"error\":\"Injected fault ({statusCode})\",\"rule\":\"{matchedRule.Name}\"}}"
                        : matchedRule.LocalPayloadOrFilePath;
                    break;

                case ProxyRuleType.Rewrite:
                    if (!string.IsNullOrWhiteSpace(matchedRule.RewriteTargetUrl))
                    {
                        effectiveUrl = matchedRule.RewriteTargetUrl;
                    }

                    if (!string.IsNullOrWhiteSpace(matchedRule.RewriteHeaderName))
                    {
                        headerSummary = $"{headerSummary}; {matchedRule.RewriteHeaderName}: {matchedRule.RewriteHeaderValue}";
                    }

                    statusCode = matchedRule.ResponseStatusCode;
                    responsePreview = string.IsNullOrWhiteSpace(matchedRule.LocalPayloadOrFilePath)
                        ? $"{{\"rewrittenUrl\":\"{effectiveUrl}\",\"injectedHeader\":\"{matchedRule.RewriteHeaderName}: {matchedRule.RewriteHeaderValue}\"}}"
                        : matchedRule.LocalPayloadOrFilePath;
                    break;

                case ProxyRuleType.LatencyInjection:
                default:
                    statusCode = matchedRule.ResponseStatusCode;
                    responsePreview = string.IsNullOrWhiteSpace(matchedRule.LocalPayloadOrFilePath)
                        ? $"{{\"latencyInjectedMs\":{matchedRule.InjectedDelayMs},\"url\":\"{effectiveUrl}\"}}"
                        : matchedRule.LocalPayloadOrFilePath;
                    break;
            }
        }
        else
        {
            (statusCode, responsePreview) = await TryForwardUpstreamOrSimulateAsync(
                normalizedMethod,
                effectiveUrl,
                requestBody,
                cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        Interlocked.Increment(ref _totalInterceptedCount);

        Uri.TryCreate(effectiveUrl, UriKind.Absolute, out var parsedUri);
        var host = parsedUri?.Host ?? "localhost";
        var path = parsedUri?.PathAndQuery ?? effectiveUrl;

        var entry = new CapturedTrafficEntry(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            normalizedMethod,
            effectiveUrl,
            host,
            path,
            statusCode,
            Math.Max(1, stopwatch.ElapsedMilliseconds),
            isMocked,
            matchedRuleName,
            headerSummary,
            responsePreview);

        lock (_syncRoot)
        {
            _captures.Insert(0, entry);
            if (_captures.Count > 250)
            {
                _captures.RemoveAt(_captures.Count - 1);
            }
        }

        // Direct update via IoC sink to avoid high-frequency reflection binding overhead
        _trafficSink.Publish(entry);
        StatusChanged?.Invoke(this, CurrentStatus);

        return entry;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        _isRunning = false;
        try
        {
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();
        }
        catch
        {
            // Ignore cancellation disposal errors on shutdown
        }
        finally
        {
            _listenerCts = null;
        }

        try
        {
            _tcpListener?.Stop();
        }
        catch
        {
            // Ignore socket stop exceptions
        }
        finally
        {
            _tcpListener = null;
        }
    }

    private async Task AcceptConnectionsLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleProxyClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleProxyClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
        {
            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return;
            }

            var method = parts[0];
            var rawTarget = parts[1];
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)))
            {
                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex > 0)
                {
                    var key = headerLine[..separatorIndex].Trim();
                    var value = headerLine[(separatorIndex + 1)..].Trim();
                    headers[key] = value;
                }
            }

            var fullUrl = rawTarget.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rawTarget
                : headers.TryGetValue("Host", out var hostHeader)
                    ? $"http://{hostHeader}{rawTarget}"
                    : $"http://localhost:{_listenPort}{rawTarget}";

            var captured = await EvaluateOrForwardRequestAsync(
                method,
                fullUrl,
                headers,
                null,
                cancellationToken).ConfigureAwait(false);

            var payloadBytes = Encoding.UTF8.GetBytes(captured.ResponsePreview);
            var responseHeader =
                $"HTTP/1.1 {captured.StatusCode} {(captured.StatusCode < 400 ? "OK" : "MockedError")}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {payloadBytes.Length}\r\n" +
                "X-DevToolbox-Proxy: Intercepted\r\n" +
                "Connection: close\r\n\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(responseHeader);
            await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private MockRule? FindMatchingRule(string httpMethod, string requestUrl)
    {
        lock (_syncRoot)
        {
            foreach (var rule in _rules.Where(r => r.IsEnabled))
            {
                if (!string.Equals(rule.HttpMethodFilter, "ANY", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(rule.HttpMethodFilter, httpMethod, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (MatchesPattern(requestUrl, rule.UrlPattern))
                {
                    return rule;
                }
            }
        }

        return null;
    }

    private static bool MatchesPattern(string url, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*")
        {
            return true;
        }

        var normalizedPattern = pattern.Trim('*');
        return url.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ResolveMapLocalPayloadAsync(
        string payloadOrPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payloadOrPath))
        {
            return "{\"mocked\":true}";
        }

        var trimmed = payloadOrPath.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[') && File.Exists(trimmed))
        {
            return await File.ReadAllTextAsync(trimmed, cancellationToken).ConfigureAwait(false);
        }

        return payloadOrPath;
    }

    private async Task<(int StatusCode, string Body)> TryForwardUpstreamOrSimulateAsync(
        string method,
        string url,
        string? body,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("api.sandbox.dev", StringComparison.OrdinalIgnoreCase))
        {
            return (200, $"{{\"passThrough\":true,\"method\":\"{method}\",\"url\":\"{url}\",\"timestamp\":\"{DateTimeOffset.UtcNow:O}\"}}");
        }

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            if (!string.IsNullOrEmpty(body) && method is not ("GET" or "HEAD"))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await _upstreamClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var preview = responseText.Length > 512 ? responseText[..512] + "..." : responseText;
            return ((int)response.StatusCode, preview);
        }
        catch (Exception ex)
        {
            return (502, $"{{\"proxyForwardError\":\"{ex.GetType().Name}\",\"message\":\"{ex.Message}\"}}");
        }
    }

    private void SeedDefaultDeveloperRules()
    {
        _rules.AddRange(
        [
            new MockRule
            {
                Name = "Map Local: User Profile Override",
                IsEnabled = true,
                RuleType = ProxyRuleType.MapLocal,
                HttpMethodFilter = "GET",
                UrlPattern = "*/api/v1/user/profile*",
                ResponseStatusCode = 200,
                ResponseContentType = "application/json",
                LocalPayloadOrFilePath = "{\"id\":\"qa-override-01\",\"role\":\"SuperAdmin\",\"featureFlags\":[\" beta_FloatingHud \",\" mock_Checkout \"]}"
            },
            new MockRule
            {
                Name = "Error Injection: Checkout 503 Outage",
                IsEnabled = true,
                RuleType = ProxyRuleType.ErrorInjection,
                HttpMethodFilter = "POST",
                UrlPattern = "*/api/v1/checkout*",
                ResponseStatusCode = 503,
                ResponseContentType = "application/json",
                InjectedDelayMs = 250,
                LocalPayloadOrFilePath = "{\"errorCode\":\"SERVICE_UNAVAILABLE\",\"message\":\"Simulated gateway outage injected by DevToolbox Proxy\"}"
            },
            new MockRule
            {
                Name = "Rewrite + Latency: Staging Catalog Header",
                IsEnabled = true,
                RuleType = ProxyRuleType.Rewrite,
                HttpMethodFilter = "GET",
                UrlPattern = "*/api/v1/catalog*",
                ResponseStatusCode = 200,
                InjectedDelayMs = 180,
                RewriteHeaderName = "X-Environment-Override",
                RewriteHeaderValue = "staging-canary",
                LocalPayloadOrFilePath = "{\"catalogVersion\":\"2026.11-canary\",\"itemsCount\":42,\"source\":\"RewrittenStagingEdge\"}"
            }
        ]);
    }
}
