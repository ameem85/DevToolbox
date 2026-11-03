using DevToolbox.Core.Abstractions.Services;

namespace DevToolbox.Core.Services;

/// <summary>
/// Thread-safe IoC direct-update sink for high-frequency streams (such as network traffic capture or logcat lines).
/// Delivers items directly to subscribed UI handlers without per-item reflection binding overhead.
/// </summary>
public sealed class HighFrequencyUpdateSink<T> : IHighFrequencyUpdateSink<T>
{
    private readonly Lock _syncRoot = new();
    private readonly List<Subscription> _subscriptions = [];

    public void Publish(T item)
    {
        PublishBatch([item]);
    }

    public void PublishBatch(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Subscription[] snapshot;
        lock (_syncRoot)
        {
            snapshot = [.. _subscriptions];
        }

        foreach (var subscription in snapshot)
        {
            subscription.OnBatchReceived(items);
        }
    }

    public void Clear()
    {
        Subscription[] snapshot;
        lock (_syncRoot)
        {
            snapshot = [.. _subscriptions];
        }

        foreach (var subscription in snapshot)
        {
            subscription.OnCleared?.Invoke();
        }
    }

    public IDisposable Subscribe(Action<IReadOnlyList<T>> onBatchReceived, Action? onCleared = null)
    {
        ArgumentNullException.ThrowIfNull(onBatchReceived);

        var subscription = new Subscription(this, onBatchReceived, onCleared);
        lock (_syncRoot)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_syncRoot)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private sealed class Subscription(
        HighFrequencyUpdateSink<T> owner,
        Action<IReadOnlyList<T>> onBatchReceived,
        Action? onCleared) : IDisposable
    {
        public Action<IReadOnlyList<T>> OnBatchReceived { get; } = onBatchReceived;

        public Action? OnCleared { get; } = onCleared;

        public void Dispose() => owner.Unsubscribe(this);
    }
}
