namespace DevToolbox.Core.Abstractions.Services;

/// <summary>
/// Direct IoC update sink for high-frequency streams (such as network proxy traffic capture or logcat output)
/// to avoid per-event reflection binding overhead on UI threads.
/// </summary>
public interface IHighFrequencyUpdateSink<T>
{
    void Publish(T item);

    void PublishBatch(IReadOnlyList<T> items);

    void Clear();

    IDisposable Subscribe(Action<IReadOnlyList<T>> onBatchReceived, Action? onCleared = null);
}
